using System.Diagnostics;
using Pal.TestFixtures;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace Pal.Prepare.Tests;

public sealed partial class PrepareProcessTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"pal-prepare-test-{Guid.NewGuid():N}");
    private readonly string output;
    private readonly string repo;
    private string Input => Path.Combine(root, "input");
    private static string Godot => Environment.GetEnvironmentVariable("PAL_TEST_GODOT") ?? throw new InvalidOperationException("Set PAL_TEST_GODOT to the Godot 4.7.2 mono console executable.");
    public PrepareProcessTests()
    {
        repo = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(repo, "GodotPal.sln"))) repo = Directory.GetParent(repo)!.FullName;
        output = Path.Combine(repo, "src", "Pal.Game", "Assets", "PrepareTests", Guid.NewGuid().ToString("N"));
        M1Bundle.Write(Input);
    }
    [Fact]
    public async Task Complete_modern_bundle_becomes_loadable_native_scene_without_changing_source()
    {
        foreach (string file in Directory.GetFiles(Input, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.ReadOnly);
        var before = Snapshot();
        var result = await Prepare();
        Assert.True(result.ExitCode == 0, result.Text);
        Assert.True(File.Exists(Path.Combine(output, "Scenes", "Scene.tscn")), result.Text);
        Assert.Empty(Directory.GetFiles(output, "*.json", SearchOption.AllDirectories));
        Assert.Equal(before, Snapshot());
        // Separate actual Godot process consumes only native output, with the input unavailable.
        Directory.Move(Input, Input + "-offline");
        var uri = "res://" + Path.GetRelativePath(Path.Combine(repo, "src", "Pal.Game"), output).Replace('\\', '/');
        var loaded = await Run(Godot, "--headless", "--path", Path.Combine(repo, "src", "Pal.Game"), "--script", Path.Combine(repo, "tests", "Fixtures", "VerifyNativeScene.gd"), "--", "--native-root", uri);
        Assert.True(loaded.ExitCode == 0, loaded.Text);
        Assert.DoesNotMatch(@"(?im)^\s*(ERROR:|SCRIPT ERROR:|Unhandled exception)", loaded.Text);
        Assert.Contains("PAL_NATIVE_VERIFIED", loaded.Text);
    }
    [Fact]
    public async Task Source_record_identity_must_match_selected_scene_before_any_output()
    {
        M1Bundle.Mutate(Input, "scene.json", n => n["source"]!["recordIndex"] = 6);
        var result = await Prepare();
        Assert.True(result.ExitCode == 1, result.Text);
        Assert.Contains("recordIndex", result.Text);
        Assert.False(Directory.Exists(output));
    }
    private string[] Snapshot() => Snapshot(Input);
    private static string[] Snapshot(string directory) => Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
        .Select(p => $"{Path.GetRelativePath(directory, p)}:{M1Bundle.Hash(File.ReadAllBytes(p))}:{File.GetLastWriteTimeUtc(p).Ticks}").ToArray();
    private Task<Result> Prepare(params string[] args) => Run("pwsh", ["-NoProfile", "-NonInteractive", "-File", Path.Combine(repo, "scripts", "Prepare-GameScene.ps1"), "-InputPath", Input, "-OutputPath", output, "-GodotPath", Godot, .. args]);
    internal static async Task<Result> Run(string file, params string[] args)
    {
        var info = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var p = Process.Start(info)!;
        var stdout = p.StandardOutput.ReadToEndAsync(); var stderr = p.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        try { await p.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { p.Kill(true); await p.WaitForExitAsync(); throw new TimeoutException($"{file} timed out (process tree killed)"); }
        return new(p.ExitCode, await stdout + "\n" + await stderr);
    }
    internal sealed record Result(int ExitCode, string Text);
    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(root, true);
        }
        if (Directory.Exists(output))
        {
            foreach (string file in Directory.GetFiles(output, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(output, true);
        }
    }
}
