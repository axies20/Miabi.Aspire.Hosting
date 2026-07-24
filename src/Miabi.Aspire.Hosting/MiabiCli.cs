using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Miabi.Aspire.Hosting;

internal sealed class MiabiCli(ILogger logger)
{
    public async Task RunAsync(
        MiabiEnvironmentResource environment,
        string token,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        string? standardInput,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("MIABI_CLI_PATH") ?? "miabi",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput is not null,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.Environment["MIABI_SERVER"] = environment.Server;
        startInfo.Environment["MIABI_TOKEN"] = token;
        startInfo.ArgumentList.Add("--workspace");
        startInfo.ArgumentList.Add(environment.Workspace);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("The Miabi CLI could not be started.");
            }
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new InvalidOperationException(
                "Miabi CLI was not found. Install it from https://github.com/miabi-io/miabi-cli.", exception);
        }

        if (standardInput is not null)
        {
            await process.StandardInput.WriteAsync(standardInput.AsMemory(), timeoutSource.Token);
            process.StandardInput.Close();
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(timeoutSource.Token);
        var errorTask = process.StandardError.ReadToEndAsync(timeoutSource.Token);
        await process.WaitForExitAsync(timeoutSource.Token);
        var output = await outputTask;
        var error = await errorTask;

        if (!string.IsNullOrWhiteSpace(output))
        {
            logger.LogInformation("{MiabiOutput}", output.Trim());
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Miabi CLI exited with code {process.ExitCode}: {Sanitize(error, token)}");
        }
    }

    private static string Sanitize(string value, string token) =>
        string.IsNullOrEmpty(token)
            ? value.Trim()
            : value.Replace(token, "***", StringComparison.Ordinal).Trim();
}
