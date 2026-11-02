using System.Text.Json.Nodes;
using Xunit;

namespace Pal.Core.Tests;

public sealed class ModernBattleContentTests
{
    [Theory]
    [InlineData("role-level")]
    [InlineData("role-mp")]
    [InlineData("role-cover")]
    [InlineData("role-equipment")]
    [InlineData("role-magics")]
    [InlineData("role-statuses")]
    [InlineData("role-xp")]
    [InlineData("hidden-name")]
    [InlineData("hidden-null")]
    [InlineData("player-attack")]
    [InlineData("player-defense")]
    [InlineData("player-dexterity")]
    [InlineData("player-equipment")]
    [InlineData("player-statuses")]
    [InlineData("player-poisons")]
    [InlineData("player-dying")]
    [InlineData("player-friend")]
    [InlineData("player-cover")]
    [InlineData("threshold-shape")]
    [InlineData("threshold-word")]
    [InlineData("magic-level")]
    [InlineData("magic-id")]
    [InlineData("magic-null")]
    [InlineData("empty-teams")]
    [InlineData("team-id")]
    [InlineData("enemy-id")]
    [InlineData("enemy-level")]
    [InlineData("enemy-hp")]
    [InlineData("enemy-attack")]
    [InlineData("enemy-defense")]
    [InlineData("enemy-dexterity")]
    [InlineData("enemy-resistance")]
    [InlineData("enemy-statuses")]
    [InlineData("enemy-poisons")]
    [InlineData("enemy-start")]
    [InlineData("enemy-ready")]
    [InlineData("enemy-end")]
    [InlineData("enemy-magic")]
    [InlineData("enemy-dual")]
    [InlineData("enemy-item")]
    [InlineData("enemy-rate")]
    [InlineData("enemy-xp")]
    [InlineData("enemy-cash")]
    public void Typed_format_rejects_invalid_shapes_ranges_and_Ordinal_experience(string defect)
    {
        var data = ModernBattleContent.Parse(Data().ToJsonString());
        var role = data.InitialRole;
        var player = data.Player;
        var growth = data.Growth;
        var enemy = data.Teams[18];
        var hidden = role.HiddenExperience.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        switch (defect)
        {
            case "role-level": role = role with { Level = 65536 }; break;
            case "role-mp": role = role with { Mp = 51 }; break;
            case "role-cover": role = role with { CoveredByRoleId = 6 }; break;
            case "role-equipment": role = role with { Equipment = new int[5] }; break;
            case "role-magics": role = role with { Magics = new int[33] }; break;
            case "role-statuses": role = role with { Statuses = [-1,0,0,0,0,0,0,0,0] }; break;
            case "role-xp": role = role with { PrimaryExperience = new(0, 65536, 0) }; break;
            case "hidden-name": hidden["magicpower"] = hidden["magicPower"]; hidden.Remove("magicPower"); role = role with { HiddenExperience = hidden }; break;
            case "hidden-null": hidden["flee"] = null!; role = role with { HiddenExperience = hidden }; break;
            case "player-attack": player = player with { Attack = -1 }; break;
            case "player-defense": player = player with { Defense = 65536 }; break;
            case "player-dexterity": player = player with { Dexterity = -1 }; break;
            case "player-equipment": player = player with { Equipment = [] }; break;
            case "player-statuses": player = player with { Statuses = new int[10] }; break;
            case "player-poisons": player = player with { Poisons = [65536] }; break;
            case "player-dying": player = player with { DyingScript = -1 }; break;
            case "player-friend": player = player with { FriendDeathScript = 65536 }; break;
            case "player-cover": player = player with { CoveredByRoleId = -1 }; break;
            case "threshold-shape": growth = growth with { LevelUpExperience = new int[99] }; break;
            case "threshold-word": growth = growth with { LevelUpExperience = Enumerable.Repeat(-1, 100).ToArray() }; break;
            case "magic-level": growth = growth with { LevelUpMagics = [new(-1, 0)] }; break;
            case "magic-id": growth = growth with { LevelUpMagics = [new(0, 65536)] }; break;
            case "magic-null": growth = growth with { LevelUpMagics = [null!] }; break;
            case "enemy-id": enemy = enemy with { Id = 0 }; break;
            case "enemy-level": enemy = enemy with { Level = 65536 }; break;
            case "enemy-hp": enemy = enemy with { Hp = 0 }; break;
            case "enemy-attack": enemy = enemy with { Attack = -32769 }; break;
            case "enemy-defense": enemy = enemy with { Defense = 32768 }; break;
            case "enemy-dexterity": enemy = enemy with { Dexterity = -32769 }; break;
            case "enemy-resistance": enemy = enemy with { PhysicalResistance = -1 }; break;
            case "enemy-statuses": enemy = enemy with { Statuses = new int[8] }; break;
            case "enemy-poisons": enemy = enemy with { Poisons = [-1] }; break;
            case "enemy-start": enemy = enemy with { TurnStartScript = 65536 }; break;
            case "enemy-ready": enemy = enemy with { ReadyScript = -1 }; break;
            case "enemy-end": enemy = enemy with { BattleEndScript = 65536 }; break;
            case "enemy-magic": enemy = enemy with { MagicId = -1 }; break;
            case "enemy-dual": enemy = enemy with { DualMove = 65536 }; break;
            case "enemy-item": enemy = enemy with { AttackEquivItem = -1 }; break;
            case "enemy-rate": enemy = enemy with { AttackEquivItemRate = 65536 }; break;
            case "enemy-xp": enemy = enemy with { Experience = -1 }; break;
            case "enemy-cash": enemy = enemy with { Cash = 65536 }; break;
        }
        var teams = new Dictionary<int, ClassicBattleEnemy> { [defect == "team-id" ? 65536 : 18] = enemy };
        if (defect == "empty-teams") teams.Clear();
        var invalid = new TriggerBattleContent(role, player, growth, teams);
        Assert.Contains("battle-data.json.$", Assert.Throws<InvalidDataException>(() => ModernBattleContent.Validate(invalid)).Message);
    }

