using System.Text.Json.Nodes;
using Xunit;

namespace Pal.Core.Tests;

public sealed class BattleEquipmentStateTests
{
    // Hand-computed from the reference formulas (global.c PAL_GetPlayer* sums over seven WORD effect registers,
    // WORD wrap; PAL_GetPlayerBattleSprite last non-zero wins; PAL_SetPlayerStatus good-status max):
    //   role1 attack bonus  = 197(3) + 210(4) + 170(0) + 237(0) + 248(0) = 7  -> 35 + 7 = 42
    //   role1 defense bonus = 197(1) + 237(6) = 7                            -> 20 + 7 = 27
    //   role1 dexterity bonus = 248(8) = 8                                   -> 100 + 8 = 108
    //   role1 battle sprite: 210 sets 400 (slot 2), 248 sets 450 (slot 5)    -> last non-zero 450
    //   role0 attack bonus = 196(5) + 166(10) = 15                           -> 40 + 15 = 55
    //   role0 battle sprite: 208 sets 500 (slot 2), 249 sets 600 (slot 5)    -> last non-zero 600
    //   role1 persistent dualAttack 60000 (>999) overlays zeros and a timed 300 without stacking.
    [Fact]
    public void SixRoleClosureInitializesSlotRegistersAndProjectsEffectivePanels()
    {
        var content = ModernBattleContent.Parse(EquipmentData().ToJsonString());
        Assert.Equal([197, 0, 210, 170, 237, 248], content.InitialRole.Equipment);
        Assert.Equal(6, content.RoleEquipment.Count);
        Assert.Equal([196, 225, 208, 166, 235, 249], content.RoleEquipment[0]);
        Assert.Equal([197, 0, 210, 170, 237, 248], content.RoleEquipment[1]);
        Assert.Contains(content.EquipmentObjects, pair => pair.Key == 170 && pair.Value.Effects[1].Statuses[8] == 60000);
        var state = BattleEquipmentState.Initialize(content);
        Assert.Equal(7, state.AttackBonus(1));
        Assert.Equal(7, state.DefenseBonus(1));
        Assert.Equal(8, state.DexterityBonus(1));
        Assert.Equal(42, state.EffectiveAttack(1, 35));
        Assert.Equal(27, state.EffectiveDefense(1, 20));
        Assert.Equal(108, state.EffectiveDexterity(1, 100));
        Assert.Equal(42, state.EffectivePlayer(content.Player).Attack);
        Assert.Equal(27, state.EffectivePlayer(content.Player).Defense);
        Assert.Equal(108, state.EffectivePlayer(content.Player).Dexterity);
        // All six roles initialize, not only the party roles: role0 is equipped although it is not in this party.
        Assert.Equal(15, state.AttackBonus(0));
        Assert.Equal(55, state.EffectiveAttack(0, 40));
        Assert.Equal(600, state.BattleSprite(0, 3));
        Assert.Equal(450, state.BattleSprite(1, 3));
        Assert.Equal(0, state.AttackBonus(2));
        Assert.Equal(0, state.AttackBonus(5));
        Assert.Equal(new int[9], state.StatusOverlay(0));
        Assert.Equal([0, 0, 0, 0, 0, 0, 0, 0, 60000], state.StatusOverlay(1));
        Assert.Equal([0, 0, 0, 0, 0, 0, 0, 0, 60000], state.ApplyStatuses(1, new int[9]));
        Assert.Equal([0, 0, 0, 0, 0, 0, 0, 0, 60000], state.ApplyStatuses(1, state.StatusOverlay(1)));
        var timed = new int[9]; timed[8] = 300;
        Assert.Equal([0, 0, 0, 0, 0, 0, 0, 0, 60000], state.ApplyStatuses(1, timed));
        var higher = new int[9]; higher[8] = 60001;
        Assert.Equal([0, 0, 0, 0, 0, 0, 0, 0, 60001], state.ApplyStatuses(1, higher));
    }

    [Fact]
    public void Retail_stat18_21_22_equipment_writes_project_into_the_same_effective_panel_rule()
    {
        // Independent hand-computed expectation from the retail tables: role1 base panels
        // (Data.Mkf chunk3 fields 17..22) and the #45-audited equip scripts; item248 slot5 writes
        // stat18=8, stat21=9, stat22=20 for role1 only (global.c 1768/1868/1900 sum rgEquipmentEffect).
        JsonNode Row(int attack, int defense, int dexterity, int magic = 0, int flee = 0, int poison = 0) => new JsonObject
        {
            ["attack"] = attack, ["defense"] = defense, ["dexterity"] = dexterity, ["battleSprite"] = 0,
            ["magicPower"] = magic, ["fleeRate"] = flee, ["poisonResistance"] = poison,
            ["statuses"] = new JsonArray(0, 0, 0, 0, 0, 0, 0, 0, 0)
        };
        JsonNode Object(int id, JsonNode role0, JsonNode role1) => new JsonObject
        { ["objectId"] = id, ["effects"] = new JsonArray(role0.DeepClone(), role1.DeepClone(), Row(0, 0, 0), Row(0, 0, 0), Row(0, 0, 0), Row(0, 0, 0)) };
        var objects = new JsonArray(
            Object(196, Row(0, 1, 0), Row(0, 2, 0)), Object(225, Row(0, 2, 0), Row(0, 0, 0)),
            Object(208, Row(0, 3, 0), Row(0, 0, 0)), Object(166, Row(2, 0, 3), Row(0, 0, 0)),
            Object(235, Row(0, 1, 0), Row(0, 0, 0)), Object(249, Row(0, 2, 0), Row(0, 0, 0)),
            Object(197, Row(0, 0, 0), Row(0, 2, 0)), Object(210, Row(0, 0, 0), Row(0, 0, 4)),
            Object(170, Row(0, 0, 0), Row(8, 5, 0)), Object(237, Row(0, 0, 0), Row(0, 3, 2)),
            Object(248, Row(0, 0, 0), Row(0, 0, 0, 8, 9, 20)));
        string grid = "[[196,225,208,166,235,249],[197,0,210,170,237,248],[0,0,0,0,0,0],[0,0,0,0,0,0],[0,0,0,0,0,0],[0,0,0,0,0,0]]";
        var data = JsonNode.Parse(EquipmentData().ToJsonString())!;
        data["equipments"] = objects;
        data["roleEquipment"] = JsonNode.Parse(grid);
        var content = ModernBattleContent.Parse(data.ToJsonString());

        var state = BattleEquipmentState.Initialize(content);
        Assert.Equal((2, 9, 3), (state.AttackBonus(0), state.DefenseBonus(0), state.DexterityBonus(0)));
        Assert.Equal((8, 10, 6), (state.AttackBonus(1), state.DefenseBonus(1), state.DexterityBonus(1)));
        Assert.Equal((8, 9, 20), (state.MagicPowerBonus(1), state.FleeRateBonus(1), state.PoisonResistanceBonus(1)));
        Assert.Equal((0, 0, 0), (state.MagicPowerBonus(0), state.FleeRateBonus(0), state.PoisonResistanceBonus(0)));
        Assert.Equal(72, state.EffectiveMagicPower(1, 64));   // retail base 64 + 8
        Assert.Equal(27, state.EffectiveFleeRate(1, 18));     // retail base 18 + 9
        Assert.Equal(20, state.EffectivePoisonResistance(1, 0));
        Assert.Equal(0xFFFF, state.EffectiveMagicPower(1, 0xFFFF - 8 + 65536)); // WORD wraparound, same rule as attack
    }

