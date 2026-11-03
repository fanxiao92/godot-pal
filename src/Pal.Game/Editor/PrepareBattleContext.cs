using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Pal.Core;
using Pal.Game.Map;
using Pal.Game.Profiles;
using Pal.Game.Resources;

namespace Pal.Game.Editor;

// Explicit modern JSON -> native Resource preparation. Runtime never references this node.
public partial class PrepareBattleContext : Node
{
    public override void _Ready()
    {
        try
        {
            string[] args = OS.GetCmdlineUserArgs();
            Require(args.Length is 8 or 10 && args[0] == "--battle-input" && args[2] == "--battle-output"
                && args[4] == "--static-resources" && args[6] == "--static-profile"
                && (args.Length == 8 || args[8] == "--battle-resources"), "invalid battle-context prepare arguments");
            _ = Prepare(args[1], args[3], args[5], args[7], args.Length == 10 ? args[9] : null);
            GD.Print("PAL_PREPARE_BATTLE_CONTEXT:" + FirstMandatoryBattleProfile.ProfileIdValue);
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            GD.PrintErr("Battle context native import failed: " + error.Message);
            GetTree().Quit(1);
        }
    }

    // Explicit offline preparation shared by the editor entry and in-host callers, never runtime startup.
    public static string Prepare(string inputDirectory, string outputDirectory, string staticResources, string staticProfile,
        string? resourceBindingPath = null)
    {
        string? temporary = null;
        var createdDirectories = new List<string>();
        try
        {
            RealClosureBinding? binding = resourceBindingPath is null ? null : RealClosureBinding.Parse(resourceBindingPath);
            string input = Path.TrimEndingDirectorySeparator(Path.GetFullPath(inputDirectory));
            string outputPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputDirectory));
            string output = ProjectSettings.LocalizePath(outputPath).TrimEnd('/');
            Require(IsInside(outputPath, ProjectSettings.GlobalizePath("res://Assets")), "battle native output must be under project Assets");
            ValidatePreparationPaths(input, outputPath);
            RequireEmptyOutput(outputPath);
            NativeSceneLoader.ValidateRoot(staticResources);
            NativeExplorationProfile.ValidateUri(staticProfile);
            string staticRootPath = PreparationPathSafety.AssetResourcePath(staticResources, directory: true);
            string staticProfilePath = PreparationPathSafety.AssetResourcePath(staticProfile, directory: false);
            Require(IsInside(staticProfilePath, staticRootPath), "selected static profile must be inside selected static resources");
            Require(!IsInside(outputPath, staticRootPath) && !IsInside(staticRootPath, outputPath),
                "battle native output and selected static resources must not overlap in either direction");
            string staticScene = staticResources + "/Scenes/Scene.tscn";
            string staticScenePath = PreparationPathSafety.AssetResourcePath(staticScene, directory: false);
            PreparationPathSafety.CheckTreeNoReparse(staticRootPath);

            using FileStream sceneLease = PreparationPathSafety.OpenStableRead(staticScenePath);
            using FileStream profileLease = PreparationPathSafety.OpenStableRead(staticProfilePath);
            string sceneHash = PreparationPathSafety.Sha256(sceneLease);
            string staticProfileHash = PreparationPathSafety.Sha256(profileLease);

