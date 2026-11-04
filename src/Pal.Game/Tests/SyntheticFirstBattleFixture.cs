using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Godot;
using Pal.Game.Resources;

namespace Pal.Game.Tests;

/// <summary>Synthetic full-closure fixture (committable; no retail data): builds every binding
/// resource through the production prepares and publishes a bound Profile.tres the same way the
/// real closure does. Fast enemy values keep the continuous Won/Lost flows inside the batch
/// budget; identities stay exactly the frozen first-battle closure.</summary>
internal sealed class SyntheticFirstBattleFixture : IDisposable
{
    private readonly string input = Path.Combine(Path.GetTempPath(), "pal-synth-closure-" + Guid.NewGuid().ToString("N"));
    private static readonly string RootName = "SyntheticFirstBattle-" + Guid.NewGuid().ToString("N");
    private readonly string root = ProjectSettings.GlobalizePath("res://Assets/RuntimeTests/" + RootName);
    private static string RootUri => "res://Assets/RuntimeTests/" + RootName;
    internal string ProfileUri { get; }

    internal SyntheticFirstBattleFixture(NativeProfileFixture staticFixture, string staticRoot, string staticProfile, bool lost)
    {
        Directory.CreateDirectory(input);
        Directory.CreateDirectory(root);
        try
        {
            // Single approach: the committed authored leaves are self-contained (embedded
            // textures; only Script refs outside), so the fixture copies them into its own
            // Assets root; every binding seed then lives under res://Assets as required.
            string samples = ProjectSettings.GlobalizePath("res://Samples/AuthoredBattle");
            Directory.CreateDirectory(Path.Combine(root, "Leaves/Dialogue"));
            foreach (var (relative, name) in new[] {
                ("Static/Leader.tres", "Leaves/Leader.tres"), ("Static/Palette.tres", "Leaves/Palette.tres"),
                ("Visual/Background.tres", "Leaves/Background.tres"), ("Visual/Enemy.tres", "Leaves/Enemy.tres"),
                ("Dialogue/Font.tres", "Leaves/Dialogue/Font.tres"),
                ("Dialogue/Language.zh-Hans.tres", "Leaves/Dialogue/Language.zh-Hans.tres"),
                ("Dialogue/Language.en.tres", "Leaves/Dialogue/Language.en.tres"),
                ("Dialogue/Prompt-00.tres", "Leaves/Dialogue/Prompt-00.tres"),
                ("Dialogue/Prompt-01.tres", "Leaves/Dialogue/Prompt-01.tres"),
                ("Dialogue/Prompt-02.tres", "Leaves/Dialogue/Prompt-02.tres") })
                File.Copy(Path.Combine(samples, relative), Path.Combine(root, name));
            // 1. Synthetic modern events at the frozen addresses (fast battle, hooks, Won
            //    continuation with dynamic verbs, Lost reload boundary, dynamic autos).
            string eventsUri = WriteEvents(lost);
            // 2. Synthetic battle data (fast enemies) through the production numeric prepare.
            string numbers = WriteNumbers(lost);
            // 3. Synthetic visual binding team18's two enemy slots over the authored leaves.
            string visualUri = WriteVisual(staticFixture);
            // 4. Synthetic global event-object table (one record the events address).
            string globalUri = WriteGlobalObjects();
            // 5. Authored battle context with prepared statuses, through the strict author CLI
            //    shape (direct canonical validation is the same production seam used here).
            // The authored context starts from the committed Core fixture document (shared
            // source identity, no retail values) and only adjusts the frozen start geometry.
            var context = SyntheticBattleContext.Valid();
            context.Document["snapshot"]!["world"]!["leader"]!["x"] = 1264;
            context.Document["snapshot"]!["world"]!["leader"]!["y"] = 248;
            context.Document["snapshot"]!["world"]!["searchProof"]!["sampleIndex"] = 1;
            foreach (var node in context.Document["closure"]!["entries"]!.AsArray()) node!["status"] = "prepared";
            string authored = Path.Combine(input, "authored");
            Directory.CreateDirectory(authored);
            File.WriteAllBytes(Path.Combine(authored, "battle-context.json"), context.ContextBytes);
            File.WriteAllBytes(Path.Combine(authored, "index.json"), context.IndexBytes);
            // 6. Binding JSON -> production prepare with -BattleResources semantics.
            var binding = new JsonObject
            {
                ["formatVersion"] = 1,
                ["eventsIndex"] = eventsUri,
                ["battleContentUri"] = numbers,
                ["dialogueDirectory"] = Localize("Leaves/Dialogue"),
                ["visualUri"] = visualUri,
                ["globalObjectsUri"] = globalUri,
                ["portraits"] = new JsonArray(),
                ["playerSpriteUris"] = new JsonArray(Localize("Leaves/Leader.tres"), Localize("Leaves/Leader.tres")),
                ["enemySpriteId"] = 97, ["enemyArchiveEntry"] = "ABC97",
                ["roleSpriteIds"] = new JsonArray("F0", "F1"),
                ["battlefieldId"] = 24, ["battlefieldArchiveEntry"] = "FBP24",
                ["includesLayoutAndEffectFrames"] = true,
            };
            string bindingPath = Path.Combine(input, "binding.json");
            File.WriteAllText(bindingPath, binding.ToJsonString());
            ProfileUri = Editor.PrepareBattleContext.Prepare(authored, Path.Combine(root, "Closure"),
                staticRoot, staticProfile, bindingPath);
        }
        catch { Dispose(); throw; }
    }