    [Fact]
    public void EffectivePanelsWrapAsWordsAndRecomputationIsIdempotent()
    {
        var content = ModernBattleContent.Parse(EquipmentData().ToJsonString());
        var first = BattleEquipmentState.Initialize(content);
        var second = BattleEquipmentState.Initialize(content);
        // 65530 + 7 wraps to 1 (WORD arithmetic, global.c WORD w += ...).
        Assert.Equal(1, first.EffectiveAttack(1, 65530));
        Assert.Equal(42, second.EffectiveAttack(1, 35));
        Assert.Equal(first.AttackBonus(1), second.AttackBonus(1));
        Assert.Equal(first.BattleSprite(1, 3), second.BattleSprite(1, 3));
        Assert.Equal(first.StatusOverlay(1), second.StatusOverlay(1));
        Assert.Equal(first.ApplyStatuses(1, new int[9]), second.ApplyStatuses(1, first.ApplyStatuses(1, new int[9])));
    }

    [Fact]
    public void UndeclaredEquipmentContentStartsAtZeroStateAndLegacyContentStaysLegal()
    {
        var content = ModernBattleContent.Parse(EquipmentData().ToJsonString());
        Assert.Equal(new int[9], BattleEquipmentState.Initialize(content).StatusOverlay(0));
        var legacy = ModernBattleContent.Parse(Data().ToJsonString());
        Assert.Empty(legacy.RoleEquipment);
        Assert.Empty(legacy.EquipmentObjects);
        var zero = BattleEquipmentState.Initialize(legacy);
        Assert.Equal(0, zero.AttackBonus(1));
        Assert.Equal(40, zero.EffectivePlayer(legacy.Player).Attack);
        Assert.Equal(100, zero.EffectiveDexterity(1, 100));
        Assert.Equal(7, zero.BattleSprite(1, 7));
    }

    [Fact]
    public void NonZeroEquipmentWithoutClosureIsRejectedBeforeInitializationButStaysFormatLegal()
    {
        var data = Data();
        Set(data, "roles.0.equipment.3", 170);
        Set(data, "players.0.equipment.3", 170);
        var content = ModernBattleContent.Parse(data.ToJsonString());
        Assert.Equal(170, content.InitialRole.Equipment[3]);
        var error = Assert.Throws<InvalidDataException>(() => BattleEquipmentState.Initialize(content));
        Assert.Contains("six-role equipment closure", error.Message);
        // The format layer stays permissive and the selected entry still refuses non-zero equipment before any RNG.
        var random = new CountingRandom();
        var script = Script(content, random);
        Assert.Throws<InvalidDataException>(() => script.Start("shared", "npc", 28));
        Assert.Equal(0, random.Draws);
        Assert.Null(script.PendingBattle);
    }

    [Theory]
    [InlineData("grid-rows", "expected exactly 6 rows")]
    [InlineData("grid-slots", "expected exactly 6 items")]
    [InlineData("grid-nonword", "expected integer in 0..65535")]
    [InlineData("grid-role-mismatch", "must equal the role's equipment snapshot")]
    [InlineData("duplicate-object", "duplicate equipment object ID 197")]
    [InlineData("missing-object", "equipment object 999 is not declared")]
    [InlineData("effects-count", "expected exactly 6 items")]
    [InlineData("effect-field", "unknown field")]
    [InlineData("status-bad-slot", "only bravery, protect, haste and dualAttack")]
    [InlineData("status-rounds", "persistent equipment status must be 1000..65535")]
    [InlineData("optional-stat-type", "magicPower")]
    public void StrictEquipmentSectionDefectsAreRejectedWithPreciseDiagnostics(string defect, string diagnostic)
    {
        var data = EquipmentData();
        switch (defect)
        {
            case "grid-rows":
                data["roleEquipment"]!.AsArray().RemoveAt(5);
                break;
            case "grid-slots":
                data["roleEquipment"]![1]!.AsArray().RemoveAt(5);
                break;
            case "grid-nonword":
                data["roleEquipment"]![1]![3] = 65536;
                break;
            case "grid-role-mismatch":
                data["roleEquipment"]![1]![5] = 0;
                break;
            case "duplicate-object":
                data["equipments"]!.AsArray().Add(data["equipments"]![0]!.DeepClone());
                break;
            case "missing-object":
                data["roleEquipment"]![1]![3] = 999;
                data["roles"]![0]!["equipment"]![3] = 999;
                data["players"]![0]!["equipment"]![3] = 999;
                break;
            case "effects-count":
                data["equipments"]![0]!["effects"]!.AsArray().RemoveAt(5);
                break;
            case "effect-field":
                data["equipments"]![0]!["effects"]![0]!["power"] = 1;
                break;
            case "status-bad-slot":
                data["equipments"]![0]!["effects"]![1]!["statuses"]![0] = 60000;
                break;
            case "status-rounds":
                data["equipments"]![0]!["effects"]![1]!["statuses"]![8] = 500;
                break;
            case "optional-stat-type":
                data["equipments"]![0]!["effects"]![1]!["magicPower"] = "8";
                break;
        }
        var error = Assert.Throws<InvalidDataException>(() => ModernBattleContent.Parse(data.ToJsonString()));
        Assert.Contains(diagnostic, error.Message);
    }

    [Fact]
    public void EquipmentsSectionWithoutGridAndZeroGridWithoutObjectsStayLegal()
    {
        var declaredOnly = EquipmentData();
        declaredOnly["roleEquipment"] = new JsonArray(
            Enumerable.Range(0, 6).Select(_ => (JsonNode?)new JsonArray(0, 0, 0, 0, 0, 0)).ToArray());
        declaredOnly["roles"]![0]!["equipment"] = new JsonArray(0, 0, 0, 0, 0, 0);
        declaredOnly["players"]![0]!["equipment"] = new JsonArray(0, 0, 0, 0, 0, 0);
        var content = ModernBattleContent.Parse(declaredOnly.ToJsonString());
        var state = BattleEquipmentState.Initialize(content);
        Assert.Equal(0, state.AttackBonus(1));
        Assert.Equal([0, 0, 0, 0, 0, 0], state.Equipment(1));
        var unusedObject = EquipmentData();
        unusedObject["roleEquipment"]!.AsArray().RemoveAt(5);
        Assert.Throws<InvalidDataException>(() => ModernBattleContent.Parse(unusedObject.ToJsonString()));
    }

