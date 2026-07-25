using System.Net.Sockets;
using System.Text.RegularExpressions;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Miabi.Aspire.Hosting;

internal sealed class MiabiManifestGenerator
{
    private readonly ISerializer _serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull |
                                        DefaultValuesHandling.OmitEmptyCollections)
        .Build();

    public async Task<string> GenerateAsync(
        DistributedApplicationModel model,
        DistributedApplicationExecutionContext executionContext,
        IServiceProvider services,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var manifests = new List<MiabiManifest>();

        foreach (var resource in model.Resources
                     .Where(static resource => resource is IComputeResource)
                     .OrderBy(static resource => resource.Name, StringComparer.Ordinal))
        {
            var image = await GetImageAsync(
                resource, executionContext, services, logger, cancellationToken);
            if (image is null)
            {
                throw new InvalidOperationException(
                    $"Miabi cannot determine an image for compute resource '{resource.Name}'. " +
                    "Project resources must participate in the Aspire build pipeline.");
            }

            var (repository, tag) = SplitImage(image);
            var spec = new MiabiApplicationSpec { Image = repository, Tag = tag };

            var endpoints = resource.Annotations.OfType<EndpointAnnotation>().ToArray();
            foreach (var endpoint in endpoints
                         .Where(endpoint =>
                             resource is not ProjectResource ||
                             endpoint.UriScheme != "https" ||
                             !endpoints.Any(candidate => candidate.UriScheme == "http"))
                         .OrderBy(static endpoint => endpoint.Name, StringComparer.Ordinal))
            {
                var targetPort = GetTargetPort(resource, endpoint);

                spec.Ports.Add(new MiabiPortSpec
                {
                    Container = targetPort,
                    Protocol = endpoint.Protocol == ProtocolType.Udp ? "udp" : "tcp",
                    Scheme = endpoint.UriScheme,
                    ExternalAccess = endpoint.IsExternal,
                    Publish = endpoint.Port.HasValue && !endpoint.IsProxied,
                    HostPort = endpoint.Port ?? 0
                });
            }

            if (spec.Ports.Any(static port => port.ExternalAccess))
            {
                spec.ExternalLabel = NormalizeName(resource.Name);
            }

            if (resource is IResourceWithEnvironment environmentResource)
            {
                var miabiSecretVariables = resource.Annotations
                    .OfType<MiabiSecretAnnotation>()
                    .Select(static secret => secret.EnvironmentVariable)
                    .ToHashSet(StringComparer.Ordinal);
                var values = await environmentResource.GetEnvironmentVariableValuesAsync(
                    executionContext.Operation);
                foreach (var pair in values.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
                {
                    if (miabiSecretVariables.Contains(pair.Key))
                    {
                        continue;
                    }

                    spec.Environment[pair.Key] = ResolveEnvironmentValue(
                        resource,
                        pair.Key,
                        pair.Value);
                }
            }

            foreach (var secret in resource.Annotations.OfType<MiabiSecretAnnotation>())
            {
                spec.Environment[secret.EnvironmentVariable] = $"{{{{ .secrets.{secret.SecretName} }}}}";
                spec.SecretEnvironment.Add(secret.EnvironmentVariable);
            }

            foreach (var mount in resource.Annotations.OfType<ContainerMountAnnotation>())
            {
                if (mount.Type != ContainerMountType.Volume || string.IsNullOrWhiteSpace(mount.Source))
                {
                    throw new InvalidOperationException(
                        $"Miabi currently supports only named volumes; mount '{resource.Name}:{mount.Target}' is unsupported.");
                }

                var volumeName = NormalizeName(mount.Source);
                spec.Mounts.Add(new MiabiMountSpec
                {
                    Volume = volumeName,
                    Path = mount.Target,
                    ReadOnly = mount.IsReadOnly
                });
                if (!manifests.Any(x => x.Kind == "Volume" && x.Metadata.Name == volumeName))
                {
                    manifests.Add(Create("Volume", volumeName, new MiabiVolumeSpec()));
                }
            }

            manifests.Add(Create("Application", NormalizeName(resource.Name), spec));

            foreach (var domain in resource.Annotations.OfType<MiabiDomainAnnotation>())
            {
                var endpoint = resource.Annotations.OfType<EndpointAnnotation>()
                                   .LastOrDefault(x =>
                                       string.Equals(x.Name, domain.EndpointName, StringComparison.Ordinal))
                               ?? throw new InvalidOperationException(
                                   $"Miabi domain '{domain.Host}' references missing endpoint " +
                                   $"'{resource.Name}/{domain.EndpointName}'.");
                var targetPort = GetTargetPort(resource, endpoint);
                if (!manifests.Any(x => x.Kind == "Domain" && x.Metadata.Name == domain.Host))
                {
                    manifests.Add(Create("Domain", domain.Host, new MiabiDomainSpec
                    {
                        Tls = domain.Tls == "custom" ? "custom" : "acme"
                    }));
                }

                manifests.Add(Create("Route", NormalizeName($"{resource.Name}-{domain.EndpointName}"),
                    new MiabiRouteSpec
                    {
                        Hosts = [domain.Host],
                        Application = NormalizeName(resource.Name),
                        Port = targetPort,
                        Tls = domain.Tls
                    }));
            }
        }

        if (manifests.Count == 0)
        {
            throw new InvalidOperationException("The Aspire model contains no Miabi-compatible compute resources.");
        }

        return string.Join("---\n", manifests.Select(_serializer.Serialize));
    }

    private static MiabiManifest Create(string kind, string name, object spec) => new()
    {
        Kind = kind,
        Metadata = new MiabiMetadata
        {
            Name = name,
            Labels = new Dictionary<string, string>
            {
                ["app.kubernetes.io/managed-by"] = "aspire",
                ["miabi.io/aspire-resource"] = name
            }
        },
        Spec = spec
    };

    private static async Task<string?> GetImageAsync(
        IResource resource,
        DistributedApplicationExecutionContext executionContext,
        IServiceProvider services,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var registryReference = resource.Annotations
            .OfType<ContainerRegistryReferenceAnnotation>()
            .LastOrDefault();
        if (registryReference is not null)
        {
            var pushOptions = new ContainerImagePushOptions
            {
                RemoteImageName = resource.Name,
                RemoteImageTag = "latest"
            };
            foreach (var annotation in resource.Annotations
                         .OfType<ContainerImagePushOptionsCallbackAnnotation>())
            {
                await annotation.Callback(new ContainerImagePushOptionsCallbackContext
                {
                    Resource = resource,
                    Options = pushOptions,
                    CancellationToken = cancellationToken
                });
            }

            return await pushOptions.GetFullRemoteImageNameAsync(
                registryReference.Registry,
                cancellationToken);
        }

        // Dockerfile resources carry a placeholder image annotation. Resolve their
        // build callback first so the manifest receives Aspire's content-addressed tag.
        foreach (var annotation in resource.Annotations.OfType<ContainerBuildOptionsCallbackAnnotation>())
        {
            var context = new ContainerBuildOptionsCallbackContext(
                resource,
                services,
                logger,
                cancellationToken,
                executionContext);
            await annotation.Callback(context);
            if (!string.IsNullOrWhiteSpace(context.LocalImageName))
            {
                return $"{context.LocalImageName}:{context.LocalImageTag ?? "latest"}";
            }
        }

        if (resource is ProjectResource)
        {
            return $"{resource.Name}:latest";
        }

        var imageAnnotation = resource.Annotations.OfType<ContainerImageAnnotation>().LastOrDefault();
        if (imageAnnotation is not null)
        {
            var registry = string.IsNullOrWhiteSpace(imageAnnotation.Registry)
                ? null
                : imageAnnotation.Registry.TrimEnd('/') + "/";
            var tag = string.IsNullOrWhiteSpace(imageAnnotation.Tag) ? "latest" : imageAnnotation.Tag;
            return $"{registry}{imageAnnotation.Image}:{tag}";
        }

        return null;
    }

    private static string ResolveEnvironmentValue(
        IResource resource,
        string variableName,
        string value)
    {
        var resolved = Regex.Replace(
            value,
            @"\{(?<resource>[^.{}]+)\.bindings\.(?<endpoint>[^.{}]+)\.targetPort\}",
            match =>
            {
                if (!string.Equals(
                        match.Groups["resource"].Value,
                        resource.Name,
                        StringComparison.Ordinal))
                {
                    return match.Value;
                }

                var endpointName = match.Groups["endpoint"].Value;
                var endpoint = resource.Annotations.OfType<EndpointAnnotation>()
                    .LastOrDefault(annotation => string.Equals(
                        annotation.Name,
                        endpointName,
                        StringComparison.Ordinal));
                return endpoint is null
                        ? match.Value
                        : GetTargetPort(resource, endpoint).ToString(
                            System.Globalization.CultureInfo.InvariantCulture)
                    ;
            });

        if (resolved.Contains('{', StringComparison.Ordinal) ||
            resolved.Contains('}', StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Environment variable '{resource.Name}/{variableName}' contains " +
                $"an Aspire reference that Miabi cannot translate yet: '{value}'.");
        }

        return resolved;
    }

    private static int GetTargetPort(IResource resource, EndpointAnnotation endpoint)
    {
        if (endpoint.TargetPort is { } targetPort)
        {
            return targetPort;
        }

        if (resource is ProjectResource &&
            endpoint.UriScheme is "http" or "https")
        {
            // .NET SDK container images listen on 8080 by default. Aspire's
            // publish targets apply the same convention when a project endpoint
            // came from launchSettings and has no container target port.
            return 8080;
        }

        throw new InvalidOperationException(
            $"Endpoint '{resource.Name}/{endpoint.Name}' needs a target port for Miabi.");
    }

    private static string NormalizeName(string value)
    {
        var normalized = new string(value.ToLowerInvariant()
            .Select(static character =>
                char.IsAsciiLetterOrDigit(character) || character == '-' ? character : '-')
            .ToArray()).Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? "app" : normalized;
    }

    internal static (string Repository, string? Tag) SplitImage(string image)
    {
        if (image.Contains('@', StringComparison.Ordinal))
        {
            return (image, null);
        }

        var slash = image.LastIndexOf('/');
        var colon = image.LastIndexOf(':');
        return colon > slash ? (image[..colon], image[(colon + 1)..]) : (image, null);
    }
}