using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using Pal.Game.Map;
using static GdUnit4.Assertions;

namespace Pal.Game.Tests;

[TestSuite, RequireGodotRuntime]
public class MainProcessTests
{
    [TestCase]
    public void MainArgumentsKeepM2DefaultAndRequireExplicitCompleteStaticMode()
    {
        var defaults = NativeSceneLoader.Arguments([]);
        AssertThat(defaults.Root).IsEqual("res://Assets");
        AssertThat(defaults.Profile).IsEqual(Pal.Game.Profiles.NativeExplorationProfile.DefaultUri);
        AssertThat(defaults.StaticAcceptance).IsFalse();

        var selected = NativeSceneLoader.Arguments(["--m3-static-resources", "res://Assets/StaticScene4",
            "--m3-static-profile", "res://Tests/Scene4.tres"]);
        AssertThat(selected.Root).IsEqual("res://Assets/StaticScene4");
        AssertThat(selected.Profile).IsEqual("res://Tests/Scene4.tres");
        AssertThat(selected.StaticAcceptance).IsTrue();
        bool incompleteRejected = false;
        try { NativeSceneLoader.Arguments(["--m3-static-resources", "res://Assets/StaticScene4"]); }
        catch (InvalidDataException) { incompleteRejected = true; }
        AssertThat(incompleteRejected).IsTrue();
        bool mixedRejected = false;
        try { NativeSceneLoader.Arguments(["--m2-resources", "res://Assets", "--m3-static-profile", "res://Tests/Scene4.tres"]); }
        catch (InvalidDataException) { mixedRejected = true; }
        AssertThat(mixedRejected).IsTrue();
    }

    [TestCase(Timeout = 20000)]
    public async Task StaticModeLoadsValidScene4FixtureAndReportsStandaloneAcceptance()
    {
        using var fixture = new NativeProfileFixture();
        fixture.ConfigureStaticScene4();
        fixture.SavePacked();
        var result = await RunMain("--m3-static-resources", fixture.ResourceRoot,
            "--m3-static-profile", fixture.Uri);
        AssertThat(result.ExitCode).IsEqual(0);
        AssertThat(result.Text.Contains("PAL_M3_STATIC_ACCEPTANCE:scene4/map10; events are not scheduled", StringComparison.Ordinal)).IsTrue();
        AssertThat(result.Text.Contains("PAL_GAME_READY:Core ready", StringComparison.Ordinal)).IsTrue();
        AssertThat(result.Text.Contains("ERROR:", StringComparison.Ordinal)).IsFalse();
    }

    [TestCase(Timeout = 20000)]
    public async Task StaticModeRejectsMismatchedFixtureWithoutFallingBackToM2()
    {
        using var fixture = new NativeProfileFixture();
        fixture.SavePacked();
        var result = await RunMain("--m3-static-resources", fixture.ResourceRoot,
            "--m3-static-profile", fixture.Uri);
        AssertThat(result.ExitCode).IsEqual(1);
        AssertThat(result.Text.Contains("M3 static acceptance requires scene 4 / map 10", StringComparison.Ordinal)).IsTrue();
        AssertThat(result.Text.Contains("PAL_GAME_READY", StringComparison.Ordinal)).IsFalse();
        AssertThat(result.Text.Contains("PAL_M3_STATIC_ACCEPTANCE", StringComparison.Ordinal)).IsFalse();
    }

    [TestCase(Timeout = 20000)]
    public async Task RealMainLoadsSelectedNativeSceneBeforeCoreReady()
    {
        using var fixture = new NativeProfileFixture();
        fixture.SavePacked();
        var result = await RunMain("--m2-resources", fixture.ResourceRoot, "--m2-profile", fixture.Uri);
        AssertThat(result.ExitCode).IsEqual(0);
        AssertThat(result.Text.Contains("PAL_MAP_LOADED:", StringComparison.Ordinal)).IsTrue();
        AssertThat(result.Text.Contains("PAL_GAME_READY:Core ready", StringComparison.Ordinal)).IsTrue();
        AssertThat(result.Text.Contains("ERROR:", StringComparison.Ordinal)).IsFalse();
    }

