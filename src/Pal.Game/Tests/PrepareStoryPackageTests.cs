using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using GdUnit4;
using Godot;
using Pal.Game.Editor;
using Pal.Game.Profiles;
using static GdUnit4.Assertions;

namespace Pal.Game.Tests;

[TestSuite, RequireGodotRuntime]
public class PrepareStoryPackageTests
{
    [TestCase]
    public void PublicPreparePublishesCompleteEventsAndStrictTypedProfilesWithoutChangingSources()
    {
        using var source = new Preparation();
        string[] before = source.Snapshot();
        Directory.CreateDirectory(source.Output);
        var result = PrepareStoryPackage.Prepare(source.Input, source.Output);
        AssertThat(result.StoryUri).IsEqual(source.OutputUri + "/Story.tres");
        AssertThat(result.VisualUri).IsEqual(source.OutputUri + "/Visual.tres");
        var story = NativeStoryProfile.Load(result.StoryUri, "en");
        var visual = NativeBattleVisualProfile.Load(result.VisualUri, story);
        AssertThat(story.Entry.OwnerId).IsEqual(1);
        AssertThat(story.Requirements.BattleTeamIds.ToArray()).IsEqual(new[] { 18 });
        AssertThat(visual.Teams[18].TargetId).IsEqual(7);
        AssertThat(story.ResourceSha256.ContainsKey(source.OutputUri + "/Events/outcomes.json")).IsTrue();
        AssertThat(File.ReadAllBytes(Path.Combine(source.Output, "Events", "shared.json")))
            .IsEqual(File.ReadAllBytes(Path.Combine(source.Input, "events", "shared.json")));
        AssertThat(Directory.GetFileSystemEntries(source.Output).Select(Path.GetFileName).Order().ToArray())
            .IsEqual(new[] { "Events", "Story.tres", "Visual.tres" });
        AssertThat(source.Snapshot()).IsEqual(before);
    }

    [TestCase("version")]
    [TestCase("missing")]
    [TestCase("unknown")]
    [TestCase("duplicate")]
    [TestCase("nested-unknown")]
    [TestCase("position")]
    [TestCase("type")]
    [TestCase("utf8")]
    [TestCase("oversize")]
    [TestCase("extra-file")]
    [TestCase("empty-directory")]
    [TestCase("missing-event")]
    [TestCase("unselected-raw-corrupt")]
    public void InvalidModernInputPreservesSourceAndCallerEmptyDirectory(string defect)
    {
        using var source = new Preparation();
        string json = File.ReadAllText(source.Metadata);
        switch (defect)
        {
            case "version": json = json.Replace("\"formatVersion\":1", "\"formatVersion\":2"); break;
            case "missing": json = json.Replace("\"formatVersion\":1,", ""); break;
            case "unknown": json = json.Replace("\"formatVersion\":1", "\"formatVersion\":1,\"unknown\":0"); break;
            case "duplicate": json = json.Replace("\"formatVersion\":1", "\"formatVersion\":1,\"formatVersion\":1"); break;
            case "nested-unknown": json = json.Replace("\"roleId\":0", "\"roleId\":0,\"unknown\":0"); break;
            case "position": json = json.Replace("[240,170]", "[32768,170]"); break;
            case "type": json = json.Replace("\"ownerId\":1", "\"ownerId\":true"); break;
            case "extra-file": File.WriteAllText(Path.Combine(source.Input, "extra"), "keep"); break;
            case "empty-directory": Directory.CreateDirectory(Path.Combine(source.Input, "unexpected")); break;
            case "missing-event": File.Delete(Path.Combine(source.Input, "events", "outcomes.json")); break;
            case "unselected-raw-corrupt":
                string path = Path.Combine(source.Input, "events", "shared.json");
                File.WriteAllText(path, File.ReadAllText(path).Replace("65535,8", "65536,8")); break;
        }
        File.WriteAllText(source.Metadata, json);
        if (defect == "utf8") File.WriteAllBytes(source.Metadata, [0xc3, 0x28]);
        if (defect == "oversize") using (var file = File.OpenWrite(source.Metadata)) file.SetLength(16777217);
        string[] before = source.Snapshot(); Directory.CreateDirectory(source.Output);
        Reject(source.Input, source.Output, defect switch
        {
            "version" => "formatVersion", "missing" or "unknown" or "duplicate" or "nested-unknown" => "fields",
            "position" => "SHORT", "type" => "type", "utf8" => "UTF-8", "oversize" => "16777216",
            "extra-file" => "exactly", "empty-directory" => "events directory", "missing-event" => "missing complete event",
            _ => "operands"
        });
        AssertThat(source.Snapshot()).IsEqual(before);
        AssertThat(Directory.Exists(source.Output)).IsTrue();
        AssertThat(Directory.GetFileSystemEntries(source.Output).Length).IsEqual(0);
    }