    [Fact]
    public void NewEquipmentWordsAcceptIndependentAuthorSource()
    {
        // Independent minimal author document, not a serialized production model or a full retail closure.
        // #166 source audit: item163 @9AF7 writes field4=1, @9AF8 writes field1=6;
        // item260 @98F6 writes field65=351. Roles 2/3 remain outside the role0 party.
        var data = JsonNode.Parse("""
        {"formatVersion":1,"roles":[{
          "roleId":0,"level":1,"hp":100,"maxHp":100,"mp":0,"maxMp":0,
          "primaryExperience":{"value":0,"level":1,"count":0},
          "hiddenExperience":{
            "health":{"value":0,"level":1,"count":0},"magic":{"value":0,"level":1,"count":0},
            "attack":{"value":0,"level":1,"count":0},"magicPower":{"value":0,"level":1,"count":0},
            "defense":{"value":0,"level":1,"count":0},"dexterity":{"value":0,"level":1,"count":0},
            "flee":{"value":0,"level":1,"count":0}},
          "equipment":[0,0,0,0,0,0],"magics":[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0],
          "statuses":[0,0,0,0,0,0,0,0,0],"coveredByRoleId":0}],
         "players":[{"roleId":0,"hp":100,"maxHp":100,"attack":40,"defense":20,"dexterity":100,
          "equipment":[0,0,0,0,0,0],"statuses":[0,0,0,0,0,0,0,0,0],"poisons":[],
          "dyingScript":0,"friendDeathScript":0,"coveredByRoleId":null,"attackAll":false}],
         "growth":[{"roleId":0,"levelUpExperience":[
          0,0,0,0,0,0,0,0,0,0, 0,0,0,0,0,0,0,0,0,0,
          0,0,0,0,0,0,0,0,0,0, 0,0,0,0,0,0,0,0,0,0,
          0,0,0,0,0,0,0,0,0,0, 0,0,0,0,0,0,0,0,0,0,
          0,0,0,0,0,0,0,0,0,0, 0,0,0,0,0,0,0,0,0,0,
          0,0,0,0,0,0,0,0,0,0, 0,0,0,0,0,0,0,0,0,0],"levelUpMagics":[]}],
         "teams":[{"teamId":18,"enemies":[{"id":7,"level":0,"hp":43,"attack":0,"defense":0,"dexterity":0,"physicalResistance":1,
          "statuses":[0,0,0,0,0,0,0,0,0],"poisons":[],"turnStartScript":0,"readyScript":0,"battleEndScript":0,
          "magicId":0,"dualMove":0,"attackEquivItem":0,"attackEquivItemRate":0,"experience":0,"cash":0}]}],
         "roleEquipment":[[0,0,0,0,0,0],[0,0,0,0,0,0],[0,0,0,163,0,0],[0,0,0,0,0,260],[0,0,0,0,0,0],[0,0,0,0,0,0]],
         "equipments":[{"objectId":163,"effects":[
          {"attack":0,"defense":0,"dexterity":0,"battleSprite":0,"statuses":[0,0,0,0,0,0,0,0,0]},
          {"attack":0,"defense":0,"dexterity":0,"battleSprite":0,"statuses":[0,0,0,0,0,0,0,0,0]},
          {"attack":0,"defense":0,"dexterity":0,"battleSprite":6,"statuses":[0,0,0,0,0,0,0,0,0]},
          {"attack":0,"defense":0,"dexterity":0,"battleSprite":0,"statuses":[0,0,0,0,0,0,0,0,0]},
          {"attack":0,"defense":0,"dexterity":0,"battleSprite":0,"statuses":[0,0,0,0,0,0,0,0,0]},
          {"attack":0,"defense":0,"dexterity":0,"battleSprite":0,"statuses":[0,0,0,0,0,0,0,0,0]}]},
          {"objectId":260,"effects":[
          {"attack":0,"defense":0,"dexterity":0,"battleSprite":0,"statuses":[0,0,0,0,0,0,0,0,0]},
          {"attack":0,"defense":0,"dexterity":0,"battleSprite":0,"statuses":[0,0,0,0,0,0,0,0,0]},
          {"attack":0,"defense":0,"dexterity":0,"battleSprite":0,"statuses":[0,0,0,0,0,0,0,0,0]},
          {"attack":0,"defense":15,"dexterity":0,"battleSprite":0,"statuses":[0,0,0,0,0,0,0,0,0]},
          {"attack":0,"defense":0,"dexterity":0,"battleSprite":0,"statuses":[0,0,0,0,0,0,0,0,0]},
          {"attack":0,"defense":0,"dexterity":0,"battleSprite":0,"statuses":[0,0,0,0,0,0,0,0,0]}]}]}
        """)!.AsObject();

        var legacy = ModernBattleContent.Parse(data.ToJsonString());
        AssertOldClosure(legacy);

        data["equipments"]![0]!["effects"]![2]!["attackAll"] = 1;
        data["equipments"]![1]!["effects"]![3]!["cooperativeMagic"] = 351;
        var content = ModernBattleContent.Parse(data.ToJsonString());
        AssertOldClosure(content);

        static void AssertOldClosure(TriggerBattleContent content)
        {
            Assert.Equal(0, content.InitialRole.RoleId);
            Assert.Equal(0, content.Player.RoleId);
            Assert.False(content.Player.AttackAll);
            Assert.Equal(40, content.Player.Attack);
            Assert.Equal([0, 0, 0, 0, 0, 0], content.InitialRole.Equipment);
            Assert.Equal(6, content.RoleEquipment.Count);
            int[][] expectedGrid = [
                [0, 0, 0, 0, 0, 0], [0, 0, 0, 0, 0, 0], [0, 0, 0, 163, 0, 0],
                [0, 0, 0, 0, 0, 260], [0, 0, 0, 0, 0, 0], [0, 0, 0, 0, 0, 0]];
            Assert.Equal(2, content.EquipmentObjects.Count);
            Assert.Equal(6, content.EquipmentObjects[163].Effects.Count);
            Assert.Equal(6, content.EquipmentObjects[260].Effects.Count);
            Assert.Equal(6, content.EquipmentObjects[163].Effects[2].BattleSprite);
            Assert.Equal(15, content.EquipmentObjects[260].Effects[3].Defense);
            Assert.Equal(0, content.EquipmentObjects[163].Effects[2].Attack);
            Assert.Equal(new int[9], content.EquipmentObjects[260].Effects[3].Statuses);
            var state = BattleEquipmentState.Initialize(content);
            Assert.True(state.Declared);
            for (int role = 0; role < 6; role++)
            {
                Assert.Equal(expectedGrid[role], content.RoleEquipment[role]);
                Assert.Equal(expectedGrid[role], state.Equipment(role));
            }
            Assert.Equal(6, state.BattleSprite(2, 0));
            Assert.Equal(15, state.DefenseBonus(3));
            Assert.Equal(0, state.AttackBonus(0));
            Assert.Equal(40, state.EffectivePlayer(content.Player).Attack);
        }
    }

