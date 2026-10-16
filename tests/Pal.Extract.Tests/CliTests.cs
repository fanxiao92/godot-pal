using System.Diagnostics;
using Xunit;

namespace Pal.Extract.Tests;

public class CliTests
{
    [Theory]
    [InlineData("--help")]
    [InlineData()]
    public async Task Help_describes_the_tool_and_exits_successfully(params string[] arguments)
    {
        var result = await RunCli(arguments);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("pal-extract", result.Stdout);
        Assert.Contains("Usage:", result.Stdout);
        Assert.Contains("--help", result.Stdout);
        Assert.Contains("not implemented", result.Stdout);
        Assert.Empty(result.Stderr);
    }

    [Theory]
    [InlineData("--unknown")]
    [InlineData("--help", "--unknown")]
    public async Task Unsupported_arguments_report_an_error_and_fail(params string[] arguments)
    {
        var result = await RunCli(arguments);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Unsupported arguments:", result.Stderr);
        Assert.Contains("--unknown", result.Stderr);
        Assert.Contains("--help", result.Stderr);
        Assert.Empty(result.Stdout);
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunCli(params string[] arguments)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"pal-cli-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var start = new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
                WorkingDirectory = directory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "cli", "Pal.Extract.dll"));
            foreach (var argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            using var process = Process.Start(start) ?? throw new InvalidOperationException("CLI did not start.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                throw new TimeoutException("CLI did not exit within 15 seconds.");
            }

            return (process.ExitCode, await stdout, await stderr);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
