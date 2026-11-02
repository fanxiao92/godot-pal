using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Godot;
using Pal.Core;
using Pal.Game.Profiles;
using Pal.Game.Resources;

namespace Pal.Game.Editor;

// Explicit offline package binding. Main never invokes this converter.
public partial class PrepareStoryPackage : Node
{
    private const int MaximumFileBytes = 16 * 1024 * 1024;
    private const long MaximumPackageBytes = 64 * 1024 * 1024;
    public override void _Ready()
    {
        try
        {
            string[] args = OS.GetCmdlineUserArgs();
            Require(args.Length == 4 && args[0] == "--story-input" && args[2] == "--story-output", "invalid story-package prepare arguments");
            var result = Prepare(args[1], args[3]);
            GD.Print("PAL_PREPARE_STORY_PACKAGE:" + result.StoryUri + ";" + result.VisualUri);
            GetTree().Quit(0);
        }
        catch (Exception error) { GD.PrintErr("Story package native import failed: " + error.Message); GetTree().Quit(1); }
    }

    public static (string StoryUri, string VisualUri) Prepare(string inputDirectory, string outputDirectory)
    {
        string input = Normalize(inputDirectory), output = Normalize(outputDirectory);
        string assets = Normalize(ProjectSettings.GlobalizePath("res://Assets"));
        Require(Inside(output, assets), "story output must be under project Assets");
        string relative = Path.GetRelativePath(assets, output);
        // Do not use LocalizePath: its project-prefix comparison is case-sensitive on Windows.
        output = Normalize(Path.Combine(assets, relative));
        string uri = "res://Assets" + (relative == "." ? "" : "/" + relative.Replace('\\', '/'));
        NativeExplorationProfile.ValidateUri(uri);
        var leases = new Dictionary<string, (FileStream Stream, string Hash)>(PathComparer);
        var ownedFiles = new Dictionary<string, string>(PathComparer);
        var ownedDirectories = new List<string>();
        var allowedFiles = new HashSet<string>(PathComparer);
        var allowedDirectories = new HashSet<string>(PathComparer);
        try
        {
            ValidatePaths(); CheckOutput();
            string[] inputs = Directory.EnumerateFiles(input, "*", SearchOption.AllDirectories).Take(1027).ToArray();
            Require(inputs.Length is >= 3 and <= 1026, "story input requires metadata, index and at most 1024 event files");
            long total = 0;
            var texts = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string path in inputs)
            {
                var lease = Lease(path);
                Require(lease.Length is > 0 and <= MaximumFileBytes, "story input file must contain 1..16777216 bytes: " + path);
                total += lease.Length;
                Require(total <= MaximumPackageBytes, "story input exceeds 67108864 bytes");
                byte[] bytes = new byte[checked((int)lease.Length)]; lease.ReadExactly(bytes); lease.Position = 0;
                try { texts.Add(Path.GetRelativePath(input, path).Replace('\\', '/'), new UTF8Encoding(false, true).GetString(bytes)); }
                catch (DecoderFallbackException error) { throw new InvalidDataException(path + ": invalid UTF-8", error); }
            }
            Require(texts.ContainsKey("story-package.json") && texts.ContainsKey("events/index.json"), "story input requires story-package.json and events/index.json");
            using var metadata = JsonDocument.Parse(texts["story-package.json"]);
            var (story, visual) = ReadMetadata(metadata.RootElement);
            using var index = JsonDocument.Parse(texts["events/index.json"]);
            var eventTexts = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var file in index.RootElement.GetProperty("files").EnumerateArray())
            {
                string? id = file.GetString();
                Require(id is not null && Regex.IsMatch(id, @"^[A-Za-z_][A-Za-z0-9_-]*\z", RegexOptions.CultureInvariant), "invalid event file identifier");
                Require(texts.TryGetValue("events/" + id + ".json", out string? text), "missing complete event file: " + id);
                Require(eventTexts.TryAdd(id!, text!), "duplicate event file identifier");
            }
            Require(eventTexts.Count is > 0 and <= 1024 && texts.Count == eventTexts.Count + 2, "story input must contain exactly metadata and complete events");
            _ = TriggerEventCollection.Parse(texts["events/index.json"], eventTexts);
            story.EventsIndex = uri + "/Events/index.json";
            string[] storySeeds = new[] { story.StaticResources + "/Scenes/Scene.tscn", story.StaticProfile, story.BattleContentUri,
                story.DialogueDirectory + "/Font.tres", story.DialogueDirectory + "/Language.zh-Hans.tres", story.DialogueDirectory + "/Language.en.tres",
                story.DialogueDirectory + "/Prompt-00.tres", story.DialogueDirectory + "/Prompt-01.tres", story.DialogueDirectory + "/Prompt-02.tres" }.Concat(story.Portraits.Values).ToArray();
            string[] visualSeeds = new[] { visual.PlayerSpriteUri, visual.PaletteUri }
                .Concat(visual.Teams.SelectMany(t => new[] { t.BackgroundUri, t.EnemySpriteUri })).ToArray();
            foreach (string root in new[] { story.StaticResources, story.DialogueDirectory })
            {
                string path = DataPath(root, directory: true);
                Require(!Inside(output, path) && !Inside(path, output), "story output and prepared dependency directories must not overlap");
            }
            foreach (string seed in storySeeds.Concat(visualSeeds)) _ = DataPath(seed, directory: false);
            var storyGraph = NativeContentDependencies.ContentDependencies(storySeeds);
            var visualGraph = NativeContentDependencies.ContentDependencies(visualSeeds);
            foreach (string resource in storyGraph.Concat(visualGraph).Distinct(StringComparer.Ordinal))
            {
                string path = DataPath(resource, directory: false);
                Require(!Inside(path, output) && !Inside(output, path), "story output and prepared dependencies must not overlap");
                Lease(path);
            }
            foreach (string resource in storyGraph) story.ResourceSha256.Add(resource, leases[Normalize(ProjectSettings.GlobalizePath(resource))].Hash);
            foreach (string resource in visualGraph) visual.ResourceSha256.Add(resource, leases[Normalize(ProjectSettings.GlobalizePath(resource))].Hash);
            Recheck();
            CreateDirectory(output);
            string stage = Path.Combine(output, ".story-package-" + Guid.NewGuid().ToString("N"));
            Require(!Directory.Exists(stage) && !File.Exists(stage), "story staging collision");
            CreateDirectory(stage); CreateDirectory(Path.Combine(stage, "Events"));
            foreach (string name in eventTexts.Keys.Select(x => x + ".json").Prepend("index.json"))
            {
                string source = Path.Combine(input, "events", name), target = Path.Combine(stage, "Events", name);
                var lease = leases[Normalize(source)];
                using (var stream = new FileStream(target, FileMode.CreateNew, System.IO.FileAccess.Write, FileShare.None))
                { lease.Stream.Position = 0; lease.Stream.CopyTo(stream); lease.Stream.Position = 0; }
                RecordFile(target, lease.Hash);
                story.ResourceSha256.Add(uri + "/Events/" + name, lease.Hash);
            }
            Recheck();
            string events = Path.Combine(output, "Events");
            Directory.Move(Path.Combine(stage, "Events"), events); // no overwrite; not a multi-file transaction
            allowedDirectories.Remove(Path.Combine(stage, "Events")); allowedDirectories.Add(events);
            ownedDirectories.Remove(Path.Combine(stage, "Events")); ownedDirectories.Add(events);
            foreach (string old in ownedFiles.Keys.Where(p => Inside(p, Path.Combine(stage, "Events"))).ToArray())
            {
                string target = Path.Combine(events, Path.GetFileName(old));
                string hash = ownedFiles[old]; ownedFiles.Remove(old); allowedFiles.Remove(old); RecordFile(target, hash);
            }
            string storyTemporary = Path.Combine(stage, "Story.tres"), visualTemporary = Path.Combine(stage, "Visual.tres");
            Save(story, storyTemporary); Save(visual, visualTemporary);
            Recheck();
            string stageUri = uri + "/" + Path.GetFileName(stage);
            var checkedStory = NativeStoryProfile.Load(stageUri + "/Story.tres");
            _ = NativeBattleVisualProfile.Load(stageUri + "/Visual.tres", checkedStory);
            Recheck();
            Publish(visualTemporary, Path.Combine(output, "Visual.tres"));
            Recheck();
            // Story is the completion entry. No success marker is emitted until final validation succeeds.
            Publish(storyTemporary, Path.Combine(output, "Story.tres"));
            Recheck();
            var finalStory = NativeStoryProfile.Load(uri + "/Story.tres");
            _ = NativeBattleVisualProfile.Load(uri + "/Visual.tres", finalStory);
            Recheck();
            Directory.Delete(stage, false); allowedDirectories.Remove(stage); ownedDirectories.Remove(stage);
            return (uri + "/Story.tres", uri + "/Visual.tres");

