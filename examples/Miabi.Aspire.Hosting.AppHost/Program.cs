var builder = DistributedApplication.CreateBuilder(args);

var miabiToken = builder.AddParameter("miabiToken", secret: true);
var miabiServer = builder.Configuration["Miabi:Server"]
                  ?? throw new InvalidOperationException("Miabi:Server is required.");
var miabiWorkspace = builder.Configuration["Miabi:Workspace"]
                     ?? throw new InvalidOperationException("Miabi:Workspace is required.");
var registryServer = builder.Configuration["Miabi:Registry"]
                     ?? throw new InvalidOperationException("Miabi:Registry is required.");
builder.AddProject<Projects.Miabi_Aspire_Hosting_Blazor>("blazor")
    .WithExternalHttpEndpoints();

var miabi = builder.AddMiabiEnvironment(
    "production",
    miabiServer,
    miabiWorkspace,
    miabiToken)
    .WithMiabiContainerRegistry(registryServer);

if (builder.Configuration["Miabi:CertificateAuthority"] is { Length: > 0 } certificateAuthority)
{
    miabi.WithMiabiCertificateAuthority(certificateAuthority);
}

if (bool.TryParse(
        builder.Configuration["Miabi:InsecureSkipTlsVerify"],
        out var insecureSkipTlsVerify) && insecureSkipTlsVerify)
{
    miabi.WithMiabiInsecureSkipTlsVerify();
}

builder.Build().Run();
