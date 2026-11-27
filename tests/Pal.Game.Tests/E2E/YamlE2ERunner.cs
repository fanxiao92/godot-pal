using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using GdUnit4;

namespace Pal.Game.Tests.E2E;

public sealed class ScenarioRunResult
{
    public string Scenario { get; init; } = "";
    public string Version { get; init; } = "1";
    public int PhaseIndex { get; init; }
    public string Status { get; set; } = "passed";
    public string? Error { get; set; }
    public double DurationSec { get; set; }
    public bool ProbePassed { get; set; } = true;
    public List<string> Assertions { get; } = new();
    public List<FrameComparisonResult> Frames { get; } = new();
}

public static class YamlE2ERunner
{
    public static async Task<ScenarioRunResult> RunAsync(
        YamlScenarioDocument doc,
        int phaseIndex,
        string artifactsDir,
        string goldenDir,
        bool updateGolden,
        string commitSha)
    {
        GameProjectMount.EnsureMounted();
        if (DisplayServer.GetName() == "headless")
            throw new InvalidOperationException("T3 E2E GPU runner requires a graphical renderer (DisplayServer is headless).");

        var sw = Stopwatch.StartNew();
        var result = new ScenarioRunResult
        {
            Scenario = doc.Name,
            Version = doc.Version,
            PhaseIndex = phaseIndex
        };

        ulong deadline = Time.GetTicksMsec() + (ulong)Math.Max(10, doc.TimeoutSeconds) * 1000UL;
        var sessions = doc.ResolveSessionsForPhase(phaseIndex);

        try
        {
            foreach (var session in sessions)
            {
                await RunSessionAsync(doc, session, deadline, artifactsDir, goldenDir, updateGolden, commitSha, result);
            }

            if (result.Frames.Any(f => f.Status == "failed"))
            {
                result.Status = "failed";
                var failed = result.Frames.First(f => f.Status == "failed");
                result.Error = $"{failed.FrameName}: {failed.Error}";
            }
        }
        catch (Exception ex)
        {
            result.Status = "failed";
            result.ProbePassed = false;
            result.Error = $"{ex.GetType().Name}: {ex.Message}";
            throw;
        }
        finally
        {
            sw.Stop();
            result.DurationSec = Math.Round(sw.Elapsed.TotalSeconds, 2);
            SaveScenarioResult(artifactsDir, doc.Name, phaseIndex, result);
            GoldenComparator.BuildContactSheet(artifactsDir);
        }

        if (result.Status != "passed")
            throw new InvalidOperationException($"Scenario '{doc.Name}' failed: {result.Error}");

        return result;
    }

