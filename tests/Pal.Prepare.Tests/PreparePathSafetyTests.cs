using System.Diagnostics;
using Pal.TestFixtures;
using Xunit;

namespace Pal.Prepare.Tests;

public sealed partial class PrepareProcessTests
{
    [Theory]
    [InlineData("missing")]
    [InlineData("empty")]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    [InlineData("timeout")]
    public async Task Argument_errors_are_noninteractive_exit_two(string kind)
    {
        string[] args = kind switch
        {
            "missing" => [],
            "empty" => ["-InputPath", "", "-OutputPath", output],
            "duplicate" => ["-InputPath", Input, "-OutputPath", output, "-inputpath", Input],
            "unknown" => ["-InputPath", Input, "-OutputPath", output, "-Overwrite", "true"],
            _ => ["-InputPath", Input, "-OutputPath", output, "-TimeoutSeconds", "0"]
        };
        var result = await Run("pwsh", ["-NoProfile", "-NonInteractive", "-File", Path.Combine(repo, "scripts", "Prepare-GameScene.ps1"), .. args]);
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("argument error", result.Text);
        Assert.False(Directory.Exists(output));
    }
    [Theory]
    [InlineData("outside-assets")]
    [InlineData("nonempty")]
    [InlineData("target-file")]
    [InlineData("output-inside-input")]
    [InlineData("input-inside-output")]
    [InlineData("same-path")]
    public async Task Unsafe_destinations_never_overwrite_or_change_the_source(string kind)
    {
        string inputArg = Input, outputArg = output;
        switch (kind)
        {
            case "outside-assets": outputArg = Path.Combine(root, "outside"); break;
            case "nonempty": Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "keep.txt"), "keep"); break;
            case "target-file": Directory.CreateDirectory(Path.GetDirectoryName(output)!); File.WriteAllText(output, "keep"); break;
            case "output-inside-input": inputArg = Path.GetDirectoryName(output)!; Directory.CreateDirectory(inputArg); break;
            case "input-inside-output": outputArg = Path.GetDirectoryName(output)!; inputArg = output; Directory.CreateDirectory(inputArg); break;
            case "same-path": inputArg = output; Directory.CreateDirectory(inputArg); break;
        }
        var before = Snapshot();
        var result = await RunPrepare(inputArg, outputArg);
        Assert.Equal(1, result.ExitCode);
        Assert.Equal(before, Snapshot());
        if (kind == "nonempty") Assert.Equal("keep", File.ReadAllText(Path.Combine(output, "keep.txt")));
        if (kind == "target-file") { Assert.Equal("keep", File.ReadAllText(output)); File.Delete(output); }
        if (kind.Contains("inside") || kind == "same-path") Assert.Contains("overlap", result.Text);
    }
    [Theory]
    [InlineData("input-ancestor")]
    [InlineData("input-descendant")]
    [InlineData("output-ancestor")]
    public async Task Links_in_ancestors_or_recursively_in_package_are_rejected(string kind)
    {
        string link = kind == "output-ancestor" ? output : kind == "input-descendant" ? Path.Combine(Input, "linked") : Path.Combine(root, "linked");
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        await CreateDirectoryLink(link, kind == "input-descendant" ? root : Input);
        try
        {
            var result = await RunPrepare(kind == "input-ancestor" ? Path.Combine(link, "tiles") : Input, kind == "output-ancestor" ? Path.Combine(link, "new") : output);
            Assert.Equal(1, result.ExitCode);
            Assert.Contains("link", result.Text, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(Path.Combine(output, "Scenes", "Scene.tscn")));
        }
        finally { Directory.Delete(link); }
    }
    [Fact]
    public async Task Project_contained_input_is_rejected_unchanged_before_Godot_can_start()
    {
        string projectInput = output + "-input";
        try
        {
            // TEMP and the checkout can be on different drives in CI. Author the
            // artificial package in place instead of moving the shared temp input.
            M1Bundle.Write(projectInput);
            var before = Snapshot(projectInput);
            // A deliberately absent executable makes even the RED run safe: no engine can
            // touch this project-contained package. The location diagnostic must win over
            // executable resolution, proving rejection happens before any Godot launch.
            string unavailableGodot = Path.Combine(root, "godot-must-not-start.exe");
            var result = await Run("pwsh", "-NoProfile", "-NonInteractive", "-File", Path.Combine(repo, "scripts", "Prepare-GameScene.ps1"),
                "-InputPath", Path.Combine(projectInput, "..", Path.GetFileName(projectInput)),
                "-OutputPath", output, "-GodotPath", unavailableGodot);
            Assert.Equal(1, result.ExitCode);
            Assert.Equal(before, Snapshot(projectInput));
            Assert.False(Directory.Exists(output));
            Assert.Contains("InputPath must be outside src/Pal.Game", result.Text);
            Assert.DoesNotContain(unavailableGodot, result.Text);
            Assert.DoesNotContain("Prepared native Godot scene", result.Text);
        }
        finally { if (Directory.Exists(projectInput)) Directory.Delete(projectInput, true); }
    }
    [Fact]
    public async Task Missing_input_is_data_error_not_argument_error()
    {
        var result = await RunPrepare(Path.Combine(root, "absent"), output);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("existing complete M1 package", result.Text);
    }
    private Task<Result> RunPrepare(string inputPath, string outputPath) => Run("pwsh", "-NoProfile", "-NonInteractive", "-File", Path.Combine(repo, "scripts", "Prepare-GameScene.ps1"), "-InputPath", inputPath, "-OutputPath", outputPath, "-GodotPath", Godot);
    private static async Task CreateDirectoryLink(string link, string target)
    {
        if (!OperatingSystem.IsWindows()) { Directory.CreateSymbolicLink(link, target); return; }
        var result = await Run("cmd.exe", "/c", "mklink", "/J", link, target);
        Assert.True(result.ExitCode == 0, result.Text);
    }
}
