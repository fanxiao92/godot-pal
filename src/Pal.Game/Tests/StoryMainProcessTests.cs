using System;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace Pal.Game.Tests;

[TestSuite, RequireGodotRuntime]
public class StoryMainProcessTests
{
    [TestCase("effect", Timeout = 20000)]
    [TestCase("idle", Timeout = 20000)]
    [TestCase("number", Timeout = 20000)]
    public async Task MissingSelectedReferencePresentationExitsBeforeReady(string missing)
    {
        using var fixture = new AuthoredStoryFixture("Win", missing);
        var result = await MainProcessTests.RunMain("--story-profile", fixture.StoryUri,
            "--battle-visual-profile", fixture.VisualUri, "--battle-seed", "1");
        AssertThat(result.ExitCode).IsEqual(1);
        AssertThat(result.Text.Contains(missing == "effect" ? "attack effect" : missing == "number" ? "NumberSpriteUri" : "IdleAnimSpeed", StringComparison.Ordinal)).IsTrue();
        AssertThat(result.Text.Contains("selected-profile=" + fixture.StoryUri, StringComparison.Ordinal)).IsTrue();
        AssertThat(result.Text.Contains("PAL_GAME_READY", StringComparison.Ordinal)).IsFalse();
        AssertThat(result.Text.Contains("PAL_MAP_LOADED", StringComparison.Ordinal)).IsFalse();
    }

    [TestCase("mixed", Timeout = 20000)]
    [TestCase("missing-value", Timeout = 20000)]
    [TestCase("missing-visual", Timeout = 20000)]
    [TestCase("duplicate", Timeout = 20000)]
    [TestCase("seed", Timeout = 20000)]
    public async Task InvalidNormalStoryArgvKeepsIdentityAndExitsWithoutReady(string kind)
    {
        const string story = "res://Assets/Selected-Story.tres";
        string[] args = kind switch
        {
            "mixed" => ["--story-profile", story, "--m2-resources", "res://Assets"],
            "missing-value" => ["--story-profile", story, "--battle-visual-profile"],
            "missing-visual" => ["--story-profile", story],
            "duplicate" => ["--story-profile", story, "--story-profile", story],
            _ => ["--story-profile", story, "--battle-visual-profile", "res://Assets/Visual.tres", "--battle-seed", "-1"]
        };
        var result = await MainProcessTests.RunMain(args);
        AssertThat(result.ExitCode).IsEqual(1);
        AssertThat(result.Text.Contains("selected-profile=" + story, StringComparison.Ordinal)).IsTrue();
        AssertThat(result.Text.Contains("PAL_GAME_READY", StringComparison.Ordinal)).IsFalse();
        AssertThat(result.Text.Contains("PAL_MAP_LOADED", StringComparison.Ordinal)).IsFalse();
    }

    [TestCase(Timeout = 20000)]
    public async Task MissingStoryResourceExitsWithoutPartialReady()
    {
        var result = await MainProcessTests.RunMain("--story-profile", "res://Assets/Missing-Story.tres",
            "--battle-visual-profile", "res://Assets/Missing-Visual.tres");
        AssertThat(result.ExitCode).IsEqual(1);
        AssertThat(result.Text.Contains("selected-profile=res://Assets/Missing-Story.tres", StringComparison.Ordinal)).IsTrue();
        AssertThat(result.Text.Contains("PAL_GAME_READY", StringComparison.Ordinal)).IsFalse();
        AssertThat(result.Text.Contains("PAL_MAP_LOADED", StringComparison.Ordinal)).IsFalse();
    }

    [TestCase(Timeout = 30000)]
    public async Task LostTerminalPrintsOneReloadDiagnosticAndClosesThroughTheNormalExit()
    {
        // Real-process representative: the shipped story Main reaches the 0x004E boundary with
        // real input, prints its single lost-reload diagnostic to stdout and still exits 0.
        // playerHp150 (same shape as the SceneRunner case): the first-round flee attempt fails
        // and the second round's attack trades into the killing enemy strike.
        using var fixture = new AuthoredStoryFixture("LostReload", playerHp: 150);
        var result = await MainProcessTests.Run(["--headless", "--path", ProjectSettings.GlobalizePath("res://"),
            "res://Tests/ObserveStoryLost.tscn", "--",
            "--story-profile", fixture.StoryUri, "--battle-visual-profile", fixture.VisualUri, "--battle-seed", "1"]);
        foreach (string line in result.Text.Split('\n')) GD.Print("Story-lost child: " + line.TrimEnd('\r'));
        AssertThat(result.ExitCode).IsEqual(0);
        int diagnostics = 0;
        foreach (string line in result.Text.Split('\n'))
            if (line.Contains("PAL_LOST_RELOAD_TODO", StringComparison.Ordinal)) diagnostics++;
        // The console executable mirrors GD.Print to stdout and stderr, so each real line can
        // appear twice; the exactly-once guarantee is the in-child counter the observer checks.
        AssertThat(diagnostics is 1 or 2).IsTrue();
        AssertThat(result.Text.Contains("PAL_STORY_LOST_OBSERVED:terminal=1", StringComparison.Ordinal)).IsTrue();
        AssertThat(result.Text.Contains("STORY_LOST_OBSERVE_FAIL", StringComparison.Ordinal)).IsFalse();
        AssertThat(result.Text.Contains("ERROR:", StringComparison.Ordinal)).IsFalse();
    }

    [TestCase(Timeout = 20000)]
    public async Task NormalStoryShippedMainPreloadsThenReportsReadyLast()
    {
        using var fixture = new AuthoredStoryFixture("Win");
        var result = await MainProcessTests.RunMain("--story-profile", fixture.StoryUri,
            "--battle-visual-profile", fixture.VisualUri, "--story-language", "en", "--battle-seed", "1");
        AssertThat(result.ExitCode).IsEqual(0);
        int story = result.Text.IndexOf("PAL_STORY_LOADED:", StringComparison.Ordinal);
        int map = result.Text.IndexOf("PAL_MAP_LOADED:", StringComparison.Ordinal);
        int ready = result.Text.IndexOf("PAL_GAME_READY:Core ready", StringComparison.Ordinal);
        AssertThat(story >= 0 && map > story && ready > map).IsTrue();
        AssertThat(result.Text.Contains("ERROR:", StringComparison.Ordinal)).IsFalse();
    }
}
