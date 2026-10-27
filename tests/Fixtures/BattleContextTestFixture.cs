using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Pal.TestFixtures;

public sealed record BattleContextTestFixture(JsonObject Document, byte[] ContextBytes, byte[] IndexBytes, IReadOnlyDictionary<string, byte[]> Files)
{
    public static BattleContextTestFixture Valid()
    {
        JsonObject document = CreateAuthorDocument();
        byte[] context = Encoding.UTF8.GetBytes(document.ToJsonString());
        string hash = Convert.ToHexStringLower(SHA256.HashData(context));
        byte[] index = Encoding.UTF8.GetBytes(new JsonObject
        {
            ["formatVersion"] = 1,
            ["profile"] = "first-mandatory-battle-pre-trigger-v1",
            ["contextFile"] = "battle-context.json",
            ["contextSha256"] = hash
        }.ToJsonString());
        return new(document, context, index, new Dictionary<string, byte[]> { ["battle-context.json"] = context });
    }

    private static JsonObject CreateAuthorDocument()
    {
        JsonObject Experience(int level) => new() { ["value"] = 0, ["level"] = level, ["count"] = 0 };
        JsonObject Role(int id, int level, int hp, int maxHp, int mp, int maxMp, int coveredBy, int[] equipment, int[] magics) => new()
        {
            ["roleId"] = id, ["level"] = level, ["hp"] = hp, ["maxHp"] = maxHp, ["mp"] = mp, ["maxMp"] = maxMp,
            ["primaryExperience"] = Experience(level),
            ["hiddenExperience"] = new JsonObject
            {
                ["health"] = Experience(level), ["magic"] = Experience(level), ["attack"] = Experience(level),
                ["magicPower"] = Experience(level), ["defense"] = Experience(level), ["dexterity"] = Experience(level), ["flee"] = Experience(level)
            },
            ["equipment"] = new JsonArray(equipment.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()),
            ["magics"] = new JsonArray(magics.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()),
            ["statuses"] = new JsonArray(Enumerable.Repeat(0, 9).Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()), ["coveredByRoleId"] = coveredBy
        };
        JsonObject Closure(string id, string category, JsonObject identity, int[] owners, string[] resources) => new()
        {
            ["id"] = id, ["category"] = category, ["identity"] = identity,
            ["ownerTickets"] = new JsonArray(owners.Select(x => (JsonNode?)x).ToArray()), ["status"] = "contract-only",
            ["resourceIds"] = new JsonArray(resources.Select(x => (JsonNode?)x).ToArray())
        };
        var closureEntries = new JsonArray(
            Closure("trigger-09c3-0a32", "command", new JsonObject { ["kind"] = "event-range", ["startAddress"] = 0x09c3, ["battleAddress"] = 0x09fe, ["wonStartAddress"] = 0x09ff, ["endAddress"] = 0x0a32 }, [52, 53], ["events"]),
            Closure("battle-team-18", "command", new JsonObject { ["kind"] = "battle-command", ["commandAddress"] = 0x09fe, ["operands"] = new JsonArray(0x0012, 0xa073, 0x0000), ["enemyTeamId"] = 18, ["enemyObjectId"] = 495, ["enemyId"] = 97, ["enemyCount"] = 2, ["battlefieldId"] = 24, ["specialValue"] = "third-operand-zero-is-boss-no-flee", ["reverseEncoding"] = "encode-operands-from-semantic-fields" }, [46, 47, 51, 53], ["events", "battle-data"]),
            Closure("role-hooks-random", "command", new JsonObject { ["kind"] = "role-script-ranges", ["dyingRoleId"] = 1, ["dyingStartAddress"] = 0xa96e, ["dyingEndAddress"] = 0xa987, ["friendDeathRoleId"] = 0, ["friendDeathStartAddress"] = 0xa9b5, ["friendDeathEndAddress"] = 0xa9d1, ["branchCoverage"] = "all-random-branches" }, [49], ["events", "battle-data", "dialogue-zh-cn", "dialogue-en", "glyphs"]),
            Closure("equipment-six-role", "validation", new JsonObject { ["kind"] = "equipment-layout", ["roleCount"] = 6, ["slotCount"] = 6, ["dualAttackObjectId"] = 170, ["persistentEquipmentStatus"] = true }, [48], ["battle-data"]),
            Closure("growth-five-role", "validation", new JsonObject { ["kind"] = "growth-layout", ["roleCount"] = 5, ["includesPrimaryExperience"] = true, ["includesHiddenExperience"] = true, ["includesRecoveryAndCleanup"] = true }, [50], ["battle-data"]),
            Closure("dynamic-auto-0a36-0a3b", "command", new JsonObject { ["kind"] = "dynamic-auto-range", ["startAddress"] = 0x0a36, ["endAddress"] = 0x0a3b, ["effects"] = new JsonArray("movement", "direction", "hidden"), ["advance"] = "update-driven" }, [52], ["events"]),
            Closure("failure-reload-todo", "command", new JsonObject { ["kind"] = "failure-boundary", ["lostEntryAddress"] = 0xa073, ["reloadCommand"] = 0x004e, ["behavior"] = "diagnose-and-stop-without-reload-retry-or-exploration" }, [51], ["events", "dialogue-zh-cn", "dialogue-en", "glyphs"]),
            Closure("battle-images", "resource", new JsonObject { ["kind"] = "battle-image-set", ["enemyId"] = 97, ["enemyArchiveEntry"] = "ABC97", ["roleSpriteIds"] = new JsonArray("F0", "F1"), ["battlefieldId"] = 24, ["battlefieldArchiveEntry"] = "FBP24", ["includesLayoutAndEffectFrames"] = true }, [53], ["battle-images"]),
            Closure("dialogue-languages", "resource", new JsonObject { ["kind"] = "localized-dialogue-set", ["languages"] = new JsonArray("zh-CN", "en"), ["contents"] = new JsonArray("messages", "names", "portraits", "glyphs", "ui") }, [49, 53], ["dialogue-zh-cn", "dialogue-en", "glyphs"]));
        JsonObject Resource(string id, string kind, string reference, int[] owners) => new()
        {
            ["id"] = id, ["kind"] = kind, ["reference"] = reference, ["sha256"] = new string('a', 64),
            ["ownerTickets"] = new JsonArray(owners.Select(x => (JsonNode?)x).ToArray())
        };
        var roles = new JsonArray(
            Role(0, 1, 150, 150, 100, 100, 2, [196, 225, 208, 166, 235, 249], [296, 345]),
            Role(1, 5, 28, 240, 10, 240, 0, [197, 0, 210, 170, 237, 248], [312, 316, 321, 327, 332, 306, 297, 309, 303, 307]),
            Role(2, 1, 1, 1, 0, 0, 0, [0, 0, 0, 0, 0, 0], []),
            Role(3, 1, 1, 1, 0, 0, 0, [0, 0, 0, 0, 0, 0], []),
            Role(4, 1, 1, 1, 0, 0, 0, [0, 0, 0, 0, 0, 0], []),
            Role(5, 1, 1, 1, 0, 0, 0, [0, 0, 0, 0, 0, 0], []));
        var document = new JsonObject
        {
            ["formatVersion"] = 1, ["profile"] = "first-mandatory-battle-pre-trigger-v1",
            ["identity"] = new JsonObject { ["snapshotKind"] = "authored-precondition-not-natural-save", ["sceneId"] = 2, ["mapId"] = 12, ["ownerId"] = 28, ["triggerAddress"] = 0x09c3 },
            ["provenance"] = new JsonObject
            {
                ["optionalGrinding"] = false, ["playerEquipmentChanges"] = false,
                ["fieldSources"] = new JsonArray(
                    new JsonObject { ["path"] = "snapshot.roles[0].magics[1]", ["sourceKind"] = "required-precondition", ["sourceAddress"] = 0x0905, ["appliesTo"] = "role0" },
                    new JsonObject { ["path"] = "snapshot.roles[1].hp", ["sourceKind"] = "original-base", ["sourceAddress"] = null, ["appliesTo"] = "role1" },
                    new JsonObject { ["path"] = "snapshot.roles[1].mp", ["sourceKind"] = "original-base", ["sourceAddress"] = null, ["appliesTo"] = "role1" })
            },
            ["layout"] = new JsonObject { ["equipmentRoleCount"] = 6, ["equipmentSlotCount"] = 6, ["levelUpMagicRoleCount"] = 5 },
            ["snapshot"] = new JsonObject
            {
                ["partyRoleIds"] = new JsonArray(0), ["roles"] = roles,
                ["world"] = new JsonObject
                {
                    ["sceneId"] = 2, ["mapId"] = 12, ["paletteId"] = 0, ["nightPalette"] = false, ["musicId"] = 31, ["battleMusicId"] = 37,
                    ["leader"] = new JsonObject { ["x"] = 1248, ["y"] = 256, ["direction"] = "east", ["layer"] = 0, ["phase"] = 0 },
                    ["searchProof"] = new JsonObject { ["ownerId"] = 28, ["sampleIndex"] = 3, ["collisionPass"] = true },
                    ["sceneEntries"] = new JsonArray(new JsonObject { ["sceneId"] = 2, ["enterEntry"] = 0x0e97, ["teleportEntry"] = 0 }),
                    ["eventObjects"] = new JsonArray(new JsonObject { ["ownerId"] = 28, ["x"] = 1280, ["y"] = 256, ["direction"] = 0, ["state"] = 2, ["vanishTime"] = 0, ["triggerMode"] = 1, ["triggerEntry"] = 0x09c3, ["autoEntry"] = 0, ["autoIdleFrameCount"] = 0, ["spriteId"] = 210, ["spriteFrames"] = 0, ["currentFrame"] = 0, ["scriptIdleFrameCount"] = 0, ["spritePtrOffset"] = 0, ["autoSpriteFrames"] = 0, ["layer"] = 0 })
                }
            },
            ["closure"] = new JsonObject { ["entry"] = "@09C3", ["entries"] = closureEntries },
            ["resources"] = new JsonArray(
                Resource("events", "faithful-events", "events/index.json", [52, 53]),
                Resource("battle-data", "battle-data", "battle/data.json", [46, 47, 48, 49, 50, 51, 53]),
                Resource("battle-images", "battle-images", "battle/images.json", [53]),
                Resource("dialogue-zh-cn", "dialogue-language", "dialogue/zh-CN.json", [49, 53]),
                Resource("dialogue-en", "dialogue-language", "dialogue/en.json", [49, 53]),
                Resource("glyphs", "glyph-atlas", "dialogue/glyphs.json", [49, 53]))
        };
        var fieldSources = document["provenance"]!["fieldSources"]!.AsArray();
        var existing = fieldSources.Select(item => item!["path"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        AddLeafSources(document["snapshot"]!, "snapshot", fieldSources, existing, document["snapshot"]!.AsObject());
        return document;
    }

    private static void AddLeafSources(JsonNode node, string path, JsonArray sources, HashSet<string> existing, JsonObject snapshot)
    {
        if (node is JsonObject obj)
        {
            foreach (var pair in obj.Where(pair => !pair.Key.StartsWith('_'))) AddLeafSources(pair.Value!, path + "." + pair.Key, sources, existing, snapshot);
        }
        else if (node is JsonArray array && array.Count > 0)
        {
            for (int index = 0; index < array.Count; index++) AddLeafSources(array[index]!, $"{path}[{index}]", sources, existing, snapshot);
        }
        else if (existing.Add(path))
        {
            (string kind, int? address) = FixtureProvenance(path);
            sources.Add(new JsonObject { ["path"] = path, ["sourceKind"] = kind, ["sourceAddress"] = address, ["appliesTo"] = AppliesTo(path, snapshot) });
        }
    }

    private static (string Kind, int? Address) FixtureProvenance(string path)
    {
        if (path == "snapshot.world.eventObjects[0].state") return ("required-precondition", 0x0936);
        if (path.StartsWith("snapshot.roles[0].equipment[", StringComparison.Ordinal)
            || path.StartsWith("snapshot.roles[1].equipment[", StringComparison.Ordinal)
            || path == "snapshot.roles[0].magics[0]"
            || path.StartsWith("snapshot.roles[1].magics[", StringComparison.Ordinal)
            || path is "snapshot.roles[0].level" or "snapshot.roles[0].coveredByRoleId"
            || path is "snapshot.roles[1].level" or "snapshot.roles[1].hp" or "snapshot.roles[1].maxHp" or "snapshot.roles[1].mp" or "snapshot.roles[1].maxMp" or "snapshot.roles[1].coveredByRoleId")
            return ("original-base", null);
        return ("author-policy", null);
    }

    private static string AppliesTo(string path, JsonObject snapshot)
    {
        if (path.StartsWith("snapshot.roles[", StringComparison.Ordinal)) return "role" + path[15..path.IndexOf(']', 15)];
        if (path.StartsWith("snapshot.partyRoleIds", StringComparison.Ordinal)) return "team-before-09C3";
        if (path.StartsWith("snapshot.world.eventObjects[", StringComparison.Ordinal))
        {
            int index = int.Parse(path[28..path.IndexOf(']', 28)]);
            return "owner" + snapshot["world"]!["eventObjects"]![index]!["ownerId"]!.GetValue<int>();
        }
        if (path.StartsWith("snapshot.world.sceneEntries[", StringComparison.Ordinal))
        {
            int index = int.Parse(path[28..path.IndexOf(']', 28)]);
            return "scene" + snapshot["world"]!["sceneEntries"]![index]!["sceneId"]!.GetValue<int>();
        }
        return "world";
    }

}
