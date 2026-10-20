using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace Pal.Game.Tests;

[TestSuite, RequireGodotRuntime]
public class MainProcessTests
{
    [TestCase(Timeout = 20000)]
    public async Task RealMainLoadsSelectedNativeSceneBeforeCoreReady()
    {
        using var fixture = new NativeMapFixture();
        fixture.Save();
        var result = await RunMain("--m2-resources", fixture.Root);
        AssertThat(result.ExitCode).IsEqual(0);
        AssertThat(result.Text.Contains("PAL_MAP_LOADED:", StringComparison.Ordinal)).IsTrue();
        AssertThat(result.Text.Contains("PAL_GAME_READY:Core ready", StringComparison.Ordinal)).IsTrue();
        AssertThat(result.Text.Contains("ERROR:", StringComparison.Ordinal)).IsFalse();
    }

    [TestCase(Timeout = 20000)]
    public async Task ShippedMainEntersTreeAndDrawsSelectedNativeSceneAtFixedViewport()
    {
        using var fixture = new NativeMapFixture();
        fixture.Save();
        var result = await Run(["--headless", "--path", ProjectSettings.GlobalizePath("res://"),
            "--script", "res://Tests/ObserveMain.gd", "--", "--m2-resources", fixture.Root]);
        Log(result);
        AssertThat(result.ExitCode).IsEqual(0);
        AssertThat(result.Text.Contains("PAL_MAIN_OBSERVED:392 sprites", StringComparison.Ordinal)).IsTrue();
        AssertThat(result.Text.Contains("ERROR:", StringComparison.Ordinal)).IsFalse();
    }

    [TestCase("external", Timeout = 20000)]
    [TestCase("escape", Timeout = 20000)]
    [TestCase("relative", Timeout = 20000)]
    [TestCase("prefix", Timeout = 20000)]
    [TestCase("double-slash", Timeout = 20000)]
    [TestCase("encoded", Timeout = 20000)]
    [TestCase("backslash", Timeout = 20000)]
    [TestCase("duplicate", Timeout = 20000)]
    [TestCase("missing", Timeout = 20000)]
    [TestCase("unknown", Timeout = 20000)]
    public async Task InvalidResourceArgumentsFailBeforeLoading(string kind)
    {
        using var fixture = new NativeMapFixture();
        fixture.Save();
        string[] args = kind switch
        {
            "external" => ["--m2-resources", ProjectSettings.GlobalizePath(fixture.Root)],
            "escape" => ["--m2-resources", fixture.Root + "/../"],
            "relative" => ["--m2-resources", "Assets"],
            "prefix" => ["--m2-resources", "res://AssetsOther"],
            "double-slash" => ["--m2-resources", "res://Assets//RuntimeTests"],
            "encoded" => ["--m2-resources", "res://Assets/%2e%2e"],
            "backslash" => ["--m2-resources", "res://Assets\\RuntimeTests"],
            "duplicate" => ["--m2-resources", fixture.Root, "--m2-resources", fixture.Root],
            "missing" => ["--m2-resources"],
            _ => ["--unknown", fixture.Root]
        };
        var result = await RunMain(args);
        AssertThat(result.ExitCode).IsEqual(1);
        AssertThat(result.Text.Contains("--m2-resources", StringComparison.Ordinal)).IsTrue();
        AssertThat(result.Text.Contains("PAL_GAME_READY", StringComparison.Ordinal)).IsFalse();
    }

    [TestCase("array", "BottomFrames", Timeout = 20000)]
    [TestCase("frame", "bottom frame", Timeout = 20000)]
    [TestCase("texture", "texture", Timeout = 20000)]
    [TestCase("dimensions", "dimensions", Timeout = 20000)]
    [TestCase("id", "MapId", Timeout = 20000)]
    [TestCase("duplicate-frame", "frame index", Timeout = 20000)]
    [TestCase("raw", "raw cell", Timeout = 20000)]
    [TestCase("wrong-root", "PalMapScene", Timeout = 20000)]
    [TestCase("wrong-packed", "PackedScene", Timeout = 20000)]
    public async Task BrokenNativeSceneFailsWithoutReady(string kind, string diagnostic)
    {
        using var fixture = new NativeMapFixture();
        var map = fixture.Scene.Data.Map;
        switch (kind)
        {
            case "array": map.BottomFrames = []; break;
            case "frame": map.BottomFrames[262] = 4000; break;
            case "texture": map.Tiles.Frames[1].Texture = null!; break;
            case "dimensions": map.Tiles.Frames[1].Width = 99; break;
            case "id": map.MapId = 99; break;
            case "duplicate-frame": map.Tiles.Frames[1].Index = 0; break;
            case "raw": map.RawCells[0] = -1; break;
        }
        if (kind == "wrong-root") { var wrong = new Node2D(); fixture.Save(wrong); wrong.Free(); }
        else if (kind == "wrong-packed")
            System.IO.File.WriteAllText(ProjectSettings.GlobalizePath(fixture.Root + "/Scenes/Scene.tscn"), "[gd_resource type=\"Resource\" format=3]\n\n[resource]\n");
        else fixture.Save();
        var result = await RunMain("--m2-resources", fixture.Root);
        AssertThat(result.ExitCode).IsEqual(1);
        AssertThat(result.Text.Contains(diagnostic, StringComparison.OrdinalIgnoreCase)).IsTrue();
        AssertThat(result.Text.Contains("PAL_GAME_READY", StringComparison.Ordinal)).IsFalse();
    }

    [TestCase(Timeout = 20000)]
    public async Task MissingDefaultNativeSceneFailsWithItsResourceLocation()
    {
        var result = await RunMain();
        AssertThat(result.ExitCode).IsEqual(1);
        AssertThat(result.Text.Contains("res://Assets/Scenes/Scene.tscn", StringComparison.Ordinal)).IsTrue();
        AssertThat(result.Text.Contains("PAL_GAME_READY", StringComparison.Ordinal)).IsFalse();
    }

    internal static async Task<ProcessResult> RunMain(params string[] arguments)
    {
        var result = await Run(["--headless", "--path", ProjectSettings.GlobalizePath("res://"), "--quit-after", "4", "--", .. arguments]);
        Log(result);
        return result;
    }

    private static void Log(ProcessResult result)
    {
        // Single-line prints avoid the adapter rc5 multiline-output serialization defect.
        GD.Print($"Child Godot exit={result.ExitCode}");
        foreach (string line in result.Text.Split('\n')) GD.Print("Child: " + line.TrimEnd('\r'));
    }

    internal static async Task<ProcessResult> Run(string[] arguments)
    {
        var info = new ProcessStartInfo(OS.GetExecutablePath())
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in arguments) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Godot did not start");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            process.Kill(true); await process.WaitForExitAsync().ConfigureAwait(false);
            throw new TimeoutException("Godot runtime test exceeded 15s; process tree killed");
        }
        var result = new ProcessResult(process.ExitCode, await stdout + "\n" + await stderr);
        return result;
    }
    internal sealed record ProcessResult(int ExitCode, string Text);
}