    [Fact]
    public void Typed_format_validation_accepts_parsed_data_and_rejects_invalid_WORD_without_capability_gate()
    {
        var content = ModernBattleContent.Parse(Data().ToJsonString());
        ModernBattleContent.Validate(content);
        var legal = new TriggerBattleContent(content.InitialRole, content.Player with { Attack = 65535, AttackAll = true },
            new(new int[100], [new(0, 0), new(0, 0)]), content.Teams);
        ModernBattleContent.Validate(legal);
        var invalid = new TriggerBattleContent(content.InitialRole, content.Player with { Attack = 65536 }, content.Growth, content.Teams);
        Assert.Contains("attack", Assert.Throws<InvalidDataException>(() => ModernBattleContent.Validate(invalid)).Message);
    }

    [Fact]
    public void Parsed_content_runs_real_Won_settlement_and_event_dialogue_to_end()
    {
        var content = ModernBattleContent.Parse(Data().ToJsonString());
        var random = new BattleRandom();
        var script = Script(content, random);
        script.Start("shared", "npc", 28);
        Assert.Equal(0, random.Draws);
        var request = script.PendingBattle!;
        Assert.False(script.AcknowledgeBattle(request.Id, null));
        request.Lifecycle.SelectAttack(7);
        Assert.True(request.Lifecycle.ResolveNextAction());
        Assert.Equal(ClassicBattlePhase.Won, request.Lifecycle.Completion!.Outcome);
        Assert.Equal(43, request.Lifecycle.Current.Combat.LastAction!.Damage);
        Assert.False(request.Lifecycle.Current.Combat.LastAction.Critical);
        Assert.Equal(100, script.BattleRole!.Hp);
        Assert.True(script.AcknowledgeBattle(request.Id, request.Lifecycle.Completion));
        Assert.Equal(125, script.BattleRole.Hp);
        Assert.Equal(35, script.BattleRole.Mp);
        Assert.Equal(new BattleExperienceSnapshot(5, 2, 17), script.BattleRole.PrimaryExperience);
        Assert.Equal(new BattleExperienceSnapshot(7, 3, 1), script.BattleRole.HiddenExperience["attack"]);
        Assert.Equal(new BattleExperienceSnapshot(7, 3, 2), script.BattleRole.HiddenExperience["health"]);
        FinishDisplays(script);
        Assert.Equal(TriggerScriptPhase.Completed, script.Phase);
        Assert.Equal(1, script.ReturnEntry);
        Assert.Equal(28, script.Caller);
        Assert.False(script.AcknowledgeBattle(request.Id, request.Lifecycle.Completion));
        Assert.Equal(6, random.Draws);
    }