    private string WriteEvents(bool lost)
    {
        string directory = Path.Combine(root, "Events");
        Directory.CreateDirectory(directory);
        const int count = 0xA9D2;
        var rows = new System.Collections.Generic.Dictionary<int, JsonObject>();
        void Put(int address, string op, int opcode, int a, int b, int c, params (string Key, JsonNode? Value)[] fields)
        {
            var command = new JsonObject { ["label"] = $"L{address:X4}", ["op"] = op,
                ["next"] = address + 1 < count ? $"L{address + 1:X4}" : null };
            foreach (var (key, value) in fields) command[key] = value;
            command["source"] = new JsonObject { ["index"] = address, ["opcode"] = opcode, ["operands"] = new JsonArray(a, b, c) };
            rows[address] = command;
        }
        Put(0x09C3, "setDialog", 60, 0, 0, 0, ("box", "upper"), ("portraitId", 0), ("color", 0), ("rngWord", 0));
        Put(0x09C4, "showDialog", 65535, 10, 0, 0, ("messageId", 10), ("descriptionLineWord", 0));
        for (int address = 0x09C5; address < 0x09FE; address++)
            Put(address, "face", 15, 0, 0, 0, ("direction", 0), ("frame", 0));
        Put(0x09FE, "startBattle", 7, 0x0012, 0xA073, 0, ("teamId", 18), ("lossTarget", $"L{0xA073:X4}"), ("isBoss", true), ("fleeTarget", null));
        Put(0x09FF, "showDialog", 65535, 11, 0, 0, ("messageId", 11), ("descriptionLineWord", 0));
        for (int address = 0x0A00; address < 0x0A32; address++)
            Put(address, "face", 15, 0, 0, 0, ("direction", 0), ("frame", 0));
        Put(0x0A32, "end", 0, 0, 0, 0, ("mode", "keep"));
        Put(0x0A36, "face", 15, 1, 0, 0, ("direction", 1), ("frame", 0));
        Put(0x0A37, "end", 0, 0, 0, 0, ("mode", "keep"));
        Put(0x0A3B, "face", 15, 0, 0, 0, ("direction", 0), ("frame", 0));
        Put(0x0A3C, "end", 0, 0, 0, 0, ("mode", "keep"));
        Put(0xA073, "setDialog", 61, 0, 0, 0, ("box", "lower"), ("portraitId", 0), ("color", 0), ("rngWord", 0));
        Put(0xA074, "showDialog", 65535, 12, 0, 0, ("messageId", 12), ("descriptionLineWord", 0));
        Put(0xA075, "fadeToRed", 0x4f, 0, 0, 0);
        Put(0xA076, "requestReload", 0x4e, 0, 0, 0);
        Hook(Put, 0xA96E, 13);
        Hook(Put, 0xA9B5, 17);
        var commands = new JsonArray();
        for (int address = 0; address < count; address++)
        {
            if (rows.TryGetValue(address, out var command)) commands.Add(command);
            else commands.Add(new JsonObject { ["label"] = $"L{address:X4}", ["op"] = "raw",
                ["next"] = address + 1 < count ? $"L{address + 1:X4}" : null,
                ["source"] = new JsonObject { ["index"] = address, ["opcode"] = 134, ["operands"] = new JsonArray(0, 65535, 8) } });
        }
        var file = new JsonObject
        {
            ["formatVersion"] = 1, ["profile"] = "faithful", ["fileId"] = "story",
            ["entries"] = new JsonArray(new JsonObject { ["id"] = "owner28",
                ["owner"] = new JsonObject { ["type"] = "eventObject", ["id"] = 28 },
                ["slot"] = "wTriggerScript", ["kind"] = "trigger", ["target"] = "L09C3", ["sourceAddress"] = 0x09C3 }),
            ["commands"] = commands,
        };
        File.WriteAllText(Path.Combine(directory, "story.json"), file.ToJsonString());
        File.WriteAllText(Path.Combine(directory, "index.json"), new JsonObject
        {
            ["formatVersion"] = 1, ["profile"] = "faithful",
            ["source"] = new JsonObject { ["archive"] = "SSS.MKF", ["chunk"] = 4, ["byteOrder"] = "little",
                ["instructionCount"] = count, ["byteLength"] = count * 8,
                ["sha256"] = new string('e', 64) },
            ["files"] = new JsonArray("story"),
        }.ToJsonString());
        return RootUri + "/Events/index.json";
    }