    [TestCase("external", Timeout = 20000)]
    [TestCase("escape", Timeout = 20000)]
    [TestCase("relative", Timeout = 20000)]
    [TestCase("empty", Timeout = 20000)]
    [TestCase("double-slash", Timeout = 20000)]
    [TestCase("encoded", Timeout = 20000)]
    [TestCase("backslash", Timeout = 20000)]
    [TestCase("duplicate", Timeout = 20000)]
    [TestCase("missing", Timeout = 20000)]
    [TestCase("next-flag", Timeout = 20000)]
    public async Task InvalidProfileArgumentsFailBeforeLoading(string kind)
    {
        string[] args = kind switch
        {
            "external" => ["--m2-profile", "C:/Profile.tres"],
            "escape" => ["--m2-profile", "res://Tests/../Profile.tres"],
            "relative" => ["--m2-profile", "Tests/Profile.tres"],
            "empty" => ["--m2-profile", ""],
            "double-slash" => ["--m2-profile", "res://Tests//Profile.tres"],
            "encoded" => ["--m2-profile", "res://Tests/%20Profile.tres"],
            "backslash" => ["--m2-profile", "res://Tests\\Profile.tres"],
            "duplicate" => ["--m2-profile", "res://Tests/A.tres", "--m2-profile", "res://Tests/B.tres"],
            "missing" => ["--m2-profile"],
            _ => ["--m2-profile", "--m2-resources", "res://Assets"]
        };
        var result = await RunMain(args);
        AssertThat(result.ExitCode).IsEqual(1);
        AssertThat(result.Text.Contains("--m2-profile", StringComparison.Ordinal)).IsTrue();
        AssertThat(result.Text.Contains("PAL_GAME_READY", StringComparison.Ordinal)).IsFalse();
    }

    [TestCase(Timeout = 20000)]
    public async Task ShippedMainEntersTreeAndDrawsSelectedNativeSceneAtFixedViewport()
    {
        using var fixture = new StaticSceneFixture();
        fixture.Profile.SavePacked();
        // Opt-in graphics readback uses the same shipped entry and fixture; ordinary tests stay headless.
        string evidence = OS.GetEnvironment("PAL_STATIC_GPU_EVIDENCE");
        bool gpu = !string.IsNullOrEmpty(evidence);
        if (gpu)
        {
            System.IO.Directory.CreateDirectory(evidence);
            System.IO.File.Copy(ProjectSettings.GlobalizePath(fixture.Profile.ResourceRoot + "/Scenes/Scene.tscn"),
                System.IO.Path.Combine(evidence, "Scene.tscn"));
            System.IO.File.Copy(ProjectSettings.GlobalizePath(fixture.Profile.Uri), System.IO.Path.Combine(evidence, "Profile.tres"));
            System.IO.File.WriteAllText(System.IO.Path.Combine(evidence, "fixture-uris.txt"),
                fixture.Profile.ResourceRoot + "\n" + fixture.Profile.Uri + "\n");
        }
        string[] display = gpu ? [] : ["--headless"];
        var result = await Run([.. display, "--path", ProjectSettings.GlobalizePath("res://"),
            "--script", "res://Tests/ObserveMain.gd", "--", "--m2-profile", fixture.Profile.Uri,
            "--m2-resources", fixture.Profile.ResourceRoot]);
        Log(result);
        AssertThat(result.ExitCode).IsEqual(0);
        AssertThat(result.Text.Contains("PAL_MAIN_OBSERVED:static state unchanged across 90 frames", StringComparison.Ordinal)).IsTrue();
        if (gpu) AssertThat(result.Text.Contains("PAL_GPU_STATIC:all 64000 pixels identical", StringComparison.Ordinal)).IsTrue();
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
        using var fixture = new NativeProfileFixture();
        var map = fixture.Scene.Map;
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
        if (kind == "wrong-root") { var wrong = new Node2D(); fixture.SavePacked(wrong); wrong.Free(); }
        else if (kind == "wrong-packed")
        {
            fixture.SavePacked();
            System.IO.File.WriteAllText(ProjectSettings.GlobalizePath(fixture.ResourceRoot + "/Scenes/Scene.tscn"), "[gd_resource type=\"Resource\" format=3]\n\n[resource]\n");
        }
        else fixture.SavePacked();
        var result = await RunMain("--m2-resources", fixture.ResourceRoot, "--m2-profile", fixture.Uri);
        AssertThat(result.ExitCode).IsEqual(1);
        AssertThat(result.Text.Contains(diagnostic, StringComparison.OrdinalIgnoreCase)).IsTrue();
        AssertThat(result.Text.Contains(fixture.ResourceRoot + "/Scenes/Scene.tscn", StringComparison.Ordinal)).IsTrue();
        AssertThat(result.Text.Contains(fixture.Uri, StringComparison.Ordinal)).IsTrue();
        AssertThat(result.Text.Contains("PAL_GAME_READY", StringComparison.Ordinal)).IsFalse();
    }

