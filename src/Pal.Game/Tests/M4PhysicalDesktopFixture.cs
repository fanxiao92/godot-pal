using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;

namespace Pal.Game.Tests;

/// <summary>M4 #54 unit C physical-desktop acceptance entry (never selected by Main or the
/// headless suite). Builds the synthetic full-closure bundle through the same production
/// prepare/binding chain and hosts the real Main, but injects NO input: every key must arrive
/// as a real OS keyboard event (the external driver sends SendInput hardware-level events).
/// Prints stage markers for the driver, counts observed real Enter presses, asserts the Won or
/// Lost completion boundary, then parks (no self-quit) and waits for the operator/driver to
/// close the window normally; the process exit code is the desktop acceptance verdict.</summary>
public partial class M4PhysicalDesktopFixture : Node
{
    private string mode = "";
    private string evidence = "";
    private Main? main;
    private int updates;
    private int realEnterPresses;
    private int lastKeyReport;
    private string stage = "boot";
    private bool done;
    private ulong started;
    private SyntheticFirstBattleFixture? synthetic;
    private NativeProfileFixture? syntheticStatics;
    private readonly List<string> stages = [];

    public override void _Ready()
    {
        try
        {
            Require(DisplayServer.GetName() != "headless", "This fixture requires a graphical desktop session.");
            evidence = RequiredEnvironment("PAL_PHYSICAL_EVIDENCE");
            Require(Directory.Exists(evidence), "Evidence directory must already exist.");
            mode = RequiredEnvironment("PAL_PHYSICAL_MODE");
            Require(mode is "won" or "lost", "Mode must be won or lost.");
            var battleFixture = new NativeBattleProfileFixture();
            syntheticStatics = battleFixture.Static;
            var statics = battleFixture.Static;
            foreach (var frame in statics.MapScene.Data.Map.Tiles.Frames)
            {
                frame.Indices = Array.Empty<byte>().Concat(Enumerable.Repeat((byte)0, frame.Width * frame.Height)).ToArray();
                frame.Alpha = Array.Empty<byte>().Concat(Enumerable.Repeat((byte)255, frame.Width * frame.Height)).ToArray();
            }
            foreach (var sprite in statics.MapScene.Data.Sprites)
                foreach (var frame in sprite.Frames)
                {
                    frame.Indices = Array.Empty<byte>().Concat(Enumerable.Repeat((byte)0, frame.Width * frame.Height)).ToArray();
                    frame.Alpha = Array.Empty<byte>().Concat(Enumerable.Repeat((byte)255, frame.Width * frame.Height)).ToArray();
                }
            statics.MapScene.Data.Palette = (Resources.PalPalette)ResourceLoader.Load("res://Samples/AuthoredBattle/Static/Palette.tres",
                cacheMode: ResourceLoader.CacheMode.IgnoreDeep)!;
            statics.SavePacked();
            string staticProfile = statics.ResourceRoot + "/Profile.tres";
            NativeProfileFixture.SaveResource(statics.Profile, staticProfile);
            Directory.CreateDirectory(ProjectSettings.GlobalizePath(statics.ResourceRoot + "/Palettes"));
            File.Copy(ProjectSettings.GlobalizePath("res://Samples/AuthoredBattle/Static/Palette.tres"),
                ProjectSettings.GlobalizePath(statics.ResourceRoot + "/Palettes/Palette.tres"));
            synthetic = new SyntheticFirstBattleFixture(statics, statics.ResourceRoot, staticProfile, mode == "lost");
            main = new Main();
            main.Failed += m => Fail(new InvalidOperationException("Main failed: " + m));
            main.UpdateCompleted += () => updates++;
            main.Configure(GameSceneConfiguration.FromArguments("--m4-first-battle-profile", synthetic.ProfileUri));
            AddChild(main);
            started = Time.GetTicksMsec();
            GD.Print($"PAL_PHYSICAL_BEGIN:mode={mode}:renderer={DisplayServer.GetName()}:input=injected-none");
        }
        catch (Exception error) { Fail(error); }
    }