    [Fact]
    public void NewEquipmentWordsEnforceStrictAuthorAndTypedWordBoundaries()
    {
        foreach (string key in new[] { "attackAll", "cooperativeMagic" })
        {
            var omitted = ModernBattleContent.Parse(WordAuthorData().ToJsonString());
            Assert.Null(omitted.EquipmentObjects[901].Effects[0].AttackAll);
            Assert.Null(omitted.EquipmentObjects[901].Effects[0].CooperativeMagic);
            // Historical JSON stat18/21/22 omission is zero, unlike the new nullable words.
            Assert.Equal(((int?)0, (int?)0, (int?)0), (omitted.EquipmentObjects[901].Effects[0].MagicPower,
                omitted.EquipmentObjects[901].Effects[0].FleeRate, omitted.EquipmentObjects[901].Effects[0].PoisonResistance));
            foreach (int word in new[] { 0, 65535 })
            {
                var legal = WordAuthorData();
                legal["equipments"]![0]!["effects"]![0]![key] = word;
                var row = ModernBattleContent.Parse(legal.ToJsonString()).EquipmentObjects[901].Effects[0];
                Assert.Equal(key == "attackAll" ? word : (int?)null, row.AttackAll);
                Assert.Equal(key == "cooperativeMagic" ? word : (int?)null, row.CooperativeMagic);
            }
            // Integer's definition is Number + TryGetInt32, not coercion from other JSON kinds.
            // Use a genuinely fractional token here; no decimal-token normalization is assumed.
            foreach (string token in new[] { "-1", "65536", "null", "\"351\"", "true", "false", "[]", "{}", "1.5" })
            {
                var data = WordAuthorData();
                data["equipments"]![0]!["effects"]![0]![key] = 0;
                var control = ModernBattleContent.Parse(data.ToJsonString()).EquipmentObjects[901].Effects[0];
                Assert.Equal(0, key == "attackAll" ? control.AttackAll : control.CooperativeMagic);
                data["equipments"]![0]!["effects"]![0]![key] = JsonNode.Parse(token);
                var error = Assert.Throws<InvalidDataException>(() => ModernBattleContent.Parse(data.ToJsonString()));
                Assert.Contains($"$.equipments[0].effects[0].{key}: expected integer in 0..65535", error.Message);
            }
            foreach (int? word in new int?[] { null, 0, 65535, -1, 65536 })
            {
                BattleEquipmentEffect Effect(int? value) => key == "attackAll"
                    ? WordEffect() with { AttackAll = value } : WordEffect() with { CooperativeMagic = value };
                TriggerBattleContent Typed(BattleEquipmentEffect effect) => new(
                    [omitted.InitialRole], omitted.Players, omitted.GrowthByRole, omitted.EnemyTeams,
                    omitted.RoleEquipment, [new(901, [effect, WordEffect(), WordEffect(), WordEffect(), WordEffect(), WordEffect()])]);
                var control = Typed(Effect(null)); // Typed null is legal absence, not explicit JSON null.
                Assert.Null(BattleEquipmentState.Initialize(control).AttackAllWord(0, 0));
                Assert.Null(BattleEquipmentState.Initialize(control).CooperativeMagicWord(0, 0));
                if (word is -1 or 65536)
                {
                    var error = Assert.Throws<InvalidDataException>(() => Typed(Effect(word)));
                    Assert.Equal("$.equipments: equipment object 901 optional effect words must be 0..65535.", error.Message);
                    var objects = new Dictionary<int, BattleEquipmentObject>
                    { [901] = new(901, [Effect(word), WordEffect(), WordEffect(), WordEffect(), WordEffect(), WordEffect()]) };
                    Assert.Throws<InvalidDataException>(() => BattleEquipmentState.Initialize(omitted.RoleEquipment, objects));
                }
                else
                {
                    var state = BattleEquipmentState.Initialize(Typed(Effect(word)));
                    Assert.Equal(key == "attackAll" ? word : null, state.AttackAllWord(0, 0));
                    Assert.Equal(key == "cooperativeMagic" ? word : null, state.CooperativeMagicWord(0, 0));
                }
            }
        }
        var unknown = WordAuthorData();
        Assert.Null(ModernBattleContent.Parse(unknown.ToJsonString()).EquipmentObjects[901].Effects[0].AttackAll);
        unknown["equipments"]![0]!["effects"]![0]!["attackAllTypo"] = 0;
        var unknownError = Assert.Throws<InvalidDataException>(() => ModernBattleContent.Parse(unknown.ToJsonString()));
        Assert.Contains("$.equipments[0].effects[0]: unknown field 'attackAllTypo'", unknownError.Message);
    }

    [Fact]
    public void SixRoleWordRegistersRetainDormantSourceWithoutChangingSelectedAction()
    {
        var content = SixRoleWordContent();
        var state = BattleEquipmentState.Initialize(content);
        // Independent expected register tables, not transposed from the authored object rows below.
        int?[][] attack = [
            [null, 0, null, 0, null, 0, null], [1, 11, 21, 31, 41, 51, null],
            [2, 12, null, 1, 42, 52, null], [3, 13, 23, 33, null, null, null],
            [null, 14, 24, 34, 44, 54, null], [5, 15, 25, 35, 45, 65535, null]];
        int?[][] cooperative = [
            [null, 0, 0, null, null, 0, null], [101, 111, 121, 131, 141, 151, null],
            [102, 112, null, null, 142, 152, null], [103, 113, 123, 133, null, 351, null],
            [null, 114, 124, 134, 144, 154, null], [105, 115, 125, 135, 145, 65535, null]];
        int[][] grid = [[901, 902, 903, 904, 905, 906], [901, 902, 903, 904, 905, 906],
            [901, 902, 0, 163, 905, 906], [901, 902, 903, 904, 0, 260],
            [0, 902, 903, 904, 905, 906], [901, 902, 903, 904, 905, 906]];
        for (int role = 0; role < 6; role++)
        {
            Assert.Equal(grid[role], state.Equipment(role));
            for (int register = 0; register < 7; register++)
            {
                Assert.Equal(attack[role][register], state.AttackAllWord(role, register));
                Assert.Equal(cooperative[role][register], state.CooperativeMagicWord(role, register));
            }
            Assert.Equal(0, state.AttackBonus(role));
            Assert.Equal(new int[9], state.StatusOverlay(role));
        }
        Assert.Equal(6, state.BattleSprite(2, 0));
        Assert.Equal((15, 128, 35), (state.DefenseBonus(3), state.MagicPowerBonus(3), state.PoisonResistanceBonus(3)));
        Assert.Equal((40, 20, 100), (content.Player.Attack, content.Player.Defense, content.Player.Dexterity));
        Assert.Equal(9, content.InitialRole.Magics[0]); // Synthetic known-magic input is not equipment field65=351.
        Assert.DoesNotContain(351, content.InitialRole.Magics);
        Assert.Equal(0, content.Player.RoleId);
        Assert.False(content.Player.AttackAll);
        var script = Script(content, new CountingRandom());
        script.Start("shared", "npc", 28);
        var battle = Assert.IsType<TriggerBattleRequest>(script.PendingBattle).Lifecycle;
        battle.SelectAttack(7);
        Assert.True(battle.ResolveNextAction());
        // Independent classic example: attack40 vs effective defense24 => 42+1, x3 critical,
        // role0 bonus x2, scale1 => 258. Enemy43 dies; dormant equipment does not become an ability.
        Assert.Equal(new ClassicBattleAction(1, ClassicBattleSide.Player, 7, 258, true, false), battle.Current.Combat.LastAction);
        Assert.Equal(ClassicBattlePhase.Won, battle.Current.Combat.Phase);
        Assert.Equal((100, 0), (battle.Current.Combat.PlayerHp, battle.Current.Combat.EnemyHp));
        Assert.Equal((40, 20, 100), (battle.Current.Combat.Players[0].Attack,
            battle.Current.Combat.Players[0].Defense, battle.Current.Combat.Players[0].Dexterity));
        Assert.Equal(9, battle.Current.Roles[0].Magics[0]);
        Assert.Equal(1, state.AttackAllWord(2, 3));
        Assert.Equal(351, state.CooperativeMagicWord(3, 5));
    }

