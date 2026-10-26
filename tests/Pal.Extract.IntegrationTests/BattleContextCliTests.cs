using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Pal.Extract.Tests;

public sealed class BattleContextCliTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"pal-battle-context-{Guid.NewGuid():N}");
    private readonly string input;
    private readonly string output;

    public BattleContextCliTests()
    {
        Directory.CreateDirectory(root);
        input = Path.Combine(root, "author.json");
        output = Path.Combine(root, "prepared");
        File.WriteAllText(input, CreateAuthorDocument().ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    [Fact]
    public async Task Author_battle_context_writes_strict_modern_artifact_without_changing_input()
    {
        byte[] before = File.ReadAllBytes(input);

        var result = await CliProcess.Run(root, "author-battle-context", "--input", input, "--output", output);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("Battle context authored: first-mandatory-battle-pre-trigger-v1\n", result.Stdout.Replace("\r\n", "\n"));
        Assert.Empty(result.Stderr);
        Assert.Equal(before, File.ReadAllBytes(input));
        string contextPath = Path.Combine(output, "battle-context.json");
        using var context = JsonDocument.Parse(File.ReadAllText(contextPath));
        Assert.Equal("authored-precondition-not-natural-save", context.RootElement.GetProperty("identity").GetProperty("snapshotKind").GetString());
        Assert.Equal(new[] { 0 }, context.RootElement.GetProperty("snapshot").GetProperty("partyRoleIds").EnumerateArray().Select(x => x.GetInt32()));
        Assert.Equal(6, context.RootElement.GetProperty("snapshot").GetProperty("roles").GetArrayLength());
        Assert.Equal(6, context.RootElement.GetProperty("layout").GetProperty("equipmentRoleCount").GetInt32());
        Assert.Equal(5, context.RootElement.GetProperty("layout").GetProperty("levelUpMagicRoleCount").GetInt32());
        Assert.Equal(28, context.RootElement.GetProperty("snapshot").GetProperty("roles")[1].GetProperty("hp").GetInt32());
        Assert.Equal(10, context.RootElement.GetProperty("snapshot").GetProperty("roles")[1].GetProperty("mp").GetInt32());
        using var index = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "index.json")));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(contextPath))), index.RootElement.GetProperty("contextSha256").GetString());
    }

    [Theory]
    [InlineData("identity.snapshotKind", "natural-save", "not a natural save")]
    [InlineData("layout.equipmentRoleCount", "5", "six-role equipment")]
    [InlineData("layout.levelUpMagicRoleCount", "6", "five-role level-up magic")]
    [InlineData("snapshot.roles", "remove-last", "exactly six roles")]
    [InlineData("closure", "remove-failure-reload", "failure-reload-todo")]
    public async Task Author_battle_context_rejects_identity_layout_and_incomplete_closure(string mutation, string value, string diagnostic)
    {
        var rootNode = JsonNode.Parse(File.ReadAllText(input))!.AsObject();
        if (mutation == "identity.snapshotKind") rootNode["identity"]!["snapshotKind"] = value;
        else if (mutation == "layout.equipmentRoleCount") rootNode["layout"]!["equipmentRoleCount"] = int.Parse(value);
        else if (mutation == "layout.levelUpMagicRoleCount") rootNode["layout"]!["levelUpMagicRoleCount"] = int.Parse(value);
        else if (mutation == "snapshot.roles") rootNode["snapshot"]!["roles"]!.AsArray().RemoveAt(5);
        else
        {
            var entries = rootNode["closure"]!["entries"]!.AsArray();
            entries.Remove(entries.Single(x => x!["id"]!.GetValue<string>() == "failure-reload-todo"));
        }
        File.WriteAllText(input, rootNode.ToJsonString());

        var result = await CliProcess.Run(root, "author-battle-context", "--input", input, "--output", output);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Contains(diagnostic, result.Stderr, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(output));
    }

    [Theory]
    [InlineData("missing-provenance", "every snapshot field")]
    [InlineData("signed-word", "signed WORD")]
    [InlineData("unsafe-resource", "safe forward-slash relative path")]
    public async Task Author_battle_context_rejects_unprovenanced_out_of_range_and_unsafe_values(string mutation, string diagnostic)
    {
        var rootNode = JsonNode.Parse(File.ReadAllText(input))!.AsObject();
        if (mutation == "missing-provenance")
        {
            var sources = rootNode["provenance"]!["fieldSources"]!.AsArray();
            sources.Remove(sources.Single(item => item!["path"]!.GetValue<string>() == "snapshot.world.musicId"));
        }
        else if (mutation == "signed-word") rootNode["snapshot"]!["world"]!["eventObjects"]![0]!["vanishTime"] = 40000;
        else rootNode["resources"]![0]!["reference"] = "../events/index.json";
        File.WriteAllText(input, rootNode.ToJsonString());

        var result = await CliProcess.Run(root, "author-battle-context", "--input", input, "--output", output);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(diagnostic, result.Stderr, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(output));
    }

    [Fact]
    public async Task Author_battle_context_rejects_duplicate_keys_and_does_not_touch_empty_output()
    {
        string json = File.ReadAllText(input);
        File.WriteAllText(input, json.Replace("\"formatVersion\": 1", "\"formatVersion\": 1, \"formatVersion\": 1", StringComparison.Ordinal));
        Directory.CreateDirectory(output);

        var result = await CliProcess.Run(root, "author-battle-context", "--input", input, "--output", output);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("duplicate JSON key", result.Stderr, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(output));
    }

    [Fact]
    public async Task Author_battle_context_rejects_nonempty_output_and_unsupported_argv()
    {
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "owned.txt"), "keep");
        var overwrite = await CliProcess.Run(root, "author-battle-context", "--input", input, "--output", output);
        Assert.Equal(1, overwrite.ExitCode);
        Assert.Contains("not empty", overwrite.Stderr, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("keep", File.ReadAllText(Path.Combine(output, "owned.txt")));

        var argv = await CliProcess.Run(root, "author-battle-context", "--output", output, "--input", input);
        Assert.Equal(2, argv.ExitCode);
        Assert.Contains("Unsupported arguments for author-battle-context", argv.Stderr);
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
        string[] closureIds = ["trigger-09c3-0a32", "battle-team-18", "role-hooks-random", "equipment-six-role", "growth-five-role", "dynamic-auto-0a36-0a3b", "failure-reload-todo", "battle-images", "dialogue-languages"];
        var closureEntries = new JsonArray(closureIds.Select((id, i) => (JsonNode)new JsonObject
        {
            ["id"] = id, ["category"] = i < 3 ? "command" : "resource", ["identity"] = id,
            ["ownerTicket"] = id switch { "dynamic-auto-0a36-0a3b" => 52, "failure-reload-todo" => 51, "growth-five-role" => 50, "role-hooks-random" => 49, "equipment-six-role" => 48, _ => 53 },
            ["status"] = "contract-only"
        }).ToArray());
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
                    ["eventObjects"] = new JsonArray(new JsonObject { ["ownerId"] = 28, ["x"] = 1280, ["y"] = 256, ["direction"] = 0, ["state"] = 2, ["vanishTime"] = 0, ["triggerMode"] = 1, ["triggerEntry"] = 0x09c3, ["autoEntry"] = 0, ["autoIdleFrameCount"] = 0, ["spriteId"] = 210, ["spriteFrames"] = 0, ["layer"] = 0 })
                }
            },
            ["closure"] = new JsonObject { ["entry"] = "@09C3", ["entries"] = closureEntries },
            ["resources"] = new JsonArray(new JsonObject { ["id"] = "events", ["kind"] = "faithful-events", ["reference"] = "events/index.json", ["sha256"] = new string('a', 64), ["ownerTicket"] = 53 })
        };
        var fieldSources = document["provenance"]!["fieldSources"]!.AsArray();
        var existing = fieldSources.Select(item => item!["path"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        AddLeafSources(document["snapshot"]!, "snapshot", fieldSources, existing);
        return document;
    }

    private static void AddLeafSources(JsonNode node, string path, JsonArray sources, HashSet<string> existing)
    {
        if (node is JsonObject obj)
        {
            foreach (var pair in obj) AddLeafSources(pair.Value!, path + "." + pair.Key, sources, existing);
        }
        else if (node is JsonArray array)
        {
            for (int index = 0; index < array.Count; index++) AddLeafSources(array[index]!, $"{path}[{index}]", sources, existing);
        }
        else if (existing.Add(path))
        {
            sources.Add(new JsonObject { ["path"] = path, ["sourceKind"] = "author-policy", ["sourceAddress"] = null, ["appliesTo"] = "artificial-fixture" });
        }
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
