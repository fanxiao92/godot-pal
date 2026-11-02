using System.Security.Cryptography;
using Xunit;

namespace Pal.Prepare.Tests;

// Process-only contracts: the numeric converter itself is covered by Game direct public Prepare tests.
public sealed class BattleDataPrepareProcessTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "pal-battle-data-process-" + Guid.NewGuid().ToString("N"));
    private readonly string repo;
    private readonly string output;
    private string Input => Path.Combine(root, "numeric input");
    private string Source => Path.Combine(Input, "battle-data.json");
    private string Project => Path.Combine(repo, "src", "Pal.Game");
    private static string Godot => Environment.GetEnvironmentVariable("PAL_TEST_GODOT") ?? throw new InvalidOperationException("Set PAL_TEST_GODOT.");
    private const string Marker = "PAL_PREPARE_BATTLE_DATA:BattleContent.tres";

    public BattleDataPrepareProcessTests()
    {
        repo = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(repo, "GodotPal.sln"))) repo = Directory.GetParent(repo)!.FullName;
        output = Path.Combine(Project, "Assets", "PrepareTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Input);
        File.Copy(Path.Combine(repo, "tests", "Fixtures", "ModernBattleData", "battle-data.json"), Source);
    }

    [Fact]
    public async Task Normal_wrapper_runs_editor_and_publishes_native_only_without_changing_source_or_overwriting()
    {
        byte[] before = File.ReadAllBytes(Source);
        var prepared = await Wrapper("-InputPath", Input, "-OutputPath", output, "-GodotPath", Godot);
        Assert.True(prepared.ExitCode == 0, prepared.Text);
        Assert.Contains(Marker, prepared.Text);
        Assert.Contains("Prepared native Godot battle data: BattleContent.tres", prepared.Text);
        string destination = Path.Combine(output, "BattleContent.tres");
        Assert.True(new FileInfo(destination).Length > 0);
        Assert.Single(Directory.GetFileSystemEntries(output));
        Assert.Equal(before, File.ReadAllBytes(Source));
        byte[] native = File.ReadAllBytes(destination);
        var collision = await Wrapper("-InputPath", Input, "-OutputPath", output, "-GodotPath", Godot);
        Assert.Equal(1, collision.ExitCode);
        Assert.Contains("new or empty", collision.Text);
        Assert.DoesNotContain(Marker, collision.Text);
        Assert.Equal(native, File.ReadAllBytes(destination));
    }

    [Fact]
    public async Task Normal_editor_entry_accepts_exact_arguments_and_reports_native_success()
    {
        var result = await Editor("--battle-input", Input, "--battle-output", output);
        Assert.True(result.ExitCode == 0, result.Text);
        Assert.Contains(Marker, result.Text);
        Assert.True(new FileInfo(Path.Combine(output, "BattleContent.tres")).Length > 0);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("reordered")]
    [InlineData("unknown")]
    public async Task Editor_rejects_invalid_argv_with_exit_one_and_no_success_marker(string kind)
    {
        string[] args = kind switch
        {
            "missing" => ["--battle-input", Input],
            "extra" => ["--battle-input", Input, "--battle-output", output, "--extra", "value"],
            "reordered" => ["--battle-output", output, "--battle-input", Input],
            _ => ["--unknown", Input, "--battle-output", output]
        };
        var result = await Editor(args);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Battle data native import failed: invalid battle-data prepare arguments", result.Text);
        Assert.DoesNotContain(Marker, result.Text);
        Assert.False(Directory.Exists(output));
    }

    [Theory]
    [InlineData("syntax", "battle-data.json")]
    [InlineData("field", "formatVersion")]
    public async Task Editor_propagates_strict_diagnostic_and_preserves_source_and_existing_empty_output(string kind, string diagnostic)
    {
        File.WriteAllText(Source, kind == "syntax" ? "{" : "{}");
        byte[] before = File.ReadAllBytes(Source);
        Directory.CreateDirectory(output);
        var result = await Editor("--battle-input", Input, "--battle-output", output);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Battle data native import failed:", result.Text);
        Assert.Contains(diagnostic, result.Text);
        Assert.DoesNotContain(Marker, result.Text);
        Assert.Equal(before, File.ReadAllBytes(Source));
        Assert.True(Directory.Exists(output));
        Assert.Empty(Directory.GetFileSystemEntries(output));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    [InlineData("timeout")]
    public async Task Wrapper_rejects_invalid_arguments_with_exit_two_before_tool_resolution(string kind)
    {
        string[] args = kind switch
        {
            "missing" => ["-InputPath", Input],
            "duplicate" => ["-InputPath", Input, "-InputPath", Input, "-OutputPath", output],
            "unknown" => ["-InputPath", Input, "-OutputPath", output, "-Unknown", "value"],
            _ => ["-InputPath", Input, "-OutputPath", output, "-TimeoutSeconds", "601"]
        };
        var result = await Wrapper(args);
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Prepare-BattleData argument error:", result.Text);
        Assert.DoesNotContain(Marker, result.Text);
        Assert.False(Directory.Exists(output));
    }

    [Theory]
    [InlineData("extra")]
    [InlineData("directory")]
    [InlineData("missing")]
    [InlineData("output-file")]
    [InlineData("nonempty")]
    [InlineData("outside")]
    public async Task Wrapper_preflights_paths_before_starting_unavailable_tool_and_preserves_input(string kind)
    {
        string selectedOutput = output;
        switch (kind)
        {
            case "extra": File.WriteAllText(Path.Combine(Input, "extra"), "keep"); break;
            case "directory": Directory.CreateDirectory(Path.Combine(Input, "extra")); break;
            case "missing": File.Delete(Source); break;
            case "output-file": Directory.CreateDirectory(Path.GetDirectoryName(output)!); File.WriteAllText(output, "keep"); break;
            case "nonempty": Directory.CreateDirectory(Path.Combine(output, "keep")); break;
            case "outside": selectedOutput = Path.Combine(root, "outside"); break;
        }
        string[] before = Snapshot(Input);
        string unavailable = Path.Combine(root, "must-not-start.exe");
        var result = await Wrapper("-InputPath", Input, "-OutputPath", selectedOutput, "-GodotPath", unavailable);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Prepare-BattleData failed:", result.Text);
        Assert.DoesNotContain(unavailable, result.Text);
        Assert.DoesNotContain(Marker, result.Text);
        Assert.Equal(before, Snapshot(Input));
        if (kind == "output-file") Assert.Equal("keep", File.ReadAllText(output));
        else if (kind == "nonempty") Assert.True(Directory.Exists(Path.Combine(output, "keep")));
        else Assert.False(Directory.Exists(selectedOutput));
    }

    private Task<PrepareProcessTests.Result> Editor(params string[] args) => PrepareProcessTests.Run(Godot,
        ["--headless", "--path", Project, "res://Editor/PrepareBattleData.tscn", "--", .. args]);
    private Task<PrepareProcessTests.Result> Wrapper(params string[] args) => PrepareProcessTests.Run("pwsh",
        ["-NoProfile", "-NonInteractive", "-File", Path.Combine(repo, "scripts", "Prepare-BattleData.ps1"), .. args]);
    private static string[] Snapshot(string directory) => Directory.GetFileSystemEntries(directory, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal).Select(path => Path.GetRelativePath(directory, path) + ":" +
            (File.Exists(path) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) : "directory")).ToArray();
    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
        if (File.Exists(output)) File.Delete(output);
        if (Directory.Exists(output)) Directory.Delete(output, true);
    }
}
