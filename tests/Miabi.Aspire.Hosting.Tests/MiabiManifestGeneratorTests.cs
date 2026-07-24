using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Miabi.Aspire.Hosting.Tests;

public sealed class MiabiManifestGeneratorTests
{
    [Fact]
    public async Task GeneratesProjectImageAndPortWithoutConsumerPublishingConfiguration()
    {
        var builder = DistributedApplication.CreateBuilder(
            new DistributedApplicationOptions
            {
                Args = []
            });
        builder.AddResource(new ProjectResource("web"))
            .WithHttpEndpoint(name: "http")
            .WithEnvironment(
                "HTTP_PORTS",
                "{web.bindings.http.targetPort}");

        using var app = builder.Build();
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        var yaml = await new MiabiManifestGenerator().GenerateAsync(
            model,
            new DistributedApplicationExecutionContext(DistributedApplicationOperation.Publish),
            app.Services,
            NullLogger.Instance,
            CancellationToken.None);

        Assert.Contains("image: web", yaml);
        Assert.Contains("tag: latest", yaml);
        Assert.Contains("container: 8080", yaml);
        Assert.Contains("HTTP_PORTS: 8080", yaml);
        Assert.DoesNotContain("bindings", yaml);
    }

    [Fact]
    public async Task GeneratesRemoteProjectImageWhenContainerRegistryIsConfigured()
    {
        var builder = DistributedApplication.CreateBuilder(
            new DistributedApplicationOptions
            {
                Args = []
            });
#pragma warning disable ASPIRECOMPUTE003
        var registry = builder.AddContainerRegistry("registry", "localhost:5000");
        builder.AddResource(new ProjectResource("web"))
            .WithContainerRegistry(registry);
#pragma warning restore ASPIRECOMPUTE003

        using var app = builder.Build();
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        var yaml = await new MiabiManifestGenerator().GenerateAsync(
            model,
            new DistributedApplicationExecutionContext(DistributedApplicationOperation.Publish),
            app.Services,
            NullLogger.Instance,
            CancellationToken.None);

        Assert.Contains("image: localhost:5000/web", yaml);
        Assert.Contains("tag: latest", yaml);
    }

    [Fact]
    public async Task GeneratesApplicationDomainRouteAndSecretReference()
    {
        var builder = DistributedApplication.CreateBuilder(
            new DistributedApplicationOptions
            {
                Args = []
            });
        var secret = builder.AddParameter("api-key", secret: true);
        builder.AddContainer("api", "ghcr.io/example/api", "1.2.3")
            .WithHttpEndpoint(targetPort: 8080, name: "http")
            .WithEnvironment("LOG_LEVEL", "info")
            .WithMiabiSecret("API_KEY", "api_key", secret)
            .WithMiabiDomain("api.example.com");

        using var app = builder.Build();
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        var yaml = await new MiabiManifestGenerator().GenerateAsync(
            model,
            new DistributedApplicationExecutionContext(DistributedApplicationOperation.Publish),
            app.Services,
            NullLogger.Instance,
            CancellationToken.None);

        Assert.Contains("kind: Application", yaml);
        Assert.Contains("image: ghcr.io/example/api", yaml);
        Assert.Contains("tag: 1.2.3", yaml);
        Assert.Contains("API_KEY: '{{ .secrets.api_key }}'", yaml);
        Assert.DoesNotContain("api-key", yaml);
        Assert.Contains("kind: Domain", yaml);
        Assert.Contains("name: api.example.com", yaml);
        Assert.Contains("kind: Route", yaml);
    }

    [Fact]
    public async Task GeneratesHttpRouteWithoutDisablingDomainCertificateMode()
    {
        var builder = DistributedApplication.CreateBuilder(
            new DistributedApplicationOptions
            {
                Args = []
            });
        builder.AddContainer("web", "nginx", "alpine")
            .WithHttpEndpoint(targetPort: 80, name: "http")
            .WithMiabiDomain("web.localhost", tls: "off");

        using var app = builder.Build();
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        var yaml = await new MiabiManifestGenerator().GenerateAsync(
            model,
            new DistributedApplicationExecutionContext(DistributedApplicationOperation.Publish),
            app.Services,
            NullLogger.Instance,
            CancellationToken.None);

        Assert.Contains("kind: Domain", yaml);
        Assert.Contains("tls: acme", yaml);
        Assert.Contains("kind: Route", yaml);
        Assert.Contains("tls: off", yaml);
    }

    [Theory]
    [InlineData("ghcr.io/org/app:v1", "ghcr.io/org/app", "v1")]
    [InlineData("redis:8", "redis", "8")]
    [InlineData("registry:5000/org/app", "registry:5000/org/app", null)]
    [InlineData("org/app@sha256:abc", "org/app@sha256:abc", null)]
    public void SplitsImageReference(string input, string repository, string? tag)
    {
        Assert.Equal((repository, tag), MiabiManifestGenerator.SplitImage(input));
    }
}