    private delegate void PutCommand(int address, string op, int opcode, int a, int b, int c, params (string Key, JsonNode? Value)[] fields);

    private static void Hook(PutCommand put, int start, int firstMessage)
    {
        put(start, "randomJump", 6, 75, start + 6, 0, ("rate", 75), ("to", $"L{start + 6:X4}"));
        put(start + 1, "setDialog", 60, 0, 0, 0, ("box", "upper"), ("portraitId", 0), ("color", 0), ("rngWord", 0));
        put(start + 2, "showDialog", 65535, firstMessage, 0, 0, ("messageId", firstMessage), ("descriptionLineWord", 0));
        put(start + 3, "end", 0, 0, 0, 0, ("mode", "keep"));
        put(start + 4, "setDialog", 61, 0, 0, 0, ("box", "lower"), ("portraitId", 0), ("color", 0), ("rngWord", 0));
        put(start + 5, "showDialog", 65535, firstMessage + 1, 0, 0, ("messageId", firstMessage + 1), ("descriptionLineWord", 0));
        put(start + 6, "setDialog", 60, 0, 0, 0, ("box", "upper"), ("portraitId", 0), ("color", 0), ("rngWord", 0));
        put(start + 7, "showDialog", 65535, firstMessage + 2, 0, 0, ("messageId", firstMessage + 2), ("descriptionLineWord", 0));
        put(start + 8, "changeHp", 27, 0, 9999, 0, ("scope", 0), ("amount", 9999));
        put(start + 9, "end", 0, 0, 0, 0, ("mode", "keep"));
    }

