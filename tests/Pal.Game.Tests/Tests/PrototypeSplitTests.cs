using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using Pal.Core;
using static GdUnit4.Assertions;

namespace Pal.Game.Tests;

// PROTOTYPE (#244): Throwaway verification of multi-project mounting, InputMap,
// GD-526 stdout capture, Main SceneRunner wiring, and child-process execution against pure src/Pal.Game.
[TestSuite, RequireGodotRuntime, GodotExceptionMonitor]
public class PrototypeSplitTests
{
    [TestCase(Timeout = 20000)]
    public async Task Mounted_game_project_supplies_input_map_main_scene_and_stdout_tail()
    {
        // Verify src/Pal.Game's InputMap actions were mounted by gdUnit4 5.1.0-rc7.
        AssertThat(InputMap.HasAction("exploration_south")).IsTrue();
        AssertThat(ResourceLoader.Exists("res://Scenes/Main.tscn")).IsTrue();

        using var fixture = new ExplorationFixture();
        fixture.Native.SavePacked();
        var selection = GameSceneConfiguration.FromArguments(
            "--m2-resources", fixture.Native.ResourceRoot,
            "--m2-profile", fixture.Native.Uri);

        var main = new Main();
        main.Configure(selection);
        ExplorationState? firstStep = null;
        ISceneRunner? activeRunner = null;
        void Capture()
        {
            if (firstStep is not null) return;
            firstStep = main.Exploration;
            activeRunner?.SimulateKeyRelease(Key.Down);
        }

        main.UpdateCompleted += Capture;
        try
        {
            using var runner = ISceneRunner.Load(main, autoFree: true);
            activeRunner = runner;
            runner.SimulateKeyPress(Key.Down);
            ulong start = Time.GetTicksMsec();
            while (firstStep is null && main.Failure is null)
            {
                if (Time.GetTicksMsec() - start > 3000)
                    throw new TimeoutException("Timed out waiting for south step in split prototype; failure=" + main.Failure);
                await runner.AwaitIdleFrame();
            }

            AssertThat(main.Failure).IsNull();
            AssertThat(firstStep).IsNotNull();
            AssertThat((firstStep!.Leader.X, firstStep.Leader.Y, firstStep.Plan.LeaderFrameIndex))
                .IsEqual(((short)304, (short)328, 1));
        }
        finally
        {
            main.UpdateCompleted -= Capture;
            activeRunner = null;
        }

        AssertThat(GodotObject.IsInstanceValid(main)).IsFalse();
        // GD-526 tail-flush check: this final line must appear in captured stdout/TRX.
        GD.Print("PROTOTYPE_GD526_STDOUT_TAIL_MARKER_OK");
    }

    [TestCase(Timeout = 25000)]
    public async Task Child_godot_process_runs_pure_pal_game_and_external_gd_observer()
    {
        string testProjectRoot = ProjectSettings.GlobalizePath("res://").TrimEnd('/', '\\');
        string gameProjectRoot = Path.GetFullPath(Path.Combine(testProjectRoot, "..", "..", "src", "Pal.Game"));
        AssertThat(File.Exists(Path.Combine(gameProjectRoot, "project.godot"))).IsTrue();
        AssertThat(Directory.Exists(Path.Combine(gameProjectRoot, "Tests"))).IsFalse();

        // Create temporary fixture inside test project, then copy to src/Pal.Game/Assets/RuntimeTests for child process.
        using var fixture = new ExplorationFixture();
        fixture.Native.SavePacked();
        string runId = "ProtoChild-" + Guid.NewGuid().ToString("N");
        string gameFixtureDir = Path.Combine(gameProjectRoot, "Assets", "RuntimeTests", runId);
        string gameProfileDir = Path.Combine(gameFixtureDir, "Profile");
        Directory.CreateDirectory(Path.Combine(gameFixtureDir, "Scenes"));
        Directory.CreateDirectory(gameProfileDir);
        try
        {
            File.Copy(
                ProjectSettings.GlobalizePath(fixture.Native.ResourceRoot + "/Scenes/Scene.tscn"),
                Path.Combine(gameFixtureDir, "Scenes", "Scene.tscn"));
            File.Copy(
                ProjectSettings.GlobalizePath(fixture.Native.Uri),
                Path.Combine(gameProfileDir, "Profile.tres"));

            string resRoot = $"res://Assets/RuntimeTests/{runId}";
            string profileUri = $"{resRoot}/Profile/Profile.tres";

            var result = await RunGodot([
                "--headless", "--path", gameProjectRoot, "--quit-after", "4", "--",
                "--m2-resources", resRoot, "--m2-profile", profileUri
            ]);
            GD.Print($"PROTOTYPE_CHILD_EXIT={result.ExitCode}");
            AssertThat(result.ExitCode).IsEqual(0);
            AssertThat(result.Text.Contains("PAL_MAP_LOADED:", StringComparison.Ordinal)).IsTrue();
            AssertThat(result.Text.Contains("PAL_GAME_READY:Core ready", StringComparison.Ordinal)).IsTrue();
            AssertThat(result.Text.Contains("ERROR:", StringComparison.Ordinal)).IsFalse();
        }
        finally
        {
            if (Directory.Exists(gameFixtureDir))
                Directory.Delete(gameFixtureDir, true);
        }
    }

    private static async Task<(int ExitCode, string Text)> RunGodot(string[] arguments)
    {
        var info = new ProcessStartInfo(OS.GetExecutablePath())
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string arg in arguments) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Godot did not start");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            process.Kill(true);
            await process.WaitForExitAsync().ConfigureAwait(false);
            throw new TimeoutException($"Child Godot exceeded 20s; partial={await stdout}\n{await stderr}");
        }
        return (process.ExitCode, await stdout + "\n" + await stderr);
    }
}
