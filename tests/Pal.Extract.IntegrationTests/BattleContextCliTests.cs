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

    [Theory]
    [InlineData("battle-context.json")]
    [InlineData("index.json")]
    public async Task Author_battle_context_never_overwrites_an_existing_target_file(string fileName)
    {
        Directory.CreateDirectory(output);
        string existing = Path.Combine(output, fileName);
        File.WriteAllText(existing, "owned-content");

        var result = await CliProcess.Run(root, "author-battle-context", "--input", input, "--output", output);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("owned-content", File.ReadAllText(existing));
    }

    [Theory]
    [InlineData("role0-state")]
    [InlineData("role0-equipment")]
    [InlineData("role0-magics")]
    [InlineData("role1-equipment")]
    [InlineData("role1-magics")]
    [InlineData("cover")]
    public async Task Author_battle_context_rejects_changes_to_each_frozen_role_fact_group(string mutation)
    {
        JsonObject document = CreateAuthorDocument();
        JsonArray roles = document["snapshot"]!["roles"]!.AsArray();
        if (mutation == "role0-state") roles[0]!["hp"] = 149;
        else if (mutation == "role0-equipment") roles[0]!["equipment"]![0] = 195;
        else if (mutation == "role0-magics") roles[0]!["magics"]![1] = 344;
        else if (mutation == "role1-equipment") roles[1]!["equipment"]![3] = 171;
        else if (mutation == "role1-magics") roles[1]!["magics"]![0] = 311;
        else roles[0]!["coveredByRoleId"] = 1;
        File.WriteAllText(input, document.ToJsonString());

        var result = await CliProcess.Run(root, "author-battle-context", "--input", input, "--output", output);

        Assert.Equal(1, result.ExitCode);
        Assert.NotEmpty(result.Stderr);
    }

    [Theory]
    [InlineData("required-null")]
    [InlineData("base-address")]
    [InlineData("role1-kind")]
    [InlineData("wrong-applies-to")]
    [InlineData("nonexistent-path")]
    [InlineData("wrong-0905-address")]
    [InlineData("empty-container")]
    [InlineData("role0-equipment-as-policy")]
    [InlineData("role1-equipment-as-policy")]
    [InlineData("role0-base-magic-as-policy")]
    [InlineData("role1-base-magic-as-policy")]
    [InlineData("base-equipment-unrelated-precondition")]
    [InlineData("base-magic-unrelated-precondition")]
    [InlineData("owner28-state-unrelated-precondition")]
    public async Task Author_battle_context_rejects_contradictory_or_incomplete_provenance(string mutation)
    {
        JsonObject document = CreateAuthorDocument();
        JsonArray sources = document["provenance"]!["fieldSources"]!.AsArray();
        JsonNode role0Magic = sources.Single(x => x!["path"]!.GetValue<string>() == "snapshot.roles[0].magics[1]")!;
        JsonNode role1Hp = sources.Single(x => x!["path"]!.GetValue<string>() == "snapshot.roles[1].hp")!;
        if (mutation == "required-null") role0Magic["sourceAddress"] = null;
        else if (mutation == "base-address") role1Hp["sourceAddress"] = 0x0905;
        else if (mutation == "role1-kind") role1Hp["sourceKind"] = "author-policy";
        else if (mutation == "wrong-applies-to") role1Hp["appliesTo"] = "role5";
        else if (mutation == "nonexistent-path") sources.Add(new JsonObject { ["path"] = "snapshot.roles[5].notReal", ["sourceKind"] = "author-policy", ["sourceAddress"] = null, ["appliesTo"] = "role5" });
        else if (mutation == "wrong-0905-address") role0Magic["sourceAddress"] = 0x0904;
        else if (mutation == "empty-container") sources.Remove(sources.Single(x => x!["path"]!.GetValue<string>() == "snapshot.roles[2].magics"));
        else
        {
            string path = mutation switch
            {
                "role0-equipment-as-policy" => "snapshot.roles[0].equipment[0]",
                "role1-equipment-as-policy" => "snapshot.roles[1].equipment[3]",
                "role0-base-magic-as-policy" => "snapshot.roles[0].magics[0]",
                "role1-base-magic-as-policy" => "snapshot.roles[1].magics[0]",
                "base-equipment-unrelated-precondition" => "snapshot.roles[0].equipment[0]",
                "base-magic-unrelated-precondition" => "snapshot.roles[1].magics[0]",
                _ => "snapshot.world.eventObjects[0].state"
            };
            JsonNode source = sources.Single(x => x!["path"]!.GetValue<string>() == path)!;
            source["sourceKind"] = mutation.EndsWith("as-policy", StringComparison.Ordinal) ? "author-policy" : "required-precondition";
            source["sourceAddress"] = mutation.EndsWith("as-policy", StringComparison.Ordinal) ? null : 0xffff;
        }
        File.WriteAllText(input, document.ToJsonString());

        var result = await CliProcess.Run(root, "author-battle-context", "--input", input, "--output", output);

        Assert.Equal(1, result.ExitCode);
        Assert.NotEmpty(result.Stderr);
    }

    [Fact]
    public async Task Author_battle_context_rejects_sha256_with_final_line_feed()
    {
        JsonObject document = CreateAuthorDocument();
        document["resources"]![0]!["sha256"] = new string('a', 64) + "\n";
        File.WriteAllText(input, document.ToJsonString());

        var result = await CliProcess.Run(root, "author-battle-context", "--input", input, "--output", output);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("sha256", result.Stderr, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(output));
    }

    [Fact]
    public async Task Author_battle_context_ignores_root_and_nested_comment_fields_for_provenance()
    {
        JsonObject document = CreateAuthorDocument();
        document["snapshot"]!["_note"] = "comment";
        document["snapshot"]!["world"]!["leader"]!["_note"] = "comment";
        File.WriteAllText(input, document.ToJsonString());

        var result = await CliProcess.Run(root, "author-battle-context", "--input", input, "--output", output);

        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData("missing-current-frame")]
    [InlineData("unsigned-low")]
    [InlineData("unsigned-high")]
    [InlineData("signed-high")]
    [InlineData("leader-high")]
    [InlineData("leader-phase-low")]
    [InlineData("leader-phase-high")]
    public async Task Author_battle_context_rejects_incomplete_or_out_of_range_event_state(string mutation)
    {
        JsonObject document = CreateAuthorDocument();
        JsonObject item = document["snapshot"]!["world"]!["eventObjects"]![0]!.AsObject();
        if (mutation == "missing-current-frame") item.Remove("currentFrame");
        else if (mutation == "unsigned-low") item["x"] = -1;
        else if (mutation == "unsigned-high") item["direction"] = 65536;
        else if (mutation == "signed-high") item["state"] = 32768;
        else if (mutation == "leader-high") document["snapshot"]!["world"]!["leader"]!["x"] = 32768;
        else document["snapshot"]!["world"]!["leader"]!["phase"] = mutation == "leader-phase-low" ? -1 : 4;
        File.WriteAllText(input, document.ToJsonString());

        var result = await CliProcess.Run(root, "author-battle-context", "--input", input, "--output", output);

        Assert.Equal(1, result.ExitCode);
        Assert.NotEmpty(result.Stderr);
    }

    [Theory]
    [InlineData("category")]
    [InlineData("identity")]
    [InlineData("owners")]
    [InlineData("prepared")]
    [InlineData("resource-link")]
    [InlineData("09fe-operands")]
    [InlineData("resource-kind")]
    [InlineData("resource-owners")]
    [InlineData("remove-owner-51")]
    public async Task Author_battle_context_rejects_false_closure_or_resource_claims(string mutation)
    {
        JsonObject document = CreateAuthorDocument();
        JsonArray entries = document["closure"]!["entries"]!.AsArray();
        JsonNode battle = entries.Single(x => x!["id"]!.GetValue<string>() == "battle-team-18")!;
        if (mutation == "category") battle["category"] = "resource";
        else if (mutation == "identity") battle["identity"]!["enemyId"] = 98;
        else if (mutation == "owners") battle["ownerTickets"] = new JsonArray(53);
        else if (mutation == "prepared") battle["status"] = "prepared";
        else if (mutation == "resource-link") battle["resourceIds"] = new JsonArray("events");
        else if (mutation == "09fe-operands") battle["identity"]!["operands"]![2] = 1;
        else if (mutation == "resource-kind") document["resources"]![0]!["kind"] = "anything";
        else if (mutation == "resource-owners") document["resources"]![0]!["ownerTickets"] = new JsonArray(53);
        else battle["ownerTickets"] = new JsonArray(46, 47, 53);
        File.WriteAllText(input, document.ToJsonString());

        var result = await CliProcess.Run(root, "author-battle-context", "--input", input, "--output", output);

        Assert.Equal(1, result.ExitCode);
        Assert.NotEmpty(result.Stderr);
    }

    [Theory]
    [InlineData("duplicate-scene-id")]
    [InlineData("missing-selected-scene")]
    public async Task Author_battle_context_rejects_ambiguous_or_missing_selected_scene_entry(string mutation)
    {
        JsonObject document = CreateAuthorDocument();
        JsonArray entries = document["snapshot"]!["world"]!["sceneEntries"]!.AsArray();
        JsonArray sources = document["provenance"]!["fieldSources"]!.AsArray();
        if (mutation == "duplicate-scene-id")
        {
            entries.Add(entries[0]!.DeepClone());
            foreach (JsonNode? original in sources.Where(x => x!["path"]!.GetValue<string>().StartsWith("snapshot.world.sceneEntries[0]", StringComparison.Ordinal)).ToArray())
            {
                JsonObject copy = original!.DeepClone().AsObject();
                copy["path"] = copy["path"]!.GetValue<string>().Replace("sceneEntries[0]", "sceneEntries[1]", StringComparison.Ordinal);
                sources.Add(copy);
            }
        }
        else
        {
            entries[0]!["sceneId"] = 3;
            foreach (JsonNode? source in sources.Where(x => x!["path"]!.GetValue<string>().StartsWith("snapshot.world.sceneEntries[0]", StringComparison.Ordinal)))
                source!["appliesTo"] = "scene3";
        }
        File.WriteAllText(input, document.ToJsonString());

        var result = await CliProcess.Run(root, "author-battle-context", "--input", input, "--output", output);

        Assert.Equal(1, result.ExitCode);
        Assert.NotEmpty(result.Stderr);
        Assert.False(Directory.Exists(output));
    }

    [Theory]
    [InlineData("--output", "out")]
    [InlineData("--bad", "out")]
    [InlineData("", "out")]
    [InlineData("input.json", "--input")]
    [InlineData("input.json", "--bad")]
    [InlineData("input.json", "")]
    public async Task Author_battle_context_classifies_missing_option_or_blank_values_as_argv_errors(string inputValue, string outputValue)
    {
        var result = await CliProcess.Run(root, "author-battle-context", "--input", inputValue, "--output", outputValue);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Unsupported arguments for author-battle-context", result.Stderr);
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

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