    [Theory]
    [InlineData("$")]
    [InlineData("roles.0")]
    [InlineData("roles.0.primaryExperience")]
    [InlineData("roles.0.hiddenExperience")]
    [InlineData("roles.0.hiddenExperience.attack")]
    [InlineData("players.0")]
    [InlineData("growth.0")]
    [InlineData("growth.0.levelUpMagics.0")]
    [InlineData("teams.0")]
    [InlineData("teams.0.enemies.0")]
    public void Unknown_fields_are_rejected_at_every_object_boundary(string path)
    {
        var data = Data();
        At(data, path).AsObject()["typo"] = JsonNode.Parse("""{"value":0,"level":0,"count":0}""");
        var error = Assert.Throws<InvalidDataException>(() => ModernBattleContent.Parse(data.ToJsonString()));
        Assert.Contains("unknown field 'typo'", error.Message);
        Assert.Contains("battle-data.json.$", error.Message);
    }

    [Fact]
    public void Parsed_data_exposes_immutable_typed_inputs_for_native_preparation()
    {
        var content = ModernBattleContent.Parse(Data().ToJsonString());
        Assert.Equal(100, content.InitialRole.Hp);
        Assert.Equal(40, content.Player.Attack);
        Assert.Equal(100, content.Growth.LevelUpExperience[99]);
        Assert.Equal(7, content.Teams[18].Id);
        Assert.Throws<NotSupportedException>(() => ((IList<int>)content.InitialRole.Equipment)[0] = 3);
        Assert.Throws<NotSupportedException>(() => ((IList<int>)content.InitialRole.Magics)[0] = 3);
        Assert.Throws<NotSupportedException>(() => ((IList<int>)content.InitialRole.Statuses)[0] = 3);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, BattleExperienceSnapshot>)content.InitialRole.HiddenExperience)["attack"] = new(0, 0, 0));
        Assert.Throws<NotSupportedException>(() => ((IList<int>)content.Player.Equipment)[0] = 3);
        Assert.Throws<NotSupportedException>(() => ((IList<int>)content.Player.Statuses)[0] = 3);
        Assert.Throws<NotSupportedException>(() => ((IList<int>)content.Player.Poisons)[0] = 3);
        Assert.Throws<NotSupportedException>(() => ((IList<int>)content.Growth.LevelUpExperience)[0] = 3);
        Assert.Throws<NotSupportedException>(() => ((IList<BattleLevelUpMagic>)content.Growth.LevelUpMagics)[0] = new(0, 0));
        Assert.Throws<NotSupportedException>(() => ((IDictionary<int, ClassicBattleEnemy>)content.Teams).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<int>)content.Teams[18].Statuses)[0] = 3);
        Assert.Throws<NotSupportedException>(() => ((IList<int>)content.Teams[18].Poisons)[0] = 3);
        var changedCopy = content.Player with { Attack = 999 };
        Assert.Equal(999, changedCopy.Attack);
        Assert.Equal(40, content.Player.Attack);
    }

    [Fact]
    public void Parsed_content_runs_real_Lost_without_recovery_and_uses_loss_end()
    {
        var data = Data();
        data["roles"]![0]!["hp"] = 20;
        data["players"]![0]!["hp"] = 20;
        data["teams"]![0]!["enemies"]![0]!["hp"] = 100;
        var random = new BattleRandom();
        var script = Script(ModernBattleContent.Parse(data.ToJsonString()), random);
        script.Start("shared", "npc", 28);
        var request = script.PendingBattle!;
        request.Lifecycle.SelectAttack(7);
        Assert.True(request.Lifecycle.ResolveNextAction());
        Assert.Null(request.Lifecycle.Completion);
        Assert.True(request.Lifecycle.ResolveNextAction());
        Assert.Equal(ClassicBattlePhase.Lost, request.Lifecycle.Completion!.Outcome);
        Assert.Equal(20, request.Lifecycle.Current.Combat.LastAction!.Damage);
        Assert.True(script.AcknowledgeBattle(request.Id, request.Lifecycle.Completion));
        Assert.Equal(0, script.BattleRole!.Hp);
        Assert.Equal(20, script.BattleRole.Mp);
        Assert.Equal(new BattleExperienceSnapshot(5, 2, 17), script.BattleRole.PrimaryExperience);
        Assert.Equal(new BattleExperienceSnapshot(7, 3, 1), script.BattleRole.HiddenExperience["attack"]);
        Assert.Equal(new BattleExperienceSnapshot(7, 3, 2), script.BattleRole.HiddenExperience["health"]);
        Assert.Null(script.PendingDisplay);
        Assert.Equal(TriggerScriptPhase.Completed, script.Phase);
        Assert.Equal(new TriggerPosition("shared", "L0004", 4), script.CurrentPosition);
        Assert.Equal(1, script.ReturnEntry);
        Assert.Equal(28, script.Caller);
        Assert.Equal(11, random.Draws);
    }

    [Theory]
    [InlineData("roles.0.equipment.0")]
    [InlineData("roles.0.statuses.0")]
    [InlineData("roles.0.coveredByRoleId")]
    [InlineData("players.0.equipment.0")]
    [InlineData("players.0.statuses.0")]
    [InlineData("players.0.poisons.0")]
    [InlineData("players.0.dyingScript")]
    [InlineData("players.0.friendDeathScript")]
    [InlineData("players.0.coveredByRoleId")]
    [InlineData("teams.0.enemies.0.statuses.0")]
    [InlineData("teams.0.enemies.0.poisons.0")]
    [InlineData("teams.0.enemies.0.turnStartScript")]
    [InlineData("teams.0.enemies.0.readyScript")]
    [InlineData("teams.0.enemies.0.battleEndScript")]
    [InlineData("teams.0.enemies.0.magicId")]
    [InlineData("teams.0.enemies.0.dualMove")]
    [InlineData("teams.0.enemies.0.attackEquivItem")]
    [InlineData("teams.0.enemies.0.attackEquivItemRate")]
    [InlineData("teams.0.enemies.0.experience")]
    [InlineData("teams.0.enemies.0.cash")]
    public void Legal_nonzero_effects_are_preserved_and_selected_entry_rejects_before_RNG(string path)
    {
        var data = Data();
        Set(data, path, JsonValue.Create(1));
        var content = ModernBattleContent.Parse(data.ToJsonString());
        var enemy = content.Teams[18];
        int preserved = path switch
        {
            "roles.0.equipment.0" => content.InitialRole.Equipment[0],
            "roles.0.statuses.0" => content.InitialRole.Statuses[0],
            "roles.0.coveredByRoleId" => content.InitialRole.CoveredByRoleId,
            "players.0.equipment.0" => content.Player.Equipment[0],
            "players.0.statuses.0" => content.Player.Statuses[0],
            "players.0.poisons.0" => content.Player.Poisons[0],
            "players.0.dyingScript" => content.Player.DyingScript,
            "players.0.friendDeathScript" => content.Player.FriendDeathScript,
            "players.0.coveredByRoleId" => content.Player.CoveredByRoleId!.Value,
            "teams.0.enemies.0.statuses.0" => enemy.Statuses[0],
            "teams.0.enemies.0.poisons.0" => enemy.Poisons[0],
            "teams.0.enemies.0.turnStartScript" => enemy.TurnStartScript,
            "teams.0.enemies.0.readyScript" => enemy.ReadyScript,
            "teams.0.enemies.0.battleEndScript" => enemy.BattleEndScript,
            "teams.0.enemies.0.magicId" => enemy.MagicId,
            "teams.0.enemies.0.dualMove" => enemy.DualMove,
            "teams.0.enemies.0.attackEquivItem" => enemy.AttackEquivItem,
            "teams.0.enemies.0.attackEquivItemRate" => enemy.AttackEquivItemRate,
            "teams.0.enemies.0.experience" => enemy.Experience,
            "teams.0.enemies.0.cash" => enemy.Cash,
            _ => throw new ArgumentOutOfRangeException(nameof(path))
        };
        Assert.Equal(1, preserved);
        AssertUnsupported(content);
    }

    [Fact]
    public void True_attack_all_is_preserved_but_not_executable()
    {
        var data = Data();
        data["players"]![0]!["attackAll"] = true;
        var content = ModernBattleContent.Parse(data.ToJsonString());
        Assert.True(content.Player.AttackAll);
        AssertUnsupported(content);
    }

    [Theory]
    [InlineData("growth.0.levelUpExperience.2", 0)]
    [InlineData("roles.0.primaryExperience.value", 100)]
    [InlineData("roles.0.hiddenExperience.attack.value", 100)]
    [InlineData("roles.0.level", 100)]
    [InlineData("growth.0.levelUpMagics.1.level", 2)]
    public void Structurally_legal_growth_is_not_mistaken_for_zero_growth_capability(string path, int value)
    {
        var data = Data();
        Set(data, path, JsonValue.Create(value));
        var content = ModernBattleContent.Parse(data.ToJsonString());
        if (value == 0) Assert.Equal(0, content.Growth.LevelUpExperience[2]);
        AssertUnsupported(content);
    }

    [Fact]
    public void Ordered_duplicate_zero_and_known_magic_rows_survive_and_remain_executable()
    {
        var data = Data();
        data["growth"]![0]!["levelUpMagics"] = JsonNode.Parse("""
            [{"level":2,"magicId":9},{"level":2,"magicId":0},{"level":2,"magicId":9},{"level":2,"magicId":0},{"level":3,"magicId":10}]
            """);
        var content = ModernBattleContent.Parse(data.ToJsonString());
        Assert.Equal(new BattleLevelUpMagic[] { new(2, 9), new(2, 0), new(2, 9), new(2, 0), new(3, 10) }, content.Growth.LevelUpMagics);
        var script = Script(content, new BattleRandom());
        script.Start("shared", "npc", 28);
        Assert.NotNull(script.PendingBattle);
    }

    [Fact]
    public void Signed_offsets_WORD_edges_and_distinct_team_identity_are_preserved()
    {
        var data = Data();
        var enemy = data["teams"]![0]!["enemies"]![0]!;
        enemy["attack"] = -32768;
        enemy["defense"] = 32767;
        enemy["dexterity"] = -123;
        enemy["physicalResistance"] = 65535;
        enemy["level"] = 65535;
        var secondTeam = data["teams"]![0]!.DeepClone();
        secondTeam["teamId"] = 65535;
        secondTeam["enemies"]![0]!["id"] = 65535;
        data["teams"]!.AsArray().Add(secondTeam);
        var content = ModernBattleContent.Parse(data.ToJsonString());
        Assert.Equal(2, content.Teams.Count);
        Assert.Equal(7, content.Teams[18].Id);
        Assert.Equal(65535, content.Teams[65535].Id);
        Assert.Equal(-32768, content.Teams[18].Attack);
        Assert.Equal(32767, content.Teams[18].Defense);
        Assert.Equal(-123, content.Teams[18].Dexterity);
        Assert.Equal(65535, content.Teams[18].PhysicalResistance);
        Assert.Equal(65535, content.Teams[18].Level);
        Assert.Equal(1, content.Player.RoleId);
        Assert.Equal(100, content.Player.Hp);
        Assert.Equal(150, content.Player.MaxHp);
        Assert.Equal(40, content.Player.Attack);
        Assert.Equal(20, content.Player.Defense);
        Assert.Equal(100, content.Player.Dexterity);
        Assert.Equal(2, content.InitialRole.Level);
        Assert.Equal(50, content.InitialRole.MaxMp);
        Assert.All(content.InitialRole.HiddenExperience.Values, xp => Assert.Equal(new BattleExperienceSnapshot(7, 3, 55), xp));
    }

    [Theory]
    [InlineData("formatVersion", "2")]
    [InlineData("formatVersion", "1.0")]
    [InlineData("roles", "null")]
    [InlineData("roles.0.roleId", "6")]
    [InlineData("roles.0.roleId", "-1")]
    [InlineData("roles.0.level", "65536")]
    [InlineData("roles.0.hp", "151")]
    [InlineData("roles.0.maxHp", "0")]
    [InlineData("roles.0.mp", "51")]
    [InlineData("roles.0.primaryExperience.value", "-1")]
    [InlineData("roles.0.primaryExperience.level", "65536")]
    [InlineData("roles.0.primaryExperience.count", "\"17\"")]
    [InlineData("roles.0.hiddenExperience.attack", "null")]
    [InlineData("roles.0.hiddenExperience.magicPower.value", "1.5")]
    [InlineData("roles.0.magics.0", "65536")]
    [InlineData("roles.0.coveredByRoleId", "null")]
    [InlineData("players.0.roleId", "0")]
    [InlineData("players.0.hp", "99")]
    [InlineData("players.0.maxHp", "151")]
    [InlineData("players.0.attack", "true")]
    [InlineData("players.0.defense", "-1")]
    [InlineData("players.0.dexterity", "65536")]
    [InlineData("players.0.dyingScript", "65536")]
    [InlineData("players.0.coveredByRoleId", "6")]
    [InlineData("players.0.attackAll", "0")]
    [InlineData("players.0.poisons", "{}")]
    [InlineData("growth.0.roleId", "2")]
    [InlineData("growth.0.levelUpExperience.99", "65536")]
    [InlineData("growth.0.levelUpMagics.0.level", "-1")]
    [InlineData("growth.0.levelUpMagics.0.magicId", "null")]
    [InlineData("teams", "[]")]
    [InlineData("teams.0.teamId", "-1")]
    [InlineData("teams.0.enemies.0.id", "0")]
    [InlineData("teams.0.enemies.0.id", "65536")]
    [InlineData("teams.0.enemies.0.level", "-1")]
    [InlineData("teams.0.enemies.0.hp", "0")]
    [InlineData("teams.0.enemies.0.attack", "-32769")]
    [InlineData("teams.0.enemies.0.defense", "32768")]
    [InlineData("teams.0.enemies.0.dexterity", "\"0\"")]
    [InlineData("teams.0.enemies.0.physicalResistance", "-1")]
    [InlineData("teams.0.enemies.0.experience", "65536")]
    public void Invalid_types_ranges_and_relationships_are_rejected_with_field_location(string path, string json)
    {
        var data = Data();
        if (!path.Contains('.')) data[path] = JsonNode.Parse(json);
        else Set(data, path, JsonNode.Parse(json));
        var error = Assert.Throws<InvalidDataException>(() => ModernBattleContent.Parse(data.ToJsonString()));
        Assert.Contains("battle-data.json.$", error.Message);
        // Cross-object consistency errors identify all three related inputs rather than guessing which was intended.
        Assert.Contains(path.StartsWith("players.0.") && path is "players.0.hp" or "players.0.roleId" or "players.0.maxHp"
            ? "players[0]" : path == "growth.0.roleId" ? "growth[0]" : path.Split('.').Last(), error.Message);
    }

    [Theory]
    [InlineData("roles.0.equipment")]
    [InlineData("roles.0.magics")]
    [InlineData("roles.0.statuses")]
    [InlineData("players.0.equipment")]
    [InlineData("players.0.statuses")]
    [InlineData("growth.0.levelUpExperience")]
    [InlineData("teams.0.enemies.0.statuses")]
    public void Fixed_width_arrays_reject_both_missing_and_extra_slots(string path)
    {
        foreach (bool extra in new[] { false, true })
        {
            var data = Data();
            var list = At(data, path).AsArray();
            if (extra) list.Add(0); else list.RemoveAt(list.Count - 1);
            var error = Assert.Throws<InvalidDataException>(() => ModernBattleContent.Parse(data.ToJsonString()));
            Assert.Contains(path.Split('.').Last(), error.Message);
        }
    }

    [Theory]
    [InlineData("roles")]
    [InlineData("players")]
    [InlineData("growth")]
    [InlineData("teams.0.enemies")]
    public void Single_actor_binding_rejects_zero_and_multiple_items_including_duplicate_IDs(string path)
    {
        foreach (bool duplicate in new[] { false, true })
        {
            var data = Data();
            var list = At(data, path).AsArray();
            if (duplicate) list.Add(list[0]!.DeepClone()); else list.Clear();
            var error = Assert.Throws<InvalidDataException>(() => ModernBattleContent.Parse(data.ToJsonString()));
            Assert.Contains("exactly one item", error.Message);
        }
    }

    [Fact]
    public void Duplicate_team_ID_is_rejected_but_target_ID_can_be_reused_in_another_team()
    {
        var data = Data();
        data["teams"]!.AsArray().Add(data["teams"]![0]!.DeepClone());
        var error = Assert.Throws<InvalidDataException>(() => ModernBattleContent.Parse(data.ToJsonString()));
        Assert.Contains("teams[1].teamId: duplicate team ID 18", error.Message);
        data["teams"]![1]!["teamId"] = 19;
        data["teams"]![1]!["enemies"]![0]!["cash"] = 5;
        var content = ModernBattleContent.Parse(data.ToJsonString());
        Assert.Equal(7, content.Teams[19].Id);
        Assert.Equal(5, content.Teams[19].Cash);
        var random = new BattleRandom();
        var script = Script(content, random);
        script.Start("shared", "npc", 28);
        Assert.NotNull(script.PendingBattle);
        Assert.Equal(0, random.Draws);
    }

    public static IEnumerable<object[]> RequiredFields()
    {
        string[] paths = ["formatVersion", "roles", "players", "growth", "teams"];
        foreach (string path in paths) yield return [path];
        foreach (string field in new[] { "roleId", "level", "hp", "maxHp", "mp", "maxMp", "primaryExperience", "hiddenExperience", "equipment", "magics", "statuses", "coveredByRoleId" })
            yield return ["roles.0." + field];
        foreach (string field in new[] { "health", "magic", "attack", "magicPower", "defense", "dexterity", "flee" })
            yield return ["roles.0.hiddenExperience." + field];
        foreach (string field in new[] { "value", "level", "count" })
        {
            yield return ["roles.0.primaryExperience." + field];
            yield return ["roles.0.hiddenExperience.attack." + field];
        }
        foreach (string field in new[] { "roleId", "hp", "maxHp", "attack", "defense", "dexterity", "equipment", "statuses", "poisons", "dyingScript", "friendDeathScript", "coveredByRoleId", "attackAll" })
            yield return ["players.0." + field];
        foreach (string field in new[] { "roleId", "levelUpExperience", "levelUpMagics" }) yield return ["growth.0." + field];
        foreach (string field in new[] { "level", "magicId" }) yield return ["growth.0.levelUpMagics.0." + field];
        foreach (string field in new[] { "teamId", "enemies" }) yield return ["teams.0." + field];
        foreach (string field in new[] { "id", "level", "hp", "attack", "defense", "dexterity", "physicalResistance", "statuses", "poisons", "turnStartScript", "readyScript", "battleEndScript", "magicId", "dualMove", "attackEquivItem", "attackEquivItemRate", "experience", "cash" })
            yield return ["teams.0.enemies.0." + field];
    }

    [Theory]
    [MemberData(nameof(RequiredFields))]
    public void Every_numeric_or_capability_field_is_required_even_when_zero_or_null(string path)
    {
        var data = Data();
        int dot = path.LastIndexOf('.');
        var parent = dot < 0 ? data : At(data, path[..dot]).AsObject();
        parent.Remove(path[(dot + 1)..]);
        var error = Assert.Throws<InvalidDataException>(() => ModernBattleContent.Parse(data.ToJsonString()));
        Assert.Contains("missing field '" + path[(dot + 1)..] + "'", error.Message);
    }

    [Theory]
    [InlineData("formatVersion", "1")]
    [InlineData("dyingScript", "0")]
    [InlineData("readyScript", "0")]
    [InlineData("value", "7")]
    public void Duplicate_JSON_keys_are_rejected_recursively_including_same_values(string key, string value)
    {
        string json = Data().ToJsonString();
        string property = $"\"{key}\":{value}";
        json = json.Replace(property, property + "," + property, StringComparison.Ordinal);
        var error = Assert.Throws<InvalidDataException>(() => ModernBattleContent.Parse(json));
        Assert.Contains("duplicate JSON key '" + key + "'", error.Message);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"formatVersion\":NaN}")]
    [InlineData("{\"formatVersion\":1,}")]
    [InlineData("{/*comment*/\"formatVersion\":1}")]
    public void Invalid_JSON_or_root_kind_is_rejected(string json) =>
        Assert.Throws<InvalidDataException>(() => ModernBattleContent.Parse(json));

    [Fact]
    public void Underscore_annotations_are_ignored_but_do_not_hide_duplicate_keys_or_case_aliases()
    {
        var data = Data();
        data["_comment"] = "numeric data is not provenance";
        data["roles"]![0]!["hiddenExperience"]!["_note"] = false;
        var content = ModernBattleContent.Parse(data.ToJsonString());
        Assert.Equal(7, content.InitialRole.HiddenExperience.Count);
        string json = data.ToJsonString().Replace("\"_note\":false", "\"_note\":false,\"_note\":false", StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => ModernBattleContent.Parse(json));
        var hidden = data["roles"]![0]!["hiddenExperience"]!.AsObject();
        hidden.Remove("magicPower");
        hidden["magicpower"] = JsonNode.Parse("""{"value":7,"level":3,"count":55}""");
        Assert.Throws<InvalidDataException>(() => ModernBattleContent.Parse(data.ToJsonString()));
    }

    private static void AssertUnsupported(TriggerBattleContent content)
    {
        var random = new BattleRandom();
        var script = Script(content, random);
        var before = (script.Phase, script.Caller, script.CurrentPosition, script.BattleRole);
        Assert.Throws<InvalidDataException>(() => script.Start("shared", "npc", 28));

        Assert.Equal(0, random.Draws);
        Assert.Equal(before, (script.Phase, script.Caller, script.CurrentPosition, script.BattleRole));
        Assert.Null(script.PendingBattle);
        Assert.Null(script.PendingDisplay);
    }

    private static void Set(JsonNode root, string path, JsonNode? value)
    {
        int dot = path.LastIndexOf('.');
        var parent = At(root, path[..dot]);
        string key = path[(dot + 1)..];
        if (int.TryParse(key, out int index)) parent[index] = value;
        else parent[key] = value;
    }

    private static JsonNode At(JsonNode root, string path)
    {
        if (path == "$") return root;
        foreach (string part in path.Split('.')) root = int.TryParse(part, out int index) ? root[index]! : root[part]!;
        return root;
    }

    private static JsonObject Data()
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

    private static TriggerScriptSession Script(TriggerBattleContent content, BattleRandom random)
    {
        var events = new EventFixture();
        events.Commands[1] = JsonNode.Parse("""
          {"label":"L0001","op":"startBattle","teamId":18,"lossTarget":"L0004","isBoss":true,"fleeTarget":null,
           "next":"L0002","source":{"index":1,"opcode":7,"operands":[18,4,0]}}
          """);
        return new(events.Load(), TriggerScriptSessionTests.Language(), content, random);
    }

    private static void FinishDisplays(TriggerScriptSession script)
    {
        Assert.Equal(TriggerScriptPhase.WaitingForDisplay, script.Phase);
        var display = Assert.IsType<TriggerDialogueRequest>(script.PendingDisplay);
        var reference = Assert.Single(display.MessageReferences);
        Assert.Equal(61, reference.StartMessageId);
        Assert.Equal(1, reference.Span);
        Assert.Equal(new TriggerPosition("shared", "L0002", 2), reference.Position);
        display.Display.Update(24, false, true);
        display.Display.Update(100, true, false);
        Assert.True(script.AcknowledgeDisplay(display.Id));
        Assert.Null(script.PendingDisplay);
        Assert.Equal(TriggerScriptPhase.Completed, script.Phase);
        Assert.Equal(new TriggerPosition("shared", "L0004", 4), script.CurrentPosition);
    }

    private sealed class BattleRandom : IBattleRandom
    {
        public int Draws { get; private set; }
        public int NextInclusive(int from, int to) { Draws++; return from == 0 && to == 5 ? 1 : from; }
        public float NextInclusive(float from, float to) { Draws++; return 1; }
    }
}
