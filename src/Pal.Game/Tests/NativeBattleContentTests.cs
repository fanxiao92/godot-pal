using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GdUnit4;
using Godot;
using Pal.Core;
using Pal.Game.Profiles;
using Pal.Game.Resources;
using System.Text.Json;
using System.Text.Json.Nodes;
using static GdUnit4.Assertions;

namespace Pal.Game.Tests;

[TestSuite, RequireGodotRuntime]
public class NativeBattleContentTests
{
    [TestCase]
    public void ProjectionFieldsWithoutPresenceRejectThroughLegacyCreateCore()
    {
        using var native = NativeBattleContent.Write(Data());
        native.ProjectionReceiptSha256 = new string('a', 64);
        InvalidDataException? error = null;
        try { NativeBattleContent.CreateCore(native, "projection-presence.tres"); }
        catch (InvalidDataException caught) { error = caught; }
        AssertThat(error is not null).OverrideFailureMessage("Projection fields without presence must be rejected by CreateCore").IsTrue();
        AssertThat(error!.Message.Contains("projection-presence.tres", StringComparison.Ordinal)).IsTrue();
        AssertThat(error.Message.Contains("ProjectionPresent", StringComparison.Ordinal)).IsTrue();
    }

    [TestCase]
    public void ProjectionSourceRoundtripKeepsIndependentIdentitiesOrderSlotsAndFrozenCollections()
    {
        BattleProjectionModernInput[] inputs = ProjectionInputs();
        int[] roles = [4, 0], teams = [65535, 0];
        BattleProjectionEnemySlot[] slots = ProjectionSlots();
        var source = ProjectionSource(inputs, roles, teams, slots);
        inputs[0] = new("wrong", "", 0); roles[0] = 2; teams[0] = 2;
        slots[0] = new(2, 0, 0, "wrong");
        using var native = NativeBattleContent.Write(ProjectionContent(), source);
        var checkedContent = NativeBattleContent.CreateChecked(native, "projection-valid.tres");
        AssertProjection(checkedContent);
        string root = "res://Tests/RuntimeBattleContent/" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(root));
        try
        {
            string uri = root + "/Projection.tres";
            AssertThat(ResourceSaver.Save(native, uri)).IsEqual(Error.Ok);
            var loaded = NativeBattleContent.LoadChecked(uri);
            AssertProjection(loaded);
            AssertThat(NativeBattleContent.Load(uri).Players.Select(p => p.RoleId).ToArray()).IsEqual(new[] { 4, 0 });
            native.ProjectionInputFiles[0] = "changed"; native.ProjectionInputSha256[0] = "changed";
            native.ProjectionInputByteLengths[0] = 0; native.ProjectionRoleIds[0] = 3;
            native.ProjectionTeamIds[0] = 3; native.ProjectionEnemySlots[0] = 3;
            native.ProjectionReceiptByteLength = 0; native.Players[0].Attack = 1;
            foreach (var copy in new[] { checkedContent, loaded, NativeBattleContent.LoadChecked(uri) }) AssertProjection(copy);
            var frozen = loaded.ProjectedSource!;
            AssertReadOnly(frozen.ModernInputs, new BattleProjectionModernInput("changed", "", 0));
            AssertReadOnly(frozen.RoleIds, 3); AssertReadOnly(frozen.TeamIds, 3);
            AssertReadOnly(frozen.EnemySlots, new BattleProjectionEnemySlot(0, 0, 0, "changed"));
        }
        finally { Directory.Delete(ProjectSettings.GlobalizePath(root), true); }
    }

    [TestCase(4)]
    [TestCase(0)]
    public void ProjectionSingleRoleAndEnemyRemainSupportedWithoutRequiringRoleZero(int roleId)
    {
        var data = Data();
        var content = new TriggerBattleContent(data.InitialRole with { RoleId = roleId }, data.Player with { RoleId = roleId }, data.Growth,
            new Dictionary<int, ClassicBattleEnemy> { [0] = data.Teams[18] with { Id = 1 } });
        var source = new BattleProjectionSourceInfo(1, "m5-base-table-physical-projection", "independent-base-table-context-not-natural-save",
            new string('0', 64), 1, new string('f', 64), 1, ProjectionInputs(), [roleId], [0], [
            new(0, 0, 65535, "skip-FFFF"), new(0, 1, 1, "loaded", 0, 0, 1),
            new(0, 2, 65535, "skip-FFFF"), new(0, 3, 65535, "skip-FFFF"), new(0, 4, 65535, "skip-FFFF")]);
        using var native = NativeBattleContent.Write(content, source);
        var result = NativeBattleContent.CreateChecked(native);
        AssertThat(result.ProjectedSource!.RoleIds.ToArray()).IsEqual(new[] { roleId });
        AssertThat(result.Content.Players.Count).IsEqual(1);
        AssertThat(result.Content.EnemyTeams[0].Count).IsEqual(1);
    }

    [TestCase]
    public void ProjectionAbsentStaysNullAcrossOldAndCheckedSaveLoad()
    {
        using var native = NativeBattleContent.Write(Data());
        AssertThat(NativeBattleContent.CreateChecked(native).ProjectedSource is null).IsTrue();
        AssertThat(NativeBattleContent.CreateCore(native).Player.RoleId).IsEqual(1);
        using var explicitNull = NativeBattleContent.Write(Data(), null);
        AssertThat(explicitNull.ProjectionFormatVersion).IsEqual(-1);
        AssertThat(explicitNull.ProjectionReceiptByteLength).IsEqual(-1L);
        AssertThat(explicitNull.ProjectionNumericByteLength).IsEqual(-1L);
        string root = "res://Tests/RuntimeBattleContent/" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(root));
        try
        {
            string uri = root + "/Legacy.tres";
            AssertThat(ResourceSaver.Save(native, uri)).IsEqual(Error.Ok);
            AssertThat(NativeBattleContent.LoadChecked(uri).ProjectedSource is null).IsTrue();
            AssertThat(NativeBattleContent.Load(uri).Player.RoleId).IsEqual(1);
        }
        finally { Directory.Delete(ProjectSettings.GlobalizePath(root), true); }
    }

    [TestCase("version")]
    [TestCase("kind")]
    [TestCase("snapshot")]
    [TestCase("receipt-hash")]
    [TestCase("receipt-length")]
    [TestCase("numeric-hash")]
    [TestCase("numeric-length")]
    [TestCase("files")]
    [TestCase("hashes")]
    [TestCase("lengths")]
    [TestCase("roles")]
    [TestCase("teams")]
    [TestCase("slots")]
    [TestCase("null-array")]
    [TestCase("null-string")]
    public void ProjectionAbsentRejectsEveryNonDefaultField(string field)
    {
        using var native = NativeBattleContent.Write(Data());
        switch (field)
        {
            case "version": native.ProjectionFormatVersion = 0; break;
            case "kind": native.ProjectionKind = "x"; break;
            case "snapshot": native.ProjectionSnapshotKind = "x"; break;
            case "receipt-hash": native.ProjectionReceiptSha256 = "x"; break;
            case "receipt-length": native.ProjectionReceiptByteLength = 0; break;
            case "numeric-hash": native.ProjectionNumericSha256 = "x"; break;
            case "numeric-length": native.ProjectionNumericByteLength = 0; break;
            case "files": native.ProjectionInputFiles = ["x"]; break;
            case "hashes": native.ProjectionInputSha256 = ["x"]; break;
            case "lengths": native.ProjectionInputByteLengths = [0]; break;
            case "roles": native.ProjectionRoleIds = [0]; break;
            case "teams": native.ProjectionTeamIds = [0]; break;
            case "slots": native.ProjectionEnemySlots = [0]; break;
            case "null-array": native.ProjectionInputFiles = null!; break;
            case "null-string": native.ProjectionKind = null!; break;
            default: throw new ArgumentException(field);
        }
        AssertProjectionRejected(native, "ProjectionPresent");
    }

    [TestCase("half-declared", "FormatVersion")]
    [TestCase("version", "FormatVersion")]
    [TestCase("kind", "Kind")]
    [TestCase("snapshot", "SnapshotKind")]
    [TestCase("receipt-empty", "ReceiptSha256")]
    [TestCase("receipt-short", "ReceiptSha256")]
    [TestCase("receipt-upper", "ReceiptSha256")]
    [TestCase("receipt-nonhex", "ReceiptSha256")]
    [TestCase("receipt-zero", "ReceiptByteLength")]
    [TestCase("receipt-negative", "ReceiptByteLength")]
    [TestCase("numeric-empty", "NumericSha256")]
    [TestCase("numeric-short", "NumericSha256")]
    [TestCase("numeric-upper", "NumericSha256")]
    [TestCase("numeric-nonhex", "NumericSha256")]
    [TestCase("numeric-zero", "NumericByteLength")]
    [TestCase("numeric-negative", "NumericByteLength")]
    [TestCase("numeric-large", "NumericByteLength")]
    [TestCase("inputs-missing", "ModernInputs")]
    [TestCase("inputs-extra", "ModernInputs")]
    [TestCase("inputs-duplicate", "ModernInputs")]
    [TestCase("inputs-unknown", "ModernInputs")]
    [TestCase("inputs-path", "ModernInputs")]
    [TestCase("inputs-case", "ModernInputs")]
    [TestCase("inputs-order", "ModernInputs")]
    [TestCase("input-hash", "Sha256")]
    [TestCase("input-zero", "ByteLength")]
    [TestCase("input-negative", "ByteLength")]
    [TestCase("input-large", "ByteLength")]
    [TestCase("inputs-hashes-short", "ModernInputs")]
    [TestCase("inputs-lengths-short", "ModernInputs")]
    [TestCase("inputs-null", "ProjectionInputFiles")]
    [TestCase("input-row-null", "ProjectionInputFiles")]
    [TestCase("roles-empty", "RoleIds")]
    [TestCase("roles-many", "RoleIds")]
    [TestCase("roles-duplicate", "RoleIds")]
    [TestCase("roles-negative", "RoleIds")]
    [TestCase("role5", "RoleIds")]
    [TestCase("roles-order", "RoleIds")]
    [TestCase("roles-identity", "RoleIds")]
    [TestCase("teams-empty", "TeamIds")]
    [TestCase("teams-duplicate", "TeamIds")]
    [TestCase("teams-negative", "TeamIds")]
    [TestCase("teams-overflow", "TeamIds")]
    [TestCase("teams-order", "TeamIds")]
    [TestCase("teams-identity", "TeamIds")]
    [TestCase("slot-half", "EnemySlots")]
    [TestCase("slot-missing", "EnemySlots")]
    [TestCase("slot-extra", "EnemySlots")]
    [TestCase("slot-duplicate", "EnemySlots")]
    [TestCase("slot-negative", "EnemySlots")]
    [TestCase("slot-overflow", "EnemySlots")]
    [TestCase("slot-team", "EnemySlots")]
    [TestCase("slot-disposition", "EnemySlots")]
    [TestCase("loaded-zero", "EnemySlots")]
    [TestCase("loaded-FFFF", "EnemySlots")]
    [TestCase("loaded-negative", "EnemySlots")]
    [TestCase("loaded-overflow", "EnemySlots")]
    [TestCase("enemy-missing", "EnemySlots")]
    [TestCase("enemy-overflow", "EnemySlots")]
    [TestCase("enemy-negative", "EnemySlots")]
    [TestCase("compact-gap", "EnemySlots")]
    [TestCase("compact-missing", "EnemySlots")]
    [TestCase("battle-id", "EnemySlots")]
    [TestCase("battle-missing", "EnemySlots")]
    [TestCase("skip-zero", "EnemySlots")]
    [TestCase("skip-enemy", "EnemySlots")]
    [TestCase("skip-compact", "EnemySlots")]
    [TestCase("skip-battle", "EnemySlots")]
    [TestCase("all-FFFF", "EnemySlots")]
    [TestCase("three-loaded", "EnemySlots")]
    [TestCase("native-enemy-id", "EnemySlots")]
    [TestCase("native-enemy-count", "EnemySlots")]
    [TestCase("native-role-coverage", "growth")]
    [TestCase("native-role-hook", "dyingScript")]
    [TestCase("native-enemy-hook", "battleEndScript")]
    [TestCase("native-magic", "magic")]
    public void ProjectionSelectedRejectsMalformedMetadataOrPhysicalCapabilityThroughBothReads(string defect, string cause)
    {
        using var native = NativeBattleContent.Write(ProjectionContent(), ProjectionSource());
        switch (defect)
        {
            case "half-declared": native.ProjectionFormatVersion = -1; break;
            case "version": native.ProjectionFormatVersion = 2; break;
            case "kind": native.ProjectionKind = "unknown"; break;
            case "snapshot": native.ProjectionSnapshotKind = "natural-save"; break;
            case "receipt-empty": native.ProjectionReceiptSha256 = ""; break;
            case "receipt-short": native.ProjectionReceiptSha256 = "a"; break;
            case "receipt-upper": native.ProjectionReceiptSha256 = new string('A', 64); break;
            case "receipt-nonhex": native.ProjectionReceiptSha256 = new string('g', 64); break;
            case "receipt-zero": native.ProjectionReceiptByteLength = 0; break;
            case "receipt-negative": native.ProjectionReceiptByteLength = -2; break;
            case "numeric-empty": native.ProjectionNumericSha256 = ""; break;
            case "numeric-short": native.ProjectionNumericSha256 = "a"; break;
            case "numeric-upper": native.ProjectionNumericSha256 = new string('B', 64); break;
            case "numeric-nonhex": native.ProjectionNumericSha256 = new string('x', 64); break;
            case "numeric-zero": native.ProjectionNumericByteLength = 0; break;
            case "numeric-negative": native.ProjectionNumericByteLength = -1; break;
            case "numeric-large": native.ProjectionNumericByteLength = 16777217; break;
            case "inputs-missing": native.ProjectionInputFiles = native.ProjectionInputFiles[..13]; break;
            case "inputs-extra": native.ProjectionInputFiles = [.. native.ProjectionInputFiles, "extra.json"]; break;
            case "inputs-duplicate": native.ProjectionInputFiles[1] = native.ProjectionInputFiles[0]; break;
            case "inputs-unknown": native.ProjectionInputFiles[0] = "extra.json"; break;
            case "inputs-path": native.ProjectionInputFiles[0] = "../battle-effect-index.json"; break;
            case "inputs-case": native.ProjectionInputFiles[0] = "Battle-effect-index.json"; break;
            case "inputs-order": native.ProjectionInputFiles = native.ProjectionInputFiles.Reverse().ToArray(); break;
            case "input-hash": native.ProjectionInputSha256[13] = new string('Z', 64); break;
            case "input-zero": native.ProjectionInputByteLengths[13] = 0; break;
            case "input-negative": native.ProjectionInputByteLengths[13] = -1; break;
            case "input-large": native.ProjectionInputByteLengths[13] = 67108865; break;
            case "inputs-hashes-short": native.ProjectionInputSha256 = []; break;
            case "inputs-lengths-short": native.ProjectionInputByteLengths = []; break;
            case "inputs-null": native.ProjectionInputFiles = null!; break;
            case "input-row-null": native.ProjectionInputFiles[0] = null!; break;
            case "roles-empty": native.ProjectionRoleIds = []; break;
            case "roles-many": native.ProjectionRoleIds = [4, 0, 1]; break;
            case "roles-duplicate": native.ProjectionRoleIds = [4, 4]; break;
            case "roles-negative": native.ProjectionRoleIds = [-1, 0]; break;
            case "role5": native.ProjectionRoleIds = [5, 0]; break;
            case "roles-order": native.ProjectionRoleIds = [0, 4]; break;
            case "roles-identity": native.ProjectionRoleIds = [3, 0]; break;
            case "teams-empty": native.ProjectionTeamIds = []; break;
            case "teams-duplicate": native.ProjectionTeamIds = [65535, 65535]; break;
            case "teams-negative": native.ProjectionTeamIds = [-1, 0]; break;
            case "teams-overflow": native.ProjectionTeamIds = [65536, 0]; break;
            case "teams-order": native.Teams = native.Teams.Reverse().ToArray(); break;
            case "teams-identity": native.Teams[0].TeamId = 65534; break;
            case "slot-half": native.ProjectionEnemySlots = native.ProjectionEnemySlots[..^1]; break;
            case "slot-missing": native.ProjectionEnemySlots = native.ProjectionEnemySlots[..^7]; break;
            case "slot-extra": native.ProjectionEnemySlots = [.. native.ProjectionEnemySlots, 0, 0, 0, -1, -1, -1, 0]; break;
            case "slot-duplicate": native.ProjectionEnemySlots[8] = 0; break;
            case "slot-negative": native.ProjectionEnemySlots[1] = -1; break;
            case "slot-overflow": native.ProjectionEnemySlots[29] = 5; break;
            case "slot-team": native.ProjectionEnemySlots[0] = 0; break;
            case "slot-disposition": native.ProjectionEnemySlots[6] = 2; break;
            case "loaded-zero": native.ProjectionEnemySlots[2] = 0; break;
            case "loaded-FFFF": native.ProjectionEnemySlots[2] = 65535; break;
            case "loaded-negative": native.ProjectionEnemySlots[2] = -1; break;
            case "loaded-overflow": native.ProjectionEnemySlots[2] = 65536; break;
            case "enemy-missing": native.ProjectionEnemySlots[3] = -1; break;
            case "enemy-overflow": native.ProjectionEnemySlots[3] = 65536; break;
            case "enemy-negative": native.ProjectionEnemySlots[3] = -2; break;
            case "compact-gap": native.ProjectionEnemySlots[18] = 2; break;
            case "compact-missing": native.ProjectionEnemySlots[4] = -1; break;
            case "battle-id": native.ProjectionEnemySlots[5] = 2; break;
            case "battle-missing": native.ProjectionEnemySlots[5] = -1; break;
            case "skip-zero": native.ProjectionEnemySlots[9] = 0; break;
            case "skip-enemy": native.ProjectionEnemySlots[10] = 0; break;
            case "skip-compact": native.ProjectionEnemySlots[11] = 0; break;
            case "skip-battle": native.ProjectionEnemySlots[12] = 0; break;
            case "all-FFFF":
                native.ProjectionEnemySlots[2] = 65535; native.ProjectionEnemySlots[3] = -1;
                native.ProjectionEnemySlots[4] = -1; native.ProjectionEnemySlots[5] = -1; native.ProjectionEnemySlots[6] = 0;
                native.ProjectionEnemySlots[16] = 65535; native.ProjectionEnemySlots[17] = -1;
                native.ProjectionEnemySlots[18] = -1; native.ProjectionEnemySlots[19] = -1; native.ProjectionEnemySlots[20] = 0; break;
            case "three-loaded":
                native.ProjectionEnemySlots[30] = 97; native.ProjectionEnemySlots[31] = 0;
                native.ProjectionEnemySlots[32] = 2; native.ProjectionEnemySlots[33] = 3; native.ProjectionEnemySlots[34] = 1; break;
            case "native-enemy-id": native.Teams[0].Enemies[0].Id = 7; break;
            case "native-enemy-count": native.Teams[0].Enemies = [native.Teams[0].Enemies[0]]; break;
            case "native-role-coverage": native.Growth = native.Growth[..1]; break;
            case "native-role-hook": native.Players[1].DyingScript = 7; break;
            case "native-enemy-hook": native.Teams[1].Enemies[1].BattleEndScript = 7; break;
            case "native-magic": native.Teams[1].Enemies[1].MagicId = 65535; break;
            default: throw new ArgumentException(defect);
        }
        AssertProjectionRejected(native, cause);
    }

    [TestCase]
    public void ProjectionWriterRejectsMismatchedSelectionBeforePublishingResource()
    {
        InvalidDataException? error = null;
        try { NativeBattleContent.Write(Data(), ProjectionSource()); }
        catch (InvalidDataException caught) { error = caught; }
        AssertThat(error is not null).IsTrue();
        AssertThat(error!.Message.Contains("RoleIds", StringComparison.Ordinal)).IsTrue();
    }

    [TestCase]
    public void ProjectionBadSavedResourceCannotBypassLegacyLoad()
    {
        using var native = NativeBattleContent.Write(ProjectionContent(), ProjectionSource());
        native.ProjectionNumericByteLength = 0;
        string root = "res://Tests/RuntimeBattleContent/" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(root));
        try
        {
            string uri = root + "/BadProjection.tres";
            AssertThat(ResourceSaver.Save(native, uri)).IsEqual(Error.Ok);
            foreach (Action read in new Action[] { () => NativeBattleContent.Load(uri), () => NativeBattleContent.LoadChecked(uri) })
            {
                InvalidDataException? error = null;
                try { read(); } catch (InvalidDataException caught) { error = caught; }
                AssertThat(error is not null).IsTrue();
                AssertThat(error!.Message.Contains(uri, StringComparison.Ordinal)).IsTrue();
                AssertThat(error.Message.Contains("NumericByteLength", StringComparison.Ordinal)).IsTrue();
            }
        }
        finally { Directory.Delete(ProjectSettings.GlobalizePath(root), true); }
    }

    private static void AssertProjectionRejected(PalBattleContent native, string cause)
    {
        foreach (Action read in new Action[] { () => NativeBattleContent.CreateCore(native, "selected-projection.tres"),
            () => NativeBattleContent.CreateChecked(native, "selected-projection.tres") })
        {
            InvalidDataException? error = null;
            try { read(); } catch (InvalidDataException caught) { error = caught; }
            AssertThat(error is not null).OverrideFailureMessage("Projection must reject " + cause).IsTrue();
            AssertThat(error!.Message.Contains("selected-projection.tres", StringComparison.Ordinal)).IsTrue();
            AssertThat(error.Message.Contains(cause, StringComparison.OrdinalIgnoreCase)).OverrideFailureMessage(error.Message).IsTrue();
        }
    }

    private static void AssertReadOnly<T>(IReadOnlyList<T> values, T replacement)
    {
        AssertThat(values is T[]).IsFalse();
        var list = (IList<T>)values;
        AssertThat(list.IsReadOnly).IsTrue();
        bool rejected = false;
        try { list[0] = replacement; } catch (NotSupportedException) { rejected = true; }
        AssertThat(rejected).IsTrue();
    }

    private static void AssertProjection(CheckedNativeBattleContent result)
    {
        var source = result.ProjectedSource!;
        AssertThat(source is not null).IsTrue();
        AssertThat(source!.FormatVersion).IsEqual(1);
        AssertThat(source.Kind).IsEqual("m5-base-table-physical-projection");
        AssertThat(source.SnapshotKind).IsEqual("independent-base-table-context-not-natural-save");
        AssertThat(source.InputTrust).IsEqual("caller-verified-upstream; declarations-and-byte-identities-not-independent-authentication");
        AssertThat(source.ReceiptSha256).IsEqual(new string('a', 64));
        AssertThat(source.ReceiptByteLength).IsEqual(long.MaxValue);
        AssertThat(source.NumericSha256).IsEqual(new string('b', 64));
        AssertThat(source.NumericByteLength).IsEqual(16777216L);
        AssertThat(source.RoleIds.ToArray()).IsEqual(new[] { 4, 0 });
        AssertThat(source.TeamIds.ToArray()).IsEqual(new[] { 65535, 0 });
        AssertThat(string.Join("|", source.ModernInputs.Select(x => x.File))).IsEqual("battle-effect-index.json|battle-field.json|enemy-pos.json|enemy-team.json|enemy.json|event-object.json|index.json|level-up-exp.json|level-up-magic.json|magic.json|object.json|player-roles.json|scene.json|store.json");
        AssertThat(source.ModernInputs.Select(x => x.Sha256).ToArray()).IsEqual(new[] { new string('c', 64), new string('d', 64), new string('e', 64), new string('f', 64),
            new string('0', 64), new string('1', 64), new string('2', 64), new string('3', 64), new string('4', 64), new string('5', 64),
            new string('6', 64), new string('7', 64), new string('8', 64), new string('9', 64) });
        AssertThat(source.ModernInputs.Select(x => x.ByteLength).ToArray()).IsEqual(new long[] { 1, 102, 103, 104, 105, 106, 107, 108, 109, 110, 111, 112, 113, 67108864 });
        AssertThat(string.Join("|", source.EnemySlots.Select(x => $"{x.TeamId},{x.SourceSlot},{x.ObjectId},{x.Disposition},{x.EnemyId},{x.CompactSlot},{x.BattleId}")))
            .IsEqual("65535,0,97,loaded,0,0,1|65535,1,65535,skip-FFFF,,,|65535,2,97,loaded,0,1,2|65535,3,65535,skip-FFFF,,,|65535,4,65535,skip-FFFF,,,|0,0,65535,skip-FFFF,,,|0,1,65534,loaded,65535,0,1|0,2,1,loaded,0,1,2|0,3,65535,skip-FFFF,,,|0,4,65535,skip-FFFF,,,");
        AssertThat(result.Content.Players.Select(p => p.RoleId).ToArray()).IsEqual(new[] { 4, 0 });
        AssertThat(result.Content.Players[0].Attack).IsEqual(40);
        AssertThat(result.Content.EnemyTeams.Keys.ToArray()).IsEqual(new[] { 65535, 0 });
        AssertThat(result.Content.EnemyTeams[65535].Select(e => e.Id).ToArray()).IsEqual(new[] { 1, 2 });
    }

    private static TriggerBattleContent ProjectionContent()
    {
        var data = Data();
        return new([data.InitialRole with { RoleId = 4 }, data.InitialRole with { RoleId = 0 }],
            [data.Player with { RoleId = 4 }, data.Player with { RoleId = 0 }],
            new Dictionary<int, ClassicBattleGrowthData> { [4] = data.Growth, [0] = data.Growth },
            new Dictionary<int, IReadOnlyList<ClassicBattleEnemy>> {
                [65535] = [data.Teams[18] with { Id = 1 }, data.Teams[18] with { Id = 2 }],
                [0] = [data.Teams[18] with { Id = 1 }, data.Teams[18] with { Id = 2 }] });
    }

    private static BattleProjectionModernInput[] ProjectionInputs() => [
        new("battle-effect-index.json", new string('c', 64), 1), new("battle-field.json", new string('d', 64), 102),
        new("enemy-pos.json", new string('e', 64), 103), new("enemy-team.json", new string('f', 64), 104),
        new("enemy.json", new string('0', 64), 105), new("event-object.json", new string('1', 64), 106),
        new("index.json", new string('2', 64), 107), new("level-up-exp.json", new string('3', 64), 108),
        new("level-up-magic.json", new string('4', 64), 109), new("magic.json", new string('5', 64), 110),
        new("object.json", new string('6', 64), 111), new("player-roles.json", new string('7', 64), 112),
        new("scene.json", new string('8', 64), 113), new("store.json", new string('9', 64), 67108864)];

    private static BattleProjectionEnemySlot[] ProjectionSlots() => [
        new(65535, 0, 97, "loaded", 0, 0, 1), new(65535, 1, 65535, "skip-FFFF"), new(65535, 2, 97, "loaded", 0, 1, 2),
        new(65535, 3, 65535, "skip-FFFF"), new(65535, 4, 65535, "skip-FFFF"), new(0, 0, 65535, "skip-FFFF"),
        new(0, 1, 65534, "loaded", 65535, 0, 1), new(0, 2, 1, "loaded", 0, 1, 2),
        new(0, 3, 65535, "skip-FFFF"), new(0, 4, 65535, "skip-FFFF")];

    private static BattleProjectionSourceInfo ProjectionSource() => ProjectionSource(ProjectionInputs(), [4, 0], [65535, 0], ProjectionSlots());
    private static BattleProjectionSourceInfo ProjectionSource(BattleProjectionModernInput[] inputs, int[] roles, int[] teams, BattleProjectionEnemySlot[] slots) =>
        new(1, "m5-base-table-physical-projection", "independent-base-table-context-not-natural-save", new string('a', 64), long.MaxValue,
            new string('b', 64), 16777216, inputs, roles, teams, slots);

    [TestCase(321, 654)]
    [TestCase(0, 65535)]
    [TestCase(65535, 0)]
    [TestCase(0, 0)]
    public void PlayerPanelsSurviveNativeReloadAndFeedPublicBossFlee(int magicPower, int fleeRate)
    {
        var data = DataJson();
        data["players"]![0]!["magicPower"] = magicPower;
        data["players"]![0]!["fleeRate"] = fleeRate;
        string root = "res://Tests/RuntimeBattleContent/" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(root));
        try
        {
            string uri = root + "/Panels.tres";
            var native = NativeBattleContent.Write(ModernBattleContent.Parse(data.ToJsonString()));
            if (magicPower == 0 && fleeRate == 0)
            {
                // Save non-default sentinels so the legacy fixture must actually remove both exports.
                native.Players[0].MagicPower = 321;
                native.Players[0].FleeRate = 654;
            }
            AssertThat(ResourceSaver.Save(native, uri)).IsEqual(Error.Ok);
            if (magicPower == 0 && fleeRate == 0)
            {
                // Only the PalBattlePlayer sub-resource predates these exports, not Hidden.MagicPower.
                string path = ProjectSettings.GlobalizePath(uri);
                string[] lines = File.ReadAllLines(path);
                string declaration = lines.Single(line => line.StartsWith("[ext_resource ", StringComparison.Ordinal)
                    && line.Contains("path=\"res://Scripts/Resources/PalBattlePlayer.cs\"", StringComparison.Ordinal));
                string scriptId = declaration.Split(" id=\"", StringSplitOptions.None)[1].Split('"')[0];
                int scriptLine = Array.IndexOf(lines, "script = ExtResource(\"" + scriptId + "\")");
                AssertThat(scriptLine >= 0).IsTrue();
                int start = Array.FindLastIndex(lines, scriptLine, line => line.StartsWith("[sub_resource ", StringComparison.Ordinal));
                int end = Array.FindIndex(lines, scriptLine + 1, line => line.StartsWith("[", StringComparison.Ordinal));
                AssertThat(start >= 0 && end > scriptLine).IsTrue();
                string[] legacy = lines.Where((line, index) => index < start || index >= end
                    || (!line.StartsWith("MagicPower =", StringComparison.Ordinal) && !line.StartsWith("FleeRate =", StringComparison.Ordinal))).ToArray();
                AssertThat(lines.Length - legacy.Length).IsEqual(2);
                File.WriteAllLines(path, legacy);
            }
            var loaded = NativeBattleContent.Load(uri);
            AssertThat(loaded.Player.MagicPower).IsEqual(magicPower);
            AssertThat(loaded.Player.FleeRate).IsEqual(fleeRate);
            var script = Script(loaded, new BattleRandom());
            script.Start("shared", "npc", 28);
            var battle = script.PendingBattle!.Lifecycle;
            battle.SelectFlee(0);
            AssertThat(battle.ResolveNextAction()).IsTrue();
            // fight.c: living enemy dex0 + (level0+6)*4; draw0 still fails for a boss.
            AssertThat(battle.Current.Combat.LastFleeAttempt).IsEqual(new ClassicBattleFleeAttempt(1, 0, 1, fleeRate, 24, 0, false));
            AssertThat(battle.Completion is null).IsTrue();
        }
        finally { Directory.Delete(ProjectSettings.GlobalizePath(root), true); }
    }

    [TestCase]
    public void PartyNativeRoundtripRetainsRoleKeysSlotsAndRealOwnedCompletion()
    {
        var initial = Data();
        var content = new TriggerBattleContent([initial.InitialRole, initial.InitialRole with { RoleId = 4, Hp = 80 }],
            [initial.Player with { RoleId = 4, Hp = 80 }, initial.Player],
            new Dictionary<int, ClassicBattleGrowthData> { [4] = initial.Growth, [initial.Player.RoleId] = initial.Growth },
            new Dictionary<int, IReadOnlyList<ClassicBattleEnemy>> { [18] = [initial.Teams[18], initial.Teams[18]] });
        string root = "res://Tests/RuntimeBattleContent/" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(root));
        try
        {
            string uri = root + "/Party.tres";
            AssertThat(ResourceSaver.Save(NativeBattleContent.Write(content), uri)).IsEqual(Error.Ok);
            var loaded = NativeBattleContent.Load(uri);
            AssertThat(loaded.Players.Select(x => x.RoleId).ToArray()).IsEqual(new[] { 4, initial.Player.RoleId });
            AssertThat(loaded.EnemyTeams[18].Select(x => x.Id).ToArray()).IsEqual(new[] { 7, 7 });
            AssertThat(loaded.InitialRoles[4].Hp).IsEqual(80);
            var script = Script(loaded, new BattleRandom());
            script.Start("shared", "npc", 28);
            var request = script.PendingBattle!;
            var battle = request.Lifecycle;
            for (int i = 0; i < 100 && battle.Completion is null; i++)
                if (battle.Current.Combat.SelectingPlayerIndex is int party)
                    battle.SelectAttack(party, battle.Current.Combat.Enemies.First(x => x.Hp > 0).Slot);
                else AssertThat(battle.ResolveNextAction()).IsTrue();
            AssertThat(battle.Completion is not null).IsTrue();
            AssertThat(battle.Completion!.Outcome).IsEqual(ClassicBattlePhase.Won);
            AssertThat(battle.Current.Roles[4].Hp).IsEqual(115);
            AssertThat(battle.Current.Roles[initial.Player.RoleId].Hp).IsEqual(125);
            AssertThat(script.AcknowledgeBattle(request.Id, battle.Completion)).IsTrue();
            AssertThat(script.Phase).IsEqual(TriggerScriptPhase.Completed);
            AssertThat(script.AcknowledgeBattle(request.Id, battle.Completion)).IsFalse();
            AssertThat(((IList<ClassicBattleEnemy>)loaded.EnemyTeams[18]).IsReadOnly).IsTrue();
            AssertThat(NativeBattleContent.Load(uri).InitialRoles[4].Hp).IsEqual(80);
        }
        finally { Directory.Delete(ProjectSettings.GlobalizePath(root), true); }
    }

    [TestCase]
    public void NameObjectIdZeroSurvivesNativeRoundtrip()
    {
        var initial = Data();
        var content = new TriggerBattleContent([initial.InitialRole with { RoleId = 4 }, initial.InitialRole],
            [initial.Player with { RoleId = 4, NameObjectId = 0, DyingScript = 123, FriendDeathScript = 456 },
                initial.Player with { NameObjectId = 0, DyingScript = 123, FriendDeathScript = 456 }],
            new Dictionary<int, ClassicBattleGrowthData> { [4] = initial.Growth, [1] = initial.Growth },
            initial.EnemyTeams);
        AssertNameNativeRoundtrip(content, 0, 0);
    }

    [TestCase]
    public void NameObjectId65535SurvivesNativeRoundtrip()
    {
        var initial = Data();
        var content = new TriggerBattleContent([initial.InitialRole with { RoleId = 4 }, initial.InitialRole],
            [initial.Player with { RoleId = 4, NameObjectId = 65535, DyingScript = 123, FriendDeathScript = 456 },
                initial.Player with { NameObjectId = 65535, DyingScript = 123, FriendDeathScript = 456 }],
            new Dictionary<int, ClassicBattleGrowthData> { [4] = initial.Growth, [1] = initial.Growth },
            initial.EnemyTeams);
        AssertNameNativeRoundtrip(content, 65535, 65535);
    }

    [TestCase("alias")]
    [TestCase("distinct-extremes")]
    [TestCase("distinct")]
    public void NameObjectAliasesAndDistinctNamesSurviveNativeRoundtrip(string kind)
    {
        var (first, second) = kind switch
        {
            "alias" => (37, 37),
            "distinct-extremes" => (0, 65535),
            "distinct" => (36, 37),
            _ => throw new ArgumentException(nameof(kind))
        };
        var initial = Data();
        var content = new TriggerBattleContent([initial.InitialRole with { RoleId = 4 }, initial.InitialRole],
            [initial.Player with { RoleId = 4, NameObjectId = first, DyingScript = 123, FriendDeathScript = 456 },
                initial.Player with { NameObjectId = second, DyingScript = 123, FriendDeathScript = 456 }],
            new Dictionary<int, ClassicBattleGrowthData> { [4] = initial.Growth, [1] = initial.Growth },
            initial.EnemyTeams);
        AssertNameNativeRoundtrip(content, first, second);
    }

    [TestCase]
    public void NameObjectNativeCanonicalValueRejects()
    {
        using var native = NativeBattleContent.Write(Data());
        AssertThat(native.Players[0].HasNameObjectId).IsFalse();
        AssertThat(native.Players[0].NameObjectId).IsEqual(0);
        native.Players[0].NameObjectId = 7;
        AssertNameNativeRejection(native, "Player.NameObjectId", "HasNameObjectId");
    }

    [TestCase(-1)]
    [TestCase(65536)]
    public void NameObjectNativeWordValueRejects(int value)
    {
        using var native = NativeBattleContent.Write(Data());
        native.Players[0].HasNameObjectId = true;
        native.Players[0].NameObjectId = value;
        AssertNameNativeRejection(native, "players[0].nameObjectId", "0..65535");
    }

    [TestCase(0)]
    [TestCase(1)]
    public void NameObjectNativePartialMappingRejects(int mappedIndex)
    {
        var initial = Data();
        var content = new TriggerBattleContent([initial.InitialRole with { RoleId = 4 }, initial.InitialRole],
            [initial.Player with { RoleId = 4 }, initial.Player],
            new Dictionary<int, ClassicBattleGrowthData> { [4] = initial.Growth, [1] = initial.Growth },
            initial.EnemyTeams);
        using var native = NativeBattleContent.Write(content);
        native.Players[mappedIndex].HasNameObjectId = true;
        native.Players[mappedIndex].NameObjectId = 37;
        AssertNameNativeRejection(native, "players", "nameObjectId", "declared together");
    }

    [TestCase("dying-forward")]
    [TestCase("dying-reverse")]
    [TestCase("friend-forward")]
    [TestCase("friend-reverse")]
    public void NameObjectNativeConflictingSeedsRejects(string kind)
    {
        var initial = Data();
        var content = new TriggerBattleContent([initial.InitialRole with { RoleId = 4 }, initial.InitialRole],
            [initial.Player with { RoleId = 4 }, initial.Player],
            new Dictionary<int, ClassicBattleGrowthData> { [4] = initial.Growth, [1] = initial.Growth },
            initial.EnemyTeams);
        using var native = NativeBattleContent.Write(content);
        foreach (var player in native.Players)
        {
            player.HasNameObjectId = true;
            player.NameObjectId = 37;
        }
        bool dying = kind.StartsWith("dying-", StringComparison.Ordinal);
        if (dying) native.Players[1].DyingScript = 1;
        else native.Players[1].FriendDeathScript = 1;
        if (kind.EndsWith("-reverse", StringComparison.Ordinal)) native.Players = native.Players.Reverse().ToArray();
        AssertNameNativeRejection(native, dying ? "players[1].dyingScript" : "players[1].friendDeathScript", "same nameObjectId");
    }

    [TestCase]
    public void LegacyNativePlayerWithoutNameExportsRemainsUnmapped()
    {
        // Write a legacy valid source first: the A guard must not prevent constructing this fixture.
        using var native = NativeBattleContent.Write(Data());
        native.Players[0].HasNameObjectId = true;
        native.Players[0].NameObjectId = 37;
        native.Players[0].DyingScript = 123;
        native.Players[0].FriendDeathScript = 456;
        string root = "res://Tests/RuntimeBattleContent/" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(root));
        try
        {
            string uri = root + "/LegacyName.tres";
            AssertThat(ResourceSaver.Save(native, uri)).IsEqual(Error.Ok);
            string path = ProjectSettings.GlobalizePath(uri);
            string[] lines = File.ReadAllLines(path);
            string declaration = lines.Single(line => line.StartsWith("[ext_resource ", StringComparison.Ordinal)
                && line.Contains("path=\"res://Scripts/Resources/PalBattlePlayer.cs\"", StringComparison.Ordinal));
            string scriptId = declaration.Split(" id=\"", StringSplitOptions.None)[1].Split('"')[0];
            int scriptLine = Array.IndexOf(lines, "script = ExtResource(\"" + scriptId + "\")");
            AssertThat(scriptLine >= 0).IsTrue();
            int start = Array.FindLastIndex(lines, scriptLine, line => line.StartsWith("[sub_resource ", StringComparison.Ordinal));
            int end = Array.FindIndex(lines, scriptLine + 1, line => line.StartsWith("[", StringComparison.Ordinal));
            AssertThat(start >= 0 && end > scriptLine).IsTrue();
            string[] legacy = lines.Where((line, index) => index < start || index >= end
                || (!line.StartsWith("HasNameObjectId =", StringComparison.Ordinal) && !line.StartsWith("NameObjectId =", StringComparison.Ordinal))).ToArray();
            AssertThat(lines.Length - legacy.Length).IsEqual(2);
            File.WriteAllLines(path, legacy);
            var loaded = NativeBattleContent.Load(uri);
            AssertThat(loaded.Player.NameObjectId is null).IsTrue();
            AssertThat(loaded.Player.DyingScript).IsEqual(123);
            AssertThat(loaded.Player.FriendDeathScript).IsEqual(456);
        }
        finally { Directory.Delete(ProjectSettings.GlobalizePath(root), true); }
    }

    [TestCase(100)]
    [TestCase(20)]
    public void NumericContentSavesLoadsAndCompletesRealLifecycle(int hp)
    {
        var initial = Data();
        var content = new TriggerBattleContent(initial.InitialRole with { Hp = hp }, initial.Player with { Hp = hp }, initial.Growth,
            new System.Collections.Generic.Dictionary<int, ClassicBattleEnemy> { [18] = initial.Teams[18] with { Hp = hp == 20 ? 100 : 43 } });
        string root = "res://Tests/RuntimeBattleContent/" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(root));
        try
        {
            string uri = root + "/Battle.tres";
            AssertThat(ResourceSaver.Save(NativeBattleContent.Write(content), uri)).IsEqual(Error.Ok);
            var loaded = NativeBattleContent.Load(uri);
            var script = Script(loaded, new BattleRandom());
            script.Start("shared", "npc", 28);
            var request = script.PendingBattle!;
            var battle = request.Lifecycle;
            AssertThat(script.AcknowledgeBattle(request.Id, null)).IsFalse();
            battle.SelectAttack(7);
            AssertThat(battle.ResolveNextAction()).IsTrue();
            if (hp == 20) AssertThat(battle.ResolveNextAction()).IsTrue();
            AssertThat(battle.Completion!.Outcome).IsEqual(hp == 20 ? ClassicBattlePhase.Lost : ClassicBattlePhase.Won);
            AssertThat(battle.Current.Role.Hp).IsEqual(hp == 20 ? 0 : 125);
            AssertThat(battle.Current.Role.Mp).IsEqual(hp == 20 ? 20 : 35);
            AssertThat(battle.OwnsCompletion(battle.Completion)).IsTrue();
            AssertThat(battle.ResolveNextAction()).IsFalse();
            AssertThat(battle.Current.Role.HiddenExperience["attack"].Count).IsEqual(1);
            AssertThat(battle.Current.Role.HiddenExperience["health"].Count).IsEqual(2);
            AssertThat(script.AcknowledgeBattle(request.Id, battle.Completion)).IsTrue();
            AssertThat(script.Phase).IsEqual(TriggerScriptPhase.Completed);
            AssertThat(script.AcknowledgeBattle(request.Id, battle.Completion)).IsFalse();
        }
        finally { Directory.Delete(ProjectSettings.GlobalizePath(root), true); }
    }

    [TestCase]
    public void EveryTypedFieldSurvivesNativeSaveLoadAndMutableResourceIsolation()
    {
        var initial = Data();
        var role = initial.InitialRole with
        {
            Level = 65535, Hp = 0, MaxHp = 65535, Mp = 65535, MaxMp = 65535,
            Attack = 41, MagicPower = 42, Defense = 43, Dexterity = 44, FleeRate = 45,
            Equipment = [1, 2, 3, 4, 5, 65535], Magics = Enumerable.Range(1, 32).ToArray(),
            Statuses = Enumerable.Range(1, 9).ToArray(), CoveredByRoleId = 5,
            PrimaryExperience = new(65535, 65534, 65533),
            HiddenExperience = new[] { "health", "magic", "attack", "magicPower", "defense", "dexterity", "flee" }
                .Select((name, index) => (name, index)).ToDictionary(x => x.name, x => new BattleExperienceSnapshot(x.index + 1, x.index + 11, x.index + 21), StringComparer.Ordinal)
        };
        var player = initial.Player with
        {
            Hp = 0, MaxHp = 65535, Attack = 65535, Defense = 123, Dexterity = 234,
            MagicPower = 321, FleeRate = 654,
            Equipment = [6, 5, 4, 3, 2, 1], Statuses = Enumerable.Range(10, 9).ToArray(), Poisons = [7, 65535],
            DyingScript = 12, FriendDeathScript = 13, AttackAll = true, CoveredByRoleId = 0
        };
        var enemy = initial.Teams[18] with
        {
            Id = 65535, Level = 65535, Hp = 65535, Attack = -32768, Defense = 32767, Dexterity = -123,
            PhysicalResistance = 65535, Statuses = Enumerable.Range(20, 9).ToArray(), Poisons = [9, 10],
            TurnStartScript = 11, ReadyScript = 12, BattleEndScript = 13, MagicId = 14, DualMove = 15,
            AttackEquivItem = 16, AttackEquivItemRate = 17, Experience = 18, Cash = 19
        };
        var content = new TriggerBattleContent(role, player,
            new(new int[100], [new(65535, 65535), new(0, 0), new(0, 0)]),
            new System.Collections.Generic.Dictionary<int, ClassicBattleEnemy> { [18] = enemy, [65535] = enemy with { Cash = 20 } });
        var native = NativeBattleContent.Write(content);
        AssertThat(native.Players[0].HasCoveredByRoleId).IsTrue();
        AssertThat(native.Players[0].CoveredByRoleId).IsEqual(0);
        var decoded = NativeBattleContent.CreateCore(native);
        AssertThat(JsonSerializer.Serialize(decoded)).IsEqual(JsonSerializer.Serialize(content));
        AssertThat(content.Player.CoveredByRoleId!.Value).IsEqual(0);
        AssertThat(content.Teams[65535].Attack).IsEqual(-32768);
        AssertThat(content.Growth.LevelUpExperience[99]).IsEqual(0);
        AssertThat(content.Growth.LevelUpMagics.Count).IsEqual(3);
        string expected = JsonSerializer.Serialize(content);
        string root = "res://Tests/RuntimeBattleContent/" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(root));
        try
        {
            string uri = root + "/Battle.tres";
            var written = NativeBattleContent.Write(content);
            AssertThat(ResourceSaver.Save(written, uri)).IsEqual(Error.Ok);
            AssertThat(File.ReadAllLines(ProjectSettings.GlobalizePath(uri)).Contains("FormatVersion = 1")).IsTrue();
            AssertThat(JsonSerializer.Serialize(NativeBattleContent.Load(uri))).IsEqual(expected);
            written.Roles[0].Equipment[0] = 99; written.Roles[0].HiddenExperience!.Attack!.Count = 99;
            native.Roles[0].PrimaryExperience!.Value = 99; native.Roles[0].HiddenExperience!.Attack!.Count = 99;
            native.Roles[0].Equipment[0] = 99; native.Roles[0].Magics[0] = 99; native.Roles[0].Statuses[0] = 99;
            native.Players[0].Attack = 99; native.Players[0].Equipment[0] = 99;
            native.Players[0].Statuses[0] = 99; native.Players[0].Poisons[0] = 99;
            native.Growth[0].LevelUpExperience[99] = 99;
            native.Growth[0].LevelUpMagics[0].Level = 99; native.Growth[0].LevelUpMagics[0].MagicId = 99;
            native.Teams[0].Enemies[0].Attack = 99; native.Teams[0].Enemies[0].Cash = 99;
            native.Teams[0].Enemies[0].Statuses[0] = 99; native.Teams[0].Enemies[0].Poisons[0] = 99;
            AssertThat(JsonSerializer.Serialize(content)).IsEqual(expected);
            AssertThat(JsonSerializer.Serialize(decoded)).IsEqual(expected);
            AssertThat(decoded.InitialRole.PrimaryExperience.Value).IsEqual(65535);
        AssertThat(decoded.InitialRole.Attack).IsEqual(41);
        AssertThat(decoded.InitialRole.MagicPower).IsEqual(42);
        AssertThat(decoded.InitialRole.Defense).IsEqual(43);
        AssertThat(decoded.InitialRole.Dexterity).IsEqual(44);
        AssertThat(decoded.InitialRole.FleeRate).IsEqual(45);
            AssertThat(decoded.InitialRole.HiddenExperience["attack"].Count).IsEqual(23);
            AssertThat(decoded.InitialRole.Equipment[0]).IsEqual(1);
            AssertThat(decoded.InitialRole.Magics[0]).IsEqual(1);
            AssertThat(decoded.Player.Attack).IsEqual(65535);
            AssertThat(decoded.Player.Poisons[0]).IsEqual(7);
            AssertThat(decoded.Growth.LevelUpExperience[99]).IsEqual(0);
            AssertThat(decoded.Growth.LevelUpMagics[0]).IsEqual(new BattleLevelUpMagic(65535, 65535));
            AssertThat(decoded.Teams[18].Attack).IsEqual(-32768);
            AssertThat(decoded.Teams[18].Cash).IsEqual(19);
            AssertThat(decoded.Teams[18].Statuses[0]).IsEqual(20);
            AssertThat(decoded.Teams[18].Poisons[0]).IsEqual(9);
            foreach (var values in new[] { decoded.InitialRole.Equipment, decoded.InitialRole.Magics, decoded.InitialRole.Statuses,
                decoded.Player.Equipment, decoded.Player.Statuses, decoded.Player.Poisons, decoded.Growth.LevelUpExperience,
                decoded.Teams[18].Statuses, decoded.Teams[18].Poisons, decoded.Teams[65535].Statuses, decoded.Teams[65535].Poisons })
                AssertThat(((IList<int>)values).IsReadOnly).IsTrue();
            AssertThat(((IDictionary<string, BattleExperienceSnapshot>)decoded.InitialRole.HiddenExperience).IsReadOnly).IsTrue();
            AssertThat(((IList<BattleLevelUpMagic>)decoded.Growth.LevelUpMagics).IsReadOnly).IsTrue();
            AssertThat(((IDictionary<int, ClassicBattleEnemy>)decoded.Teams).IsReadOnly).IsTrue();
            AssertThat(JsonSerializer.Serialize(NativeBattleContent.Load(uri))).IsEqual(expected);
        }
        finally { Directory.Delete(ProjectSettings.GlobalizePath(root), true); }
    }

    [TestCase]
    public void EquipmentClosureRoundtripsTypedResourcesAndStaysPureData()
    {
        var content = ModernBattleContent.Parse(EquipmentData().ToJsonString());
        var before = BattleEquipmentState.Initialize(content);
        string root = "res://Tests/RuntimeBattleContent/" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(root));
        try
        {
            string uri = root + "/Equipment.tres";
            AssertThat(ResourceSaver.Save(NativeBattleContent.Write(content), uri)).IsEqual(Error.Ok);
            var loaded = NativeBattleContent.Load(uri);
            AssertThat(loaded.RoleEquipment.Count).IsEqual(6);
            AssertThat(string.Join(",", loaded.RoleEquipment[1])).IsEqual("197,0,210,170,237,248");
            AssertThat(loaded.EquipmentObjects[170].Effects[1].Statuses[8]).IsEqual(60000);
            var after = BattleEquipmentState.Initialize(loaded);
            // Hand-computed from the reference getters: attack 35+3+4, defense 20+1+6, dexterity 100+8,
            // battle sprite 450 is the last non-zero register, and role0 (not in this party) still initializes to 600.
            AssertThat(after.EffectiveAttack(1, 35)).IsEqual(42);
            AssertThat(after.EffectiveDefense(1, 20)).IsEqual(27);
            AssertThat(after.EffectiveDexterity(1, 100)).IsEqual(108);
            AssertThat(after.BattleSprite(1, 3)).IsEqual(450);
            AssertThat(after.BattleSprite(0, 3)).IsEqual(600);
            AssertThat(after.AttackBonus(1)).IsEqual(before.AttackBonus(1));
            AssertThat(after.StatusOverlay(1).ToArray()).IsEqual(new[] { 0, 0, 0, 0, 0, 0, 0, 0, 60000 });
            AssertThat(after.EffectivePlayer(loaded.Player).Attack).IsEqual(42);
            // Pure data slice: the selected entry still refuses non-zero equipment before any draw.
            var random = new BattleRandom();
            InvalidDataException? rejection = null;
            try { Script(loaded, random).Start("shared", "npc", 28); }
            catch (InvalidDataException caught) { rejection = caught; }
            AssertThat(rejection is not null).IsTrue();
            AssertThat(random.Draws).IsEqual(0);
        }
        finally { Directory.Delete(ProjectSettings.GlobalizePath(root), true); }
    }

    [TestCase("version")]
    [TestCase("roles-null")]
    [TestCase("roles-empty")]
    [TestCase("roles-multiple")]
    [TestCase("role-null")]
    [TestCase("xp-null")]
    [TestCase("hidden-null")]
    [TestCase("hidden-xp-null")]
    [TestCase("equipment-null")]
    [TestCase("players-empty")]
    [TestCase("player-null")]
    [TestCase("player-hp")]
    [TestCase("player-role")]
    [TestCase("players-duplicate")]
    [TestCase("player-magic-negative")]
    [TestCase("player-magic-overflow")]
    [TestCase("player-flee-negative")]
    [TestCase("player-flee-overflow")]
    [TestCase("player-statuses")]
    [TestCase("player-poisons-null")]
    [TestCase("cover-conflict")]
    [TestCase("cover-range")]
    [TestCase("growth-null")]
    [TestCase("growth-role")]
    [TestCase("growth-threshold-null")]
    [TestCase("growth-threshold-shape")]
    [TestCase("growth-rows-null")]
    [TestCase("growth-row-null")]
    [TestCase("teams-empty")]
    [TestCase("team-null")]
    [TestCase("duplicate-team")]
    [TestCase("team-id")]
    [TestCase("enemies-empty")]
    [TestCase("enemies-overcapacity")]
    [TestCase("enemy-null")]
    [TestCase("enemy-statuses-null")]
    [TestCase("enemy-attack")]
    [TestCase("enemy-hp")]
    [TestCase("grid-row-count")]
    [TestCase("grid-row-null")]
    [TestCase("grid-slot-count")]
    [TestCase("grid-undeclared")]
    [TestCase("grid-role-mismatch")]
    [TestCase("equipments-null")]
    [TestCase("equipment-object-null")]
    [TestCase("equipment-effects-count")]
    [TestCase("equipment-statuses-null")]
    [TestCase("equipment-status-rounds")]
    public void TypedNativeBoundaryRejectsMalformedFieldsBeforeLosingShapeOrIdentity(string defect)
    {
        var native = NativeBattleContent.Write(Data());
        switch (defect)
        {
            case "version": native.FormatVersion = 2; break;
            case "roles-null": native.Roles = null!; break;
            case "roles-empty": native.Roles = []; break;
            case "roles-multiple": native.Roles = [native.Roles[0], native.Roles[0]]; break;
            case "role-null": native.Roles = [null!]; break;
            case "xp-null": native.Roles[0].PrimaryExperience = null; break;
            case "hidden-null": native.Roles[0].HiddenExperience = null; break;
            case "hidden-xp-null": native.Roles[0].HiddenExperience!.Flee = null; break;
            case "equipment-null": native.Roles[0].Equipment = null!; break;
            case "players-empty": native.Players = []; break;
            case "player-null": native.Players = [null!]; break;
            case "player-hp": native.Players[0].Hp--; break;
            case "player-role": native.Players[0].RoleId = 0; break;
            case "players-duplicate": native.Players = [native.Players[0], native.Players[0]]; break;
            case "player-magic-negative": native.Players[0].MagicPower = -1; break;
            case "player-magic-overflow": native.Players[0].MagicPower = 65536; break;
            case "player-flee-negative": native.Players[0].FleeRate = -1; break;
            case "player-flee-overflow": native.Players[0].FleeRate = 65536; break;
            case "player-statuses": native.Players[0].Statuses = new int[8]; break;
            case "player-poisons-null": native.Players[0].Poisons = null!; break;
            case "cover-conflict": native.Players[0].CoveredByRoleId = 1; break;
            case "cover-range": native.Players[0].HasCoveredByRoleId = true; native.Players[0].CoveredByRoleId = 6; break;
            case "growth-null": native.Growth = [null!]; break;
            case "growth-role": native.Growth[0].RoleId = 0; break;
            case "growth-threshold-null": native.Growth[0].LevelUpExperience = null!; break;
            case "growth-threshold-shape": native.Growth[0].LevelUpExperience = new int[101]; break;
            case "growth-rows-null": native.Growth[0].LevelUpMagics = null!; break;
            case "growth-row-null": native.Growth[0].LevelUpMagics = [null!]; break;
            case "teams-empty": native.Teams = []; break;
            case "team-null": native.Teams = [null!]; break;
            case "duplicate-team": native.Teams = [native.Teams[0], native.Teams[0]]; break;
            case "team-id": native.Teams[0].TeamId = -1; break;
            case "enemies-empty": native.Teams[0].Enemies = []; break;
            case "enemies-overcapacity": native.Teams[0].Enemies = [native.Teams[0].Enemies[0], native.Teams[0].Enemies[0], native.Teams[0].Enemies[0]]; break;
            case "enemy-null": native.Teams[0].Enemies = [null!]; break;
            case "enemy-statuses-null": native.Teams[0].Enemies[0].Statuses = null!; break;
            case "enemy-attack": native.Teams[0].Enemies[0].Attack = 32768; break;
            case "enemy-hp": native.Teams[0].Enemies[0].Hp = 0; break;
            case "grid-row-count": native.RoleEquipment = Grid(5); break;
            case "grid-row-null": native.RoleEquipment = new int[35]; break;
            case "grid-slot-count": native.RoleEquipment = Grid(7); break;
            case "grid-undeclared": native.RoleEquipment = Grid(6, 999); break;
            case "grid-role-mismatch": native.Equipments = Objects(170); native.RoleEquipment = Grid(6, 170); break;
            case "equipments-null": native.Equipments = null!; break;
            case "equipment-object-null": native.Equipments = [null!]; break;
            case "equipment-effects-count":
                native.Equipments = Objects(170);
                native.Equipments[0].Effects = native.Equipments[0].Effects[..5];
                break;
            case "equipment-statuses-null": native.Equipments = Objects(170); native.Equipments[0].Effects[1].Statuses = null!; break;
            case "equipment-status-rounds": native.Equipments = Objects(170); native.Equipments[0].Effects[1].Statuses[8] = 500; break;
        }
        InvalidDataException? error = null;
        try { NativeBattleContent.CreateCore(native, "selected-native.tres"); }
        catch (InvalidDataException caught) { error = caught; }
        AssertThat(error is not null).IsTrue();
        AssertThat(error!.Message.Contains("selected-native.tres", StringComparison.Ordinal)).IsTrue();
    }

    [TestCase("C:/Battle.tres")]
    [TestCase("res://Tests/../Battle.tres")]
    [TestCase("res://Tests/MissingBattleContent.tres")]
    public void LoadRejectsMissingOrIllegalUri(string uri)
    {
        bool rejected = false;
        try { NativeBattleContent.Load(uri); }
        catch (InvalidDataException error) { rejected = error.Message.Contains(uri, StringComparison.Ordinal); }
        AssertThat(rejected).IsTrue();
    }

    [TestCase]
    public void LoadRejectsMissingExplicitFormatVersion()
    {
        string root = "res://Tests/RuntimeBattleContent/" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(root));
        try
        {
            string uri = root + "/Battle.tres";
            AssertThat(ResourceSaver.Save(NativeBattleContent.Write(Data()), uri)).IsEqual(Error.Ok);
            AssertThat(NativeBattleContent.Load(uri).InitialRole.Hp).IsEqual(100);
            string path = ProjectSettings.GlobalizePath(uri);
            File.WriteAllLines(path, File.ReadAllLines(path).Where(line => !line.StartsWith("FormatVersion =", StringComparison.Ordinal)));
            InvalidDataException? error = null;
            try { NativeBattleContent.Load(uri); }
            catch (InvalidDataException caught) { error = caught; }
            AssertThat(error is not null).OverrideFailureMessage("Load must reject a saved native resource missing explicit FormatVersion").IsTrue();
            AssertThat(error!.Message.Contains("FormatVersion", StringComparison.Ordinal)).IsTrue();
            AssertThat(error.Message.Contains(uri, StringComparison.Ordinal)).IsTrue();
        }
        finally { Directory.Delete(ProjectSettings.GlobalizePath(root), true); }
    }

    [TestCase("version")]
    [TestCase("type")]
    public void LoadRejectsWrongVersionOrResourceType(string defect)
    {
        string root = "res://Tests/RuntimeBattleContent/" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(root));
        try
        {
            var native = NativeBattleContent.Write(Data()); native.FormatVersion = 2;
            string uri = root + "/Bad.tres";
            AssertThat(ResourceSaver.Save(defect == "type" ? new PalBattleRole() : native, uri)).IsEqual(Error.Ok);
            bool rejected = false;
            try { NativeBattleContent.Load(uri); }
            catch (InvalidDataException error) { rejected = error.Message.Contains(defect == "type" ? "expected PalBattleContent" : "FormatVersion", StringComparison.Ordinal); }
            AssertThat(rejected).IsTrue();
        }
        finally { Directory.Delete(ProjectSettings.GlobalizePath(root), true); }
    }

    [TestCase]
    public void UnreferencedNonzeroTeamSurvivesButSelectedUnsupportedTeamRejectsBeforeRngOrRequest()
    {
        var native = NativeBattleContent.Write(Data());
        var extra = new PalBattleTeam { TeamId = 19, Enemies = [new() { Id = 7, Hp = 43, Statuses = new int[9], MagicId = 1 }] };
        native.Teams = [native.Teams[0], extra];
        var random = new BattleRandom();
        var content = NativeBattleContent.CreateCore(native);
        AssertThat(content.Teams[19].MagicId).IsEqual(1);
        var script = Script(content, random);
        script.Start("shared", "npc", 28);
        AssertThat(script.PendingBattle is not null).IsTrue();
        AssertThat(random.Draws).IsEqual(0);
        native.Teams[0].Enemies[0].MagicId = 1;
        script = Script(NativeBattleContent.CreateCore(native), random);
        bool rejected = false;
        try { script.Start("shared", "npc", 28); }
        catch (InvalidDataException) { rejected = true; }
        AssertThat(rejected).IsTrue();
        AssertThat(script.Phase).IsEqual(TriggerScriptPhase.Ready);
        AssertThat(script.PendingBattle is null).IsTrue();
        AssertThat(script.Caller).IsEqual(0);
        AssertThat(random.Draws).IsEqual(0);
    }

    private static void AssertNameNativeRoundtrip(TriggerBattleContent content, int first, int second)
    {
        ModernBattleContent.Validate(content);
        using var native = NativeBattleContent.Write(content);
        AssertThat(native.Players.All(player => player.HasNameObjectId)).IsTrue();
        AssertThat(native.Players.Select(player => player.NameObjectId).ToArray()).IsEqual(new[] { first, second });
        AssertThat(native.Players.Select(player => player.DyingScript).ToArray()).IsEqual(new[] { 123, 123 });
        AssertThat(native.Players.Select(player => player.FriendDeathScript).ToArray()).IsEqual(new[] { 456, 456 });
        var decoded = NativeBattleContent.CreateCore(native);
        string root = "res://Tests/RuntimeBattleContent/" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(root));
        try
        {
            string uri = root + "/Name.tres";
            AssertThat(ResourceSaver.Save(native, uri)).IsEqual(Error.Ok);
            var loaded = NativeBattleContent.Load(uri);
            native.Players[0].HasNameObjectId = false;
            native.Players[0].NameObjectId = 39;
            native.Players[0].DyingScript = 9;
            native.Players[0].FriendDeathScript = 9;
            native.Players[0].Equipment[0] = 99;
            native.Roles[0].Equipment[0] = 99;
            foreach (var copy in new[] { content, decoded, loaded, NativeBattleContent.Load(uri) })
            {
                AssertThat(copy.Players.Count).IsEqual(2);
                AssertThat(copy.Players.Select(player => player.RoleId).ToArray()).IsEqual(new[] { 4, 1 });
                AssertThat(copy.InitialRoles.Keys.ToArray()).IsEqual(new[] { 4, 1 });
                AssertThat(copy.GrowthByRole.Keys.ToArray()).IsEqual(new[] { 4, 1 });
                AssertThat(copy.Players[0].NameObjectId == first).IsTrue();
                AssertThat(copy.Players[1].NameObjectId == second).IsTrue();
                AssertThat(copy.Players.Select(player => player.DyingScript).ToArray()).IsEqual(new[] { 123, 123 });
                AssertThat(copy.Players.Select(player => player.FriendDeathScript).ToArray()).IsEqual(new[] { 456, 456 });
                AssertThat(copy.Players[0].Equipment[0]).IsEqual(0);
                AssertThat(copy.InitialRoles[4].Equipment[0]).IsEqual(0);
            }
        }
        finally { Directory.Delete(ProjectSettings.GlobalizePath(root), true); }
    }

    private static void AssertNameNativeRejection(PalBattleContent native, params string[] causes)
    {
        InvalidDataException? error = null;
        try { NativeBattleContent.CreateCore(native, "selected-name.tres"); }
        catch (InvalidDataException caught) { error = caught; }
        AssertThat(error is not null).OverrideFailureMessage("CreateCore must reject the declared native Name mapping, not silently ignore exported fields.").IsTrue();
        if (error is null) return;
        AssertThat(error.Message.Contains("selected-name.tres", StringComparison.Ordinal)).IsTrue();
        foreach (string cause in causes) AssertThat(error.Message.Contains(cause, StringComparison.Ordinal)).IsTrue();
    }

    private static TriggerScriptSession Script(TriggerBattleContent content, BattleRandom random)
    {
        const string index = """
            {"formatVersion":1,"profile":"faithful","source":{"archive":"SSS.MKF","chunk":4,"byteOrder":"little","instructionCount":3,"byteLength":24,"sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"},"files":["shared"]}
            """;
        const string events = """
            {"formatVersion":1,"profile":"faithful","fileId":"shared","entries":[{"id":"npc","owner":{"type":"eventObject","id":28},"slot":"wTriggerScript","kind":"trigger","target":"L0001","sourceAddress":1}],"commands":[
            {"label":"L0000","op":"end","mode":"keep","next":"L0001","source":{"index":0,"opcode":0,"operands":[0,0,0]}},
            {"label":"L0001","op":"startBattle","enemyTeamId":18,"lossTarget":"L0002","isBoss":true,"fleeTarget":null,"next":"L0002","source":{"index":1,"opcode":7,"operands":[18,2,0]}},
            {"label":"L0002","op":"end","mode":"keep","next":null,"source":{"index":2,"opcode":0,"operands":[0,0,0]}}]}
            """;
        var collection = TriggerEventCollection.Parse(index, new System.Collections.Generic.Dictionary<string, string> { ["shared"] = events });
        return new(collection, new DialogueLanguagePackage("en", []), content, random);
    }

    private static TriggerBattleContent Data() => ModernBattleContent.Parse(DataJson().ToJsonString());

    private static int[] Grid(int rows, int role1Object = 0)
    {
        var flat = new int[rows * BattleEquipmentState.SlotCount];
        if (role1Object != 0) flat[BattleEquipmentState.SlotCount + 3] = role1Object;
        return flat;
    }

    private static PalBattleEquipment[] Objects(int objectId) =>
    [
        new() { ObjectId = objectId, Effects = Enumerable.Range(0, 6).Select(_ => new PalBattleEquipmentEffect
            { Attack = 0, Defense = 0, Dexterity = 0, BattleSprite = 0, Statuses = new int[9] }).ToArray() }
    ];

    private static JsonObject DataJson()
    {
        var data = JsonNode.Parse("""
        {"formatVersion":1,"roles":[{
          "roleId":1,"level":2,"hp":100,"maxHp":150,"mp":20,"maxMp":50,
          "primaryExperience":{"value":5,"level":2,"count":17},
          "hiddenExperience":{
            "health":{"value":7,"level":3,"count":55},"magic":{"value":7,"level":3,"count":55},
            "attack":{"value":7,"level":3,"count":55},"magicPower":{"value":7,"level":3,"count":55},
            "defense":{"value":7,"level":3,"count":55},"dexterity":{"value":7,"level":3,"count":55},
            "flee":{"value":7,"level":3,"count":55}},
          "equipment":[0,0,0,0,0,0],"magics":[],"statuses":[0,0,0,0,0,0,0,0,0],"coveredByRoleId":0}],
         "players":[{"roleId":1,"hp":100,"maxHp":150,"attack":40,"defense":20,"dexterity":100,
          "equipment":[0,0,0,0,0,0],"statuses":[0,0,0,0,0,0,0,0,0],"poisons":[0],
          "dyingScript":0,"friendDeathScript":0,"coveredByRoleId":null,"attackAll":false}],
         "growth":[{"roleId":1,"levelUpExperience":[],"levelUpMagics":[{"level":2,"magicId":9},{"level":3,"magicId":10}]}],
         "teams":[{"teamId":18,"enemies":[{"id":7,"level":0,"hp":43,"attack":0,"defense":0,"dexterity":0,"physicalResistance":1,
          "statuses":[0,0,0,0,0,0,0,0,0],"poisons":[0],"turnStartScript":0,"readyScript":0,"battleEndScript":0,
          "magicId":0,"dualMove":0,"attackEquivItem":0,"attackEquivItemRate":0,"experience":0,"cash":0}]}]}
        """)!.AsObject();
        data["roles"]![0]!["magics"] = new JsonArray(Enumerable.Range(0, 32).Select(i => JsonValue.Create(i == 0 ? 9 : 0) as JsonNode).ToArray());
        data["growth"]![0]!["levelUpExperience"] = new JsonArray(Enumerable.Repeat(100, 100).Select(x => JsonValue.Create(x) as JsonNode).ToArray());
        return data;
    }

    private static JsonObject EquipmentData()
    {
        var data = DataJson();
        JsonNode Row(int attack, int defense, int dexterity, int battleSprite, int dualAttack) => new JsonObject
        {
            ["attack"] = attack, ["defense"] = defense, ["dexterity"] = dexterity, ["battleSprite"] = battleSprite,
            ["statuses"] = new JsonArray(0, 0, 0, 0, 0, 0, 0, 0, dualAttack)
        };
        JsonNode Object(int id, params JsonNode[] rows) => new JsonObject
        { ["objectId"] = id, ["effects"] = new JsonArray(
            rows.Concat(Enumerable.Range(0, 6 - rows.Length).Select(_ => Row(0, 0, 0, 0, 0))).ToArray()) };
        JsonNode Zero() => Row(0, 0, 0, 0, 0);
        data["roles"]![0]!["equipment"] = new JsonArray(197, 0, 210, 170, 237, 248);
        data["players"]![0]!["equipment"] = new JsonArray(197, 0, 210, 170, 237, 248);
        data["players"]![0]!["attack"] = 35;
        data["roleEquipment"] = new JsonArray(
            new JsonArray(196, 225, 208, 166, 235, 249), new JsonArray(197, 0, 210, 170, 237, 248),
            new JsonArray(0, 0, 0, 0, 0, 0), new JsonArray(0, 0, 0, 0, 0, 0),
            new JsonArray(0, 0, 0, 0, 0, 0), new JsonArray(0, 0, 0, 0, 0, 0));
        data["equipments"] = new JsonArray(
            Object(197, Row(0, 0, 0, 0, 0), Row(3, 1, 0, 0, 0)),
            Object(210, Zero(), Row(4, 0, 0, 400, 0)),
            Object(170, Zero(), Row(0, 0, 0, 0, 60000)),
            Object(237, Zero(), Row(0, 6, 0, 0, 0)),
            Object(248, Zero(), Row(0, 0, 8, 450, 0)),
            Object(196, Row(5, 2, 0, 0, 0)),
            Object(225, Row(0, 4, 0, 0, 0)),
            Object(208, Row(0, 0, 0, 500, 0)),
            Object(166, Row(10, 0, 0, 0, 0)),
            Object(235, Row(0, 0, 6, 0, 0)),
            Object(249, Row(0, 0, 0, 600, 0)));
        return data;
    }

    private sealed class BattleRandom : IBattleRandom
    {
        public int Draws { get; private set; }
        public int NextInclusive(int from, int to) { Draws++; return from == 0 && to == 5 ? 1 : from; }
        public float NextInclusive(float from, float to) { Draws++; return 1; }
    }
}