    private static async Task RunSessionAsync(
        YamlScenarioDocument doc,
        YamlScenarioSession session,
        ulong deadline,
        string artifactsDir,
        string goldenDir,
        bool updateGolden,
        string commitSha,
        ScenarioRunResult result)
    {
        if (session.CleanupSaveBefore && !string.IsNullOrWhiteSpace(session.SaveNamespace))
            CleanupSaveNamespace(session.SaveNamespace);

        var config = GameSceneConfiguration.FromArguments(session.Args.ToArray());
        var main = new Main();
        int updates = 0;
        var failures = new List<string>();
        main.Failed += msg => failures.Add(msg);
        main.UpdateCompleted += () => updates++;
        main.Configure(config);

        var window = ((SceneTree)Engine.GetMainLoop()).Root;
        var origMinSize = window.MinSize;
        var origSize = window.Size;
        var origFocus = window.GuiGetFocusOwner();
        var heldKeys = new HashSet<Key>();

        ISceneRunner? runner = null;
        try
        {
            var container = new Node();
            container.AddChild(main);
            runner = ISceneRunner.Load(container, autoFree: true);

            void AssertHealthy()
            {
                if (failures.Count > 0 || main.Failure is not null)
                    throw new InvalidOperationException($"Main failed in '{doc.Name}/{session.Name}': {main.Failure ?? failures[0]}");
                if (Time.GetTicksMsec() > deadline)
                    throw new TimeoutException($"Scenario '{doc.Name}/{session.Name}' exceeded liveness timeout ({doc.TimeoutSeconds}s).");
            }

            async Task AwaitFrame()
            {
                AssertHealthy();
                await runner.AwaitIdleFrame();
                AssertHealthy();
            }

            void SendKey(Key key, bool pressed)
            {
                if (pressed)
                {
                    runner.SimulateKeyPress(key);
                    heldKeys.Add(key);
                }
                else
                {
                    runner.SimulateKeyRelease(key);
                    heldKeys.Remove(key);
                }
            }

            async Task TapKey(Key key)
            {
                SendKey(key, true);
                await AwaitFrame();
                SendKey(key, false);
                await AwaitFrame();
            }

            foreach (var step in session.Steps)
            {
                AssertHealthy();
                string action = (step.Action ?? "").Trim().ToLowerInvariant();
                switch (action)
                {
                    case "wait":
                    {
                        ulong stepDeadline = Math.Min(deadline, Time.GetTicksMsec() + (ulong)Math.Max(100, step.TimeoutMs));
                        while (true)
                        {
                            var snap = ProbeSnapshot.Capture(main, updates);
                            if (snap.Evaluate(step.Condition ?? "", out var diag))
                            {
                                result.Assertions.Add($"[wait] {diag}");
                                break;
                            }
                            if (Time.GetTicksMsec() > stepDeadline)
                                throw new TimeoutException($"Wait condition timed out after {step.TimeoutMs}ms: {diag}");
                            await AwaitFrame();
                        }
                        break;
                    }
                    case "key_tap":
                    {
                        Key key = ParseKey(step.Key);
                        int repeat = Math.Max(1, step.Repeat);
                        for (int i = 0; i < repeat; i++)
                            await TapKey(key);
                        break;
                    }
                    case "key_hold":
                    {
                        Key key = ParseKey(step.Key);
                        SendKey(key, true);
                        try
                        {
                            ulong stepDeadline = Math.Min(deadline, Time.GetTicksMsec() + (ulong)Math.Max(100, step.TimeoutMs));
                            if (!string.IsNullOrWhiteSpace(step.Condition))
                            {
                                while (true)
                                {
                                    var snap = ProbeSnapshot.Capture(main, updates);
                                    if (snap.Evaluate(step.Condition, out var diag))
                                    {
                                        result.Assertions.Add($"[key_hold:{key}] {diag}");
                                        break;
                                    }
                                    if (Time.GetTicksMsec() > stepDeadline)
                                        throw new TimeoutException($"key_hold({key}) timed out: {diag}");
                                    await AwaitFrame();
                                }
                            }
                            else if (step.HoldUpdates > 0)
                            {
                                int targetUpdates = updates + step.HoldUpdates;
                                while (updates < targetUpdates)
                                {
                                    if (Time.GetTicksMsec() > stepDeadline)
                                        throw new TimeoutException($"key_hold({key}) timed out waiting for {step.HoldUpdates} updates.");
                                    await AwaitFrame();
                                }
                            }
                            else
                            {
                                ulong endMs = Time.GetTicksMsec() + (ulong)Math.Max(100, step.HoldMs);
                                while (Time.GetTicksMsec() < endMs)
                                    await AwaitFrame();
                            }
                        }
                        finally
                        {
                            SendKey(key, false);
                            await AwaitFrame();
                        }
                        break;
                    }
                    case "advance_dialogue":
                    {
                        ulong stepDeadline = Math.Min(deadline, Time.GetTicksMsec() + (ulong)Math.Max(500, step.TimeoutMs));
                        int ticks = 0;
                        while (true)
                        {
                            var snap = ProbeSnapshot.Capture(main, updates);
                            if (snap.Evaluate(step.Condition ?? "dialogue.active == false", out var diag))
                            {
                                result.Assertions.Add($"[advance_dialogue] {diag}");
                                break;
                            }
                            if (Time.GetTicksMsec() > stepDeadline)
                                throw new TimeoutException($"advance_dialogue timed out: {diag}");
                            if (snap.DialogueActive && (snap.DialoguePhase == "WaitingForAdvance" || (ticks & 7) == 0))
                                await TapKey(Key.Enter);
                            else
                                await AwaitFrame();
                            ticks++;
                        }
                        break;
                    }
                    case "battle_attack_rounds":
                    {
                        ulong stepDeadline = Math.Min(deadline, Time.GetTicksMsec() + (ulong)Math.Max(1000, step.TimeoutMs));
                        int rounds = 0;
                        while (true)
                        {
                            var snap = ProbeSnapshot.Capture(main, updates);
                            if (snap.Evaluate(step.Condition ?? "battle.active == false", out var diag))
                            {
                                result.Assertions.Add($"[battle_attack_rounds] {diag}");
                                break;
                            }
                            if (Time.GetTicksMsec() > stepDeadline || rounds > step.MaxRounds)
                                throw new TimeoutException($"battle_attack_rounds stopped (rounds={rounds}): {diag}");
                            if (snap.DialogueActive && snap.DialoguePhase == "WaitingForAdvance")
                                await TapKey(Key.Enter);
                            else if (snap.BattleMode is "AttackMenu" or "TargetMenu" or "Settlement")
                            {
                                await TapKey(Key.Enter);
                                rounds++;
                            }
                            else
                                await AwaitFrame();
                        }
                        break;
                    }
                    case "assert":
                    {
                        var snap = ProbeSnapshot.Capture(main, updates);
                        var checks = step.Assertions.Count > 0 ? step.Assertions : (string.IsNullOrWhiteSpace(step.Condition) ? new List<string>() : new List<string> { step.Condition });
                        foreach (var expr in checks)
                        {
                            bool ok = snap.Evaluate(expr, out var diag);
                            result.Assertions.Add($"[assert] {diag}");
                            if (!ok)
                            {
                                result.ProbePassed = false;
                                throw new InvalidOperationException($"Probe assertion failed: {diag}");
                            }
                        }
                        break;
                    }
                    case "probe_dump":
                    {
                        var snap = ProbeSnapshot.Capture(main, updates);
                        string probeDir = Path.Combine(artifactsDir, "probes", doc.Name);
                        Directory.CreateDirectory(probeDir);
                        string dumpName = string.IsNullOrWhiteSpace(step.Name) ? $"probe-{updates}" : step.Name;
                        File.WriteAllText(
                            Path.Combine(probeDir, dumpName + ".json"),
                            JsonSerializer.Serialize(snap, new JsonSerializerOptions { WriteIndented = true }) + "\n",
                            new UTF8Encoding(false));
                        break;
                    }
                    case "capture":
                    {
                        string frameName = string.IsNullOrWhiteSpace(step.Name)
                            ? $"frame-{result.Frames.Count + 1:00}"
                            : step.Name;
                        int settle = Math.Max(1, step.SettleFrames);
                        for (int i = 0; i < settle; i++) await AwaitFrame();
                        using var image = await CaptureNextFramePostDrawAsync(main, runner, deadline);
                        double tol = step.TolerancePercent ?? doc.TolerancePercent;
                        var cmp = GoldenComparator.CompareOrUpdate(
                            image, doc.Name, doc.Version, frameName, tol,
                            artifactsDir, goldenDir, updateGolden, commitSha);
                        result.Frames.Add(cmp);
                        // Also dump probe state alongside capture for 1:1 traceability
                        var snap = ProbeSnapshot.Capture(main, updates);
                        string probeDir = Path.Combine(artifactsDir, "probes", doc.Name);
                        Directory.CreateDirectory(probeDir);
                        File.WriteAllText(
                            Path.Combine(probeDir, frameName + ".json"),
                            JsonSerializer.Serialize(snap, new JsonSerializerOptions { WriteIndented = true }) + "\n",
                            new UTF8Encoding(false));
                        break;
                    }
                    default:
                        throw new InvalidDataException($"Unknown YAML step action '{step.Action}'.");
                }
            }
        }
        finally
        {
            if (runner is not null)
            {
                foreach (var key in heldKeys.ToArray())
                    runner.SimulateKeyRelease(key);
            }
            runner?.Dispose();
            if (GodotObject.IsInstanceValid(main)) main.Free();
            window.MinSize = origMinSize;
            window.Size = origSize;
            if (GodotObject.IsInstanceValid(origFocus)) origFocus!.GrabFocus();
            if (session.CleanupSaveAfter && !string.IsNullOrWhiteSpace(session.SaveNamespace))
                CleanupSaveNamespace(session.SaveNamespace);
        }
    }