    [Fact]
    public void OldEightPositionEffectAndSnapshotsKeepLegacyWordAndStatusSemantics()
    {
        int[] oldStatuses = [0, 0, 0, 0, 0, 0, 0, 0, 32760];
        var old = new BattleEquipmentEffect(65535, 8, 9, 10, oldStatuses, 11, 12, 13);
        var (attack, defense, dexterity, sprite, statuses, magic, flee, poison) = old;
        Assert.Equal((65535, 8, 9, 10, (int?)11, (int?)12, (int?)13), (attack, defense, dexterity, sprite, magic, flee, poison));
        Assert.Same(oldStatuses, statuses);
        Assert.Null(old.AttackAll);
        Assert.Null(old.CooperativeMagic);
        var defaults = new BattleEquipmentEffect(0, 0, 0, 0, new int[9]);
        Assert.Null(defaults.MagicPower);
        Assert.Null(defaults.FleeRate);
        Assert.Null(defaults.PoisonResistance);
        Assert.Null(defaults.AttackAll);
        Assert.Null(defaults.CooperativeMagic);
        var basis = ModernBattleContent.Parse(WordAuthorData().ToJsonString());
        int[] worn = [901, 902, 0, 0, 0, 0], known = new int[32];
        known[0] = 9;
        int[] roleStatuses = new int[9];
        BattleRoleSnapshot[] roles = [basis.InitialRole with { Equipment = worn, Magics = known, Statuses = roleStatuses }];
        IReadOnlyList<int>[] grid = [worn, new int[6], new int[6], new int[6], new int[6], new int[6]];
        BattleEquipmentEffect[] effects = [old with { AttackAll = 0, CooperativeMagic = 65535 }, defaults, defaults, defaults, defaults, defaults];
        BattleEquipmentObject[] objects = [new(901, effects), new(902, [
            new(2, 0, 0, 0, [0, 0, 0, 0, 0, 0, 0, 0, 60000], 65535, 65535, 65535), defaults, defaults, defaults, defaults, defaults])];
        var content = new TriggerBattleContent(roles, [basis.Player with { Equipment = worn }],
            basis.GrowthByRole, basis.EnemyTeams, grid, objects);
        var rawObjects = objects.ToDictionary(obj => obj.ObjectId);
        var direct = BattleEquipmentState.Initialize(grid, rawObjects);
        var first = BattleEquipmentState.Initialize(content);
        // Mutate each upstream layer after both public snapshot seams, including row/object replacements.
        oldStatuses[8] = 65535;
        effects[0] = defaults with { AttackAll = 1, CooperativeMagic = 1 };
        objects[0] = new(901, [defaults, defaults, defaults, defaults, defaults, defaults]);
        rawObjects[901] = objects[0];
        worn[0] = 0;
        grid[0] = new int[6];
        known[0] = 351;
        roleStatuses[8] = 65535;
        roles[0] = roles[0] with { Hp = 1 };
        var second = BattleEquipmentState.Initialize(content);
        Assert.NotSame(first, second);
        foreach (var state in new[] { direct, first, second })
        {
            Assert.Equal([901, 902, 0, 0, 0, 0], state.Equipment(0));
            Assert.Equal((1, 8, 9), (state.AttackBonus(0), state.DefenseBonus(0), state.DexterityBonus(0)));
            Assert.Equal((10, 11, 12), (state.MagicPowerBonus(0), state.FleeRateBonus(0), state.PoisonResistanceBonus(0)));
            Assert.Equal(0, state.EffectiveAttack(0, 65535)); // 65535 + 2 wraps to1, then base65535 wraps to0.
            Assert.Equal(10, state.BattleSprite(0, 3));
            Assert.Equal(0, state.AttackAllWord(0, 0));
            Assert.Equal(65535, state.CooperativeMagicWord(0, 0));
            Assert.Null(state.AttackAllWord(0, 1));
            Assert.Null(state.CooperativeMagicWord(0, 6));
            Assert.Equal([0, 0, 0, 0, 0, 0, 0, 0, 60000], state.StatusOverlay(0));
            foreach (int current in new[] { 999, 32760, 60000, 60001 })
                Assert.Equal([0, 0, 0, 0, 0, 0, 0, 0, current == 60001 ? 60001 : 60000],
                    state.ApplyStatuses(0, [0, 0, 0, 0, 0, 0, 0, 0, current]));
        }
        Assert.Equal(32760, content.EquipmentObjects[901].Effects[0].Statuses[8]);
        Assert.Equal(9, content.InitialRole.Magics[0]);
        Assert.Equal(100, content.InitialRole.Hp);
        Assert.Equal(new int[9], content.InitialRole.Statuses);
        var changed = BattleEquipmentState.Initialize(grid, rawObjects);
        Assert.Equal(0, changed.AttackBonus(0));
        Assert.Null(changed.AttackAllWord(0, 0));
        Assert.Null(changed.CooperativeMagicWord(0, 0));
        foreach (int rounds in new[] { 999, 1000, 32760, 65535, 65536 })
        {
            var row = defaults with { Statuses = [0, 0, 0, 0, 0, 0, 0, 0, rounds] };
            var closure = new Dictionary<int, BattleEquipmentObject> { [901] = new(901, [row, defaults, defaults, defaults, defaults, defaults]) };
            if (rounds is 999 or 65536)
                Assert.Throws<InvalidDataException>(() => BattleEquipmentState.Initialize(basis.RoleEquipment, closure));
            else
                Assert.Equal(rounds, BattleEquipmentState.Initialize(basis.RoleEquipment, closure).StatusOverlay(0)[8]);
        }
    }

    [Fact]
    public void WordAccessorsValidateRoleBeforeRegisterAndNeverMutateState()
    {
        var state = BattleEquipmentState.Initialize(SixRoleWordContent());
        Func<int, int, int?>[] getters = [state.AttackAllWord, state.CooperativeMagicWord];
        var before = getters.Select(get => Enumerable.Range(0, 6).SelectMany(role =>
            Enumerable.Range(0, 7).Select(register => get(role, register))).ToArray()).ToArray();
        foreach (var get in getters)
        {
            foreach (int role in new[] { -1, 6 })
                foreach (int register in new[] { -1, 0, 6, 7 })
                {
                    var error = Assert.Throws<ArgumentOutOfRangeException>(() => get(role, register));
                    Assert.Equal("roleId", error.ParamName);
                    Assert.Equal(role, error.ActualValue);
                }
            foreach (int role in new[] { 0, 5 })
            {
                foreach (int register in new[] { -1, 7 })
                {
                    var error = Assert.Throws<ArgumentOutOfRangeException>(() => get(role, register));
                    Assert.Equal("registerIndex", error.ParamName);
                    Assert.Equal(register, error.ActualValue);
                }
                Assert.Null(get(role, 6));
            }
            Assert.Null(get(0, 0));
            Assert.Equal(0, get(0, 1));
            Assert.Equal(65535, get(5, 5));
            Assert.NotNull(get(5, 0));
        }
        Assert.Equal(5, state.AttackAllWord(5, 0));
        Assert.Equal(105, state.CooperativeMagicWord(5, 0));
        for (int pass = 0; pass < 3; pass++)
            for (int role = 0; role < 6; role++)
                for (int register = 0; register < 7; register++)
                    foreach (var get in getters) _ = get(role, register);
        for (int getter = 0; getter < getters.Length; getter++)
            Assert.Equal(before[getter], Enumerable.Range(0, 6).SelectMany(role =>
                Enumerable.Range(0, 7).Select(register => getters[getter](role, register))).ToArray());
        for (int role = 0; role < 6; role++)
        {
            Assert.Equal((0, role == 3 ? 15 : 0, 0), (state.AttackBonus(role), state.DefenseBonus(role), state.DexterityBonus(role)));
            Assert.Equal((role == 3 ? 128 : 0, 0, role == 3 ? 35 : 0),
                (state.MagicPowerBonus(role), state.FleeRateBonus(role), state.PoisonResistanceBonus(role)));
            Assert.Equal(new int[9], state.StatusOverlay(role));
            Assert.Equal(40, state.EffectiveAttack(role, 40));
        }
    }

