using Aspire.Hosting.Pipelines;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Miabi.Aspire.Hosting;

internal static class MiabiPipelineSteps
{
    public static IEnumerable<PipelineStep> Create(MiabiEnvironmentResource environment)
    {
        var publish = new PipelineStep
        {
            Name = $"miabi-publish-{environment.Name}",
            Description = $"Generates Miabi manifests for '{environment.Name}'",
            Resource = environment,
            Action = async context =>
            {
                var output = context.Services.GetRequiredService<IPipelineOutputService>();
                var outputDirectory = output.GetOutputDirectory(environment);
                Directory.CreateDirectory(outputDirectory);
                var generator = new MiabiManifestGenerator();
                var manifest = await generator.GenerateAsync(
                    context.Model,
                    context.ExecutionContext,
                    context.Services,
                    context.Logger,
                    context.CancellationToken);
                await File.WriteAllTextAsync(
                    Path.Combine(outputDirectory, "miabi.yaml"),
                    manifest,
                    context.CancellationToken);
            }
        };
        publish.DependsOn(WellKnownPipelineSteps.Build);
        publish.RequiredBy(WellKnownPipelineSteps.Publish);

        var preflight = new PipelineStep
        {
            Name = $"miabi-preflight-{environment.Name}",
            Description = $"Validates Miabi authentication for '{environment.Workspace}'",
            Resource = environment,
            Action = async context =>
            {
                var options = GetOptions(environment);
                var token = await GetTokenAsync(environment, context.CancellationToken);
                await new MiabiCli(context.Logger).RunAsync(
                    environment, token, ["whoami"], options.Timeout, null, context.CancellationToken);
            }
        };
        preflight.DependsOn(WellKnownPipelineSteps.DeployPrereq);

        var deploy = new PipelineStep
        {
            Name = $"miabi-deploy-{environment.Name}",
            Description = $"Applies Aspire resources to Miabi workspace '{environment.Workspace}'",
            Resource = environment,
            Action = async context =>
            {
                var options = GetOptions(environment);
                var token = await GetTokenAsync(environment, context.CancellationToken);
                var cli = new MiabiCli(context.Logger);
                await UploadSecretsAsync(context, environment, token, cli, options);
                var output = context.Services.GetRequiredService<IPipelineOutputService>();
                var manifestPath = Path.Combine(output.GetOutputDirectory(environment), "miabi.yaml");
                if (!File.Exists(manifestPath))
                {
                    throw new InvalidOperationException(
                        $"Miabi manifest '{manifestPath}' does not exist. Run the publish step first.");
                }

                await cli.RunAsync(
                    environment, token, ["apply", "-f", manifestPath, "--dry-run"],
                    options.Timeout, null, context.CancellationToken);
                var arguments = new List<string> { "apply", "-f", manifestPath };
                if (options.Prune)
                {
                    arguments.Add("--prune");
                }
                await cli.RunAsync(
                    environment, token, arguments, options.Timeout, null, context.CancellationToken);
            }
        };
        deploy.DependsOn(preflight);
        deploy.DependsOn(publish);
        deploy.DependsOn(WellKnownPipelineSteps.Push);
        deploy.RequiredBy(WellKnownPipelineSteps.Deploy);

        var destroy = new PipelineStep
        {
            Name = $"miabi-destroy-{environment.Name}",
            Description = $"Deletes Aspire-managed resources from '{environment.Workspace}'",
            Resource = environment,
            Action = async context =>
            {
                var options = GetOptions(environment);
                var token = await GetTokenAsync(environment, context.CancellationToken);
                var output = context.Services.GetRequiredService<IPipelineOutputService>();
                var manifestPath = Path.Combine(output.GetOutputDirectory(environment), "miabi.yaml");
                if (!File.Exists(manifestPath))
                {
                    throw new InvalidOperationException(
                        $"Cannot safely destroy Miabi resources because '{manifestPath}' is missing.");
                }
                await new MiabiCli(context.Logger).RunAsync(
                    environment, token, ["delete", "-f", manifestPath, "--dry-run"],
                    options.Timeout, null, context.CancellationToken);
                await new MiabiCli(context.Logger).RunAsync(
                    environment, token, ["delete", "-f", manifestPath],
                    options.Timeout, null, context.CancellationToken);
            }
        };
        destroy.DependsOn(WellKnownPipelineSteps.DestroyPrereq);
        destroy.RequiredBy(WellKnownPipelineSteps.Destroy);

        return [publish, preflight, deploy, destroy];
    }

    private static MiabiDeploymentOptionsAnnotation GetOptions(MiabiEnvironmentResource environment) =>
        environment.Annotations.OfType<MiabiDeploymentOptionsAnnotation>().LastOrDefault()
        ?? new MiabiDeploymentOptionsAnnotation(false, TimeSpan.FromMinutes(10));

    private static async Task<string> GetTokenAsync(
        MiabiEnvironmentResource environment,
        CancellationToken cancellationToken)
    {
        var token = await environment.ApiToken.GetValueAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException(
                $"Miabi API token parameter '{environment.ApiToken.Name}' has no value.");
        }
        return token;
    }

    private static async Task UploadSecretsAsync(
        PipelineStepContext context,
        MiabiEnvironmentResource environment,
        string token,
        MiabiCli cli,
        MiabiDeploymentOptionsAnnotation options)
    {
        var secrets = context.Model.Resources
            .SelectMany(resource => resource.Annotations.OfType<MiabiSecretAnnotation>())
            .GroupBy(secret => secret.SecretName, StringComparer.Ordinal)
            .Select(group => group.Last());

        foreach (var secret in secrets)
        {
            var value = await secret.Parameter.GetValueAsync(context.CancellationToken);
            if (string.IsNullOrEmpty(value))
            {
                throw new InvalidOperationException(
                    $"Miabi secret parameter '{secret.Parameter.Name}' has no value.");
            }
            context.Logger.LogInformation("Uploading Miabi Vault secret {SecretName}", secret.SecretName);
            await cli.RunAsync(
                environment,
                token,
                ["secrets", "set", secret.SecretName, "--from-file", "-"],
                options.Timeout,
                value,
                context.CancellationToken);
        }
    }
}
