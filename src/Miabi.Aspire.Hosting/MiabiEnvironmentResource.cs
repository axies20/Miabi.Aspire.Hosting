using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Miabi.Aspire.Hosting;

/// <summary>
/// Represents an existing Miabi workspace used as an Aspire deployment environment.
/// </summary>
[AspireExport]
public sealed class MiabiEnvironmentResource(
    string name,
    string server,
    string workspace,
    ParameterResource apiToken) : Resource(name), IComputeEnvironmentResource
{
    /// <summary>Gets the Miabi control-plane URL.</summary>
    public string Server { get; } = server;

    /// <summary>Gets the Miabi workspace name.</summary>
    public string Workspace { get; } = workspace;

    /// <summary>Gets the secret API-token parameter.</summary>
    public ParameterResource ApiToken { get; } = apiToken;

    ReferenceExpression IComputeEnvironmentResource.GetEndpointPropertyExpression(
        EndpointReferenceExpression endpointReferenceExpression) =>
        throw new NotSupportedException(
            "Miabi's current declarative schema does not expose deterministic " +
            "application-to-application endpoint references.");

    ReferenceExpression IComputeEnvironmentResource.GetHostAddressExpression(
        EndpointReference endpointReference) =>
        throw new NotSupportedException(
            "Miabi's current declarative schema does not expose deterministic " +
            "application-to-application host references.");
}
