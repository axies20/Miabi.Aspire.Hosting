# Miabi hosting integration for Aspire

This experimental integration turns the Aspire application model into Miabi
declarative resources and adds `publish`, `deploy`, and `destroy` steps to the
Aspire CLI.

It targets an existing Miabi workspace. It does not install Miabi on a remote
server, configure DNS, or provision a container registry.

## Blazor example

The included example models Blazor as an Aspire project. The application does
not need a Dockerfile, `PublishAsDockerFile()`, a fixed host port, or Miabi-only
environment variables:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var miabiToken = builder.AddParameter("miabi-token", secret: true);

builder.AddProject<Projects.Miabi_Aspire_Hosting_Blazor>("blazor")
    .WithMiabiDomain(
        builder.Configuration["Miabi:AppDomain"] ?? "blazor.localhost",
        tls: builder.Configuration["Miabi:RouteTls"] ?? "off");

builder.AddMiabiEnvironment(
    "production",
    builder.Configuration["Miabi:Server"] ?? "http://localhost:9000",
    builder.Configuration["Miabi:Workspace"] ?? "local",
    miabiToken);

builder.Build().Run();
```

Aspire supplies the project image and endpoint metadata during publish. The
Miabi target converts them to an Application, Domain, and Route.

`blazor.localhost` is the HTTP-only development default. For production, set a
real DNS name and enable ACME:

```text
Miabi__AppDomain=app.example.com
Miabi__RouteTls=acme
```

## Try it locally

Requirements:

- Docker Desktop with Linux containers;
- .NET SDK and the Aspire CLI;
- ports `80`, `5000`, and `9000` available.

Start the local Miabi control plane and Goma gateway:

```powershell
.\scripts\start-miabi-local.ps1 -Pull
```

Open `http://localhost:9000` and sign in:

```text
admin@example.com
MiabiLocal2026!
```

Create a workspace named `local`, create an API token in that workspace, then
run:

```powershell
.\scripts\deploy-example-local.ps1
```

The script installs the checksum-verified Miabi CLI into `tools/miabi`, asks for
the token without saving it, and runs `aspire deploy`.

After deployment, open:

```text
http://blazor.localhost
```

Names under `.localhost` resolve to the local machine in modern browsers, so a
hosts-file entry is normally unnecessary. Public DNS verification cannot
validate `blazor.localhost`, so the local deployment script uses its
platform-admin API token to force-verify this development-only domain after
applying the manifest. Real domains must use Miabi's normal DNS verification.

Stop the stack without deleting its database:

```powershell
.\scripts\stop-miabi-local.ps1
```

## Publish, deploy, and destroy

Generate an inspectable `miabi.yaml` without changing Miabi:

```shell
aspire publish
```

Build the project image, run Miabi's dry-run preflight, upload referenced
secrets, and apply the manifest:

```shell
aspire deploy
```

Delete only resources recorded by this deployment:

```shell
aspire destroy
```

The local example runs a registry on `localhost:5000`. Aspire pushes the project
image there before Miabi applies the manifest, and Miabi pulls the same image
through the shared Docker daemon. A remote Miabi server needs a registry that is
reachable from its deployment nodes; CI must publish images there before
applying or committing the generated manifest.

Miabi GitOps reconciles declarative manifests from Git; it does not build or
push application images by itself.

## Supported mapping

- container and containerizable project resources to Miabi Applications;
- Aspire endpoint target ports to Miabi ports;
- named volumes to Miabi Volumes;
- literal environment variables;
- secret parameters to Miabi Vault references;
- explicit `WithMiabiDomain` annotations to Domain and Route resources.

Secrets are supplied to the Miabi CLI through stdin or its process environment.
Their values are not written to `miabi.yaml`, command arguments, logs, or
deployment state.

## Current limitations

- Miabi and the target workspace must already exist.
- Registry push and Git commit/pull-request automation are not implemented.
- Managed database and cache resources are not translated yet.
- General Aspire service references are rejected when they cannot be translated
  safely to deterministic Miabi addresses.
- Only named Docker volumes are supported; bind mounts are rejected.
- Non-project endpoints need an explicit target port.
- `WithPrune()` is opt-in because a workspace may contain unrelated resources.
