using System;
using System.IO;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace Pal.Game.Tests;

[TestSuite, RequireGodotRuntime]
[TestCategory("Process")]
public class StoryMainProcessTests
{
    [TestCase("effect", Timeout = 20000)]
    [TestCase("idle", Timeout = 20000)]
    [TestCase("number", Timeout = 20000)]
    public async Task MissingSelectedReferencePresentationExitsBeforeReady(string missing)
    {
        if (missing == "idle")
        {
            // Per-slot idle speed is required author data, not an optional reference capability.
            // Since the per-slot presentation group is all-or-none, the partial enemy-slot
            // declaration is rejected by formal Prepare during fixture construction (same
            // migration as the StoryRoundtripSceneTests idle branch, with the current group
            // diagnostic): no story/visual resource is produced, so no process can reach
            // READY — the original semantics, enforced one stage earlier. The fixture cleans
            // its failed output.
            InvalidDataException? rejected = null;
            try { using var unexpected = new AuthoredStoryFixture("Win", missing); }
            catch (InvalidDataException error) { rejected = error; }
            AssertThat(rejected is not null).IsTrue();
            string diagnostic = rejected!.Message;
            AssertThat(diagnostic.StartsWith(Path.Combine(Path.GetTempPath(), "pal-story-c-"), StringComparison.Ordinal)).IsTrue();
            AssertThat(diagnostic.Contains("story-package.json: visual.teams[0]: enemies[0]: enemy presentation fields must be declared together", StringComparison.Ordinal)).IsTrue();
            return;
        }
        using var fixture = new AuthoredStoryFixture("Win", missing);
        var result = await MainProcessTests.RunMain("--story-profile", fixture.StoryUri,
            "--battle-visual-profile", fixture.VisualUri, "--battle-seed", "1");
        AssertThat(result.ExitCode).IsEqual(1);
        AssertThat(result.Text.Contains(missing == "effect" ? "attack effect" : "NumberSpriteUri", StringComparison.Ordinal)).IsTrue();
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
    public async Task LostRunRegistersReloadFailsWithoutSlotAndClosesThroughTheNormalExit()
    {
        // Real-process representative (#228): the shipped story Main reaches the 0x004E request
        // with real input, registers its single lost-reload request and — this independent run
        // never saved — closes in the no-current-slot failure terminal with exactly one located
        // failure line, still exiting 0 through the normal process close. playerHp150 (same
        // shape as the SceneRunner case): the first-round flee attempt fails and the second
        // round's attack trades into the killing enemy strike.
        using var fixture = new AuthoredStoryFixture("LostReload", playerHp: 150);
        var result = await MainProcessTests.Run(["--headless", "--path", ProjectSettings.GlobalizePath("res://"),
            "res://Tests/ObserveStoryLost.tscn", "--",
            "--story-profile", fixture.StoryUri, "--battle-visual-profile", fixture.VisualUri, "--battle-seed", "1"]);
        foreach (string line in result.Text.Split('\n')) GD.Print("Story-lost child: " + line.TrimEnd('\r'));
        AssertThat(result.ExitCode).IsEqual(0);
        int requested = 0, failed = 0;
        foreach (string line in result.Text.Split('\n'))
        {
            if (line.Contains("PAL_LOST_RELOAD_REQUESTED", StringComparison.Ordinal)) requested++;
            if (line.Contains("PAL_LOST_RELOAD_FAILED:reason=no-current-slot", StringComparison.Ordinal)) failed++;
        }
        // The console executable mirrors GD.Print to stdout and stderr, so each real line can
        // appear twice; the exactly-once guarantee is the in-child counter the observer checks
        // across every terminal frame — a per-frame retry would print once per boundary.
        AssertThat(requested is 1 or 2).IsTrue();
        AssertThat(failed is 1 or 2).IsTrue();
        AssertThat(result.Text.Contains("PAL_LOST_RELOAD_TODO", StringComparison.Ordinal)).IsFalse(); // the retired path never prints
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