    [TestCase("missing", "native profile missing", Timeout = 20000)]
    [TestCase("malformed", "PalExplorationProfile", Timeout = 20000)]
    [TestCase("version", "FormatVersion", Timeout = 20000)]
    [TestCase("scene", "SceneId", Timeout = 20000)]
    [TestCase("bundle", "BundleSha256", Timeout = 20000)]
    [TestCase("source", "sha256", Timeout = 20000)]
    [TestCase("role", "RoleId", Timeout = 20000)]
    [TestCase("blocked", "start (320,320) is blocked", Timeout = 20000)]
    [TestCase("trapped", "no legal neighbor", Timeout = 20000)]
    [TestCase("leader-frame", "missing frame 3", Timeout = 20000)]
    [TestCase("object-frame", "Object 1", Timeout = 20000)]
    public async Task BrokenProfileFailsWithBothSelectedUrisAndNoReady(string kind, string diagnostic)
    {
        using var fixture = new NativeProfileFixture();
        switch (kind)
        {
            case "version": fixture.Profile.FormatVersion = 2; break;
            case "scene": fixture.Profile.SceneId = 3; break;
            case "bundle": fixture.Profile.BundleSha256 = new string('c', 64); break;
            case "source": fixture.Profile.Leader.WalkFramesSource.Sha256 = new string('c', 64); break;
            case "role": fixture.Profile.Leader.RoleId = 1; break;
            case "blocked": var blocker = fixture.AddObject(); blocker.X = 320; blocker.Y = 320; blocker.SState = 2; break;
            case "trapped":
                foreach (var (x, y) in new[] { (304, 328), (304, 312), (336, 312), (336, 328) })
                { var obj = fixture.AddObject(fixture.Scene.Objects.Count + 1); obj.X = x; obj.Y = y; obj.SState = 2; }
                break;
            case "leader-frame": fixture.Profile.Start.Direction = 1; break;
            case "object-frame": var missing = fixture.AddObject(); missing.X = 320; missing.Y = 320; missing.WCurrentFrameNum = 1; break;
        }
        fixture.SavePacked();
        string uri = fixture.Uri;
        if (kind == "missing") uri = fixture.Root + "/Missing-" + Guid.NewGuid().ToString("N") + ".tres";
        if (kind == "malformed") System.IO.File.WriteAllText(ProjectSettings.GlobalizePath(uri), "not a native resource\n");
        var result = await RunMain("--m2-profile", uri, "--m2-resources", fixture.ResourceRoot);
        AssertThat(result.ExitCode).IsEqual(1);
        AssertThat(result.Text.Contains(diagnostic, StringComparison.OrdinalIgnoreCase)).IsTrue();
        AssertThat(result.Text.Contains(uri, StringComparison.Ordinal)).IsTrue();
        AssertThat(result.Text.Contains(fixture.ResourceRoot + "/Scenes/Scene.tscn", StringComparison.Ordinal)).IsTrue();
        AssertThat(result.Text.Contains("PAL_GAME_READY", StringComparison.Ordinal)).IsFalse();
    }

