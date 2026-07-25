var builder = DistributedApplication.CreateBuilder(args);

var miabiToken = builder.AddParameter("miabi-token", secret: true);
var registry = builder.AddContainerRegistry("local-registry", "localhost:5000");

builder.AddProject<Projects.Miabi_Aspire_Hosting_Blazor>("blazor")
    .WithContainerRegistry(registry)
    .WithExternalHttpEndpoints();

builder.AddMiabiEnvironment(
    "production",
    builder.Configuration["Miabi:Server"] ?? "http://localhost:9000",
    builder.Configuration["Miabi:Workspace"] ?? "local",
    miabiToken);

builder.Build().Run();