    [Fact]
    public void Item_count_includes_only_current_party_equipment_without_consuming_or_caching_stock()
    {
        // PAL_CountItem (global.c:957): inventory3 + role5 two copies + role3 two copies = 7.
        // All placements/effects are synthetic sentinels, not a natural save or retail equipment claim.
        IReadOnlyList<int>[] grid = [[7, 0, 7, 0, 0, 0], [7, 7, 7, 7, 7, 7],
            [0, 0, 0, 0, 0, 0], [7, 0, 0, 0, 0, 7], [7, 7, 7, 7, 7, 7], [65535, 0, 7, 0, 0, 7]];
        var effect = new BattleEquipmentEffect(2, 3, 4, 5, [0, 0, 0, 0, 0, 0, 0, 0, 60000], 6, 7, 8)
            { AttackAll = 65535, CooperativeMagic = 351 };
        var objects = new Dictionary<int, BattleEquipmentObject>
        {
            [7] = new(7, [effect, effect, effect, effect, effect, effect]),
            [65535] = new(65535, [effect, effect, effect, effect, effect, effect])
        };
        var state = BattleEquipmentState.Initialize(grid, objects);
        var inventory = new InventoryLiveState([new(7, 3, 65535), new(65535, 0, 9)]);
        Assert.Equal(7, state.CountItem(7, inventory, [5, 3])); // Designated RED: inventory-only shell returns3.
        var slots = inventory.Slots.ToArray();
        for (int pass = 0; pass < 3; pass++)
        {
            Assert.Equal(7, state.CountItem(7, inventory, [3, 5]));
            Assert.Equal(3, state.CountItem(7, inventory, []));
            Assert.Equal(3, state.CountItem(7, inventory, [2]));
            Assert.Equal(5, state.CountItem(7, inventory, [0]));
            Assert.Equal(15, state.CountItem(7, inventory, [1, 4]));
            Assert.Equal(21, state.CountItem(7, inventory, [0, 1, 3, 4, 5]));
            Assert.Equal(1, state.CountItem(65535, inventory, [5]));
            Assert.Equal(0, state.CountItem(65535, inventory, [3]));
            Assert.Equal(0, state.CountItem(0, inventory, [5, 3]));
            Assert.Equal(0, state.CountItem(8, inventory, [5, 3]));
        }
        foreach (int item in new[] { 0, 7 })
        {
            Assert.Throws<ArgumentNullException>(() => state.CountItem(item, null!, [5]));
            Assert.Throws<ArgumentNullException>(() => state.CountItem(item, inventory, null!));
            foreach (int[] party in new int[][] { [-1], [6], [5, 5], [0, 1, 2, 3, 4, 5] })
                Assert.ThrowsAny<ArgumentException>(() => state.CountItem(item, inventory, party));
        }
        foreach (int item in new[] { -1, 65536 })
            Assert.Throws<ArgumentOutOfRangeException>(() => state.CountItem(item, inventory, [5]));
        Assert.Equal(slots, inventory.Slots);
        for (int role = 0; role < 6; role++)
        {
            Assert.Equal(grid[role], state.Equipment(role));
            int copies = role == 2 ? 0 : role is 1 or 4 ? 6 : role == 5 ? 3 : 2;
            Assert.Equal((2 * copies, 3 * copies, 4 * copies),
                (state.AttackBonus(role), state.DefenseBonus(role), state.DexterityBonus(role)));
            Assert.Equal((6 * copies, 7 * copies, 8 * copies),
                (state.MagicPowerBonus(role), state.FleeRateBonus(role), state.PoisonResistanceBonus(role)));
            Assert.Equal(role == 2 ? 0 : 5, state.BattleSprite(role, 0));
            Assert.Equal(role == 2 ? new int[9] : [0, 0, 0, 0, 0, 0, 0, 0, 60000], state.StatusOverlay(role));
            Assert.Equal(role == 2 ? null : (int?)65535, state.AttackAllWord(role, 0));
            Assert.Equal(role == 2 ? null : (int?)351, state.CooperativeMagicWord(role, 0));
        }
        Assert.Equal(1, inventory.RemoveItem(7, 3));
        Assert.Equal(4, state.CountItem(7, inventory, [5, 3])); // Depleted occupied slot, not a cached count.
        Assert.Equal(1, inventory.AddItem(7, 2));
        Assert.Equal(6, state.CountItem(7, inventory, [5, 3]));
        Assert.Equal(65535, inventory.Slots[0].AmountInUse);
        var empty = new InventoryLiveState([]);
        Assert.Equal(4, state.CountItem(7, empty, [5, 3]));
        Assert.Equal(2, inventory.GetAmount(7));
        var legacy = BattleEquipmentState.Initialize(ModernBattleContent.Parse(Data().ToJsonString()));
        Assert.False(legacy.Declared);
        Assert.Equal(2, legacy.CountItem(7, inventory, [5, 3]));
        Assert.Equal(0, legacy.CountItem(7, empty, [5, 3]));
        var full = new InventoryLiveState(Enumerable.Range(1, 255).Select(id => new InventorySlot(id, 0, 0))
            .Append(new(65535, 99, 65535)).ToArray());
        Assert.Equal(100, state.CountItem(65535, full, [5])); // Last inventory slot; no 99 cap on total count.
        Assert.Equal(99, full.GetAmount(65535));
        IReadOnlyList<int>[] allSeven = Enumerable.Range(0, 6).Select(_ => (IReadOnlyList<int>)new[] { 7, 7, 7, 7, 7, 7 }).ToArray();
        var allEquipped = BattleEquipmentState.Initialize(allSeven, objects);
        var maximum = new InventoryLiveState([new(7, 99, 65535)]);
        Assert.Equal(129, allEquipped.CountItem(7, maximum, [5, 4, 3, 2, 1])); // 99 + five rows of six, not six roles.
        Assert.Equal(103, state.CountItem(7, maximum, [5, 3])); // Other equipment instance remains independent.
        Assert.Equal(2, inventory.GetAmount(7));
    }

