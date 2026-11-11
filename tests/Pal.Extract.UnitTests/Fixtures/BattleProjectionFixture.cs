using System.Text;
using System.Text.Json.Nodes;
using Pal.Extract;

namespace Pal.Extract.Tests;

// Artificial modern documents only; no raw archive decoding or game assets.
internal static class BattleProjectionFixture
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
        for (int role = 0; role < 6; role++)
        {
            roles["name"]![role] = role;
            roles["level"]![role] = 2;
            roles["hp"]![role] = 30;
            roles["maxHp"]![role] = 50;
            roles["attackStrength"]![role] = 41;
            roles["defense"]![role] = 13;
            roles["dexterity"]![role] = 17;
        }
        roles["level"]![4] = 7;
        roles["hp"]![4] = 80;
        roles["maxHp"]![4] = 100;
        roles["attackStrength"]![4] = 123;
        roles["magicStrength"]![4] = 27;
        roles["fleeRate"]![4] = 19;
        roles["coveredBy"]![0] = 4;
        roles["magic"]![3]![4] = 8;
        docs["level-up-exp.json"]["records"]![0]!["levelUpExp"] = new JsonArray(
            Enumerable.Range(0, 100).Select(i => (JsonNode?)JsonValue.Create(i == 0 ? 0 : 100 + i)).ToArray());
        var learn = docs["level-up-magic.json"]["records"]!.AsArray();
        learn[0]!["roles"]![4]!["level"] = 8;
        learn[0]!["roles"]![4]!["magicId"] = 8;
        learn.Add(learn[0]!.DeepClone());
        learn.Add(Blank(DataTables.LevelUpMagic));
        var enemies = docs["enemy.json"]["records"]!.AsArray();
        enemies[0]!["health"] = 70;
        enemies[0]!["level"] = 3;
        enemies[0]!["attackStrength"] = 65535;
        enemies[0]!["defense"] = 32768;
        enemies[0]!["dexterity"] = 32767;
        enemies[0]!["physicalResistance"] = 2;
        enemies[0]!["exp"] = 29;
        enemies[0]!["cash"] = 31;
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
        docs["enemy-team.json"]["records"]![0]!["enemies"] = new JsonArray(6, 65535, 7, 65535, 65535);
        var entries = new JsonArray();
        foreach (var d in descriptors)
        {
            int count = docs[d.FileId + ".json"]["records"]!.AsArray().Count;
            entries.Add(Entry(d.FileId + ".json", d.Archive, d.Chunk, count, count * d.RecordByteSize));
        }
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

    internal static void Mutate(Dictionary<string, byte[]> files, string file, Action<JsonObject> change)
    {
        var doc = JsonNode.Parse(files[file])!.AsObject();
        change(doc);
        files[file] = Encoding.UTF8.GetBytes(doc.ToJsonString());
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