    public override void _Input(InputEvent @event)
    {
        // Real-keyboard proof: this fixture never calls Input.ParseInputEvent, so any key state
        // observed here entered through the OS input pipeline. Counting uses engine key-state
        // polling because Main consumes Enter via SetInputAsHandled, which stops _Input
        // propagation before this root node would see the event.
    }

    private bool enterWasDown;

    private void CountRealEnter()
    {
        bool down = Input.IsKeyPressed(Key.Enter) || Input.IsKeyPressed(Key.KpEnter);
        if (down && !enterWasDown)
        {
            realEnterPresses++;
            if (realEnterPresses - lastKeyReport >= 50)
            {
                lastKeyReport = realEnterPresses;
                GD.Print($"PAL_PHYSICAL_KEYS:count={realEnterPresses}");
            }
        }
        enterWasDown = down;
    }

    public override void _Process(double delta)
    {
        if (main is null || done) return;
        CountRealEnter();
        ulong now = Time.GetTicksMsec();
        if (now - started > 900000u)
        {
            Fail(new TimeoutException($"mode={mode} stage={stage} did not complete in the 900s bound."));
            return;
        }
        try
        {
            if (stage == "boot" && updates > 0) Stage("static");
            if (stage != "done" && main.Dialogue?.Session?.PendingDisplay is not null && stage != "dialogue") Stage("dialogue");
            if (main.StoryBattle is not null && stage != "battle") Stage("battle");
            if (main.StoryBattleMode == Pal.Game.Dialogue.StoryBattlePresentation.Settlement && stage != "settlement") Stage("settlement");
            if (mode == "won")
            {
                var session = main.Dialogue?.Session;
                if (stage != "won-continuation" && main.StoryBattle is null && session?.PendingDisplay is not null
                    && session.Phase != Pal.Core.TriggerScriptPhase.Completed) Stage("won-continuation");
                if (session is { Phase: Pal.Core.TriggerScriptPhase.Completed, PendingLostReload: null } && main.StoryLostTerminal is false)
                {
                    Stage("done");
                    done = true;
                    GD.Print($"PAL_PHYSICAL_DONE:mode=won:keys={realEnterPresses}:updates={updates}:terminal=false");
                    SaveResult(true, null);
                }
            }
            else if (main.StoryLostTerminal && main.StoryRedFadeStep >= 31)
            {
                Stage("done");
                done = true;
                GD.Print($"PAL_PHYSICAL_DONE:mode=lost:keys={realEnterPresses}:reloadDiagnostics={main.StoryLostReloadDiagnostics}:fadeStep={main.StoryRedFadeStep}");
                SaveResult(true, null);
            }
        }
        catch (Exception error) { Fail(error); }
    }

    private void Stage(string value)
    {
        stage = value;
        stages.Add(value);
        GD.Print($"PAL_PHYSICAL_STAGE:{value}:keys={realEnterPresses}");
    }

    private void Fail(Exception error)
    {
        done = true;
        GD.PrintErr($"PAL_PHYSICAL_FAILED:{error.GetType().Name}:{error.Message}");
        SaveResult(false, error.GetType().Name + ": " + error.Message);
        GD.Print("PAL_PHYSICAL_DONE:mode=" + mode + ":failed=true");
        GetTree().Quit(1); // abnormal end: not a normal-close acceptance verdict
    }

    private void SaveResult(bool completed, string? failure)
    {
        File.WriteAllText(Path.Combine(evidence, "fixture-result.json"), JsonSerializer.Serialize(new
        {
            mode, completed, failure, enterPollEdges = realEnterPresses, updates, stages,
            scope = "Real desktop process hosting the same synthetic full-closure production chain with zero in-process input. Real-keyboard proof = the external driver's SendInput tap count (each call verified against the SendInput return) plus this flow completing; the in-fixture poll count is only a lower bound because an occluded window throttles frames. Normal window close by the external operator/driver ends the process; exit code and lifecycle evidence live in the launcher's process-result.json."
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    public override void _ExitTree()
    {
        synthetic?.Dispose();
        syntheticStatics?.Dispose();
    }

    private static string RequiredEnvironment(string name) => OS.GetEnvironment(name) is { Length: > 0 } value
        ? value : throw new InvalidDataException($"Explicit {name} is required; no fallback or preparation is performed.");
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