    private string WriteNumbers(bool lost)
    {
        string repository = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "../.."));
        string source = Path.Combine(repository, "samples/authored-battle/Win/Numbers");
        string numbers = Path.Combine(input, "numbers");
        Directory.CreateDirectory(numbers);
        foreach (string path in Directory.GetFiles(source)) File.Copy(path, Path.Combine(numbers, Path.GetFileName(path)));
        var data = JsonNode.Parse(File.ReadAllText(Path.Combine(numbers, "battle-data.json"), System.Text.Encoding.UTF8))!;
        // Fast two-slot team18 (distinct battle target ids) with frozen hook addresses.
        var team = data["teams"]!.AsArray().Single(t => (int)t!["teamId"]! == 18)!;
        var enemy = team["enemies"]!.AsArray()[0]!;
        enemy["hp"] = lost ? 500 : 1;
        var second = (JsonNode)enemy.DeepClone();
        second["id"] = 8;
        team["enemies"]!.AsArray().Add(second);
        var players = data["players"]!.AsArray();
        players[0]!["friendDeathScript"] = 0xA9B5;
        var roleOne = (JsonNode)players[0]!.DeepClone();
        roleOne["roleId"] = 1; roleOne["hp"] = lost ? 1 : 100; roleOne["maxHp"] = lost ? 1 : 100;
        roleOne["dyingScript"] = 0xA96E; roleOne["friendDeathScript"] = 0;
        roleOne["equipment"] = new JsonArray(0, 0, 0, 0, 0, 0); roleOne["coveredByRoleId"] = 0;
        players.Add(roleOne);
        var roles = data["roles"]!.AsArray();
        var roleSnapshot = (JsonNode)roles[0]!.DeepClone();
        roleSnapshot["roleId"] = 1; roleSnapshot["hp"] = lost ? 1 : 100; roleSnapshot["maxHp"] = lost ? 1 : 100;
        roleSnapshot["mp"] = 5; roleSnapshot["maxMp"] = 5; roleSnapshot["level"] = 2;
        roleSnapshot["coveredByRoleId"] = 0; roleSnapshot["equipment"] = new JsonArray(0, 0, 0, 0, 0, 0);
        roleSnapshot["magics"] = new JsonArray(Enumerable.Range(0, 32).Select(_ => (JsonNode?)0).ToArray());
        roles.Add(roleSnapshot);
        var growth = data["growth"]!.AsArray();
        var growthOne = (JsonNode)growth[0]!.DeepClone();
        growthOne["roleId"] = 1;
        growth.Add(growthOne);
        // Six-role equipment grid with the dual-attack object the closure pins.
        var grid = new JsonArray();
        foreach (int role in Enumerable.Range(0, 6)) grid.Add(new JsonArray(0, 0, 0, 0, 0, 0));
        data["roleEquipment"] = grid;
        data["equipments"] = new JsonArray(new JsonObject
        {
            ["objectId"] = 170,
            ["effects"] = BuildEffects(),
        });
        File.WriteAllText(Path.Combine(numbers, "battle-data.json"), data.ToJsonString());
        return Editor.PrepareBattleData.Prepare(numbers, Path.Combine(root, "Numbers"));
    }

    private string WriteVisual(NativeProfileFixture staticFixture)
    {
        var visual = new PalBattleVisualProfile
        {
            FormatVersion = 1,
            SourceIdentity = "Synthetic first-battle closure visual over authored leaves; not retail data.",
            RoleId = 0,
            PlayerSpriteUri = Localize("Leaves/Leader.tres"),
            PlayerPosition = new Vector2I(200, 176),
            PaletteUri = Localize("Leaves/Palette.tres"),
        };
        var team = new PalBattleVisualTeam
        {
            TeamId = 18, TargetId = -1,
            BackgroundUri = Localize("Leaves/Background.tres"),
            EnemySpriteUri = Localize("Leaves/Enemy.tres"),
            EnemyPosition = new Vector2I(int.MinValue, int.MinValue),
            EnemySlots =
            [
                new PalBattleVisualEnemySlot { TargetId = 7, EnemyPosition = new Vector2I(70, 130) },
                new PalBattleVisualEnemySlot { TargetId = 8, EnemyPosition = new Vector2I(140, 106) },
            ],
            IdleFrames = 1, MagicFrames = 1, AttackFrames = 1, ActWaitFrames = 1, IdleAnimSpeed = 2,
        };
        visual.Teams = [team];
        foreach (string uri in new[] { visual.PlayerSpriteUri, visual.PaletteUri, team.BackgroundUri, team.EnemySpriteUri })
            visual.ResourceSha256[uri] = Godot.FileAccess.GetSha256(uri);
        string path = Localize("Visual.tres");
        NativeProfileFixture.SaveResource(visual, Path.Combine(root, "Visual.tres"));
        return path;
    }

    private string WriteGlobalObjects()
    {
        var table = new PalGlobalEventObjects
        {
            FormatVersion = 1,
            SourceIdentity = "Synthetic global event-object table; not retail data.",
            Objects = [new PalGlobalEventObject { OwnerId = 61, X = 100, Y = 100, SpriteId = 21 }],
        };
        string path = Localize("GlobalEventObjects.tres");
        NativeProfileFixture.SaveResource(table, Path.Combine(root, "GlobalEventObjects.tres"));
        return path;
    }

    private string Localize(string relative) => RootUri + "/" + relative;

    public void Dispose()
    {
        if (Directory.Exists(input)) Directory.Delete(input, true);
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    private static JsonArray BuildEffects()
    {
        var effects = new JsonArray();
        foreach (int role in Enumerable.Range(0, 6))
        {
            var statuses = new JsonArray(0, 0, 0, 0, 0, 0, 0, 0, 0);
            if (role == 1) statuses[8] = 32760;
            effects.Add(new JsonObject
            {
                ["attack"] = 0, ["defense"] = 0, ["dexterity"] = 0, ["battleSprite"] = 0,
                ["statuses"] = statuses,
            });
        }
        return effects;
    }
}
