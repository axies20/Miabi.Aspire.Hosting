# Miabi hosting integration for Aspire

This integration publishes an Aspire application model as `miabi.io/v1`
manifests and deploys it to an existing remote Miabi workspace. It does not run
or install Miabi locally.

## AppHost configuration

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var miabiToken = builder.AddParameter("miabiToken", secret: true);
var miabiServer = builder.Configuration["Miabi:Server"]
                  ?? throw new InvalidOperationException("Miabi:Server is required.");
var miabiWorkspace = builder.Configuration["Miabi:Workspace"]
                     ?? throw new InvalidOperationException("Miabi:Workspace is required.");
var registryServer = builder.Configuration["Miabi:Registry"]
                     ?? throw new InvalidOperationException("Miabi:Registry is required.");
builder.AddProject<Projects.MyApplication>("app")
    .WithExternalHttpEndpoints();

var miabi = builder.AddMiabiEnvironment(
    "production",
    miabiServer,
    miabiWorkspace,
    miabiToken)
    .WithMiabiContainerRegistry(registryServer);

if (builder.Configuration["Miabi:CertificateAuthority"] is { Length: > 0 } ca)
{
    miabi.WithMiabiCertificateAuthority(ca);
}

// Development/homelab escape hatch only:
if (bool.TryParse(
        builder.Configuration["Miabi:InsecureSkipTlsVerify"],
        out var insecureSkipTlsVerify) && insecureSkipTlsVerify)
{
    miabi.WithMiabiInsecureSkipTlsVerify();
}

builder.Build().Run();
```

## Required settings

Set these values in the environment that runs the Aspire CLI:

```shell
export MIABI__SERVER="https://miabi.example.com"
export MIABI__WORKSPACE="production"
export MIABI__REGISTRY="registry.example.com"
export PARAMETERS__MIABITOKEN="mb_your_workspace_api_token"
```

- `MIABI__SERVER` is the public URL of the remote Miabi control plane.
- `MIABI__WORKSPACE` is the target workspace name or handle.
- `MIABI__REGISTRY` is a registry reachable by both the deployment machine and
  the remote Miabi node. For Miabi's built-in registry this is normally the
  configured registry hostname.
- `PARAMETERS__MIABITOKEN` is a secret workspace API token with permissions to
  inspect the workspace, manage Vault secrets, and apply/delete resources.
- `MIABI__CERTIFICATEAUTHORITY` is optional in the example AppHost and points to
  a PEM CA bundle on the deployment machine. It is forwarded to Miabi CLI as
  `MIABI_CA`.
- `MIABI__INSECURESKIPTLSVERIFY=true` is optional in the example AppHost and
  disables Miabi CLI certificate verification. Use it only for development;
  prefer `MIABI__CERTIFICATEAUTHORITY`.
- `MIABI_CLI_PATH` is optional and points to the `miabi` executable when it is
  not available on `PATH`.

The integration registers Miabi's registry as Aspire's default image target and
authenticates the selected Docker or Podman runtime automatically before the
standard Aspire push step. The token is passed through the runtime API rather
than as a command-line argument.

The token must be bound to the target workspace, whose role must be Developer
or higher. For this complete workflow, grant `read`, `write`, and `deploy`
scopes (or `*`). Miabi also accepts `deploy` for a built-in registry push. A
registry-only token cannot run `whoami`, upload Vault secrets, or apply
manifests.

## Publish and deploy

Install the current [Miabi CLI](https://github.com/miabi-io/cli/releases), then
run:

```shell
aspire publish --project examples/Miabi.Aspire.Hosting.AppHost/Miabi.Aspire.Hosting.AppHost.csproj --non-interactive
aspire deploy --project examples/Miabi.Aspire.Hosting.AppHost/Miabi.Aspire.Hosting.AppHost.csproj --non-interactive
```

Deployment performs the following operations:

1. The integration authenticates Aspire's Docker or Podman runtime to the Miabi
   registry using the workspace name and API token.
2. Aspire builds and pushes application images to
   `MIABI__REGISTRY/MIABI__WORKSPACE`.
3. The integration verifies the remote token with `miabi whoami`.
4. Secret parameters mapped with `WithMiabiSecret` are uploaded to Miabi Vault.
5. Miabi validates the generated manifest using `apply --dry-run` and then
   applies it to `MIABI__WORKSPACE`.

To delete only resources described by the generated deployment manifest:

```shell
aspire destroy --project examples/Miabi.Aspire.Hosting.AppHost/Miabi.Aspire.Hosting.AppHost.csproj --non-interactive
```

## Supported mapping

- container and containerizable project resources to Miabi Applications;
- endpoint target ports and external HTTP endpoints;
- named volumes;
- literal environment variables;
- secret parameters through Miabi Vault;
- custom domains and routes through `WithMiabiDomain`.

`WithPrune()` remains opt-in because the remote workspace may contain resources
that are not managed by this Aspire application.

## Current limitations

- Miabi, the workspace, DNS, and the target registry must already exist.
- The selected Docker or Podman runtime must be installed and running on the
  deployment machine.
- Managed database and cache resources are not translated yet.
- General Aspire service references are rejected when they cannot be converted
  to deterministic remote addresses.
- Only named Docker volumes are supported; bind mounts are rejected.
- Non-project endpoints need an explicit target port.
