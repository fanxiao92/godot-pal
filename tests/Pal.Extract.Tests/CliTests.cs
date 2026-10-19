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
            return await CliProcess.Run(directory, arguments);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