    [TestCase("owner")]
    [TestCase("visual-target")]
    [TestCase("missing-language-glyph")]
    public void StrictNativeRejectionCleansOnlyOwnedPublishedFilesAndRetainsCallerDirectory(string defect)
    {
        using var source = new Preparation();
        string json = File.ReadAllText(source.Metadata);
        if (defect == "owner") json = json.Replace("\"ownerId\":1", "\"ownerId\":2");
        if (defect == "visual-target") json = json.Replace("\"targetId\":7", "\"targetId\":8");
        if (defect == "missing-language-glyph")
        {
            source.Native.Story.English.Messages[0].Entries[0].Text = "Z";
            source.Native.Story.Save(source.Native.Story.English, "Language.en.tres");
        }
        File.WriteAllText(source.Metadata, json);
        Directory.CreateDirectory(source.Output); string[] before = source.Snapshot();
        Reject(source.Input, source.Output, defect == "visual-target" ? "TargetId" : defect == "owner" ? "identity" : "glyph");
        AssertThat(Directory.Exists(source.Output)).IsTrue();
        AssertThat(Directory.GetFileSystemEntries(source.Output).Length).IsEqual(0);
        AssertThat(source.Snapshot()).IsEqual(before);
    }

    [TestCase("file")]
    [TestCase("directory")]
    [TestCase("published")]
    public void CallerOutputIsNeverOverwrittenOrRemoved(string kind)
    {
        using var source = new Preparation(); Directory.CreateDirectory(source.Output);
        string path = Path.Combine(source.Output, kind == "published" ? "Story.tres" : "sentinel");
        if (kind == "directory") Directory.CreateDirectory(path); else File.WriteAllText(path, "caller-owned");
        Reject(source.Input, source.Output, "new or empty");
        AssertThat(File.Exists(path) || Directory.Exists(path)).IsTrue();
        if (kind != "directory") AssertThat(File.ReadAllText(path)).IsEqual("caller-owned");
    }

    [TestCase("outside")]
    [TestCase("input-project")]
    [TestCase("overlap")]
    [TestCase("native-overlap")]
    public void PublicApiRejectsUnsafePhysicalBoundariesBeforePublication(string defect)
    {
        using var source = new Preparation();
        string input = source.Input, output = source.Output;
        if (defect == "outside") output = Path.Combine(source.Input, "output");
        if (defect == "input-project") input = ProjectSettings.GlobalizePath("res://Tests");
        if (defect == "overlap") input = Path.GetDirectoryName(output)!;
        if (defect == "native-overlap") output = ProjectSettings.GlobalizePath(source.Native.Story.Profile.StaticResources + "/new-output");
        Reject(input, output, defect == "outside" ? "Assets" : defect == "input-project" ? "outside src/Pal.Game" : "overlap");
        AssertThat(Directory.Exists(output)).IsFalse();
    }

    [TestCase]
    public void NormalizedWindowsOutputUriPreservesCaseAndSupportsMissingAncestors()
    {
        using var source = new Preparation();
        string output = Path.Combine(source.Output, "MixedCase", "Native");
        if (OperatingSystem.IsWindows()) output = output.Replace("Pal.Game", "pal.game");
        var result = PrepareStoryPackage.Prepare(source.Input + "/.", output + "/");
        AssertThat(result.StoryUri).IsEqual(source.OutputUri + "/MixedCase/Native/Story.tres");
        _ = NativeBattleVisualProfile.Load(result.VisualUri, NativeStoryProfile.Load(result.StoryUri));
        Reject(source.Input, output);
        using var exclusive = new FileStream(source.Metadata, FileMode.Open, System.IO.FileAccess.ReadWrite, FileShare.None);
    }

    [TestCase("file-count")]
    [TestCase("total-bytes")]
    public void FixedModernPackageBoundsRejectBeforeParsingOrPublication(string defect)
    {
        using var source = new Preparation();
        if (defect == "file-count")
            for (int i = 0; i < 1025; i++) File.WriteAllText(Path.Combine(source.Input, "events", "extra" + i + ".json"), "{}");
        else
            for (int i = 0; i < 5; i++) using (var file = File.Create(Path.Combine(source.Input, "events", "extra" + i + ".json"))) file.SetLength(16 * 1024 * 1024);
        Reject(source.Input, source.Output, defect == "file-count" ? "1024" : "67108864");
        AssertThat(Directory.Exists(source.Output)).IsFalse();
    }

