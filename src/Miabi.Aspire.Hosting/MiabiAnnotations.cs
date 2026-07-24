using Aspire.Hosting.ApplicationModel;

namespace Miabi.Aspire.Hosting;

internal sealed record MiabiDomainAnnotation(string Host, string EndpointName, string Tls)
    : IResourceAnnotation;

internal sealed record MiabiSecretAnnotation(
    string EnvironmentVariable,
    string SecretName,
    ParameterResource Parameter) : IResourceAnnotation;

internal sealed record MiabiDeploymentOptionsAnnotation(bool Prune, TimeSpan Timeout)
    : IResourceAnnotation;
