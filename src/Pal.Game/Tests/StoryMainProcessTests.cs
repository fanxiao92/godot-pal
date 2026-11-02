using System;
using System.Threading.Tasks;
using GdUnit4;
using static GdUnit4.Assertions;

namespace Pal.Game.Tests;

[TestSuite, RequireGodotRuntime]
public class StoryMainProcessTests
{
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