    [TestCase("input")]
    [TestCase("events")]
    [TestCase("output")]
    [TestCase("native")]
    public void PublicApiRejectsDirectoryReparsePointsWithoutTouchingTargets(string kind)
    {
        using var source = new Preparation();
        string link = ProjectSettings.GlobalizePath(source.OutputUri + "-link"), target = source.Input;
        string input = source.Input, output = source.Output;
        if (kind == "input") { link = source.Input + "-link"; input = link; }
        if (kind == "events")
        {
            target = Path.Combine(source.Input, "events-original");
            Directory.Move(Path.Combine(source.Input, "events"), target);
            link = Path.Combine(source.Input, "events");
        }
        if (kind == "output") output = link;
        if (kind == "native")
        {
            target = ProjectSettings.GlobalizePath(source.Native.Root);
            string json = File.ReadAllText(source.Metadata).Replace(source.Native.Root + "/Player.tres", source.OutputUri + "-link/Player.tres");
            File.WriteAllText(source.Metadata, json);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        if (OperatingSystem.IsWindows())
        {
            var start = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string argument in new[] { "/c", "mklink", "/J", Path.GetFullPath(link), Path.GetFullPath(target) }) start.ArgumentList.Add(argument);
            using var process = System.Diagnostics.Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            string diagnostic = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
            if (process.ExitCode != 0) throw new InvalidOperationException("mklink exit " + process.ExitCode + ": " + diagnostic);
        }
        else Directory.CreateSymbolicLink(link, target);
        try { Reject(input, output, "reparse"); AssertThat(Directory.Exists(target)).IsTrue(); }
        finally
        {
            Directory.Delete(link);
            if (kind == "events") Directory.Move(target, Path.Combine(source.Input, "events"));
        }
        AssertThat(Directory.Exists(source.Output)).IsFalse();
    }

    [TestCase]
    public void PublicPrepareBindsNumberSpriteWithExactNativeClosure()
    {
        using var source = new Preparation();
        source.Native.AddNumberSprite();
        var metadata = JsonNode.Parse(File.ReadAllText(source.Metadata))!;
        metadata["visual"]!["numberSpriteUri"] = source.Native.Profile.NumberSpriteUri;
        File.WriteAllText(source.Metadata, metadata.ToJsonString());
        string[] before = source.Snapshot();
        var result = PrepareStoryPackage.Prepare(source.Input, source.Output);
        var visual = NativeBattleVisualProfile.Load(result.VisualUri, NativeStoryProfile.Load(result.StoryUri));
        visual.RequireNumberPresentation();
        AssertThat(visual.BlueNumberFrames[0].Index).IsEqual(29);
        AssertThat(visual.CyanNumberFrames[9].Index).IsEqual(65);
        AssertThat(visual.ResourceSha256[source.Native.Profile.NumberSpriteUri]).IsEqual(Godot.FileAccess.GetSha256(source.Native.Profile.NumberSpriteUri));
        AssertThat(source.Snapshot()).IsEqual(before);
    }

    [TestCase("blank")]
    [TestCase("missing")]
    [TestCase("duplicate")]
    public void NumberAuthorFieldCannotBypassSchemaOrNativeDependencyChecks(string fault)
    {
        using var source = new Preparation();
        var metadata = JsonNode.Parse(File.ReadAllText(source.Metadata))!;
        metadata["visual"]!["numberSpriteUri"] = fault == "blank" ? " " : source.Native.Root + "/MissingNumbers.tres";
        string json = metadata.ToJsonString();
        if (fault == "duplicate") json = json.Replace("\"numberSpriteUri\":", "\"numberSpriteUri\":\"duplicate\",\"numberSpriteUri\":");
        File.WriteAllText(source.Metadata, json);
        string[] before = source.Snapshot();
        Reject(source.Input, source.Output, fault == "duplicate" ? "fields" : fault == "blank" ? "numberSpriteUri" : "missing");
        AssertThat(source.Snapshot()).IsEqual(before);
        AssertThat(Directory.Exists(source.Output)).IsFalse();
    }

    [TestCase]
    public void PublicPrepareBindsOptionalReferenceDataAndExactEffectClosure()
    {
        using var source = new Preparation();
        var metadata = JsonNode.Parse(File.ReadAllText(source.Metadata))!;
        metadata["visual"]!["attackEffectSpriteUri"] = source.Native.Team.EnemySpriteUri;
        metadata["visual"]!["attackEffectIndex"] = 0;
        metadata["visual"]!["teams"]![0]!["idleAnimSpeed"] = 0;
        File.WriteAllText(source.Metadata, metadata.ToJsonString());
        string[] before = source.Snapshot();
        var result = PrepareStoryPackage.Prepare(source.Input, source.Output);
        var visual = NativeBattleVisualProfile.Load(result.VisualUri, NativeStoryProfile.Load(result.StoryUri));
        visual.RequireReferencePresentation();
        AssertThat(visual.AttackEffectFrames.Select(x => x.Index).SequenceEqual(new[] { 0, 1, 2 })).IsTrue();
        AssertThat(visual.ResourceSha256.ContainsKey(source.Native.Team.EnemySpriteUri)).IsTrue();
        AssertThat(visual.Teams[18].IdleAnimSpeed).IsEqual(0);
        AssertThat(source.Snapshot()).IsEqual(before);
    }