    [TestCase(Timeout = 20000)]
    public async Task ResourceRootDoesNotRewriteDefaultProfileUri()
    {
        // Fail before profile loading so caller-owned default contents cannot affect this assertion.
        string root = "res://Assets/RuntimeTests/Missing-DefaultProfile-" + Guid.NewGuid().ToString("N");
        AssertThat(System.IO.Directory.Exists(ProjectSettings.GlobalizePath(root))).IsFalse();
        var result = await RunMain("--m2-resources", root);
        AssertThat(result.ExitCode).IsEqual(1);
        AssertThat(result.Text.Contains("native scene missing", StringComparison.Ordinal)).IsTrue();
        AssertThat(result.Text.Contains(root + "/Scenes/Scene.tscn", StringComparison.Ordinal)).IsTrue();
        AssertThat(result.Text.Contains("profile=" + Profiles.NativeExplorationProfile.DefaultUri, StringComparison.Ordinal)).IsTrue();
        AssertThat(result.Text.Contains("PAL_GAME_READY", StringComparison.Ordinal)).IsFalse();
        AssertThat(System.IO.Directory.Exists(ProjectSettings.GlobalizePath(root))).IsFalse();
    }

    [TestCase(Timeout = 20000)]
    public async Task ProfileOnlyKeepsDefaultResourceRoot()
    {
        string uri = "res://Tests/Missing-" + Guid.NewGuid().ToString("N") + ".tres";
        var result = await RunMain("--m2-profile", uri);
        AssertThat(result.ExitCode).IsEqual(1);
        AssertThat(result.Text.Contains("scene=res://Assets/Scenes/Scene.tscn", StringComparison.Ordinal)).IsTrue();
        AssertThat(result.Text.Contains("profile=" + uri, StringComparison.Ordinal)).IsTrue();
        AssertThat(result.Text.Contains("PAL_GAME_READY", StringComparison.Ordinal)).IsFalse();
    }

    [TestCase(Timeout = 20000)]
    public async Task MissingNativeSceneFailsWithItsSelectedResourceLocation()
    {
        string root = "res://Assets/RuntimeTests/Missing-" + Guid.NewGuid().ToString("N");
        AssertThat(System.IO.Directory.Exists(ProjectSettings.GlobalizePath(root))).IsFalse();
        var result = await RunMain("--m2-resources", root);
        AssertThat(result.ExitCode).IsEqual(1);
        AssertThat(result.Text.Contains(root + "/Scenes/Scene.tscn", StringComparison.Ordinal)).IsTrue();
        AssertThat(result.Text.Contains("PAL_GAME_READY", StringComparison.Ordinal)).IsFalse();
        AssertThat(result.Text.Contains("Attempt to disconnect a nonexistent connection", StringComparison.Ordinal)).IsFalse();
        AssertThat(System.IO.Directory.Exists(ProjectSettings.GlobalizePath(root))).IsFalse();
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
        GD.Print($"Owned child Godot pid={process.Id}");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            process.Kill(true); await process.WaitForExitAsync().ConfigureAwait(false);
            string partial = await stdout + "\n" + await stderr;
            foreach (string line in partial.Split('\n')) GD.Print("Timed-out child: " + line.TrimEnd('\r'));
            throw new TimeoutException($"Godot runtime test exceeded 15s; owned pid={process.Id}, exit={process.ExitCode}; process tree killed");
        }
        var result = new ProcessResult(process.ExitCode, await stdout + "\n" + await stderr);
        return result;
    }
    internal sealed record ProcessResult(int ExitCode, string Text);
}
