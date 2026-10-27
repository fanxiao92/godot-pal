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
        string? temporary = null;
        var createdDirectories = new List<string>();
        try
        {
            string[] args = OS.GetCmdlineUserArgs();
            Require(args.Length == 8 && args[0] == "--battle-input" && args[2] == "--battle-output"
                && args[4] == "--static-resources" && args[6] == "--static-profile", "invalid battle-context prepare arguments");
            string input = Path.GetFullPath(args[1]);
            string output = ProjectSettings.LocalizePath(Path.GetFullPath(args[3])).TrimEnd('/');
            Require(output == "res://Assets" || output.StartsWith("res://Assets/", StringComparison.Ordinal), "battle native output must be under project Assets");
            NativeSceneLoader.ValidateRoot(args[5]);
            NativeExplorationProfile.ValidateUri(args[7]);
            string staticRootPath = PreparationPathSafety.AssetResourcePath(args[5], directory: true);
            string staticProfilePath = PreparationPathSafety.AssetResourcePath(args[7], directory: false);
            Require(IsInside(staticProfilePath, staticRootPath), "selected static profile must be inside selected static resources");
            string staticScene = args[5] + "/Scenes/Scene.tscn";
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
                StaticResources = args[5], StaticProfile = args[7],
                StaticSceneSha256 = sceneHash, StaticProfileSha256 = staticProfileHash
            };
            NativeBattleEnvelopeAdapter.Write(native, profile.ToTypedEnvelope());

            RecheckSources(indexPath, contextPath, indexBytes, contextBytes,
                args[5], staticRootPath, staticScene, staticScenePath, args[7], staticProfilePath,
                sceneLease, profileLease, sceneHash, staticProfileHash);
            CheckedFirstMandatoryBattleProfile checkedNative = NativeFirstMandatoryBattleProfile.ValidateForPreparation(native);
            ValidateStaticSelection(checkedNative);
            RecheckSources(indexPath, contextPath, indexBytes, contextBytes,
                args[5], staticRootPath, staticScene, staticScenePath, args[7], staticProfilePath,
                sceneLease, profileLease, sceneHash, staticProfileHash);

            string destination = output + "/Battle/Profile.tres";
            string destinationPath = ProjectSettings.GlobalizePath(destination);
            Require(!File.Exists(destinationPath), "native battle profile already exists");
            string outputPath = ProjectSettings.GlobalizePath(output);
            string destinationDirectory = Path.GetDirectoryName(destinationPath)!;
            CreateOwnedDirectory(outputPath, createdDirectories);
            CreateOwnedDirectory(destinationDirectory, createdDirectories);
            temporary = destinationPath + ".tmp-" + Guid.NewGuid().ToString("N") + ".tres";
            string temporaryUri = ProjectSettings.LocalizePath(temporary);
            Require(ResourceSaver.Save(native, temporaryUri) == Error.Ok, "could not save native battle profile");

            RecheckSources(indexPath, contextPath, indexBytes, contextBytes,
                args[5], staticRootPath, staticScene, staticScenePath, args[7], staticProfilePath,
                sceneLease, profileLease, sceneHash, staticProfileHash);
            CheckedFirstMandatoryBattleProfile saved = NativeFirstMandatoryBattleProfile.Load(temporaryUri);
            ValidateStaticSelection(saved);
            RecheckSources(indexPath, contextPath, indexBytes, contextBytes,
                args[5], staticRootPath, staticScene, staticScenePath, args[7], staticProfilePath,
                sceneLease, profileLease, sceneHash, staticProfileHash);
            File.Move(temporary, destinationPath, false);
            temporary = null;
            GD.Print("PAL_PREPARE_BATTLE_CONTEXT:" + profile.ProfileId);
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            if (temporary is not null) try { File.Delete(temporary); } catch { }
            for (int i = createdDirectories.Count - 1; i >= 0; i--)
                try { Directory.Delete(createdDirectories[i], false); } catch { }
            GD.PrintErr("Battle context native import failed: " + error.Message);
            GetTree().Quit(1);
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

    private static void RecheckSources(string indexPath, string contextPath, byte[] indexBytes, byte[] contextBytes,
        string staticRootUri, string staticRootPath, string staticSceneUri, string staticScenePath,
        string staticProfileUri, string staticProfilePath, FileStream sceneLease, FileStream profileLease,
        string sceneHash, string profileHash)
    {
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

    private static void CreateOwnedDirectory(string path, List<string> created)
    {
        if (Directory.Exists(path)) return;
        Directory.CreateDirectory(path);
        created.Add(path);
    }

    private static bool IsInside(string child, string parent)
    {
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        parent = Path.TrimEndingDirectorySeparator(parent);
        return child.Equals(parent, comparison) || child.StartsWith(parent + Path.DirectorySeparatorChar, comparison);
    }

    private static void Require(bool condition, string diagnostic)
    {
        if (!condition) throw new InvalidDataException(diagnostic);
    }
}