    private static async Task<Image> CaptureNextFramePostDrawAsync(Main main, ISceneRunner runner, ulong deadline)
    {
        Image? captured = null;
        void OnPostDraw()
        {
            if (captured is not null) return;
            captured = main.GetViewport().GetTexture().GetImage();
        }
        RenderingServer.FramePostDraw += OnPostDraw;
        try
        {
            while (captured is null)
            {
                if (Time.GetTicksMsec() > deadline)
                    throw new TimeoutException("Timed out waiting for RenderingServer.FramePostDraw screenshot.");
                await runner.AwaitIdleFrame();
            }
            return captured;
        }
        finally
        {
            RenderingServer.FramePostDraw -= OnPostDraw;
        }
    }

    private static Key ParseKey(string? name) => (name ?? "").Trim().ToLowerInvariant() switch
    {
        "enter" or "return" => Key.Enter,
        "escape" or "esc" => Key.Escape,
        "space" => Key.Space,
        "up" => Key.Up,
        "down" => Key.Down,
        "left" => Key.Left,
        "right" => Key.Right,
        "w" => Key.W,
        "a" => Key.A,
        "s" => Key.S,
        "d" => Key.D,
        "q" => Key.Q,
        _ => throw new ArgumentException($"Unsupported key name '{name}'.")
    };

    private static void SaveScenarioResult(string artifactsDir, string scenarioName, int phaseIndex, ScenarioRunResult result)
    {
        string dir = Path.Combine(artifactsDir, "e2e-results");
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, $"{scenarioName}.phase{phaseIndex}.json");
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(file, json + "\n", new UTF8Encoding(false));
    }

    private static void CleanupSaveNamespace(string root)
    {
        if (!DirAccess.DirExistsAbsolute(root)) return;
        RemoveDirectoryRecursive(root);
        DirAccess.RemoveAbsolute(root);
    }

    private static void RemoveDirectoryRecursive(string directory)
    {
        using var dir = DirAccess.Open(directory);
        if (dir is null) return;
        dir.ListDirBegin();
        string entry = dir.GetNext();
        while (entry.Length > 0)
        {
            if (entry is not "." and not "..")
            {
                string child = directory + "/" + entry;
                if (dir.CurrentIsDir()) RemoveDirectoryRecursive(child);
                else DirAccess.RemoveAbsolute(child);
            }
            entry = dir.GetNext();
        }
        dir.ListDirEnd();
    }
}
