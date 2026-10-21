using System.Diagnostics;
using Vespertan.VisualStudio.AgentProxy.Client;
using Xunit;

namespace Vespertan.VisualStudio.AgentProxy.Client.Tests;

public sealed class CliProcessTests
{
    [Theory]
    [InlineData("--wait", "abc")]
    [InlineData("--wait", "1")]
    [InlineData("--paramsJson", "{\"wait\":\"true\"}")]
    [InlineData("--paramsJson", "{\"wait\":null}")]
    [InlineData("--paramsJson", "{\"wait\":[]}")]
    [InlineData("--paramsJson", "{\"wait\":{}}")]
    public async Task InvalidWaitReturnsParameterErrorBeforeInstanceDiscovery(string option, string value)
    {
        var result = await RunAsync(option, value);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("wait must be a boolean (true or false).", result.Error.Trim());
        Assert.Empty(result.Output);
    }

    [Theory]
    [InlineData("--wait", "true")]
    [InlineData("--wait", "false")]
    [InlineData("--paramsJson", "{\"wait\":true}")]
    [InlineData("--paramsJson", "{\"wait\":false}")]
    [InlineData("--paramsJson", "{}")]
    public async Task ValidOrOmittedWaitProceedsToInstanceDiscovery(string option, string value)
    {
        var result = await RunAsync(option, value);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("was not found", result.Error);
        Assert.Empty(result.Output);
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(string option, string value)
    {
        // Run the linked production entry point without a VS instance or a signed tool build.
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[] { typeof(AgentCommandClient).Assembly.Location, "--pid", "2147483647", "status", option, value })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            return (process.ExitCode, await output, await error);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }
}