            string indexPath = Path.Combine(input, "index.json"), contextPath = Path.Combine(input, "battle-context.json");
            byte[] indexBytes = File.ReadAllBytes(indexPath), contextBytes = File.ReadAllBytes(contextPath);
            FirstMandatoryBattleProfile profile = FirstMandatoryBattleProfile.Load(indexBytes,
                new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["battle-context.json"] = contextBytes });
            var native = new PalFirstMandatoryBattleProfile
            {
                FormatVersion = 1, ProfileId = profile.ProfileId,
                StaticResources = staticResources, StaticProfile = staticProfile,
                StaticSceneSha256 = sceneHash, StaticProfileSha256 = staticProfileHash
            };
            NativeBattleEnvelopeAdapter.Write(native, profile.ToTypedEnvelope());
            if (binding is not null) BindRealClosure(native, binding);

            RecheckSources(input, outputPath, indexPath, contextPath, indexBytes, contextBytes,
                staticResources, staticRootPath, staticScene, staticScenePath, staticProfile, staticProfilePath,
                sceneLease, profileLease, sceneHash, staticProfileHash);
            CheckedFirstMandatoryBattleProfile checkedNative = NativeFirstMandatoryBattleProfile.ValidateForPreparation(native);
            ValidateStaticSelection(checkedNative);
            RecheckSources(input, outputPath, indexPath, contextPath, indexBytes, contextBytes,
                staticResources, staticRootPath, staticScene, staticScenePath, staticProfile, staticProfilePath,
                sceneLease, profileLease, sceneHash, staticProfileHash);

            string destination = output + "/Battle/Profile.tres";
            string destinationPath = ProjectSettings.GlobalizePath(destination);
            Require(!File.Exists(destinationPath), "native battle profile already exists");
            string destinationDirectory = Path.GetDirectoryName(destinationPath)!;
            RequireEmptyOutput(outputPath);
            CreateOwnedDirectory(outputPath, createdDirectories);
            CreateOwnedDirectory(destinationDirectory, createdDirectories);
            temporary = destinationPath + ".tmp-" + Guid.NewGuid().ToString("N") + ".tres";
            string temporaryUri = ProjectSettings.LocalizePath(temporary);
            Require(ResourceSaver.Save(native, temporaryUri) == Error.Ok, "could not save native battle profile");

            RecheckSources(input, outputPath, indexPath, contextPath, indexBytes, contextBytes,
                staticResources, staticRootPath, staticScene, staticScenePath, staticProfile, staticProfilePath,
                sceneLease, profileLease, sceneHash, staticProfileHash);
            CheckedFirstMandatoryBattleProfile saved = NativeFirstMandatoryBattleProfile.Load(temporaryUri);
            ValidateStaticSelection(saved);
            RecheckSources(input, outputPath, indexPath, contextPath, indexBytes, contextBytes,
                staticResources, staticRootPath, staticScene, staticScenePath, staticProfile, staticProfilePath,
                sceneLease, profileLease, sceneHash, staticProfileHash);
            File.Move(temporary, destinationPath, false);
            temporary = null;
            return destination;
        }
        catch
        {
            if (temporary is not null) try { File.Delete(temporary); } catch { }
            for (int i = createdDirectories.Count - 1; i >= 0; i--)
                try { Directory.Delete(createdDirectories[i], false); } catch { }
            throw;
        }
    }

    // Fills the #53 real resource closure binding and its exact hash graph. The author JSON never
    // carries hashes; this production function computes them from the actual native graph.
    private static void BindRealClosure(PalFirstMandatoryBattleProfile native, RealClosureBinding binding)
    {
        native.EventsIndex = binding.EventsIndex;
        native.BattleContentUri = binding.BattleContentUri;
        native.DialogueDirectory = binding.DialogueDirectory;
        native.VisualUri = binding.VisualUri;
        native.ImagePlayerSpriteUris = binding.PlayerSpriteUris.ToArray();
        native.ImageEnemyId = binding.EnemySpriteId;
        native.ImageEnemyArchiveEntry = binding.EnemyArchiveEntry;
        native.ImageRoleSpriteIds = binding.RoleSpriteIds.ToArray();
        native.ImageBattlefieldId = binding.BattlefieldId;
        native.ImageBattlefieldArchiveEntry = binding.BattlefieldArchiveEntry;
        native.ImageIncludesLayoutAndEffectFrames = binding.IncludesLayoutAndEffectFrames;
        foreach ((int id, string uri) in binding.Portraits) native.Portraits.Add(id, uri);
        string dialogue = binding.DialogueDirectory;
        string[] seeds = new[] { binding.BattleContentUri, binding.EventsIndex, dialogue + "/Font.tres",
            dialogue + "/Language.zh-Hans.tres", dialogue + "/Language.en.tres",
            dialogue + "/Prompt-00.tres", dialogue + "/Prompt-01.tres", dialogue + "/Prompt-02.tres" }
            .Concat(binding.Portraits.Select(pair => pair.Uri)).Concat(binding.PlayerSpriteUris).ToArray();
        foreach (string seed in seeds) _ = PreparationPathSafety.AssetResourcePath(seed, directory: seed.EndsWith(".tres") || seed.EndsWith(".json"));
        var graph = NativeContentDependencies.ContentDependencies(seeds);
        string indexPath = ProjectSettings.GlobalizePath(binding.EventsIndex);
        using var indexDocument = System.Text.Json.JsonDocument.Parse(File.ReadAllText(indexPath));
        string directory = binding.EventsIndex[..binding.EventsIndex.LastIndexOf('/')];
        foreach (var file in indexDocument.RootElement.GetProperty("files").EnumerateArray())
        {
            string uri = directory + "/" + file.GetString() + ".json";
            graph.Add(uri);
        }
        foreach (string uri in graph)
        {
            string path = ProjectSettings.GlobalizePath(PreparationPathSafety.AssetResourcePath(uri, directory: false));
            using FileStream stream = PreparationPathSafety.OpenStableRead(path);
            native.ResourceSha256.Add(uri, PreparationPathSafety.Sha256(stream));
        }
    }

    private sealed record RealClosureBinding(string EventsIndex, string BattleContentUri, string DialogueDirectory, string VisualUri,
        IReadOnlyList<(int Id, string Uri)> Portraits, IReadOnlyList<string> PlayerSpriteUris,
        int EnemySpriteId, string EnemyArchiveEntry, IReadOnlyList<string> RoleSpriteIds,
        int BattlefieldId, string BattlefieldArchiveEntry, bool IncludesLayoutAndEffectFrames)
    {
        public static RealClosureBinding Parse(string path)
        {
            string physical = Path.GetFullPath(path);
            PreparationPathSafety.CheckNoReparse(physical, allowMissing: false);
            Require(!IsInside(physical, ProjectSettings.GlobalizePath("res://")), "battle resource binding must be outside src/Pal.Game");
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(physical));
            var root = document.RootElement;
            Require(root.ValueKind == System.Text.Json.JsonValueKind.Object, "battle resource binding must be an object");
            foreach (var property in root.EnumerateObject())
                Require(property.Name is "formatVersion" or "eventsIndex" or "battleContentUri" or "dialogueDirectory" or "visualUri"
                    or "portraits" or "playerSpriteUris" or "enemySpriteId" or "enemyArchiveEntry" or "roleSpriteIds"
                    or "battlefieldId" or "battlefieldArchiveEntry" or "includesLayoutAndEffectFrames",
                    "battle resource binding: unknown field '" + property.Name + "'");
            Require(root.TryGetProperty("formatVersion", out var version) && version.ValueKind == System.Text.Json.JsonValueKind.Number && version.GetInt32() == 1,
                "battle resource binding: formatVersion must be 1");
            string Text(string name) => root.GetProperty(name).ValueKind == System.Text.Json.JsonValueKind.String
                ? root.GetProperty(name).GetString()! : throw new InvalidDataException("battle resource binding: " + name + " must be a string");
            int Number(string name) => root.GetProperty(name).ValueKind == System.Text.Json.JsonValueKind.Number && root.GetProperty(name).TryGetInt32(out int value)
                ? value : throw new InvalidDataException("battle resource binding: " + name + " must be an integer");
            string[] List(string name) => root.GetProperty(name).EnumerateArray().Select(item => item.ValueKind == System.Text.Json.JsonValueKind.String
                ? item.GetString()! : throw new InvalidDataException("battle resource binding: " + name + " entries must be strings")).ToArray();
            var portraits = new List<(int Id, string Uri)>();
            foreach (var item in root.GetProperty("portraits").EnumerateArray())
            {
                int portraitId = 0;
                string portraitUri = "";
                bool valid = item.TryGetProperty("id", out var id) && id.ValueKind == System.Text.Json.JsonValueKind.Number && id.TryGetInt32(out portraitId)
                    && item.TryGetProperty("uri", out var uriElement) && uriElement.ValueKind == System.Text.Json.JsonValueKind.String
                    && (portraitUri = uriElement.GetString()!) is not null;
                Require(valid, "battle resource binding: portraits entries require integer id and string uri");
                portraits.Add((portraitId, portraitUri));
            }
            return new(Text("eventsIndex"), Text("battleContentUri"), Text("dialogueDirectory"), Text("visualUri"),
                portraits, List("playerSpriteUris"), Number("enemySpriteId"), Text("enemyArchiveEntry"), List("roleSpriteIds"),
                Number("battlefieldId"), Text("battlefieldArchiveEntry"),
                root.GetProperty("includesLayoutAndEffectFrames").ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False
                    && root.GetProperty("includesLayoutAndEffectFrames").GetBoolean());
        }
    }

    private static void ValidateStaticSelection(CheckedFirstMandatoryBattleProfile battle)
    {
        PalMapScene? map = null;
        try
        {
            map = NativeSceneLoader.Load(battle.StaticResources);
            _ = NativeFirstMandatoryBattleProfile.ValidateStaticSelection(battle, map);
        }
        finally { map?.Free(); }
    }

    private static void RecheckSources(string input, string output, string indexPath, string contextPath, byte[] indexBytes, byte[] contextBytes,
        string staticRootUri, string staticRootPath, string staticSceneUri, string staticScenePath,
        string staticProfileUri, string staticProfilePath, FileStream sceneLease, FileStream profileLease,
        string sceneHash, string profileHash)
    {
        ValidatePreparationPaths(input, output);
        Require(File.ReadAllBytes(indexPath).SequenceEqual(indexBytes) && File.ReadAllBytes(contextPath).SequenceEqual(contextBytes),
            "battle context source changed during preparation");
        Require(PreparationPathSafety.AssetResourcePath(staticRootUri, directory: true) == staticRootPath
            && PreparationPathSafety.AssetResourcePath(staticSceneUri, directory: false) == staticScenePath
            && PreparationPathSafety.AssetResourcePath(staticProfileUri, directory: false) == staticProfilePath,
            "selected static native resource identity changed during preparation");
        PreparationPathSafety.CheckTreeNoReparse(staticRootPath);
        Require(PreparationPathSafety.MatchesStableRead(staticScenePath, sceneLease, sceneHash)
            && PreparationPathSafety.MatchesStableRead(staticProfilePath, profileLease, profileHash),
            "selected static native resource changed during preparation");
    }

    private static void ValidatePreparationPaths(string input, string output)
    {
        Require(!IsInside(input, output) && !IsInside(output, input),
            "battle input and output must not overlap in either direction");
        Require(!IsInside(input, ProjectSettings.GlobalizePath("res://")), "battle input must be outside src/Pal.Game");
        PreparationPathSafety.CheckNoReparse(input, allowMissing: true);
        Require(Directory.Exists(input), "battle input must be an existing authored battle-context directory");
        PreparationPathSafety.CheckTreeNoReparse(input);
        string[] files = Directory.EnumerateFiles(input, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(input, path).Replace(Path.DirectorySeparatorChar, '/')).Order(StringComparer.Ordinal).ToArray();
        Require(files.SequenceEqual(new[] { "battle-context.json", "index.json" }),
            "battle input must contain exactly index.json and battle-context.json");
        PreparationPathSafety.CheckNoReparse(output, allowMissing: true);
        Require(!File.Exists(output), "battle native output is an existing file");
    }

    private static void RequireEmptyOutput(string output) =>
        Require(!Directory.Exists(output) || !Directory.EnumerateFileSystemEntries(output).Any(),
            "battle native output must be new or empty; no overwrite is supported");

    private static void CreateOwnedDirectory(string path, List<string> created)
    {
        var missing = new Stack<string>();
        string? current = Path.GetFullPath(path);
        while (current is not null && !Directory.Exists(current))
        {
            missing.Push(current);
            current = Path.GetDirectoryName(current);
        }

        while (missing.Count > 0)
        {
            current = missing.Pop();
            if (Directory.Exists(current)) continue;
            Directory.CreateDirectory(current);
            created.Add(current);
        }
    }

    private static bool IsInside(string child, string parent)
    {
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        child = Path.TrimEndingDirectorySeparator(Path.GetFullPath(child));
        parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        string prefix = Path.EndsInDirectorySeparator(parent) ? parent : parent + Path.DirectorySeparatorChar;
        return child.Equals(parent, comparison) || child.StartsWith(prefix, comparison);
    }

    private static void Require(bool condition, string diagnostic)
    {
        if (!condition) throw new InvalidDataException(diagnostic);
    }
}
