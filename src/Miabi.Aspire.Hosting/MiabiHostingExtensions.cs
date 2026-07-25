using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;
using Miabi.Aspire.Hosting;

namespace Aspire.Hosting;

/// <summary>Extensions for deploying Aspire applications to Miabi.</summary>
public static class MiabiHostingExtensions
{
    /// <summary>
    /// Adds an existing Miabi workspace as a publish/deploy-only compute environment.
    /// The Miabi instance itself is not provisioned by this integration.
    /// </summary>
    [AspireExport]
    public static IResourceBuilder<MiabiEnvironmentResource> AddMiabiEnvironment(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        string server,
        string workspace,
        IResourceBuilder<ParameterResource> apiToken)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(server);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspace);
        ArgumentNullException.ThrowIfNull(apiToken);

        if (!apiToken.Resource.Secret)
        {
            throw new ArgumentException("The Miabi API token parameter must be secret.", nameof(apiToken));
        }

        if (!Uri.TryCreate(server, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new ArgumentException("Miabi server must be an absolute HTTP(S) URL.", nameof(server));
        }

        var resource = new MiabiEnvironmentResource(
            name,
            server.TrimEnd('/'),
            workspace,
            apiToken.Resource);

        if (builder.ExecutionContext.IsRunMode)
        {
            return builder.CreateResourceBuilder(resource);
        }

        var resourceBuilder = builder.AddResource(resource);
        resourceBuilder.WithAnnotation(new MiabiDeploymentOptionsAnnotation(false, TimeSpan.FromMinutes(10)));
        resourceBuilder.WithAnnotation(new PipelineStepAnnotation(context => MiabiPipelineSteps.Create(
            context.Resource as MiabiEnvironmentResource
            ?? throw new InvalidOperationException("Expected a Miabi environment resource."))));
        return resourceBuilder;
    }

    /// <summary>Controls whether apply prunes other Aspire-managed Miabi resources.</summary>
    [AspireExport]
    public static IResourceBuilder<MiabiEnvironmentResource> WithPrune(
        this IResourceBuilder<MiabiEnvironmentResource> builder,
        bool enabled = true)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var current = builder.Resource.Annotations
            .OfType<MiabiDeploymentOptionsAnnotation>()
            .LastOrDefault() ?? new MiabiDeploymentOptionsAnnotation(false, TimeSpan.FromMinutes(10));
        return builder.WithAnnotation(current with { Prune = enabled },
            ResourceAnnotationMutationBehavior.Replace);
    }

    /// <summary>Sets the maximum duration of a Miabi CLI operation.</summary>
    [AspireExport]
    public static IResourceBuilder<MiabiEnvironmentResource> WithDeploymentTimeout(
        this IResourceBuilder<MiabiEnvironmentResource> builder,
        TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        var current = builder.Resource.Annotations
            .OfType<MiabiDeploymentOptionsAnnotation>()
            .LastOrDefault() ?? new MiabiDeploymentOptionsAnnotation(false, TimeSpan.FromMinutes(10));
        return builder.WithAnnotation(current with { Timeout = timeout },
            ResourceAnnotationMutationBehavior.Replace);
    }

    /// <summary>
    /// Exposes a compute resource through a Miabi route.
    /// Use <c>tls: "off"</c> for an HTTP-only local domain.
    /// </summary>
    [AspireExport]
    public static IResourceBuilder<T> WithMiabiDomain<T>(
        this IResourceBuilder<T> builder,
        string host,
        string endpointName = "http",
        string tls = "acme")
        where T : IResource
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointName);
        if (tls is not ("acme" or "custom" or "off"))
        {
            throw new ArgumentException("TLS must be 'acme', 'custom', or 'off'.", nameof(tls));
        }

        return builder.WithAnnotation(new MiabiDomainAnnotation(host, endpointName, tls));
    }

    /// <summary>
    /// Maps a secret parameter to Miabi Vault and references it from an application environment variable.
    /// The value is uploaded over stdin during deploy and is never written to the published manifest.
    /// </summary>
    [AspireExport("withResourceMiabiSecret", MethodName = "withMiabiSecret")]
    public static IResourceBuilder<T> WithMiabiSecret<T>(
        this IResourceBuilder<T> builder,
        string environmentVariable,
        string secretName,
        IResourceBuilder<ParameterResource> parameter)
        where T : IResourceWithEnvironment
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentVariable);
        ArgumentException.ThrowIfNullOrWhiteSpace(secretName);
        ArgumentNullException.ThrowIfNull(parameter);
        if (!parameter.Resource.Secret)
        {
            throw new ArgumentException("Miabi Vault values must use secret parameters.", nameof(parameter));
        }

        builder.WithEnvironment(environmentVariable, parameter);
        return builder.WithAnnotation(
            new MiabiSecretAnnotation(environmentVariable, secretName, parameter.Resource));
    }
}