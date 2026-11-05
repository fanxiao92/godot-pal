using System.Text.Json.Nodes;
using Xunit;

namespace Pal.Core.Tests;

/// <summary>Designated functional RED for #48 unit 2: the selected-entry rejection surface that the
/// declared equipment closure must open (base 59cab8f0 rejects the same legal non-zero equipment).</summary>
public sealed class EquipmentEntryRedTests
{
    [Fact]
    public void Selected_entry_accepts_roles_wearing_the_declared_equipment_closure()
    {
        var requirements = TriggerScriptSession.ValidateEntry(Events(), TriggerScriptSessionTests.Language(), Content(), "shared", "npc");
        Assert.Equal(new[] { 18 }, requirements.BattleTeamIds);
        var script = new TriggerScriptSession(Events(), TriggerScriptSessionTests.Language(), Content(), new EmptyRandom());
        script.Start("shared", "npc", 28);
        Assert.NotNull(script.PendingBattle);
    }

    internal static TriggerEventCollection Events()
    {
        var events = new EventFixture();
        events.Commands[1] = JsonNode.Parse("""
          {"label":"L0001","op":"startBattle","enemyTeamId":18,"lossTarget":"L0004","isBoss":true,"fleeTarget":null,
           "next":"L0002","source":{"index":1,"opcode":7,"operands":[18,4,0]}}
          """);
        return events.Load();
    }

    internal static TriggerBattleContent Content()
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
            "equipment":[0,0,0,170,0,0],"magics":[9,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0],
            "statuses":[0,0,0,0,0,0,0,0,0],"coveredByRoleId":0}],
           "players":[{"roleId":1,"hp":100,"maxHp":150,"attack":35,"defense":18,"dexterity":100,
            "equipment":[0,0,0,170,0,0],"statuses":[0,0,0,0,0,0,0,0,0],"poisons":[0],
            "dyingScript":0,"friendDeathScript":0,"coveredByRoleId":null,"attackAll":false}],
           "growth":[{"roleId":1,"levelUpExperience":[],"levelUpMagics":[{"level":2,"magicId":9},{"level":3,"magicId":10}]}],
           "teams":[{"teamId":18,"enemies":[{"id":7,"level":0,"hp":43,"attack":0,"defense":0,"dexterity":0,"physicalResistance":1,
            "statuses":[0,0,0,0,0,0,0,0,0],"poisons":[0],"turnStartScript":0,"readyScript":0,"battleEndScript":0,
            "magicId":0,"dualMove":0,"attackEquivItem":0,"attackEquivItemRate":0,"experience":0,"cash":0}]}],
           "roleEquipment":[[0,0,0,0,0,0],[0,0,0,170,0,0],[0,0,0,0,0,0],[0,0,0,0,0,0],[0,0,0,0,0,0],[0,0,0,0,0,0]],
           "equipments":[{"objectId":170,"effects":[
             {"attack":0,"defense":0,"dexterity":0,"battleSprite":0,"statuses":[0,0,0,0,0,0,0,0,0]},
             {"attack":10,"defense":4,"dexterity":20,"battleSprite":0,"statuses":[0,0,0,0,0,0,0,0,60000]},
             {"attack":0,"defense":0,"dexterity":0,"battleSprite":0,"statuses":[0,0,0,0,0,0,0,0,0]},
             {"attack":0,"defense":0,"dexterity":0,"battleSprite":0,"statuses":[0,0,0,0,0,0,0,0,0]},
             {"attack":0,"defense":0,"dexterity":0,"battleSprite":0,"statuses":[0,0,0,0,0,0,0,0,0]},
             {"attack":0,"defense":0,"dexterity":0,"battleSprite":0,"statuses":[0,0,0,0,0,0,0,0,0]}]}]}
          """)!.AsObject();
        data["growth"]![0]!["levelUpExperience"] = new JsonArray(
            Enumerable.Repeat(0, 100).Select(_ => JsonValue.Create(100) as JsonNode).ToArray());
        return ModernBattleContent.Parse(data.ToJsonString());
    }

    private sealed class EmptyRandom : IBattleRandom
    {
        public int NextInclusive(int from, int to) => from;
        public float NextInclusive(float from, float to) => from;
    }
}
