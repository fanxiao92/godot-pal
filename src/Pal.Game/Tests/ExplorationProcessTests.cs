using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace Pal.Game.Tests;

[TestSuite, RequireGodotRuntime]
public class ExplorationProcessTests
{
    [TestCase(Timeout = 20000)]
    public async Task SouthArrowMovesActualMainAndReleaseStands()
    {
        using var fixture = new ExplorationFixture();
        fixture.Native.SavePacked();
        var result = await Observe(fixture.Native);
        foreach (string line in result.Text.Split('\n')) GD.Print("Exploration child: " + line.TrimEnd('\r'));
        AssertThat(result.ExitCode).IsEqual(0);
        AssertThat(result.Text.Contains("PAL_EXPLORATION_OBSERVED:south", StringComparison.Ordinal)).IsTrue();
        AssertThat(result.Text.Contains("ERROR:", StringComparison.Ordinal)).IsFalse();
    }

    [TestCase("three", Timeout = 20000)]
    [TestCase("four", Timeout = 20000)]
    public async Task DirectionGroupsUseLatestPressAndReleaseFallbackInActualMain(string count)
    {
        using var fixture = new ExplorationFixture();
        fixture.Native.SavePacked();
        await CheckObservation(fixture, "priority-" + count);
    }

    [TestCase("south", Timeout = 20000)]
    [TestCase("west", Timeout = 20000)]
    [TestCase("north", Timeout = 20000)]
    [TestCase("east", Timeout = 20000)]
    [TestCase("group-order", Timeout = 20000)]
    public async Task AliasesKeepIndependentHeldKeysAndDirectionGroupOrder(string direction)
    {
        using var fixture = new ExplorationFixture();
        fixture.Native.SavePacked();
        await CheckObservation(fixture, "alias-" + direction);
    }

    [TestCase(Timeout = 20000)]
    public async Task EchoAndDuplicatePressStormDoesNotReorderActivateOrAccelerate()
    {
        using var fixture = new ExplorationFixture();
        fixture.Native.SavePacked();
        await CheckObservation(fixture, "repeat");
    }

    [TestCase("south-alternate", Timeout = 20000)]
    [TestCase("north-primary", Timeout = 20000)]
    public async Task WindowFocusSignalsFreezeUntilFreshPressThenResumeImmediatelyWithoutCatchup(string fresh)
    {
        using var fixture = new ExplorationFixture();
        fixture.AddDynamicCoverExample();
        fixture.Native.SavePacked();
        await CheckObservation(fixture, "focus-" + fresh);
    }

    [TestCase(0, Timeout = 20000)]
    [TestCase(4, Timeout = 20000)]
    public async Task RawWalkWordSelectsThreeOrFourFramesWithoutMutatingMetadata(int raw)
    {
        using var fixture = new ExplorationFixture(raw);
        fixture.Native.SavePacked();
        await CheckObservation(fixture, "raw-" + raw);
    }

    [TestCase("map", Timeout = 20000)]
    [TestCase("hidden-spriteless-object", Timeout = 20000)]
    public async Task BlockedArrowTurnsAndStandsWithoutSliding(string kind)
    {
        using var fixture = new ExplorationFixture();
        if (kind == "map")
        {
            // West target(304,312) belongs to literal cell(x9,y19,h1).
            fixture.Native.Scene.Map.RawCells[2451] = 0x2000;
            fixture.Native.Scene.Map.Blocked[2451] = 1;
        }
        else
        {
            var blocker = fixture.Native.AddObject();
            blocker.X = 304; blocker.Y = 328; blocker.SState = 2; blocker.SVanishTime = 12;
            blocker.WSpriteNum = 0; blocker.Sprite = null!; blocker.InitialFrameIndex = -1;
            blocker.InitiallyVisible = false;
            blocker.WTriggerScript = 123; blocker.WAutoScript = 456;
        }
        fixture.Native.SavePacked();
        await CheckObservation(fixture, "blocked-" + kind);
    }

    [TestCase(Timeout = 20000)]
    public async Task MissingSelectedWalkingFrameExitsOneWithUpdateDiagnostic()
    {
        using var fixture = new ExplorationFixture();
        var sprite = fixture.Native.Scene.Sprites[0];
        sprite.FrameCount = 1;
        sprite.Frames.RemoveAt(1); // Valid initial frame0; only the actually selected walking frame is missing.
        fixture.Native.SavePacked();
        var result = await Observe(fixture.Native);
        foreach (string line in result.Text.Split('\n')) GD.Print("Exploration child: " + line.TrimEnd('\r'));
        AssertThat(result.ExitCode).IsEqual(1);
        AssertThat(result.Text.Contains("PAL_GAME_READY:Core ready", StringComparison.Ordinal)).IsTrue();
        AssertThat(result.Text.Contains("Exploration update failed: Leader sprite 17 is missing frame 1.", StringComparison.Ordinal)).IsTrue();
        AssertThat(result.Text.Contains("PAL_EXPLORATION_OBSERVED", StringComparison.Ordinal)).IsFalse();
    }

    [TestCase(Timeout = 20000)]
    public async Task DynamicNativeFramesCoverAndObjectPlanReachNormalRenderer()
    {
        using var fixture = new ExplorationFixture();
        fixture.AddDynamicCoverExample();
        fixture.Native.SavePacked();
        await CheckObservation(fixture, "dynamic");
    }

    private static async Task CheckObservation(ExplorationFixture fixture, string scenario)
    {
        var result = await Observe(fixture.Native, scenario);
        foreach (string line in result.Text.Split('\n')) GD.Print("Exploration child: " + line.TrimEnd('\r'));
        AssertThat(result.ExitCode).IsEqual(0);
        AssertThat(result.Text.Contains("PAL_EXPLORATION_OBSERVED:" + scenario, StringComparison.Ordinal)).IsTrue();
        AssertThat(result.Text.Contains("ERROR:", StringComparison.Ordinal)).IsFalse();
    }

    private static async Task<(int ExitCode, string Text)> Observe(NativeProfileFixture fixture, string scenario = "south")
    {
        var info = new ProcessStartInfo(OS.GetExecutablePath())
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        info.Environment["PAL_EXPLORATION_CASE"] = scenario;
        bool gpu = scenario == "dynamic" && !string.IsNullOrEmpty(OS.GetEnvironment("PAL_EXPLORATION_GPU_EVIDENCE"));
        string[] display = gpu ? [] : ["--headless"];
        string[] arguments = [.. display, "--path", ProjectSettings.GlobalizePath("res://"),
            "--max-fps", "120", "res://Tests/ExplorationObserver.tscn", "--", "--m2-resources", fixture.ResourceRoot,
            "--m2-profile", fixture.Uri];
        foreach (string arg in arguments) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Godot did not start");
        GD.Print($"OWNED_CHILD:pid={process.Id}:parent={OS.GetProcessId()}:created={process.StartTime.ToUniversalTime():O}:exe={info.FileName}:arguments={string.Join(' ', arguments)}");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            string text = await stdout.ConfigureAwait(false) + "\n" + await stderr.ConfigureAwait(false);
            GD.Print($"OWNED_CHILD_EXIT:pid={process.Id}:exit={process.ExitCode}");
            return (process.ExitCode, text);
        }
        catch (OperationCanceledException)
        {
            process.Kill(true);
            throw new TimeoutException("Exploration child exceeded bounded 15s including output drain");
        }
    }
}