    [Fact]
    public void Clearing_one_effect_register_preserves_gear_statuses_and_other_registers_without_reinitializing()
    {
        // global.c:1395-1400 writes zero to every PLAYERROLES word for ONE part and role.
        // Later hand/wear cleanup is separate: this primitive must not rescan other equip scripts.
        var primary = new BattleEquipmentEffect(7, 17, 27, 700, [0, 0, 0, 0, 0, 1000, 2000, 3000, 60000], 37, 47, 57)
            { AttackAll = 65535, CooperativeMagic = 321 };
        var secondary = new BattleEquipmentEffect(5, 11, 13, 300, new int[9], 19, 23, 29)
            { AttackAll = 0 };
        var objects = new Dictionary<int, BattleEquipmentObject>
        {
            [7] = new(7, Enumerable.Repeat(primary, 6).ToArray()),
            [8] = new(8, Enumerable.Repeat(secondary, 6).ToArray())
        };
        IReadOnlyList<int>[] grid = Enumerable.Range(0, 6).Select(_ => (IReadOnlyList<int>)new[] { 8, 0, 0, 7, 0, 0 }).ToArray();
        var state = BattleEquipmentState.Initialize(grid, objects);
        var independent = BattleEquipmentState.Initialize(grid, objects);
        var stock = new InventoryLiveState([new(7, 3, 65535)]);
        var oldGear = state.Equipment(5);
        var oldStatuses = state.StatusOverlay(5);
        Assert.Equal(12, state.AttackBonus(5));
        state.ClearEffectRegister(5, 3);
        Assert.Equal(5, state.AttackBonus(5)); // Designated RED: no-op shell leaves12, expected5.
        for (int repeat = 0; repeat < 2; repeat++)
        {
            Assert.True(state.Declared);
            Assert.Equal((5, 11, 13, 19, 23, 29), (state.AttackBonus(5), state.DefenseBonus(5),
                state.DexterityBonus(5), state.MagicPowerBonus(5), state.FleeRateBonus(5), state.PoisonResistanceBonus(5)));
            Assert.Equal(300, state.BattleSprite(5, 2)); // Last nonzero falls back to the earlier register.
            Assert.True(state.OverridesBattleSprite(5));
            Assert.Equal(0, state.AttackAllWord(5, 3));
            Assert.Equal(0, state.CooperativeMagicWord(5, 3));
            Assert.Equal(0, state.AttackAllWord(5, 0));
            Assert.Null(state.CooperativeMagicWord(5, 0));
            Assert.Null(state.AttackAllWord(5, 2));
            Assert.Equal([8, 0, 0, 7, 0, 0], state.Equipment(5));
            Assert.Equal([8, 0, 0, 7, 0, 0], oldGear);
            Assert.Equal([0, 0, 0, 0, 0, 1000, 2000, 3000, 60000], state.StatusOverlay(5));
            Assert.Equal(oldStatuses, state.StatusOverlay(5));
            Assert.Equal(oldStatuses, state.ApplyStatuses(5, new int[9]));
            Assert.Equal(4, state.CountItem(7, stock, [5])); // Gear ID and stock are NOT removed by clearing effects.
            Assert.Equal(new InventorySlot(7, 3, 65535), stock.Slots[0]);
            Assert.Equal(12, independent.AttackBonus(5));
            Assert.Equal(65535, independent.AttackAllWord(5, 3));
            Assert.Equal(7, grid[5][3]);
            Assert.Equal(7, objects[7].Effects[5].Attack);
            foreach (int otherRole in new[] { 0, 1, 2, 3, 4 })
            {
                Assert.Equal(12, state.AttackBonus(otherRole));
                Assert.Equal(700, state.BattleSprite(otherRole, 2));
                Assert.Equal(65535, state.AttackAllWord(otherRole, 3));
                Assert.Equal(321, state.CooperativeMagicWord(otherRole, 3));
                Assert.Equal(oldStatuses, state.StatusOverlay(otherRole));
            }
            foreach (int invalidRole in new[] { -1, 6, int.MinValue, int.MaxValue })
                Assert.Throws<ArgumentOutOfRangeException>(() => state.ClearEffectRegister(invalidRole, 0));
            foreach (int invalidRegister in new[] { -1, 7, int.MinValue, int.MaxValue })
                Assert.Throws<ArgumentOutOfRangeException>(() => state.ClearEffectRegister(5, invalidRegister));
            state.ClearEffectRegister(5, 3);
        }
        state.ClearEffectRegister(5, 0);
        Assert.Equal((0, 0, 0, 0, 0, 0), (state.AttackBonus(5), state.DefenseBonus(5), state.DexterityBonus(5),
            state.MagicPowerBonus(5), state.FleeRateBonus(5), state.PoisonResistanceBonus(5)));
        Assert.Equal(2, state.BattleSprite(5, 2));
        Assert.False(state.OverridesBattleSprite(5));
        Assert.Equal(0, state.CooperativeMagicWord(5, 0)); // Clearing an unwritten field is an explicit zero write.
        Assert.Equal(oldStatuses, state.StatusOverlay(5)); // No generic status reset, including hand dualAttack.

        IReadOnlyList<int>[] fullGrid = Enumerable.Range(0, 6).Select(_ => (IReadOnlyList<int>)new[] { 7, 7, 7, 7, 7, 7 }).ToArray();
        for (int role = 0; role < 6; role++)
            for (int register = 0; register < 7; register++)
            {
                var one = BattleEquipmentState.Initialize(fullGrid, objects);
                Assert.Equal(1, one.EffectiveAttack(role, 65495)); // WORD:65495+42 wraps to1.
                one.ClearEffectRegister(role, register);
                if (register < 6)
                {
                    Assert.Equal((35, 85, 135, 185, 235, 285), (one.AttackBonus(role), one.DefenseBonus(role),
                        one.DexterityBonus(role), one.MagicPowerBonus(role), one.FleeRateBonus(role), one.PoisonResistanceBonus(role)));
                    Assert.Equal(65530, one.EffectiveAttack(role, 65495));
                }
                else
                {
                    Assert.Equal((42, 102, 162, 222, 282, 342), (one.AttackBonus(role), one.DefenseBonus(role),
                        one.DexterityBonus(role), one.MagicPowerBonus(role), one.FleeRateBonus(role), one.PoisonResistanceBonus(role)));
                    Assert.Equal(1, one.EffectiveAttack(role, 65495));
                }
                for (int part = 0; part < 7; part++)
                {
                    if (part == register)
                    {
                        Assert.Equal(0, one.AttackAllWord(role, part));
                        Assert.Equal(0, one.CooperativeMagicWord(role, part));
                    }
                    else if (part < 6)
                    {
                        Assert.Equal(65535, one.AttackAllWord(role, part));
                        Assert.Equal(321, one.CooperativeMagicWord(role, part));
                    }
                    else
                    {
                        Assert.Null(one.AttackAllWord(role, part));
                        Assert.Null(one.CooperativeMagicWord(role, part));
                    }
                }
                Assert.Equal([7, 7, 7, 7, 7, 7], one.Equipment(role));
                Assert.Equal(oldStatuses, one.StatusOverlay(role));
            }

        var legacy = BattleEquipmentState.Initialize(ModernBattleContent.Parse(Data().ToJsonString()));
        Assert.False(legacy.Declared);
        legacy.ClearEffectRegister(0, 6);
        Assert.False(legacy.Declared);
        Assert.Equal(0, legacy.AttackAllWord(0, 6));
        Assert.Equal(0, legacy.CooperativeMagicWord(0, 6));
        Assert.Null(legacy.AttackAllWord(0, 0));
        Assert.Null(legacy.AttackAllWord(5, 6));
        Assert.Equal(new int[6], legacy.Equipment(0));
        Assert.Equal(new int[9], legacy.StatusOverlay(0));
        Assert.Equal(0, legacy.AttackBonus(0));
        Assert.Equal(123, legacy.BattleSprite(0, 123));
    }

    private static BattleEquipmentEffect WordEffect(int? attackAll = null, int? cooperativeMagic = null) =>
        new(0, 0, 0, 0, new int[9]) { AttackAll = attackAll, CooperativeMagic = cooperativeMagic };

    private static JsonObject WordAuthorData()
    {
        // Reuse only the existing synthetic public author fixture's non-equipment sections.
        var data = Data();
        data["roles"]![0]!["roleId"] = 0;
        data["players"]![0]!["roleId"] = 0;
        data["growth"]![0]!["roleId"] = 0;
        data["roles"]![0]!["equipment"] = new JsonArray(901, 0, 0, 0, 0, 0);
        data["players"]![0]!["equipment"] = new JsonArray(901, 0, 0, 0, 0, 0);
        data["roleEquipment"] = JsonNode.Parse("[[901,0,0,0,0,0],[0,0,0,0,0,0],[0,0,0,0,0,0],[0,0,0,0,0,0],[0,0,0,0,0,0],[0,0,0,0,0,0]]");
        data["equipments"] = new JsonArray(Equipment(901));
        return data;
    }

