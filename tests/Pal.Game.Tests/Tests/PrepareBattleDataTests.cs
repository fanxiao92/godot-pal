using System;
using System.IO;
using FileAccess = System.IO.FileAccess;
using System.Linq;
using System.Text.Json.Nodes;
using GdUnit4;
using Godot;
using Pal.Core;
using Pal.Game.Editor;
using Pal.Game.Profiles;
using static GdUnit4.Assertions;

namespace Pal.Game.Tests;

[TestSuite, RequireGodotRuntime]
[TestCategory("Prepare")]
public class PrepareBattleDataTests
{
    [TestCase]
    public void ProjectionPreparePublicOverloadExists()
    {
        AssertThat(typeof(PrepareBattleData).GetMethod(nameof(PrepareBattleData.Prepare),
            new[] { typeof(string), typeof(string), typeof(string) }) is not null)
            .OverrideFailureMessage("Projection Prepare overload must exist before binding receipt bytes").IsTrue();
    }

    [TestCase("plain")]
    [TestCase("receipt-bom")]
    [TestCase("numeric-bom")]
    [TestCase("whitespace")]
    public void ProjectionPrepareBindsOriginalBytesAndCheckedSource(string decoration)
    {
        using var fixture = new Preparation();
        fixture.UseProjection();
        if (decoration == "plain")
        {
            AssertThat(Digest(File.ReadAllBytes(fixture.Source))).IsEqual("6f72777b683bf68a725973f412f400e2c43a4c419a0b63d1bf4de82f553d4d73");
            AssertThat(Digest(File.ReadAllBytes(fixture.Receipt))).IsEqual("af419c008c0140478dc35fe248ecd9f83837d76f454a943bfb05bf772a5fb940");
        }
        if (decoration is "numeric-bom" or "whitespace")
        {
            byte[] original = File.ReadAllBytes(fixture.Source);
            File.WriteAllBytes(fixture.Source, decoration == "numeric-bom"
                ? new byte[] { 0xef, 0xbb, 0xbf }.Concat(original).ToArray() : original.Concat(new byte[] { 32, 13, 10 }).ToArray());
            var receipt = JsonNode.Parse(File.ReadAllText(fixture.Receipt))!;
            receipt["output"]!["sha256"] = Digest(File.ReadAllBytes(fixture.Source));
            receipt["output"]!["byteLength"] = new FileInfo(fixture.Source).Length;
            File.WriteAllText(fixture.Receipt, receipt.ToJsonString(), new System.Text.UTF8Encoding(false));
        }
        if (decoration == "receipt-bom") File.WriteAllBytes(fixture.Receipt,
            new byte[] { 0xef, 0xbb, 0xbf }.Concat(File.ReadAllBytes(fixture.Receipt)).ToArray());
        byte[] numericBefore = File.ReadAllBytes(fixture.Source), receiptBefore = File.ReadAllBytes(fixture.Receipt);
        string uri = PrepareBattleData.Prepare(fixture.Input, fixture.Output, fixture.Receipt);
        var loaded = NativeBattleContent.LoadChecked(uri);
        var source = loaded.ProjectedSource!;
        AssertThat(source is not null).IsTrue();
        AssertThat(source!.ReceiptSha256).IsEqual(Digest(receiptBefore));
        AssertThat(source.ReceiptByteLength).IsEqual((long)receiptBefore.Length);
        AssertThat(source.NumericSha256).IsEqual(Digest(numericBefore));
        AssertThat(source.NumericByteLength).IsEqual((long)numericBefore.Length);
        AssertThat(source.RoleIds.ToArray()).IsEqual(new[] { 4, 0 });
        AssertThat(source.TeamIds.ToArray()).IsEqual(new[] { 65535, 0 });
        AssertThat(source.ModernInputs.Count).IsEqual(14);
        AssertThat(source.ModernInputs[0]).IsEqual(new BattleProjectionModernInput("battle-effect-index.json", new string('0', 64), 100));
        AssertThat(source.ModernInputs[13]).IsEqual(new BattleProjectionModernInput("store.json", new string('d', 64), 113));
        AssertThat(source.EnemySlots.ToArray()).IsEqual(new BattleProjectionEnemySlot[] {
            new BattleProjectionEnemySlot(65535, 0, 97, "loaded", 0, 0, 1), new(65535, 1, 65535, "skip-FFFF"),
            new(65535, 2, 97, "loaded", 0, 1, 2), new(65535, 3, 65535, "skip-FFFF"), new(65535, 4, 65535, "skip-FFFF"),
            new(0, 0, 65535, "skip-FFFF"), new(0, 1, 97, "loaded", 0, 0, 1), new(0, 2, 65535, "skip-FFFF"),
            new(0, 3, 65535, "skip-FFFF"), new(0, 4, 65535, "skip-FFFF") });
        AssertThat(loaded.Content.Players.Select(player => player.RoleId).ToArray()).IsEqual(new[] { 4, 0 });
        AssertThat(loaded.Content.Players[0].MagicPower).IsEqual(321);
        AssertThat(loaded.Content.Players[1].FleeRate).IsEqual(654);
        AssertThat(loaded.Content.EnemyTeams[65535].Select(enemy => enemy.Id).ToArray()).IsEqual(new[] { 1, 2 });
        AssertThat(loaded.Content.EnemyTeams[0].Single().Id).IsEqual(1);
        AssertThat(File.ReadAllBytes(fixture.Source)).IsEqual(numericBefore);
        AssertThat(File.ReadAllBytes(fixture.Receipt)).IsEqual(receiptBefore);
        AssertThat(Directory.GetFileSystemEntries(fixture.Output).Select(Path.GetFileName).ToArray()).IsEqual(new[] { "BattleContent.tres" });
        // Both source leases have actually been returned, not only logically marked released.
        using var numericLease = new FileStream(fixture.Source, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var receiptLease = new FileStream(fixture.Receipt, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [TestCase("empty", "projection receipt must contain")]
    [TestCase("utf8", "UTF-8")]
    [TestCase("json", "JSON")]
    [TestCase("hash", "output.sha256")]
    [TestCase("length", "output.byteLength")]
    [TestCase("numeric-space", "output.byteLength")]
    [TestCase("numeric-bom", "output.byteLength")]
    [TestCase("roles", "RoleIds")]
    [TestCase("coverage", "fieldSources")]
    [TestCase("numeric-equal-length", "output.sha256")]
    public void ProjectionPrepareRejectsWrongReceiptOrNumericWithoutPublishing(string defect, string diagnostic)
    {
        using var fixture = new Preparation(); fixture.UseProjection();
        var receipt = JsonNode.Parse(File.ReadAllText(fixture.Receipt))!;
        switch (defect)
        {
            case "empty": File.WriteAllBytes(fixture.Receipt, []); break;
            case "utf8": File.WriteAllBytes(fixture.Receipt, [0xc3, 0x28]); break;
            case "json": File.WriteAllText(fixture.Receipt, "{"); break;
            case "hash": receipt["output"]!["sha256"] = new string('a', 64); break;
            case "length": receipt["output"]!["byteLength"] = 1; break;
            case "roles": receipt["roles"] = new JsonArray(0, 4); break;
            case "coverage": receipt["fieldSources"]!.AsArray().RemoveAt(0); break;
            case "numeric-space": File.AppendAllText(fixture.Source, " "); break;
            case "numeric-bom": File.WriteAllBytes(fixture.Source, new byte[] { 0xef, 0xbb, 0xbf }.Concat(File.ReadAllBytes(fixture.Source)).ToArray()); break;
            case "numeric-equal-length": File.WriteAllText(fixture.Source, File.ReadAllText(fixture.Source).Replace("\"magicPower\": 321", "\"magicPower\": 322"), new System.Text.UTF8Encoding(false)); break;
        }
        if (defect is "hash" or "length" or "roles" or "coverage")
            File.WriteAllText(fixture.Receipt, receipt.ToJsonString(), new System.Text.UTF8Encoding(false));
        byte[] numericBefore = File.ReadAllBytes(fixture.Source), receiptBefore = File.ReadAllBytes(fixture.Receipt);
        Directory.CreateDirectory(fixture.Output);
        RejectProjection(fixture, fixture.Receipt, diagnostic);
        AssertThat(Directory.GetFileSystemEntries(fixture.Output).Length).IsEqual(0);
        AssertThat(File.ReadAllBytes(fixture.Source)).IsEqual(numericBefore);
        AssertThat(File.ReadAllBytes(fixture.Receipt)).IsEqual(receiptBefore);
        using var released = new FileStream(fixture.Receipt, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [TestCase("null", "explicitly provided")]
    [TestCase("blank", "explicitly provided")]
    [TestCase("missing", "existing file")]
    [TestCase("directory", "existing file")]
    [TestCase("inside-input", "exactly battle-data.json")]
    [TestCase("inside-output", "outside numeric input")]
    [TestCase("link", "link/reparse")]
    [TestCase("link-ancestor", "link/reparse")]
    public void ProjectionPrepareRejectsUnsafeReceiptPaths(string defect, string diagnostic)
    {
        using var fixture = new Preparation(); fixture.UseProjection();
        string? receipt = fixture.Receipt;
        string? link = null;
        if (defect == "null") receipt = null;
        if (defect == "blank") receipt = " ";
        if (defect == "missing") receipt += "-missing";
        if (defect == "directory") receipt = fixture.Input + "-directory";
        if (defect == "inside-input") { receipt = Path.Combine(fixture.Input, "receipt.json"); File.Copy(fixture.Receipt, receipt); }
        if (defect == "inside-output") receipt = Path.Combine(fixture.Output, "receipt.json");
        if (defect == "directory") Directory.CreateDirectory(receipt!);
        if (defect is "link" or "link-ancestor")
        {
            link = fixture.Input + "-link";
            CreateDirectoryLink(link, Path.GetDirectoryName(fixture.Receipt)!);
            receipt = defect == "link" ? link : Path.Combine(link, Path.GetFileName(fixture.Receipt));
        }
        try
        {
            byte[] before = File.ReadAllBytes(fixture.Receipt);
            RejectProjection(fixture, receipt, diagnostic);
            AssertThat(Directory.Exists(fixture.Output)).IsFalse();
            AssertThat(File.ReadAllBytes(fixture.Receipt)).IsEqual(before);
        }
        finally
        {
            if (link is not null)
            {
                if (OperatingSystem.IsWindows()) Directory.Delete(link);
                else File.Delete(link);
            }
            if (defect == "directory") Directory.Delete(receipt!);
        }
    }

    [TestCase("numeric")]
    [TestCase("receipt")]
    public void ProjectionPrepareRejectsWritableInputsBeforeCreatingOutput(string selected)
    {
        using var fixture = new Preparation(); fixture.UseProjection();
        string path = selected == "numeric" ? fixture.Source : fixture.Receipt;
        byte[] before = File.ReadAllBytes(path);
        FileShare writerShare = OperatingSystem.IsWindows() ? FileShare.Read : FileShare.None;
        using (var writer = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, writerShare))
        {
            IOException? failure = null;
            try { PrepareBattleData.Prepare(fixture.Input, fixture.Output, fixture.Receipt); }
            catch (IOException error) { failure = error; }
            AssertThat(failure is not null).IsTrue();
            AssertThat(Directory.Exists(fixture.Output)).IsFalse();
        }
        AssertThat(File.ReadAllBytes(path)).IsEqual(before);
        using var released = new FileStream(fixture.Source, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [TestCase]
    public void ProjectionPrepareIgnoresUnselectedSiblingReceipt()
    {
        using var fixture = new Preparation(); fixture.UseProjection();
        File.WriteAllText(fixture.Receipt, "invalid receipt must not be auto-discovered");
        var loaded = NativeBattleContent.LoadChecked(PrepareBattleData.Prepare(fixture.Input, fixture.Output));
        AssertThat(loaded.ProjectedSource is null).IsTrue();
        AssertThat(loaded.Content.Players.Select(player => player.RoleId).ToArray()).IsEqual(new[] { 4, 0 });
    }

    [TestCase]
    public void ProjectionPrepareNeverOverwritesPublishedOutput()
    {
        using var fixture = new Preparation(); fixture.UseProjection();
        PrepareBattleData.Prepare(fixture.Input, fixture.Output, fixture.Receipt);
        string[] before = Snapshot(fixture.Output);
        RejectProjection(fixture, fixture.Receipt, "new or empty");
        AssertThat(Snapshot(fixture.Output)).IsEqual(before);
    }

    private static string Digest(byte[] bytes) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
    private static void RejectProjection(Preparation fixture, string? receipt, string diagnostic)
    {
        InvalidDataException? failure = null;
        try { PrepareBattleData.Prepare(fixture.Input, fixture.Output, receipt!); }
        catch (InvalidDataException error) { failure = error; }
        AssertThat(failure is not null).OverrideFailureMessage("Expected projected Prepare rejection: " + diagnostic).IsTrue();
        AssertThat(failure!.Message.Contains(diagnostic, StringComparison.OrdinalIgnoreCase)).OverrideFailureMessage(failure.Message).IsTrue();
    }

    [TestCase(321, 654)]
    [TestCase(0, 65535)]
    [TestCase(65535, 0)]
    [TestCase(0, 0)]
    public void PublicPreparePreservesPlayerPanelsAndBossFleeBase(int magicPower, int fleeRate)
    {
        using var fixture = new Preparation();
        var json = JsonNode.Parse(File.ReadAllText(fixture.Source))!;
        if (magicPower != 0 || fleeRate != 0)
        {
            json["players"]![0]!["magicPower"] = magicPower;
            json["players"]![0]!["fleeRate"] = fleeRate;
        }
        File.WriteAllText(fixture.Source, json.ToJsonString());
        byte[] before = File.ReadAllBytes(fixture.Source);
        var content = NativeBattleContent.Load(PrepareBattleData.Prepare(fixture.Input, fixture.Output));
        AssertThat(content.Player.MagicPower).IsEqual(magicPower);
        AssertThat(content.Player.FleeRate).IsEqual(fleeRate);
        var battle = new ClassicBattleLifecycleSession(content.InitialRoles.Values.ToArray(), content.Players,
            content.EnemyTeams[18], content.GrowthByRole, new BattleRandom(), isBossBattle: true);
        battle.SelectFlee(0);
        AssertThat(battle.ResolveNextAction()).IsTrue();
        AssertThat(battle.Current.Combat.LastFleeAttempt).IsEqual(new ClassicBattleFleeAttempt(1, 0, 1, fleeRate, 24, 0, false));
        AssertThat(File.ReadAllBytes(fixture.Source)).IsEqual(before);
        AssertThat(Directory.GetFileSystemEntries(fixture.Output).Length).IsEqual(1);
    }

    [TestCase("legacy")]
    [TestCase("zero")]
    [TestCase("word-max")]
    [TestCase("alias")]
    [TestCase("distinct-extremes")]
    [TestCase("distinct")]
    public void PublicPreparePreservesNameIdentityAndHookSeeds(string kind)
    {
        (int? first, int? second, int dying, int friend) = kind switch
        {
            "legacy" => ((int?)null, (int?)null, 0, 0),
            "zero" => (0, 0, 0, 0),
            "word-max" => (65535, 65535, 65535, 65535),
            "alias" => (37, 37, 123, 456),
            "distinct-extremes" => (0, 65535, 123, 456),
            "distinct" => (36, 37, 123, 456),
            _ => throw new ArgumentException(nameof(kind))
        };
        using var fixture = new Preparation();
        var json = JsonNode.Parse(File.ReadAllText(fixture.Source))!;
        foreach (string field in new[] { "roles", "players", "growth" })
        {
            var copy = json[field]![0]!.DeepClone(); copy["roleId"] = 4;
            json[field]!.AsArray().Add(copy);
        }
        json["players"]![0]!["roleId"] = 4;
        json["players"]![1]!["roleId"] = 1;
        if (first.HasValue) json["players"]![0]!["nameObjectId"] = first.Value;
        if (second.HasValue) json["players"]![1]!["nameObjectId"] = second.Value;
        foreach (var player in json["players"]!.AsArray())
        {
            player!["dyingScript"] = dying;
            player!["friendDeathScript"] = friend;
        }
        File.WriteAllText(fixture.Source, json.ToJsonString(), new System.Text.UTF8Encoding(false));
        byte[] before = File.ReadAllBytes(fixture.Source);
        string uri = PrepareBattleData.Prepare(fixture.Input, fixture.Output);
        AssertThat(uri).IsEqual(ProjectSettings.LocalizePath(fixture.Output).TrimEnd('/') + "/BattleContent.tres");
        var content = NativeBattleContent.Load(uri);
        AssertThat(content.Players.Select(player => player.RoleId).ToArray()).IsEqual(new[] { 4, 1 });
        AssertThat(content.InitialRoles.Keys.ToArray()).IsEqual(new[] { 1, 4 });
        AssertThat(content.GrowthByRole.Keys.ToArray()).IsEqual(new[] { 1, 4 });
        AssertThat(content.Players[0].NameObjectId == first).IsTrue();
        AssertThat(content.Players[1].NameObjectId == second).IsTrue();
        AssertThat(content.Players.Select(player => player.DyingScript).ToArray()).IsEqual(new[] { dying, dying });
        AssertThat(content.Players.Select(player => player.FriendDeathScript).ToArray()).IsEqual(new[] { friend, friend });
        AssertThat(File.ReadAllBytes(fixture.Source)).IsEqual(before);
        AssertThat(Directory.GetFileSystemEntries(fixture.Output).Length).IsEqual(1);
        AssertThat(File.Exists(Path.Combine(fixture.Output, "BattleContent.tres"))).IsTrue();
    }

    [TestCase("null")]
    [TestCase("true")]
    [TestCase("false")]
    [TestCase("string")]
    [TestCase("fraction")]
    [TestCase("decimal")]
    [TestCase("negative")]
    [TestCase("overflow")]
    [TestCase("object")]
    [TestCase("array")]
    [TestCase("partial-first")]
    [TestCase("partial-second")]
    [TestCase("dying-forward")]
    [TestCase("dying-reverse")]
    [TestCase("friend-forward")]
    [TestCase("friend-reverse")]
    public void PublicPrepareRejectsInvalidNameMappingWithoutPublishing(string kind)
    {
        using var fixture = new Preparation();
        var json = JsonNode.Parse(File.ReadAllText(fixture.Source))!;
        foreach (string field in new[] { "roles", "players", "growth" })
        {
            var copy = json[field]![0]!.DeepClone(); copy["roleId"] = 4;
            json[field]!.AsArray().Add(copy);
        }
        json["players"]![0]!["roleId"] = 4;
        json["players"]![1]!["roleId"] = 1;
        string? token = kind switch
        {
            "null" => "null", "true" => "true", "false" => "false", "string" => "\"0\"",
            "fraction" => "1.5", "decimal" => "1.0", "negative" => "-1", "overflow" => "65536",
            "object" => "{}", "array" => "[]", _ => null
        };
        string diagnostic;
        if (token is not null)
        {
            json["players"]![0]!["nameObjectId"] = JsonNode.Parse(token);
            diagnostic = "players[0].nameObjectId: expected integer in 0..65535";
        }
        else if (kind.StartsWith("partial-", StringComparison.Ordinal))
        {
            json["players"]![kind == "partial-first" ? 0 : 1]!["nameObjectId"] = 37;
            diagnostic = "nameObjectId must be declared together for all players";
        }
        else
        {
            foreach (var player in json["players"]!.AsArray()) player!["nameObjectId"] = 37;
            string slot = kind.StartsWith("dying-", StringComparison.Ordinal) ? "dyingScript" : "friendDeathScript";
            json["players"]![1]![slot] = 1;
            if (kind.EndsWith("-reverse", StringComparison.Ordinal))
                json["players"] = new JsonArray(json["players"]!.AsArray().Reverse().Select(player => player!.DeepClone()).ToArray());
            diagnostic = "players[1]." + slot + ": players with the same nameObjectId must declare identical " + slot + " seeds";
        }
        string text = json.ToJsonString();
        if (token is not null)
        {
            // Validate the fixture before invoking Prepare: notably JSON null is present and 1.0 stays raw 1.0.
            using var parsed = System.Text.Json.JsonDocument.Parse(text);
            AssertThat(parsed.RootElement.GetProperty("players")[0].GetProperty("nameObjectId").GetRawText()).IsEqual(token);
        }
        File.WriteAllText(fixture.Source, text, new System.Text.UTF8Encoding(false));
        byte[] before = File.ReadAllBytes(fixture.Source);
        Reject(fixture.Input, fixture.Output, diagnostic);
        AssertThat(File.ReadAllBytes(fixture.Source)).IsEqual(before);
        AssertThat(Directory.Exists(fixture.Output)).IsFalse();
    }

    [TestCase("magicPower", -1)]
    [TestCase("magicPower", 65536)]
    [TestCase("fleeRate", -1)]
    [TestCase("fleeRate", 65536)]
    [TestCase("duplicate-roles", 0)]
    [TestCase("missing-roles", 0)]
    [TestCase("duplicate-players", 0)]
    [TestCase("missing-players", 0)]
    public void PublicPrepareRejectsInvalidPanelsAndRoleIdentityWithoutPublishing(string defect, int value)
    {
        using var fixture = new Preparation();
        var json = JsonNode.Parse(File.ReadAllText(fixture.Source))!;
        if (defect is "magicPower" or "fleeRate") json["players"]![0]![defect] = value;
        else
        {
            string field = defect.Split('-')[1];
            if (defect.StartsWith("duplicate-", StringComparison.Ordinal)) json[field]!.AsArray().Add(json[field]![0]!.DeepClone());
            else json[field]!.AsArray().Clear();
        }
        File.WriteAllText(fixture.Source, json.ToJsonString());
        byte[] before = File.ReadAllBytes(fixture.Source);
        Reject(fixture.Input, fixture.Output, defect is "magicPower" or "fleeRate" ? defect : "unique matching RoleId");
        AssertThat(Directory.Exists(fixture.Output)).IsFalse();
        AssertThat(File.ReadAllBytes(fixture.Source)).IsEqual(before);
    }

    [TestCase]
    public void PublicPreparePublishesTwoRolesAndRepeatedEnemySlotsWithoutProjection()
    {
        using var fixture = new Preparation();
        var json = JsonNode.Parse(File.ReadAllText(fixture.Source))!;
        foreach (string field in new[] { "roles", "players", "growth" })
        {
            var copy = json[field]![0]!.DeepClone(); copy["roleId"] = 4;
            json[field]!.AsArray().Add(copy);
        }
        json["players"]![0]!["roleId"] = 4;
        json["players"]![1]!["roleId"] = 1;
        json["teams"]![0]!["enemies"]!.AsArray().Add(json["teams"]![0]!["enemies"]![0]!.DeepClone());
        File.WriteAllText(fixture.Source, json.ToJsonString(), new System.Text.UTF8Encoding(false));
        byte[] before = File.ReadAllBytes(fixture.Source);
        var content = NativeBattleContent.Load(PrepareBattleData.Prepare(fixture.Input, fixture.Output));
        AssertThat(content.Players.Select(x => x.RoleId).ToArray()).IsEqual(new[] { 4, 1 });
        AssertThat(content.InitialRoles.Keys.OrderBy(x => x).ToArray()).IsEqual(new[] { 1, 4 });
        AssertThat(content.GrowthByRole.Count).IsEqual(2);
        AssertThat(content.EnemyTeams[18].Select(x => x.Id).ToArray()).IsEqual(new[] { 7, 7 });
        var battle = new ClassicBattleLifecycleSession(content.InitialRoles.Values.ToArray(), content.Players,
            content.EnemyTeams[18], content.GrowthByRole, new BattleRandom());
        battle.SelectAttack(0, 0); battle.SelectAttack(1, 1);
        AssertThat(battle.ResolveNextAction()).IsTrue(); AssertThat(battle.ResolveNextAction()).IsTrue();
        AssertThat(battle.Completion!.Outcome).IsEqual(ClassicBattlePhase.Won);
        AssertThat(battle.Current.Roles[4].Hp).IsEqual(125);
        AssertThat(battle.Current.Roles[1].Mp).IsEqual(35);
        AssertThat(File.ReadAllBytes(fixture.Source)).IsEqual(before);
        AssertThat(Directory.GetFileSystemEntries(fixture.Output).Length).IsEqual(1);
    }

    [TestCase]
    public void PublicPreparePublishesStrictNativeContentAndRealKoSettlesRole()
    {
        using var fixture = new Preparation();
        byte[] before = File.ReadAllBytes(fixture.Source);
        string uri = PrepareBattleData.Prepare(fixture.Input, fixture.Output);
        AssertThat(uri).IsEqual(ProjectSettings.LocalizePath(fixture.Output).TrimEnd('/') + "/BattleContent.tres");
        var content = NativeBattleContent.Load(uri);
        AssertThat(content.InitialRole.PrimaryExperience.Count).IsEqual(17);
        AssertThat(content.InitialRole.HiddenExperience["flee"].Count).IsEqual(55);
        AssertThat(content.Growth.LevelUpMagics[1]).IsEqual(new BattleLevelUpMagic(3, 10));
        var battle = new ClassicBattleLifecycleSession(content.InitialRole, content.Player, content.Teams[18], content.Growth, new BattleRandom());
        battle.SelectAttack(7);
        AssertThat(battle.ResolveNextAction()).IsTrue();
        AssertThat(battle.Completion!.Outcome).IsEqual(ClassicBattlePhase.Won);
        AssertThat(battle.Current.Role.Hp).IsEqual(125);
        AssertThat(battle.Current.Role.Mp).IsEqual(35);
        AssertThat(battle.OwnsCompletion(battle.Completion)).IsTrue();
        AssertThat(File.ReadAllBytes(fixture.Source)).IsEqual(before);
        AssertThat(Directory.GetFileSystemEntries(fixture.Output).Length).IsEqual(1);
    }

    [TestCase]
    public void LegalNonzeroCapabilitiesAndZeroThresholdsArePreparedWithoutExecutionGate()
    {
        using var fixture = new Preparation();
        var json = JsonNode.Parse(File.ReadAllText(fixture.Source))!;
        json["roles"]![0]!["equipment"]![0] = 12;
        json["players"]![0]!["equipment"]![0] = 12;
        json["players"]![0]!["dyingScript"] = 123;
        json["teams"]![0]!["enemies"]![0]!["cash"] = 7;
        json["growth"]![0]!["levelUpExperience"]![2] = 0;
        json["growth"]![0]!["levelUpMagics"]!.AsArray().Add(new JsonObject { ["level"] = 2, ["magicId"] = 9 });
        File.WriteAllText(fixture.Source, json.ToJsonString());
        var content = NativeBattleContent.Load(PrepareBattleData.Prepare(fixture.Input, fixture.Output));
        AssertThat(content.InitialRole.Equipment[0]).IsEqual(12);
        AssertThat(content.Player.Equipment[0]).IsEqual(12);
        AssertThat(content.Player.DyingScript).IsEqual(123);
        AssertThat(content.Teams[18].Cash).IsEqual(7);
        AssertThat(content.Growth.LevelUpExperience[2]).IsEqual(0);
        AssertThat(content.Growth.LevelUpMagics.Count).IsEqual(3);
        AssertThat(content.Growth.LevelUpMagics[2]).IsEqual(new BattleLevelUpMagic(2, 9));
    }

    [TestCase]
    public void EquipmentClosurePreparesStrictlyAndInitializesSixRoleState()
    {
        using var fixture = new Preparation();
        File.WriteAllText(fixture.Source, EquipmentJson(fixture.Source).ToJsonString(), new System.Text.UTF8Encoding(false));
        byte[] before = File.ReadAllBytes(fixture.Source);
        var content = NativeBattleContent.Load(PrepareBattleData.Prepare(fixture.Input, fixture.Output));
        AssertThat(content.RoleEquipment.Count).IsEqual(6);
        AssertThat(content.EquipmentObjects[170].Effects[1].Statuses[8]).IsEqual(60000);
        var state = BattleEquipmentState.Initialize(content);
        // Hand-computed from the reference getters: 35+3+4, 20+1+6, 100+8, last non-zero battle sprite 450/600.
        AssertThat(state.EffectivePlayer(content.Player).Attack).IsEqual(42);
        AssertThat(state.EffectiveDefense(1, 20)).IsEqual(27);
        AssertThat(state.EffectiveDexterity(1, 100)).IsEqual(108);
        AssertThat(state.BattleSprite(1, 3)).IsEqual(450);
        AssertThat(state.BattleSprite(0, 3)).IsEqual(600);
        AssertThat(state.ApplyStatuses(1, new int[9]).ToArray()).IsEqual(new[] { 0, 0, 0, 0, 0, 0, 0, 0, 60000 });
        AssertThat(File.ReadAllBytes(fixture.Source)).IsEqual(before);
        AssertThat(Directory.GetFileSystemEntries(fixture.Output).Length).IsEqual(1);
    }

    [TestCase]
    public void InvalidEquipmentClosureIsRejectedWithoutPublishingOrTouchingInput()
    {
        using var fixture = new Preparation();
        var json = EquipmentJson(fixture.Source);
        json["equipments"]!.AsArray().Add(json["equipments"]![0]!.DeepClone());
        File.WriteAllText(fixture.Source, json.ToJsonString(), new System.Text.UTF8Encoding(false));
        byte[] before = File.ReadAllBytes(fixture.Source);
        InvalidDataException? error = null;
        try { PrepareBattleData.Prepare(fixture.Input, fixture.Output); }
        catch (InvalidDataException caught) { error = caught; }
        AssertThat(error is not null && error.Message.Contains("duplicate equipment object ID", StringComparison.Ordinal)).IsTrue();
        AssertThat(Directory.Exists(fixture.Output)).IsFalse();
        AssertThat(File.ReadAllBytes(fixture.Source)).IsEqual(before);
    }

    [TestCase("missing", "exactly battle-data.json")]
    [TestCase("extra-file", "exactly battle-data.json")]
    [TestCase("extra-directory", "exactly battle-data.json")]
    [TestCase("nested-file", "exactly battle-data.json")]
    [TestCase("wrong-file", "exactly battle-data.json")]
    [TestCase("empty", "1..16777216 bytes")]
    [TestCase("oversize", "1..16777216 bytes")]
    [TestCase("utf8", "UTF-8")]
    [TestCase("utf16", "UTF-8")]
    [TestCase("json", "battle-data.json")]
    [TestCase("missing-field", "formatVersion")]
    [TestCase("wrong-version", "formatVersion")]
    public void InvalidSourceIsRejectedWithoutChangingInputOrExistingEmptyOutput(string defect, string diagnostic)
    {
        using var fixture = new Preparation();
        switch (defect)
        {
            case "missing": File.Delete(fixture.Source); break;
            case "extra-file": File.WriteAllText(Path.Combine(fixture.Input, "extra"), "keep"); break;
            case "extra-directory": Directory.CreateDirectory(Path.Combine(fixture.Input, "empty")); break;
            case "nested-file": Directory.CreateDirectory(Path.Combine(fixture.Input, "nested")); File.WriteAllText(Path.Combine(fixture.Input, "nested", "extra"), "keep"); break;
            case "wrong-file": File.Move(fixture.Source, fixture.Source + ".wrong"); break;
            case "empty": File.WriteAllBytes(fixture.Source, []); break;
            case "oversize": using (var stream = File.OpenWrite(fixture.Source)) stream.SetLength(16777217); break;
            case "utf8": File.WriteAllBytes(fixture.Source, [0xc3, 0x28]); break;
            case "utf16": File.WriteAllBytes(fixture.Source, [0xff, 0xfe, 0x7b, 0]); break;
            case "json": File.WriteAllText(fixture.Source, "{"); break;
            case "missing-field": File.WriteAllText(fixture.Source, File.ReadAllText(fixture.Source).Replace("\"formatVersion\": 1,", "")); break;
            case "wrong-version": File.WriteAllText(fixture.Source, File.ReadAllText(fixture.Source).Replace("\"formatVersion\": 1", "\"formatVersion\": 2")); break;
        }
        string[] before = Snapshot(fixture.Input);
        Directory.CreateDirectory(fixture.Output);
        Reject(fixture.Input, fixture.Output, diagnostic);
        AssertThat(Snapshot(fixture.Input)).IsEqual(before);
        AssertThat(Directory.Exists(fixture.Output)).IsTrue();
        AssertThat(Directory.GetFileSystemEntries(fixture.Output).Length).IsEqual(0);
    }

    [TestCase("file")]
    [TestCase("directory")]
    [TestCase("published")]
    public void OutputEntriesAreNeverOverwrittenOrRemoved(string entry)
    {
        using var fixture = new Preparation();
        Directory.CreateDirectory(fixture.Output);
        string sentinel = Path.Combine(fixture.Output, entry == "published" ? "BattleContent.tres" : "sentinel");
        if (entry == "directory") Directory.CreateDirectory(sentinel);
        else File.WriteAllText(sentinel, "caller-owned");
        string[] before = Snapshot(fixture.Output);
        Reject(fixture.Input, fixture.Output, "new or empty");
        AssertThat(Snapshot(fixture.Output)).IsEqual(before);
    }

    [TestCase("output-file", "existing file")]
    [TestCase("outside-assets", "project Assets")]
    [TestCase("input-file", "existing directory")]
    [TestCase("input-missing", "existing directory")]
    [TestCase("input-project", "outside src/Pal.Game")]
    [TestCase("overlap-input-parent", "overlap")]
    [TestCase("overlap-output-parent", "overlap")]
    public void PublicApiRejectsUnsafePathsBeforeCreatingOutput(string defect, string diagnostic)
    {
        using var fixture = new Preparation();
        string input = fixture.Input, output = fixture.Output;
        switch (defect)
        {
            case "output-file": Directory.CreateDirectory(Path.GetDirectoryName(output)!); File.WriteAllText(output, "keep"); break;
            case "outside-assets": output = Path.Combine(fixture.Input, "outside"); break;
            case "input-file": input = fixture.Source; break;
            case "input-missing": input += "-missing"; break;
            case "input-project": input = ProjectSettings.GlobalizePath("res://Tests"); break;
            case "overlap-input-parent": input = Path.GetDirectoryName(output)!; break;
            case "overlap-output-parent": input = Path.Combine(output, "input"); break;
        }
        try
        {
            Reject(input, output, diagnostic);
            if (defect == "output-file") AssertThat(File.ReadAllText(output)).IsEqual("keep");
            else AssertThat(Directory.Exists(output)).IsFalse();
        }
        finally { if (defect == "output-file") File.Delete(output); }
    }

    [TestCase]
    public void BomAndNormalizedPathsAndMissingAncestorsAreSupportedWithoutOverwrite()
    {
        using var fixture = new Preparation();
        byte[] bytes = File.ReadAllBytes(fixture.Source);
        File.WriteAllBytes(fixture.Source, new byte[] { 0xef, 0xbb, 0xbf }.Concat(bytes).ToArray());
        string output = Path.Combine(fixture.Output, "new-parent", "native");
        string uri = PrepareBattleData.Prepare(fixture.Input + Path.DirectorySeparatorChar + ".", output + Path.DirectorySeparatorChar);
        AssertThat(NativeBattleContent.Load(uri).InitialRole.Hp).IsEqual(100);
        string[] before = Snapshot(fixture.Output);
        Reject(fixture.Input, output, "new or empty");
        AssertThat(Snapshot(fixture.Output)).IsEqual(before);
        AssertThat(File.ReadAllBytes(fixture.Source).Length).IsEqual(bytes.Length + 3);
    }

    [TestCase("input")]
    [TestCase("input-ancestor")]
    [TestCase("output")]
    [TestCase("output-ancestor")]
    [TestCase("dangling-output-ancestor")]
    [TestCase("input-extra-link")]
    public void ReparseDirectoriesAndAncestorsAreRejectedWithoutTouchingTargets(string kind)
    {
        using var fixture = new Preparation();
        string link = kind == "input-extra-link" ? Path.Combine(fixture.Input, "linked")
            : kind.StartsWith("input", StringComparison.Ordinal) ? fixture.Input + "-link" : fixture.Output;
        string target = kind is "input" or "input-ancestor" ? fixture.Input : fixture.Input + "-target";
        Directory.CreateDirectory(target);
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        CreateDirectoryLink(link, target);
        if (kind == "dangling-output-ancestor") Directory.Delete(target);
        string input = kind == "input" ? link : kind == "input-ancestor" ? Path.Combine(link, "missing") : fixture.Input;
        string output = kind == "output" ? link : kind.Contains("output", StringComparison.Ordinal) ? Path.Combine(link, "missing", "native") : fixture.Output;
        byte[] before = File.ReadAllBytes(fixture.Source);
        try
        {
            Reject(input, output, "link/reparse");
            AssertThat(File.ReadAllBytes(fixture.Source)).IsEqual(before);
            AssertThat((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0).IsTrue();
            if (Directory.Exists(target) && target != fixture.Input) AssertThat(Directory.GetFileSystemEntries(target).Length).IsEqual(0);
        }
        finally
        {
            if (OperatingSystem.IsWindows()) Directory.Delete(link);
            else File.Delete(link);
            if (target != fixture.Input && Directory.Exists(target)) Directory.Delete(target);
        }
    }

    [TestCase]
    public void ExistingEmptyOutputAcceptsNormalizedWindowsCaseAndParentSegments()
    {
        using var fixture = new Preparation();
        Directory.CreateDirectory(fixture.Output);
        string input = fixture.Input;
        string output = Path.Combine(fixture.Output, "..", Path.GetFileName(Path.TrimEndingDirectorySeparator(fixture.Output)));
        if (OperatingSystem.IsWindows()) { input = input.ToUpperInvariant(); output = output.ToUpperInvariant(); }
        string uri = PrepareBattleData.Prepare(input, output);
        AssertThat(uri.StartsWith("res://Assets/", StringComparison.Ordinal)).IsTrue();
        AssertThat(NativeBattleContent.Load(uri).InitialRole.Hp).IsEqual(100);
        AssertThat(File.Exists(Path.Combine(fixture.Output, "BattleContent.tres"))).IsTrue();
    }

    private static void CreateDirectoryLink(string link, string target)
    {
        if (!OperatingSystem.IsWindows()) { Directory.CreateSymbolicLink(link, target); return; }
        var info = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in new[] { "/c", "mklink", "/J", Path.GetFullPath(link), Path.GetFullPath(target) }) info.ArgumentList.Add(arg);
        using var process = System.Diagnostics.Process.Start(info)!;
        string text = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        AssertThat(process.ExitCode).OverrideFailureMessage(text).IsEqual(0);
    }

    private static void Reject(string input, string output, string diagnostic)
    {
        InvalidDataException? error = null;
        try { PrepareBattleData.Prepare(input, output); }
        catch (InvalidDataException caught) { error = caught; }
        AssertThat(error is not null).OverrideFailureMessage("Expected public Prepare rejection: " + diagnostic).IsTrue();
        AssertThat(error!.Message.Contains(diagnostic, StringComparison.Ordinal)).OverrideFailureMessage(error.Message).IsTrue();
    }
    private static string[] Snapshot(string root) => Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal).Select(path => Path.GetRelativePath(root, path) + ":" +
            (File.Exists(path) ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))) : "directory")).ToArray();

    private static JsonNode EquipmentJson(string source)
    {
        var data = JsonNode.Parse(File.ReadAllText(source))!;
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

    private sealed class Preparation : IDisposable
    {
        internal string Input { get; } = Path.Combine(Path.GetTempPath(), "pal-numeric-input-" + Guid.NewGuid().ToString("N"));
        internal string Output { get; } = ProjectSettings.GlobalizePath("res://Assets/PrepareTests/" + Guid.NewGuid().ToString("N"));
        internal string Source => Path.Combine(Input, "battle-data.json");
        internal string Receipt => Input + "-receipt.json";
        internal void UseProjection()
        {
            string root = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "../../tests/Fixtures/ProjectedBattleData"));
            File.Copy(Path.Combine(root, "numeric", "battle-data.json"), Source, true);
            File.Copy(Path.Combine(root, "receipt.json"), Receipt);
        }
        internal Preparation()
        {
            Directory.CreateDirectory(Input);
            File.Copy(Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "../../tests/Fixtures/ModernBattleData/battle-data.json")), Source);
        }
        public void Dispose()
        {
            if (Directory.Exists(Input)) Directory.Delete(Input, true);
            if (File.Exists(Receipt)) File.Delete(Receipt);
            if (Directory.Exists(Output)) Directory.Delete(Output, true);
        }
    }
    private sealed class BattleRandom : IBattleRandom
    {
        public int NextInclusive(int from, int to) => from == 0 && to == 5 ? 1 : from;
        public float NextInclusive(float from, float to) => 1;
    }
}
