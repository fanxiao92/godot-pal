using System.Text;
using System.Text.Json.Nodes;
using Pal.Extract;

namespace Pal.TestFixtures;

/// <summary>#236 synthetic caller-verified M5 Windows base tables for the full projection chain.
/// Not raw archive data, not retail content and not independent source authentication. Team 18
/// carries object ids 7,7 (two loaded slots); both resolve to enemy definition 1 with literal
/// hp1/attack0/defense0/exp3/cash5 so one role0 member wins in two strikes with 6 exp / 10 cash.</summary>
internal static class ProjectedStoryChainTables
{
    internal static Dictionary<string, byte[]> Create()
    {
        DataTableDescriptor[] descriptors = [DataTables.EventObject, DataTables.Scene, DataTables.Store,
            DataTables.Enemy, DataTables.EnemyTeam, DataTables.PlayerRoles, DataTables.Magic,
            DataTables.BattleField, DataTables.LevelUpMagic, DataTables.BattleEffectIndex,
            DataTables.EnemyPos, DataTables.LevelUpExp];
        var docs = descriptors.ToDictionary(d => d.FileId + ".json", d => new JsonObject
        {
            ["formatVersion"] = 1, ["profile"] = "faithful", ["fileId"] = d.FileId,
            ["records"] = new JsonArray(Blank(d))
        });
        JsonObject roles = docs["player-roles.json"]["records"]![0]!.AsObject();
        roles["level"]![0] = 2;
        roles["hp"]![0] = 30;
        roles["maxHp"]![0] = 50;
        roles["attackStrength"]![0] = 41;
        roles["defense"]![0] = 13;
        roles["dexterity"]![0] = 17;
        roles["coveredBy"]![0] = 0;
        docs["level-up-exp.json"]["records"]![0]!["levelUpExp"] = new JsonArray(
            Enumerable.Range(0, 100).Select(i => (JsonNode?)JsonValue.Create(i == 0 ? 0 : 100 + i)).ToArray());
        var learn = docs["level-up-magic.json"]["records"]!.AsArray();
        learn.Add(Blank(DataTables.LevelUpMagic));
        learn.Add(Blank(DataTables.LevelUpMagic));
        var enemies = docs["enemy.json"]["records"]!.AsArray();
        ConfigureEnemy(enemies[0]!.AsObject());
        enemies.Add(enemies[0]!.DeepClone());
        var objects = new JsonArray();
        for (int i = 0; i < 6; i++) objects.Add(JsonNode.Parse("""
            {"view":"player","reserved":[0,0],"scriptOnFriendDeath":0,"scriptOnDying":0,"padding":[0,0,0]}
            """));
        for (int i = 0; i < 2; i++) objects.Add(new JsonObject
        {
            ["view"] = "enemy", ["enemyId"] = i, ["resistanceToSorcery"] = 0,
            ["scriptOnTurnStart"] = 0, ["scriptOnBattleEnd"] = 0, ["scriptOnReady"] = 0,
            ["padding"] = new JsonArray(0, 0)
        });
        objects.Add(JsonNode.Parse("""
            {"view":"magic","magicId":0,"reserved1":0,"scriptOnSuccess":0,"scriptOnUse":0,"scriptDesc":0,"reserved2":0,"flags":0}
            """));
        docs["object.json"] = new JsonObject
        {
            ["formatVersion"] = 1, ["profile"] = "faithful", ["fileId"] = "object", ["records"] = objects
        };
        var teams = docs["enemy-team.json"]["records"]!.AsArray();
        var team = teams[0]!.DeepClone().AsObject();
        team["enemies"] = new JsonArray(7, 7, 65535, 65535, 65535);
        teams.Clear();
        for (int id = 0; id < 19; id++) teams.Add(team.DeepClone());
        var entries = new JsonArray();
        foreach (var d in descriptors)
        {
            int count = docs[d.FileId + ".json"]["records"]!.AsArray().Count;
            entries.Add(Entry(d.FileId + ".json", d.Archive, d.Chunk, count, count * d.RecordByteSize));
        }
        var enemyTeam = entries.Single(entry => entry!["file"]!.GetValue<string>() == "enemy-team.json")!;
        enemyTeam["recordCount"] = 19;
        enemyTeam["byteLength"] = 190;
        entries.Add(Entry("object.json", "SSS.MKF", 2, 9, 126));
        docs["index.json"] = new JsonObject
        {
            ["formatVersion"] = 1, ["profile"] = "faithful",
            ["sources"] = new JsonArray(new JsonObject { ["archive"] = "SSS.MKF", ["sha256"] = new string('a', 64) },
                new JsonObject { ["archive"] = "DATA.MKF", ["sha256"] = new string('b', 64) }),
            ["tables"] = entries
        };
        return docs.ToDictionary(x => x.Key, x => Encoding.UTF8.GetBytes(x.Value.ToJsonString()));
    }

    private static void ConfigureEnemy(JsonObject enemy)
    {
        enemy["health"] = 1;
        enemy["level"] = 1;
        enemy["attackStrength"] = 0;
        enemy["defense"] = 0;
        enemy["dexterity"] = 0;
        enemy["physicalResistance"] = 0;
        enemy["exp"] = 3;
        enemy["cash"] = 5;
        enemy["magicId"] = 0;
        enemy["dualMove"] = 0;
        enemy["attackEquivItemId"] = 0;
        enemy["attackEquivItemRate"] = 0;
    }

    private static JsonObject Entry(string file, string archive, int chunk, int count, int length) => new()
    {
        ["file"] = file, ["archive"] = archive, ["chunk"] = chunk, ["recordCount"] = count,
        ["byteLength"] = length, ["sha256"] = new string('c', 64)
    };

    private static JsonObject Blank(DataTableDescriptor d)
    {
        var obj = new JsonObject();
        foreach (var f in d.Fields) obj[f.Name] = BlankField(f, 0);
        if (d.DispatchedSlot is not null) obj["layerOffset"] = 0;
        return obj;
    }

    private static JsonNode BlankField(DataField f, int dimension)
    {
        if (f.Dimensions is not null && dimension < f.Dimensions.Length)
            return new JsonArray(Enumerable.Range(0, f.Dimensions[dimension]).Select(_ => BlankField(f, dimension + 1)).ToArray());
        if (f.Members is null) return JsonValue.Create(0)!;
        var obj = new JsonObject();
        foreach (var member in f.Members) obj[member.Name] = BlankField(member, 0);
        return obj;
    }
}