            FileStream Lease(string path)
            {
                path = Normalize(path);
                if (leases.TryGetValue(path, out var existing)) return existing.Stream;
                FileStream stream = PreparationPathSafety.OpenStableRead(path);
                try { leases.Add(path, (stream, PreparationPathSafety.Sha256(stream))); return stream; }
                catch { stream.Dispose(); throw; }
            }
            void ValidatePaths()
            {
                Require(!Inside(input, output) && !Inside(output, input), "story input/output must not overlap in either direction");
                Require(!Inside(input, ProjectSettings.GlobalizePath("res://")), "story input must be outside src/Pal.Game");
                PreparationPathSafety.CheckNoReparse(input, allowMissing: true);
                Require(Directory.Exists(input), "story input must be an existing directory");
                PreparationPathSafety.CheckTreeNoReparse(input);
                Require(Directory.EnumerateDirectories(input, "*", SearchOption.AllDirectories).All(p => Normalize(p) == Normalize(Path.Combine(input, "events"))),
                    "story input permits only the events directory");
                PreparationPathSafety.CheckNoReparse(output, allowMissing: true);
                Require(!File.Exists(output), "story output is an existing file");
            }
            void CheckOutput()
            {
                if (!Directory.Exists(output)) return;
                PreparationPathSafety.CheckTreeNoReparse(output);
                Require(Directory.EnumerateFileSystemEntries(output, "*", SearchOption.AllDirectories)
                    .All(p => allowedFiles.Contains(p) || allowedDirectories.Contains(p)), "story output must be new or empty; unexpected entries are never overwritten");
            }
            void Recheck()
            {
                ValidatePaths(); CheckOutput();
                foreach (var file in ownedFiles)
                {
                    using var current = PreparationPathSafety.OpenStableRead(file.Key);
                    Require(PreparationPathSafety.Sha256(current) == file.Value, "owned story output changed during preparation");
                }
                Require(Directory.EnumerateFiles(input, "*", SearchOption.AllDirectories).Select(Normalize).ToHashSet(PathComparer).SetEquals(inputs.Select(Normalize)), "story input file set changed");
                foreach (var pair in leases) Require(PreparationPathSafety.MatchesStableRead(pair.Key, pair.Value.Stream, pair.Value.Hash), "story source changed during preparation: " + pair.Key);
                Require(NativeContentDependencies.ContentDependencies(storySeeds).SetEquals(storyGraph)
                    && NativeContentDependencies.ContentDependencies(visualSeeds).SetEquals(visualGraph), "native dependency graph changed");
            }
            void CreateDirectory(string path)
            {
                var missing = new Stack<string>();
                for (string? p = path; p is not null && !Directory.Exists(p); p = Path.GetDirectoryName(p)) missing.Push(p);
                while (missing.TryPop(out string? p))
                {
                    PreparationPathSafety.CheckNoReparse(p, allowMissing: true);
                    if (Directory.Exists(p)) continue;
                    Directory.CreateDirectory(p); ownedDirectories.Add(p);
                    if (p != output && Inside(p, output)) allowedDirectories.Add(p);
                }
            }
            void RecordFile(string path, string hash) { ownedFiles.Add(path, hash); allowedFiles.Add(path); }
            void Save(Resource resource, string path)
            {
                Recheck(); Require(!File.Exists(path), "story temporary collision");
                string resourceUri = uri + "/" + Path.GetRelativePath(output, path).Replace('\\', '/');
                Require(ResourceSaver.Save(resource, resourceUri) == Error.Ok, "could not save typed story resource");
                using var file = PreparationPathSafety.OpenStableRead(path); RecordFile(path, PreparationPathSafety.Sha256(file));
            }
            void Publish(string source, string target)
            {
                Recheck(); File.Move(source, target, false);
                string hash = ownedFiles[source]; ownedFiles.Remove(source); allowedFiles.Remove(source); RecordFile(target, hash);
            }
        }
        catch
        {
            foreach (var lease in leases.Values) lease.Stream.Dispose(); leases.Clear();
            // Never recursively delete output or staging. Changed/replaced files and third-party entries survive.
            foreach (var file in ownedFiles)
                try
                {
                    PreparationPathSafety.CheckNoReparse(file.Key, allowMissing: true);
                    using (var current = PreparationPathSafety.OpenStableRead(file.Key))
                        if (PreparationPathSafety.Sha256(current) != file.Value) continue;
                    File.Delete(file.Key);
                }
                catch { }
            for (int i = ownedDirectories.Count - 1; i >= 0; i--)
                try { PreparationPathSafety.CheckNoReparse(ownedDirectories[i], allowMissing: true); Directory.Delete(ownedDirectories[i], false); } catch { }
            throw;
        }
        finally { foreach (var lease in leases.Values) lease.Stream.Dispose(); }
    }

    private static (PalStoryProfile Story, PalBattleVisualProfile Visual) ReadMetadata(JsonElement root)
    {
        Properties(root, "formatVersion", "sourceIdentity", "staticResources", "staticProfile", "bundleSha256", "entryFileId", "entryId", "ownerId", "entryAddress", "battleContentUri", "dialogueDirectory", "portraits", "visual");
        Require(Int(root, "formatVersion") == 1, "story package formatVersion must be 1");
        var story = new PalStoryProfile { FormatVersion = 1, SourceIdentity = Text(root, "sourceIdentity"), StaticResources = Text(root, "staticResources"),
            StaticProfile = Text(root, "staticProfile"), BundleSha256 = Text(root, "bundleSha256"), EntryFileId = Text(root, "entryFileId"), EntryId = Text(root, "entryId"),
            OwnerId = Int(root, "ownerId"), EntryAddress = Int(root, "entryAddress"), BattleContentUri = Text(root, "battleContentUri"), DialogueDirectory = Text(root, "dialogueDirectory") };
        Require(root.GetProperty("portraits").ValueKind == JsonValueKind.Array, "portraits must be an array");
        foreach (var portrait in root.GetProperty("portraits").EnumerateArray())
        {
            Properties(portrait, "id", "uri"); int id = Int(portrait, "id");
            Require(id is > 0 and <= ushort.MaxValue && !story.Portraits.ContainsKey(id), "invalid or duplicate portrait ID");
            story.Portraits.Add(id, Text(portrait, "uri"));
        }
        var v = root.GetProperty("visual");
        Properties(v, "roleId", "playerSpriteUri", "playerPosition", "paletteUri", "teams");
        var visual = new PalBattleVisualProfile { FormatVersion = 1, SourceIdentity = story.SourceIdentity, RoleId = Int(v, "roleId"),
            PlayerSpriteUri = Text(v, "playerSpriteUri"), PlayerPosition = Position(v, "playerPosition"), PaletteUri = Text(v, "paletteUri") };
        Require(v.GetProperty("teams").ValueKind == JsonValueKind.Array, "teams must be an array");
        visual.Teams = v.GetProperty("teams").EnumerateArray().Select(t =>
        {
            Properties(t, "teamId", "targetId", "backgroundUri", "enemySpriteUri", "enemyPosition", "idleFrames", "magicFrames", "attackFrames", "actWaitFrames");
            return new PalBattleVisualTeam { TeamId = Int(t, "teamId"), TargetId = Int(t, "targetId"), BackgroundUri = Text(t, "backgroundUri"),
                EnemySpriteUri = Text(t, "enemySpriteUri"), EnemyPosition = Position(t, "enemyPosition"), IdleFrames = Int(t, "idleFrames"),
                MagicFrames = Int(t, "magicFrames"), AttackFrames = Int(t, "attackFrames"), ActWaitFrames = Int(t, "actWaitFrames") };
        }).ToArray();
        return (story, visual);
    }
    private static void Properties(JsonElement value, params string[] expected)
    {
        Require(value.ValueKind == JsonValueKind.Object, "metadata must contain objects");
        var actual = value.EnumerateObject().Select(p => p.Name).ToArray();
        Require(actual.Length == expected.Length && actual.Distinct(StringComparer.Ordinal).Count() == actual.Length
            && actual.ToHashSet(StringComparer.Ordinal).SetEquals(expected), "metadata has missing, duplicate or unknown fields; expected " + string.Join(",", expected));
    }
    private static string Text(JsonElement value, string key)
    {
        JsonElement field = value.GetProperty(key);
        Require(field.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(field.GetString()), key + " requires a nonempty string");
        return field.GetString()!;
    }
    private static int Int(JsonElement value, string key)
    { Require(value.GetProperty(key).TryGetInt32(out int number), key + " requires an integer"); return number; }
    private static Vector2I Position(JsonElement value, string key)
    {
        var field = value.GetProperty(key);
        Require(field.ValueKind == JsonValueKind.Array && field.GetArrayLength() == 2, key + " requires two SHORT coordinates");
        Require(field[0].TryGetInt32(out int x) && x is >= short.MinValue and <= short.MaxValue, key + " requires SHORT x");
        Require(field[1].TryGetInt32(out int y) && y is >= short.MinValue and <= short.MaxValue, key + " requires SHORT y");
        return new(x, y);
    }
    private static string DataPath(string uri, bool directory)
    {
        NativeExplorationProfile.ValidateUri(uri);
        string path = Normalize(ProjectSettings.GlobalizePath(uri));
        Require(Inside(path, ProjectSettings.GlobalizePath("res://")), "native dependency must remain inside project");
        PreparationPathSafety.CheckNoReparse(path);
        Require(directory ? Directory.Exists(path) : File.Exists(path), "missing prepared native dependency: " + uri);
        return path;
    }
    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static bool Inside(string child, string parent)
    {
        child = Normalize(child); parent = Normalize(parent);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return child.Equals(parent, comparison) || child.StartsWith(Path.EndsInDirectorySeparator(parent) ? parent : parent + Path.DirectorySeparatorChar, comparison);
    }
    private static void Require(bool condition, string diagnostic) { if (!condition) throw new InvalidDataException(diagnostic); }
}
