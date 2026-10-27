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
        string? createdDirectory = null;
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
            string indexPath = Path.Combine(input, "index.json"), contextPath = Path.Combine(input, "battle-context.json");
            byte[] indexBytes = File.ReadAllBytes(indexPath), contextBytes = File.ReadAllBytes(contextPath);
            FirstMandatoryBattleProfile profile = FirstMandatoryBattleProfile.Load(indexBytes,
                new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["battle-context.json"] = contextBytes });
            string staticScene = args[5] + "/Scenes/Scene.tscn";
            Require(Godot.FileAccess.FileExists(staticScene), "selected static scene is missing");
            Require(Godot.FileAccess.FileExists(args[7]), "selected static exploration profile is missing");
            string sceneHash = Godot.FileAccess.GetSha256(staticScene), staticProfileHash = Godot.FileAccess.GetSha256(args[7]);
            var native = new PalFirstMandatoryBattleProfile
            {
                FormatVersion = 1, ProfileId = profile.ProfileId, ContextSha256 = profile.ContextSha256,
                IndexBytes = indexBytes, ContextBytes = contextBytes, StaticResources = args[5], StaticProfile = args[7],
                StaticSceneSha256 = sceneHash, StaticProfileSha256 = staticProfileHash,
                SceneId = profile.Identity.SceneId, MapId = profile.Identity.MapId, OwnerId = profile.Identity.OwnerId,
                TriggerAddress = profile.Identity.TriggerAddress,
                ResourceIds = profile.Resources.Select(item => item.Id).ToArray(),
                ResourceReferences = profile.Resources.Select(item => item.Reference).ToArray(),
                ResourceSha256 = profile.Resources.Select(item => item.Sha256).ToArray()
            };
            // Re-read every source immediately before writing; a changed source is never paired with the validated bytes.
            Require(File.ReadAllBytes(indexPath).SequenceEqual(indexBytes) && File.ReadAllBytes(contextPath).SequenceEqual(contextBytes),
                "battle context source changed during preparation");
            Require(Godot.FileAccess.GetSha256(staticScene) == sceneHash && Godot.FileAccess.GetSha256(args[7]) == staticProfileHash,
                "selected static native resource changed during preparation");
            string destination = output + "/Battle/Profile.tres";
            string destinationPath = ProjectSettings.GlobalizePath(destination);
            Require(!File.Exists(destinationPath), "native battle profile already exists");
            string destinationDirectory = Path.GetDirectoryName(destinationPath)!;
            if (!Directory.Exists(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
                createdDirectory = destinationDirectory;
            }
            temporary = destinationPath + ".tmp-" + Guid.NewGuid().ToString("N") + ".tres";
            string temporaryUri = ProjectSettings.LocalizePath(temporary);
            Require(ResourceSaver.Save(native, temporaryUri) == Error.Ok, "could not save native battle profile");
            using (var reservation = new FileStream(destinationPath, FileMode.CreateNew, System.IO.FileAccess.Write, FileShare.None)) { }
            File.Delete(destinationPath);
            File.Move(temporary, destinationPath, false);
            temporary = null;
            GD.Print("PAL_PREPARE_BATTLE_CONTEXT:" + profile.ProfileId);
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            if (temporary is not null) try { File.Delete(temporary); } catch { }
            if (createdDirectory is not null) try { Directory.Delete(createdDirectory, false); } catch { }
            GD.PrintErr("Battle context native import failed: " + error.Message);
            GetTree().Quit(1);
        }
    }

    private static void Require(bool condition, string diagnostic)
    {
        if (!condition) throw new InvalidDataException(diagnostic);
    }
}