    private static TriggerBattleContent SixRoleWordContent()
    {
        var basis = ModernBattleContent.Parse(WordAuthorData().ToJsonString());
        // Only item163/role2/slot3 (audit @9AF7/9AF8:1/sprite6) and item260/role3/slot5
        // (@98F6:351, plus audited stat18=128/defense15/poison35) carry real-source claims.
        // All 901..906 rows, item999, placements and party0 are artificial sentinels, NOT a save/retail grid.
        IReadOnlyList<int>[] grid = [[901, 902, 903, 904, 905, 906], [901, 902, 903, 904, 905, 906],
            [901, 902, 0, 163, 905, 906], [901, 902, 903, 904, 0, 260],
            [0, 902, 903, 904, 905, 906], [901, 902, 903, 904, 905, 906]];
        BattleEquipmentObject[] objects = [
            new(901, [WordEffect(), WordEffect(1, 101), WordEffect(2, 102), WordEffect(3, 103), WordEffect(444, 444), WordEffect(5, 105)]),
            new(902, [WordEffect(0, 0), WordEffect(11, 111), WordEffect(12, 112), WordEffect(13, 113), WordEffect(14, 114), WordEffect(15, 115)]),
            new(903, [WordEffect(null, 0), WordEffect(21, 121), WordEffect(222, 222), WordEffect(23, 123), WordEffect(24, 124), WordEffect(25, 125)]),
            new(904, [WordEffect(0), WordEffect(31, 131), WordEffect(333, 333), WordEffect(33, 133), WordEffect(34, 134), WordEffect(35, 135)]),
            new(905, [WordEffect(), WordEffect(41, 141), WordEffect(42, 142), WordEffect(555, 555), WordEffect(44, 144), WordEffect(45, 145)]),
            new(906, [WordEffect(0, 0), WordEffect(51, 151), WordEffect(52, 152), WordEffect(666, 666), WordEffect(54, 154), WordEffect(65535, 65535)]),
            new(163, [WordEffect(), WordEffect(), WordEffect(1) with { BattleSprite = 6 }, WordEffect(), WordEffect(), WordEffect()]),
            new(260, [WordEffect(), WordEffect(), WordEffect(), WordEffect(null, 351) with { Defense = 15, MagicPower = 128, PoisonResistance = 35 }, WordEffect(), WordEffect()]),
            new(999, Enumerable.Range(0, 6).Select(_ => WordEffect(65535, 65535) with { Attack = 65535, BattleSprite = 65000,
                Statuses = [0, 0, 0, 0, 0, 0, 0, 0, 60000] }).ToArray())];
        return new([basis.InitialRole with { Equipment = [901, 902, 903, 904, 905, 906] }],
            [basis.Player with { Equipment = [901, 902, 903, 904, 905, 906] }], basis.GrowthByRole, basis.EnemyTeams, grid, objects);
    }

    private static JsonObject EquipmentData() => EquipmentData(Data());

    private static JsonObject EquipmentData(JsonObject data)
    {
        Set(data, "roles.0.equipment", new JsonArray(197, 0, 210, 170, 237, 248));
        Set(data, "players.0.equipment", new JsonArray(197, 0, 210, 170, 237, 248));
        Set(data, "players.0.attack", 35);
        data["roleEquipment"] = new JsonArray(
            new JsonArray(196, 225, 208, 166, 235, 249),
            new JsonArray(197, 0, 210, 170, 237, 248),
            new JsonArray(0, 0, 0, 0, 0, 0), new JsonArray(0, 0, 0, 0, 0, 0),
            new JsonArray(0, 0, 0, 0, 0, 0), new JsonArray(0, 0, 0, 0, 0, 0));
        data["equipments"] = new JsonArray(
            Equipment(197, attackByRole1: 3, defenseByRole1: 1),
            Equipment(210, attackByRole1: 4, battleSpriteByRole1: 400),
            Equipment(170, statusesByRole1: 60000),
            Equipment(237, defenseByRole1: 6),
            Equipment(248, dexterityByRole1: 8, battleSpriteByRole1: 450),
            Equipment(196, attackByRole0: 5, defenseByRole0: 2),
            Equipment(225, defenseByRole0: 4),
            Equipment(208, battleSpriteByRole0: 500),
            Equipment(166, attackByRole0: 10),
            Equipment(235, dexterityByRole0: 6),
            Equipment(249, battleSpriteByRole0: 600));
        return data;
    }

    private static JsonNode Equipment(int objectId, int attackByRole0 = 0, int defenseByRole0 = 0, int dexterityByRole0 = 0,
        int battleSpriteByRole0 = 0, int attackByRole1 = 0, int defenseByRole1 = 0, int dexterityByRole1 = 0,
        int battleSpriteByRole1 = 0, int statusesByRole1 = 0)
    {
        JsonNode Row(int attack, int defense, int dexterity, int battleSprite, int dualAttack) => new JsonObject
        {
            ["attack"] = attack, ["defense"] = defense, ["dexterity"] = dexterity, ["battleSprite"] = battleSprite,
            ["statuses"] = new JsonArray(0, 0, 0, 0, 0, 0, 0, 0, dualAttack)
        };
        var node = new JsonObject { ["objectId"] = objectId, ["effects"] = new JsonArray(
            Row(attackByRole0, defenseByRole0, dexterityByRole0, battleSpriteByRole0, 0),
            Row(attackByRole1, defenseByRole1, dexterityByRole1, battleSpriteByRole1, statusesByRole1),
            Row(0, 0, 0, 0, 0), Row(0, 0, 0, 0, 0), Row(0, 0, 0, 0, 0), Row(0, 0, 0, 0, 0)) };
        return node;
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
         "growth":[{"roleId":1,"levelUpExperience":[],"levelUpMagics":[]}],
         "teams":[{"teamId":18,"enemies":[{"id":7,"level":0,"hp":43,"attack":0,"defense":0,"dexterity":0,"physicalResistance":1,
          "statuses":[0,0,0,0,0,0,0,0,0],"poisons":[0],"turnStartScript":0,"readyScript":0,"battleEndScript":0,
          "magicId":0,"dualMove":0,"attackEquivItem":0,"attackEquivItemRate":0,"experience":0,"cash":0}]}]}
        """)!.AsObject();
        data["growth"]![0]!["levelUpExperience"] = new JsonArray(Enumerable.Repeat(0, 100).Select(_ => (JsonNode?)JsonValue.Create(100)).ToArray());
        data["roles"]![0]!["magics"] = new JsonArray(Enumerable.Range(0, 32).Select(i => (JsonNode?)JsonValue.Create(i == 0 ? 9 : 0)).ToArray());
        return data;
    }

    private sealed class CountingRandom : IBattleRandom
    {
        public int Draws { get; private set; }
        public int NextInclusive(int from, int to) { Draws++; return from; }
        public float NextInclusive(float from, float to) { Draws++; return from; }
    }

    private static void Set(JsonNode root, string path, JsonNode? value)
    {
        int dot = path.LastIndexOf('.');
        JsonNode parent = At(root, path[..dot]);
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

    private static TriggerScriptSession Script(TriggerBattleContent content, IBattleRandom random)
    {
        var events = new EventFixture();
        events.Commands[1] = JsonNode.Parse("""
          {"label":"L0001","op":"startBattle","enemyTeamId":18,"lossTarget":"L0004","isBoss":true,"fleeTarget":null,
           "next":"L0002","source":{"index":1,"opcode":7,"operands":[18,4,0]}}
          """);
        return new(events.Load(), TriggerScriptSessionTests.Language(), content, random);
    }
}
