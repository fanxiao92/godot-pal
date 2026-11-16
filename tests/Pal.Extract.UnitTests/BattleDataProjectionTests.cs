using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Pal.Core;
using Pal.Extract;
using Xunit;

namespace Pal.Extract.Tests;

public sealed class BattleDataProjectionTests
{
    [Fact]
    public void Projection_preserves_role_identity_growth_signed_offsets_slots_and_all_input_bytes()
    {
        var files = BattleProjectionFixture.Create();
        var before = files.ToDictionary(x => x.Key, x => x.Value.ToArray());
        var result = BattleDataProjector.Project(files, [4, 0], [0]);
        var content = ModernBattleContent.Parse(result.NumericJson);
        ModernBattleContent.ValidatePhysicalBattle(content, 0);
        Assert.Equal(new[] { 4, 0 }, content.Players.Select(p => p.RoleId));
        var role = content.InitialRoles[4];
        Assert.Equal((7, 80, 100, 123, 27, 19), (role.Level, role.Hp, role.MaxHp, role.Attack, role.MagicPower, role.FleeRate));
        Assert.Equal(41, content.InitialRoles[0].Attack);
        Assert.Equal(new BattleExperienceSnapshot(0, 7, 0), role.PrimaryExperience);
        Assert.Equal(7, role.HiddenExperience.Count);
        Assert.All(role.HiddenExperience.Values, xp => Assert.Equal(new BattleExperienceSnapshot(0, 7, 0), xp));
        Assert.Equal(8, role.Magics[3]);
        Assert.Equal(0, role.CoveredByRoleId); // zero means role0, never null
        Assert.Equal(0, content.Players[0].CoveredByRoleId);
        Assert.Equal(4, content.Players[1].CoveredByRoleId);
        Assert.Equal(4, content.Players[0].NameObjectId);
        Assert.Equal(new[] { new BattleLevelUpMagic(8, 8), new BattleLevelUpMagic(8, 8), new BattleLevelUpMagic(0, 0) }, content.GrowthByRole[4].LevelUpMagics);
        Assert.Equal(100, content.GrowthByRole[4].LevelUpExperience.Count);
        Assert.Equal(new[] { 0, 101, 102 }, content.GrowthByRole[4].LevelUpExperience.Take(3));
        Assert.Equal(199, content.GrowthByRole[4].LevelUpExperience[99]);
        Assert.Equal(new[] { 1, 2 }, content.EnemyTeams[0].Select(e => e.Id));
        Assert.All(content.EnemyTeams[0], e => Assert.Equal((-1, -32768, 32767, 29, 31), (e.Attack, e.Defense, e.Dexterity, e.Experience, e.Cash)));
        var receipt = JsonNode.Parse(result.ReceiptJson)!;
        Assert.Equal("independent-base-table-context-not-natural-save", receipt["snapshotKind"]!.GetValue<string>());
        Assert.Equal(14, receipt["modernInputs"]!.AsArray().Count);
        Assert.Equal(new[] { 0, 2 }, receipt["enemySlots"]!.AsArray().Where(x => x!["disposition"]!.GetValue<string>() == "loaded").Select(x => x!["sourceSlot"]!.GetValue<int>()));
        Assert.Equal(3, receipt["enemySlots"]!.AsArray().Count(x => x!["disposition"]!.GetValue<string>() == "skip-FFFF"));
        Assert.Contains("not-independent-authentication", receipt["inputTrust"]!.GetValue<string>());
        Assert.Equal(result, BattleDataProjector.Project(files, [4, 0], [0]));
        foreach (string name in files.Keys) Assert.Equal(before[name], files[name]);
        Assert.True(receipt["fieldSources"]!.AsArray().Count > 300);
    }

    [Fact]
    public void Zero_hp_and_level_zero_are_preserved_not_pre_normalized()
    {
        var files = BattleProjectionFixture.Create();
        BattleProjectionFixture.Mutate(files, "player-roles.json", d => { d["records"]![0]!["hp"]![4] = 0; d["records"]![0]!["level"]![4] = 0; });
        var content = ModernBattleContent.Parse(BattleDataProjector.Project(files, [4], [0]).NumericJson);
        Assert.Equal(0, content.InitialRoles[4].Hp);
        Assert.Equal(0, content.Players[0].Hp);
        Assert.Equal(new BattleExperienceSnapshot(0, 0, 0), content.InitialRoles[4].PrimaryExperience);
        Assert.Equal(0, content.GrowthByRole[4].LevelUpExperience[0]);
    }