    [TestCase("partial")]
    [TestCase("negative-group")]
    [TestCase("idle-sentinel")]
    [TestCase("idle-overflow")]
    [TestCase("unknown")]
    [TestCase("duplicate")]
    public void OptionalPresentationAuthorFieldsAreNotDefaultsOrSchemaEscapes(string fault)
    {
        using var source = new Preparation();
        var metadata = JsonNode.Parse(File.ReadAllText(source.Metadata))!;
        var visual = metadata["visual"]!;
        visual["attackEffectSpriteUri"] = source.Native.Team.EnemySpriteUri;
        if (fault != "partial") visual["attackEffectIndex"] = fault == "negative-group" ? -1 : 0;
        visual["teams"]![0]!["idleAnimSpeed"] = fault == "idle-sentinel" ? -1 : fault == "idle-overflow" ? 65536 : 0;
        if (fault == "unknown") visual["unknown"] = 0;
        string json = metadata.ToJsonString();
        if (fault == "duplicate") json = json.Replace("\"attackEffectIndex\":0", "\"attackEffectIndex\":0,\"attackEffectIndex\":0");
        File.WriteAllText(source.Metadata, json); Directory.CreateDirectory(source.Output);
        string[] before = source.Snapshot();
        Reject(source.Input, source.Output, fault == "partial" ? "paired" : fault is "unknown" or "duplicate" ? "fields" : "WORD");
        AssertThat(Directory.GetFileSystemEntries(source.Output).Length).IsEqual(0);
        AssertThat(source.Snapshot()).IsEqual(before);
    }

    private static void Reject(string input, string output, string? diagnostic = null)
    {
        Exception? failure = null;
        try { _ = PrepareStoryPackage.Prepare(input, output); } catch (Exception error) { failure = error; }
        AssertThat(failure is not null).IsTrue();
        if (diagnostic is not null && !failure!.Message.Contains(diagnostic, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Expected diagnostic " + diagnostic + "; actual " + failure.GetType().Name + ": " + failure.Message, failure);
    }

    internal sealed class Preparation : IDisposable
    {
        public NativeBattleVisualProfileFixture Native { get; } = new();
        public string Input { get; } = Path.Combine(Path.GetTempPath(), "pal-story-package-" + Guid.NewGuid().ToString("N"));
        public string OutputUri { get; } = "res://Assets/PrepareTests/" + Guid.NewGuid().ToString("N");
        public string Output => ProjectSettings.GlobalizePath(OutputUri);
        public string Metadata => Path.Combine(Input, "story-package.json");
        public Preparation()
        {
            Directory.CreateDirectory(Path.Combine(Input, "events"));
            foreach (string name in new[] { "index.json", "shared.json", "outcomes.json" })
                File.Copy(ProjectSettings.GlobalizePath(Native.Root + "/" + name), Path.Combine(Input, "events", name));
            var s = Native.Story.Profile; var v = Native.Profile; var t = Native.Team;
            File.WriteAllText(Metadata, JsonSerializer.Serialize(new
            {
                formatVersion = 1, sourceIdentity = "synthetic preparation test; not desktop art",
                staticResources = s.StaticResources, staticProfile = s.StaticProfile, bundleSha256 = s.BundleSha256,
                entryFileId = s.EntryFileId, entryId = s.EntryId, ownerId = s.OwnerId, entryAddress = s.EntryAddress,
                battleContentUri = s.BattleContentUri, dialogueDirectory = s.DialogueDirectory,
                portraits = s.Portraits.Select(p => new { id = p.Key, uri = p.Value }).ToArray(),
                visual = new { roleId = v.RoleId, playerSpriteUri = v.PlayerSpriteUri, playerPosition = new[] { 240, 170 }, paletteUri = v.PaletteUri,
                    teams = new[] { new { teamId = t.TeamId, targetId = t.TargetId, backgroundUri = t.BackgroundUri, enemySpriteUri = t.EnemySpriteUri,
                        enemyPosition = new[] { 100, 120 }, idleFrames = 1, magicFrames = 1, attackFrames = 1, actWaitFrames = 0 } } }
            }));
        }
        public string[] Snapshot() => Directory.GetFiles(Input, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(p => Path.GetRelativePath(Input, p) + ":" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(p)))).ToArray();
        public void Dispose()
        {
            Native.Dispose();
            if (Directory.Exists(Input)) Directory.Delete(Input, true);
            if (Directory.Exists(Output)) Directory.Delete(Output, true);
        }
    }
}
