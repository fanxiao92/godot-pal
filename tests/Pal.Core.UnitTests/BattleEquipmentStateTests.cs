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
          {"label":"L0001","op":"startBattle","teamId":18,"lossTarget":"L0004","isBoss":true,"fleeTarget":null,
           "next":"L0002","source":{"index":1,"opcode":7,"operands":[18,4,0]}}
          """);
        return new(events.Load(), TriggerScriptSessionTests.Language(), content, random);
    }
}