    [Fact]
    public void Edited_legal_json_changes_modern_identity_not_the_original_source_declaration()
    {
        var files = BattleProjectionFixture.Create();
        var first = JsonNode.Parse(BattleDataProjector.Project(files, [4], [0]).ReceiptJson)!;
        BattleProjectionFixture.Mutate(files, "enemy.json", d => d["records"]![0]!["attackStrength"] = 11);
        var result = BattleDataProjector.Project(files, [4], [0]);
        var second = JsonNode.Parse(result.ReceiptJson)!;
        Assert.Equal(first["declaredSources"]!.ToJsonString(), second["declaredSources"]!.ToJsonString());
        Assert.Equal(first["declaredTables"]!.ToJsonString(), second["declaredTables"]!.ToJsonString());
        string Hash(JsonNode n) => n["modernInputs"]!.AsArray().Single(x => x!["file"]!.GetValue<string>() == "enemy.json")!["sha256"]!.GetValue<string>();
        Assert.NotEqual(Hash(first), Hash(second));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(files["enemy.json"])), Hash(second));
        Assert.Equal(11, ModernBattleContent.Parse(result.NumericJson).EnemyTeams[0][0].Attack);
    }

    [Theory]
    [InlineData("0", "zero")]
    [InlineData("three", "one or two")]
    [InlineData("empty", "no loaded")]
    [InlineData("magic", "magicId")]
    [InlineData("FFFF", "magicId")]
    [InlineData("dual", "dualMove")]
    [InlineData("equivalent", "attackEquivItemId")]
    [InlineData("rate", "attackEquivItemRate")]
    [InlineData("ready", "scriptOnReady")]
    [InlineData("end", "scriptOnBattleEnd")]
    [InlineData("turn", "scriptOnTurnStart")]
    [InlineData("dying", "scriptOnDying")]
    [InlineData("friend", "scriptOnFriendDeath")]
    [InlineData("attackAll", "attackAll")]
    [InlineData("cooperative", "cooperativeMagic")]
    [InlineData("name", "player view")]
    [InlineData("enemyView", "enemy view")]
    [InlineData("missingEnemy", "missing referenced")]
    [InlineData("missingMagic", "missing referenced")]
    [InlineData("magicView", "magic view")]
    [InlineData("cover", "cover")]
    [InlineData("maxHP", "maxHP")]
    [InlineData("level", "baseline role")]
    [InlineData("threshold99", "EXP")]
    public void Unsupported_or_dangling_content_is_rejected_with_a_location(string mutation, string message)
    {
        var files = BattleProjectionFixture.Create();
        if (mutation is "0" or "three" or "empty") BattleProjectionFixture.Mutate(files, "enemy-team.json", d =>
            d["records"]![0]!["enemies"] = mutation switch { "0" => new JsonArray(6, 0, 7, 65535, 65535), "three" => new JsonArray(6, 7, 6, 65535, 65535), _ => new JsonArray(65535, 65535, 65535, 65535, 65535) });
        else if (mutation is "magic" or "FFFF" or "dual" or "equivalent" or "rate") BattleProjectionFixture.Mutate(files, "enemy.json", d =>
            d["records"]![1]![mutation switch { "magic" or "FFFF" => "magicId", "dual" => "dualMove", "rate" => "attackEquivItemRate", _ => "attackEquivItemId" }] = mutation == "FFFF" ? 65535 : 8);
        else if (mutation is "ready" or "end" or "turn" or "dying" or "friend" or "missingEnemy") BattleProjectionFixture.Mutate(files, "object.json", d =>
            d["records"]![mutation is "dying" or "friend" ? 0 : 7]![mutation switch { "ready" => "scriptOnReady", "end" => "scriptOnBattleEnd", "turn" => "scriptOnTurnStart", "dying" => "scriptOnDying", "friend" => "scriptOnFriendDeath", _ => "enemyId" }] = 99);
        else if (mutation is "enemyView" or "magicView") BattleProjectionFixture.Mutate(files, "enemy-team.json", d => d["records"]![0]!["enemies"]![0] = mutation == "enemyView" ? 8 : 6);
        if (mutation == "magicView") BattleProjectionFixture.Mutate(files, "player-roles.json", d => d["records"]![0]!["magic"]![3]![4] = 6);
        else if (mutation == "missingMagic") BattleProjectionFixture.Mutate(files, "object.json", d => d["records"]![8]!["magicId"] = 99);
        else if (mutation == "threshold99") BattleProjectionFixture.Mutate(files, "level-up-exp.json", d => d["records"]![0]!["levelUpExp"]![99] = 0);
        else if (mutation is "cover" or "maxHP" or "level" or "name" or "attackAll" or "cooperative") BattleProjectionFixture.Mutate(files, "player-roles.json", d =>
            d["records"]![0]![mutation switch { "cover" => "coveredBy", "maxHP" => "maxHp", "name" => "name", "cooperative" => "cooperativeMagic", _ => mutation }]![4] = mutation switch { "cover" => 6, "maxHP" => 40000, "level" => 100, "name" => 6, _ => 1 });
        var error = Assert.Throws<InvalidDataException>(() => BattleDataProjector.Project(files, [4, 0], [0]));
        Assert.Contains(message, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("profile", "profile")]
    [InlineData("version", "formatVersion")]
    [InlineData("hash", "sha256")]
    [InlineData("archive", "archive")]
    [InlineData("chunk", "chunk")]
    [InlineData("count", "recordCount")]
    [InlineData("length", "byteLength")]
    [InlineData("duplicate", "duplicate")]
    [InlineData("missing", "13")]
    [InlineData("source", "source")]
    [InlineData("unknown", "unknown")]
    [InlineData("dimension", "elements")]
    [InlineData("word", "WORD")]
    [InlineData("multiRoles", "exactly one")]
    public void Complete_package_schema_and_index_are_checked_before_selection(string mutation, string message)
    {
        var files = BattleProjectionFixture.Create();
        if (mutation is "dimension" or "word") BattleProjectionFixture.Mutate(files, "player-roles.json", d =>
        {
            if (mutation == "dimension") d["records"]![0]!["hp"]!.AsArray().RemoveAt(5);
            else d["records"]![0]!["hp"]![5] = 65536; // unselected column still structurally checked
        });
        else if (mutation == "multiRoles")
        {
            BattleProjectionFixture.Mutate(files, "player-roles.json", d => d["records"]!.AsArray().Add(d["records"]![0]!.DeepClone()));
            BattleProjectionFixture.Mutate(files, "index.json", d => { var e = d["tables"]!.AsArray().Single(x => x!["file"]!.GetValue<string>() == "player-roles.json")!; e["recordCount"] = 2; e["byteLength"] = 1800; });
        }
        else BattleProjectionFixture.Mutate(files, "index.json", d =>
        {
            var entries = d["tables"]!.AsArray();
            if (mutation == "profile") d["profile"] = "other";
            else if (mutation == "version") d["formatVersion"] = 2;
            else if (mutation == "duplicate") entries.Add(entries[0]!.DeepClone());
            else if (mutation == "missing") entries.RemoveAt(0);
            else if (mutation == "source") d["sources"]!.AsArray().RemoveAt(0);
            else if (mutation == "unknown") d["mystery"] = true;
            else entries[0]![mutation switch { "hash" => "sha256", "count" => "recordCount", "length" => "byteLength", _ => mutation }] = mutation == "hash" ? JsonValue.Create("invalid") : mutation == "archive" ? JsonValue.Create("OTHER.MKF") : JsonValue.Create(999);
        });
        var error = Assert.Throws<InvalidDataException>(() => BattleDataProjector.Project(files, [4], [0]));
        Assert.Contains(message, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("", false)] [InlineData("4,0", true)] [InlineData("4,4", false)]
    [InlineData("+1", false)] [InlineData(" 1", false)] [InlineData("1,", false)]
    [InlineData("0x1", false)] [InlineData("65536", false)] [InlineData("-1", false)]
    public void Decimal_selection_is_strict(string input, bool valid) => Assert.Equal(valid, BattleDataProjector.TrySelection(input, out _));

    [Theory]
    [InlineData("role5", "sixth")]
    [InlineData("fourRoles", "roles")]
    [InlineData("duplicateRoles", "roles")]
    [InlineData("missingTeam", "enemy-team")]
    [InlineData("duplicateTeams", "teams")]
    public void Selection_domain_has_explicit_refusals(string selection, string diagnostic)
    {
        int[] roles = selection switch { "role5" => [5], "fourRoles" => [0, 1, 2, 4], "duplicateRoles" => [4, 4], _ => [4] };
        int[] teams = selection switch { "missingTeam" => [99], "duplicateTeams" => [0, 0], _ => [0] };
        Assert.Contains(diagnostic, Assert.Throws<InvalidDataException>(() => BattleDataProjector.Project(BattleProjectionFixture.Create(), roles, teams)).Message);
    }

    [Fact]
    public void Three_ordered_roles_project_party_order_into_three_player_content()
    {
        // battle.h MAX_PLAYERS_IN_PARTY: the projection selection accepts the third ordered role
        // and keeps roles/players/growth party order; four selections stay refused above.
        var result = BattleDataProjector.Project(BattleProjectionFixture.Create(), [0, 1, 4], [0]);
        var json = JsonNode.Parse(result.NumericJson)!;
        foreach (string section in new[] { "roles", "players", "growth" })
            Assert.Equal(new[] { 0, 1, 4 }, json[section]!.AsArray().Select(x => x!["roleId"]!.GetValue<int>()));
        var content = ModernBattleContent.Parse(result.NumericJson);
        Assert.Equal(new[] { 0, 1, 4 }, content.Players.Select(x => x.RoleId));
        Assert.Equal(new[] { 0, 1, 4 }, JsonNode.Parse(result.ReceiptJson)!["roles"]!.AsArray().Select(x => x!.GetValue<int>()));
        Assert.Contains(JsonNode.Parse(result.ReceiptJson)!["fieldSources"]!.AsArray(),
            x => x!["path"]!.GetValue<string>() == "players[2].hp");
    }

    [Fact]
    public void Multiple_teams_keep_selected_order_while_unselected_capabilities_remain_input_metadata()
    {
        var files = BattleProjectionFixture.Create();
        BattleProjectionFixture.Mutate(files, "enemy-team.json", d =>
        {
            d["records"]!.AsArray().Add(d["records"]![0]!.DeepClone());
            d["records"]![1]!["enemies"] = new JsonArray(7, 65535, 65535, 65535, 65535);
            d["records"]!.AsArray().Add(d["records"]![0]!.DeepClone());
            d["records"]![2]!["enemies"] = new JsonArray(8, 0, 0, 0, 0); // unused unsupported team
        });
        BattleProjectionFixture.Mutate(files, "index.json", d =>
        {
            var e = d["tables"]!.AsArray().Single(x => x!["file"]!.GetValue<string>() == "enemy-team.json")!;
            e["recordCount"] = 3; e["byteLength"] = 30;
        });
        var result = BattleDataProjector.Project(files, [0, 4], [1, 0]);
        var json = JsonNode.Parse(result.NumericJson)!;
        Assert.Equal(new[] { 1, 0 }, json["teams"]!.AsArray().Select(x => x!["teamId"]!.GetValue<int>()));
        var content = ModernBattleContent.Parse(result.NumericJson);
        Assert.Single(content.EnemyTeams[1]);
        Assert.Equal(2, content.EnemyTeams[0].Count);
        Assert.Equal(new[] { 0, 4 }, content.Players.Select(x => x.RoleId));
        var paths = JsonNode.Parse(result.ReceiptJson)!["fieldSources"]!.AsArray();
        Assert.Equal(paths.Count, paths.Select(x => x!["path"]!.GetValue<string>()).Distinct().Count());
        Assert.Contains(paths, x => x!["path"]!.GetValue<string>() == "roles[1].magics[3]"
            && x["source"]!.GetValue<string>().StartsWith("player-roles.json.records[0].magic[3][4]", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("bom")]
    [InlineData("comments")]
    public void M5_optional_bom_and_underscore_comments_are_not_gameplay_or_authentication(string mode)
    {
        var files = BattleProjectionFixture.Create();
        if (mode == "bom") files["enemy.json"] = [0xef, 0xbb, 0xbf, .. files["enemy.json"]];
        else BattleProjectionFixture.Mutate(files, "enemy.json", d => d["_note"] = "artificial, not authenticated");
        Assert.Equal(-1, ModernBattleContent.Parse(BattleDataProjector.Project(files, [4], [0]).NumericJson).EnemyTeams[0][0].Attack);
    }

    [Fact]
    public void Repeated_enemy_objects_are_distinct_compact_slots()
    {
        var files = BattleProjectionFixture.Create();
        BattleProjectionFixture.Mutate(files, "enemy-team.json", d => d["records"]![0]!["enemies"] = new JsonArray(6, 65535, 6, 65535, 65535));
        var content = ModernBattleContent.Parse(BattleDataProjector.Project(files, [4], [0]).NumericJson);
        Assert.Equal(new[] { 1, 2 }, content.EnemyTeams[0].Select(e => e.Id));
    }

    [Fact]
    public void Projection_refuses_equipment_without_script_closure_instead_of_zeroing_it()
    {
        var files = BattleProjectionFixture.Create();
        BattleProjectionFixture.Mutate(files, "player-roles.json", d => d["records"]![0]!["equipment"]![2]![4] = 8);

        var error = Assert.Throws<InvalidDataException>(() => BattleDataProjector.Project(files, [4, 0], [0]));

        Assert.Contains("player-roles.json.records[0].equipment[2][4]", error.Message);
    }
}
