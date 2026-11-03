using System.Text.Json.Nodes;
using Xunit;

namespace Pal.Core.Tests;

public sealed class BattleEquipmentStateTests
{
    [Fact]
    public void SixRoleEquipmentClosureParsesIntoTypedContent()
    {
        var data = EquipmentData();
        var content = ModernBattleContent.Parse(data.ToJsonString());
        Assert.Equal(new[] { 197, 0, 210, 170, 237, 248 }, content.InitialRole.Equipment);
    }

    private static JsonObject EquipmentData()
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
          "equipment":[197,0,210,170,237,248],"magics":[],"statuses":[0,0,0,0,0,0,0,0,0],"coveredByRoleId":0}],
         "players":[{"roleId":1,"hp":100,"maxHp":150,"attack":35,"defense":20,"dexterity":100,
          "equipment":[197,0,210,170,237,248],"statuses":[0,0,0,0,0,0,0,0,0],"poisons":[0],
          "dyingScript":0,"friendDeathScript":0,"coveredByRoleId":null,"attackAll":false}],
         "growth":[{"roleId":1,"levelUpExperience":[],"levelUpMagics":[]}],
         "teams":[{"teamId":18,"enemies":[{"id":7,"level":0,"hp":43,"attack":0,"defense":0,"dexterity":0,"physicalResistance":1,
          "statuses":[0,0,0,0,0,0,0,0,0],"poisons":[0],"turnStartScript":0,"readyScript":0,"battleEndScript":0,
          "magicId":0,"dualMove":0,"attackEquivItem":0,"attackEquivItemRate":0,"experience":0,"cash":0}]}],
         "roleEquipment":[[196,225,208,166,235,249],[197,0,210,170,237,248],[0,0,0,0,0,0],[0,0,0,0,0,0],[0,0,0,0,0,0],[0,0,0,0,0,0]],
         "equipments":[{"objectId":170,"effects":[
            {"attack":0,"defense":0,"dexterity":0,"battleSprite":0,"statuses":[0,0,0,0,0,0,0,0,60000]},
            {"attack":0,"defense":0,"dexterity":0,"battleSprite":0,"statuses":[0,0,0,0,0,0,0,0,60000]},
            {"attack":0,"defense":0,"dexterity":0,"battleSprite":0,"statuses":[0,0,0,0,0,0,0,0,60000]},
            {"attack":0,"defense":0,"dexterity":0,"battleSprite":0,"statuses":[0,0,0,0,0,0,0,0,60000]},
            {"attack":0,"defense":0,"dexterity":0,"battleSprite":0,"statuses":[0,0,0,0,0,0,0,0,60000]},
            {"attack":0,"defense":0,"dexterity":0,"battleSprite":0,"statuses":[0,0,0,0,0,0,0,0,60000]}]}]}
        """)!.AsObject();
        data["growth"]![0]!["levelUpExperience"] = new JsonArray(Enumerable.Repeat(0, 100).Select(_ => (JsonNode?)JsonValue.Create(100)).ToArray());
        return data;
    }
}
