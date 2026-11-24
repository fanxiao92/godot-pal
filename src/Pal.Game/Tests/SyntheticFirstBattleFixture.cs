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

    internal SyntheticFirstBattleFixture(NativeProfileFixture staticFixture, string staticRoot, string staticProfile, bool lost,
        bool hiddenCurrentGlyph = false, bool currentHookPositive = false, bool forceCurrentDying = false,
        bool seedDyingGlyph = false, bool liveZeroDying = false, bool futureDyingWrite = false,
        bool secondOrderDyingWrite = false, bool randomDyingBranch = false, bool titleDyingGlyph = false,
        int currentHookPortrait = 0, bool currentHookPortraitOnly = false, bool forceCurrentFriendDeath = false,
        bool zeroDisplay = false)
    {
        if (titleDyingGlyph && (!futureDyingWrite || randomDyingBranch || secondOrderDyingWrite))
            throw new ArgumentException("The title variant requires only the future-write graph.", nameof(titleDyingGlyph));
        if (randomDyingBranch && (!futureDyingWrite || secondOrderDyingWrite))
            throw new ArgumentException("The random variant requires only the future-write graph.", nameof(randomDyingBranch));
        if (secondOrderDyingWrite && !futureDyingWrite)
            throw new ArgumentException("The second-order variant requires the future-write fixture.", nameof(secondOrderDyingWrite));
        if (zeroDisplay && (lost || !currentHookPositive || hiddenCurrentGlyph || forceCurrentDying || futureDyingWrite
            || currentHookPortrait is not (0 or 7) || currentHookPortraitOnly))
            throw new ArgumentException("The zero-display variant requires the clean current-hook graph with portrait0 or7.", nameof(zeroDisplay));
        if (forceCurrentFriendDeath && (!forceCurrentDying || !secondOrderDyingWrite || randomDyingBranch || titleDyingGlyph))
            throw new ArgumentException("The controlled FriendDeath variant requires only the second-order Dying graph.", nameof(forceCurrentFriendDeath));
        if (forceCurrentDying && (!currentHookPositive || hiddenCurrentGlyph || lost))
            throw new ArgumentException("The controlled Dying variant requires only the clean Won current-hook graph.");
        if (seedDyingGlyph && !forceCurrentDying || liveZeroDying && !seedDyingGlyph)
            throw new ArgumentException("The seed-glyph variant requires the controlled Dying graph; live zero requires that seed variant.");
        if (futureDyingWrite && (!forceCurrentDying || seedDyingGlyph || liveZeroDying))
            throw new ArgumentException("The future-write variant requires the clean controlled Dying graph without the seed or live-zero variants.");
        bool currentHookGraph = hiddenCurrentGlyph || currentHookPositive;
        if (currentHookPortrait is < 0 or > ushort.MaxValue || currentHookPortrait != 0 && !currentHookGraph)
            throw new ArgumentException("A current-hook portrait requires a WORD ID and the current-hook graph.", nameof(currentHookPortrait));
        if (currentHookPortraitOnly && (!currentHookGraph || currentHookPortrait == 0 || futureDyingWrite))
            throw new ArgumentException("The portrait-only variant requires a nonzero portrait in the live current-hook graph.", nameof(currentHookPortraitOnly));
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
                ("Static/Palette.tres", "Leaves/Palette.tres"),
                ("Dialogue/Font.tres", "Leaves/Dialogue/Font.tres"),
                ("Dialogue/Language.zh-Hans.tres", "Leaves/Dialogue/Language.zh-Hans.tres"),
                ("Dialogue/Language.en.tres", "Leaves/Dialogue/Language.en.tres"),
                ("Dialogue/Prompt-00.tres", "Leaves/Dialogue/Prompt-00.tres"),
                ("Dialogue/Prompt-01.tres", "Leaves/Dialogue/Prompt-01.tres"),
                ("Dialogue/Prompt-02.tres", "Leaves/Dialogue/Prompt-02.tres") })
                File.Copy(Path.Combine(samples, relative), Path.Combine(root, name));
            if (secondOrderDyingWrite || randomDyingBranch || titleDyingGlyph)
            {
                // Z is exclusive to the second-order root; do not damage the existing FD
                // dialogue (which already uses V) or change the committed authored font.
                // #213 added 'Z' to the authored font itself, so add only when absent: a
                // duplicate codepoint fails the font check and masks each variant's own fault
                // shape (the glyph fault negatives duplicate 'G' deliberately at line ~793).
                string path = Path.Combine(root, "Leaves/Dialogue/Font.tres");
                var font = ResourceLoader.Load<PalDialogueFont>(path, cacheMode: ResourceLoader.CacheMode.IgnoreDeep)!;
                if (!font.Glyphs.Any(glyph => glyph is not null && glyph.Codepoint == 90))
                    font.Glyphs.Add(new PalDialogueGlyph { Codepoint = 90, Width = 8, Height = 15, RowBytes = 1, Bitmap = new byte[15] });
                NativeProfileFixture.SaveResource(font, path);
            }
            // Ruling (b): the copied languages become rotation-free local copies — the authored
            // texts carry color codes that drive palette rotation, and the synthetic scene
            // frames carry no indices/alpha planes for re-tinting. Plain text only (fixture
            // files; the production chain, real leaves and sprite formats are untouched).
            foreach (string languageId in new[] { "zh-Hans", "en" })
            {
                string path = Path.Combine(root, $"Leaves/Dialogue/Language.{languageId}.tres");
                var language = (PalDialogueLanguage)ResourceLoader.Load(path, cacheMode: ResourceLoader.CacheMode.IgnoreDeep)!;
                foreach (var message in language.Messages)
                    foreach (var entry in message.Entries)
                        if (entry.Kind == PalDialogueMessageEntry.EntryKind.Text)
                            entry.Text = new string(entry.Text.Where(character =>
                                character is not ('-' or '\'' or '@' or '"' or '$' or '~' or '\\' or '(' or ')')).ToArray());
                if (currentHookGraph)
                    language.Messages.Add(new PalDialogueMessageRange
                    {
                        StartMessageId = 65, EndMessageId = 65,
                        Entries = [new PalDialogueMessageEntry { Text = currentHookPositive
                            ? (languageId == "en" ? "FIGHT" : "战") : (languageId == "en" ? "Q" : "戊") }],
                    });
                if (seedDyingGlyph || futureDyingWrite)
                    language.Messages.Add(new PalDialogueMessageRange
                    {
                        StartMessageId = 66, EndMessageId = 66,
                        Entries = titleDyingGlyph
                            ? [new PalDialogueMessageEntry { Text = "Z:" }, new PalDialogueMessageEntry { Text = "U" }]
                            : [new PalDialogueMessageEntry { Text = "U" }],
                    });
                if (secondOrderDyingWrite || randomDyingBranch)
                    language.Messages.Add(new PalDialogueMessageRange
                    {
                        StartMessageId = 67, EndMessageId = 67,
                        Entries = [new PalDialogueMessageEntry { Text = "Z" }],
                    });
                language.SourceIdentity += " Synthetic rotation-free copy for #53 fixture; plain text only.";
                NativeProfileFixture.SaveResource(language, path);
            }
            // Background and enemy leaves are relabeled copies: the closure cross-checks the
            // declared FBP24 battlefield identity and the ABC bitmap number 97. The number and
            // attack-effect leaves are authored stand-ins (their Source dicts say so); binding
            // them keeps the battle view's damage digits/effect frames drawable on GPU (#54 F2).
            {
                var background = (PalDialogueIndexedImage)ResourceLoader.Load(Path.Combine(samples, "Visual/Background.tres"),
                    cacheMode: ResourceLoader.CacheMode.IgnoreDeep)!;
                background.SourceIdentity = "FBP24";
                NativeProfileFixture.SaveResource(background, Path.Combine(root, "Leaves/Background.tres"));
                var enemy = (PalSprite)ResourceLoader.Load(Path.Combine(samples, "Visual/Enemy.tres"),
                    cacheMode: ResourceLoader.CacheMode.IgnoreDeep)!;
                enemy.SpriteId = 97;
                enemy.Source["identity"] = "ABC97";
                NativeProfileFixture.SaveResource(enemy, Path.Combine(root, "Leaves/Enemy.tres"));
                File.Copy(Path.Combine(samples, "Visual/Numbers.tres"), Path.Combine(root, "Leaves/Numbers.tres"));
                File.Copy(Path.Combine(samples, "Visual/AttackEffect.tres"), Path.Combine(root, "Leaves/AttackEffect.tres"));
            }
            // Player battle sprites: two copies of the authored leader leaf, each carrying its
            // declared F0/F1 source identity (the closure cross-check reads Source["identity"]).
            foreach (string roleId in new[] { "F0", "F1" })
            {
                var sprite = (PalSprite)ResourceLoader.Load(Path.Combine(samples, "Static/Leader.tres"),
                    cacheMode: ResourceLoader.CacheMode.IgnoreDeep)!;
                sprite.Source["identity"] = roleId;
                NativeProfileFixture.SaveResource(sprite, Path.Combine(root, $"Leaves/{roleId}.tres"));
            }
            // 1. Synthetic modern events at the frozen addresses (fast battle, hooks, Won
            //    continuation with dynamic verbs, Lost reload boundary, dynamic autos).
            string eventsUri = WriteEvents(lost, currentHookGraph, seedDyingGlyph, liveZeroDying, futureDyingWrite, secondOrderDyingWrite, randomDyingBranch, currentHookPortrait, currentHookPortraitOnly, zeroDisplay);
            // 2. Synthetic battle data (fast enemies) through the production numeric prepare.
            string numbers = WriteNumbers(lost, currentHookGraph, forceCurrentDying, forceCurrentFriendDeath);
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
            // The record's stored bytes predate the prepared-status rewrite; re-serialize.
            byte[] contextBytes = System.Text.Encoding.UTF8.GetBytes(context.Document.ToJsonString());
            string contextHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(contextBytes));
            byte[] indexBytes = System.Text.Encoding.UTF8.GetBytes(
                "{\"formatVersion\":1,\"profile\":\"first-mandatory-battle-pre-trigger-v1\",\"contextFile\":\"battle-context.json\",\"contextSha256\":\"" + contextHash + "\"}");
            File.WriteAllBytes(Path.Combine(authored, "battle-context.json"), contextBytes);
            File.WriteAllBytes(Path.Combine(authored, "index.json"), indexBytes);
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
                ["playerSpriteUris"] = new JsonArray(Localize("Leaves/F0.tres"), Localize("Leaves/F1.tres")),
                ["enemySpriteId"] = 97, ["enemyArchiveEntry"] = "ABC97",
                ["roleSpriteIds"] = new JsonArray("F0", "F1"),
                ["battlefieldId"] = 24, ["battlefieldArchiveEntry"] = "FBP24",
                ["includesLayoutAndEffectFrames"] = true,
            };
            if (zeroDisplay && currentHookPortrait == 7)
            {
                string portraitUri = Localize("Leaves/Dialogue/Portrait-007.tres");
                NativeProfileFixture.SaveResource(new PalDialogueIndexedImage
                {
                    SourceIdentity = "synthetic-zero-display-portrait-7", Width = 1, Height = 1,
                    PaletteIndices = [4], Alpha = [255]
                }, portraitUri);
                binding["portraits"] = new JsonArray(new JsonObject { ["id"] = 7, ["uri"] = portraitUri });
            }
            string bindingPath = Path.Combine(input, "binding.json");
            File.WriteAllText(bindingPath, binding.ToJsonString());
            ProfileUri = Editor.PrepareBattleContext.Prepare(authored, Path.Combine(root, "Closure"),
                staticRoot, staticProfile, bindingPath);
        }
        catch { Dispose(); throw; }
    }

    private string WriteEvents(bool lost, bool hiddenCurrentGlyph, bool seedDyingGlyph, bool liveZeroDying, bool futureDyingWrite,
        bool secondOrderDyingWrite, bool randomDyingBranch, int currentHookPortrait, bool currentHookPortraitOnly, bool zeroDisplay)
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
        if (hiddenCurrentGlyph)
        {
            Put(20, "setDialog", 60, currentHookPortrait, 0, 0, ("box", "upper"), ("portraitId", currentHookPortrait), ("color", 0), ("rngWord", 0));
            if (currentHookPortraitOnly) Put(21, "end", 0, 0, 0, 0, ("mode", "keep"));
            else Put(21, "showDialog", 65535, 65, 0, 0, ("messageId", 65), ("descriptionLineWord", 0));
            Put(22, "end", 0, 0, 0, 0, ("mode", "keep"));
            int currentDying = liveZeroDying || futureDyingWrite ? 0 : 20;
            Put(50, "setObjectScript", 144, 0, currentDying, 1, ("objectId", 0), ("value", currentDying), ("slotIndex", 1));
            Put(51, "end", 0, 0, 0, 0, ("mode", "keep"));
        }
        if (futureDyingWrite)
        {
            // Future-only U path, not the unchanged declared A96E random/healing hook.
            Put(24, "setDialog", 60, 0, 0, 0, ("box", "upper"), ("portraitId", 0), ("color", 0), ("rngWord", 0));
            Put(25, "showDialog", 65535, 66, 0, 0, ("messageId", 66), ("descriptionLineWord", 0));
            if (secondOrderDyingWrite)
            {
                // Y writes the other Name's FriendDeath root; Z is preflighted, not run.
                Put(26, "setObjectScript", 144, 37, 32, 0, ("objectId", 37), ("value", 32), ("slotIndex", 0));
                Put(27, "end", 0, 0, 0, 0, ("mode", "keep"));
                Put(32, "setDialog", 60, 0, 0, 0, ("box", "upper"), ("portraitId", 0), ("color", 0), ("rngWord", 0));
                Put(33, "showDialog", 65535, 67, 0, 0, ("messageId", 67), ("descriptionLineWord", 0));
                Put(34, "end", 0, 0, 0, 0, ("mode", "keep"));
            }
            else Put(26, "end", 0, 0, 0, 0, ("mode", "keep"));
        }
        if (randomDyingBranch)
        {
            // Both structural branches require their resources before the real Start.
            Put(24, "randomJump", 6, 50, 32, 0, ("rate", 50), ("to", "L0020"));
            Put(25, "setDialog", 60, 0, 0, 0, ("box", "upper"), ("portraitId", 0), ("color", 0), ("rngWord", 0));
            Put(26, "showDialog", 65535, 66, 0, 0, ("messageId", 66), ("descriptionLineWord", 0));
            Put(27, "end", 0, 0, 0, 0, ("mode", "keep"));
            Put(32, "setDialog", 60, 0, 0, 0, ("box", "upper"), ("portraitId", 0), ("color", 0), ("rngWord", 0));
            Put(33, "showDialog", 65535, 67, 0, 0, ("messageId", 67), ("descriptionLineWord", 0));
            Put(34, "end", 0, 0, 0, 0, ("mode", "keep"));
        }
        Put(0x09C3, "setDialog", 60, 0, 0, 0, ("box", "upper"), ("portraitId", 0), ("color", 0), ("rngWord", 0));
        Put(0x09C4, "showDialog", 65535, 1, 0, 0, ("messageId", 1), ("descriptionLineWord", 0));
        for (int address = 0x09C5; address < 0x09FE; address++)
            Put(address, "face", 15, 0, 0, 0, ("direction", 0), ("frame", 0));
        if (!lost)
        {
            // Party-wide signed WORD -3. Synthetic coverage, not a retail edit.
            Put(0x09C5, "changeHpMp", 0x001D, 1, 65533, 0, ("scope", 1), ("amount", -3));
        }
        if (futureDyingWrite)
            Put(0x09C6, "setObjectScript", 144, 0, 24, 1, ("objectId", 0), ("value", 24), ("slotIndex", 1));
        Put(0x09FE, "startBattle", 7, 0x0012, 0xA073, 0, ("enemyTeamId", 18), ("lossTarget", $"L{0xA073:X4}"), ("isBoss", true), ("fleeTarget", null));
        Put(0x09FF, "showDialog", 65535, 2, 0, 0, ("messageId", 2), ("descriptionLineWord", 0));
        for (int address = 0x0A00; address < 0x0A32; address++)
            Put(address, "face", 15, 0, 0, 0, ("direction", 0), ("frame", 0));
        Put(0x0A32, "end", 0, 0, 0, 0, ("mode", "keep"));
        Put(0x0A36, "face", 15, 1, 0, 0, ("direction", 1), ("frame", 0));
        Put(0x0A37, "end", 0, 0, 0, 0, ("mode", "keep"));
        Put(0x0A3B, "face", 15, 0, 0, 0, ("direction", 0), ("frame", 0));
        Put(0x0A3C, "end", 0, 0, 0, 0, ("mode", "keep"));
        Put(0xA073, "setDialog", 61, 0, 0, 0, ("box", "lower"), ("portraitId", 0), ("color", 0), ("rngWord", 0));
        Put(0xA074, "showDialog", 65535, 3, 0, 0, ("messageId", 3), ("descriptionLineWord", 0));
        Put(0xA075, "fadeToRed", 0x4f, 0, 0, 0);
        Put(0xA076, "requestReload", 0x4e, 0, 0, 0);
        Hook(Put, 0xA96E, 4);
        Hook(Put, 0xA9B5, 17);
        if (seedDyingGlyph)
        {
            // Artificial deterministic seed-only U path. The frozen declaration and default
            // random/healing hooks stay intact outside this explicit controlled variant.
            Put(0xA96E, "setDialog", 60, 0, 0, 0, ("box", "upper"), ("portraitId", 0), ("color", 0), ("rngWord", 0));
            Put(0xA96F, "showDialog", 65535, 66, 0, 0, ("messageId", 66), ("descriptionLineWord", 0));
            Put(0xA970, "end", 0, 0, 0, 0, ("mode", "keep"));
        }
        if (zeroDisplay)
        {
            // Explicit literal fixture, not requirements-derived authoring. Preserve physical
            // successors, frozen09FE, team18 and both strict hook declarations/ranges.
            foreach (int address in new[] { 21, 0x09C4, 0x09FF, 0xA074, 0xA970, 0xA973, 0xA975, 0xA9B7, 0xA9BA, 0xA9BC })
                Put(address, "setDialog", 60, 0, 0, 0, ("box", "upper"), ("portraitId", 0), ("color", 0), ("rngWord", 0));
            if (currentHookPortrait == 7)
                Put(0xA96F, "setDialog", 60, 7, 0, 0, ("box", "upper"), ("portraitId", 7), ("color", 0), ("rngWord", 0));
        }
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
        if (hiddenCurrentGlyph)
            file["entries"]!.AsArray().Add(new JsonObject
            {
                ["id"] = "setter", ["owner"] = new JsonObject { ["type"] = "eventObject", ["id"] = 28 },
                ["slot"] = "wTriggerScript", ["kind"] = "trigger", ["target"] = "L0032", ["sourceAddress"] = 50,
            });
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

    private const int HookThirdMessage = 16; // singleton block in the authored languages

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
        put(start + 7, "showDialog", 65535, HookThirdMessage, 0, 0, ("messageId", HookThirdMessage), ("descriptionLineWord", 0));
        put(start + 8, "changeHp", 27, 0, 9999, 0, ("scope", 0), ("amount", 9999));
        put(start + 9, "end", 0, 0, 0, 0, ("mode", "keep"));
    }

    private string WriteNumbers(bool lost, bool hiddenCurrentGlyph, bool forceCurrentDying, bool forceCurrentFriendDeath)
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
        // Retail-parity settlement values (team18 retail binding: experience 26/cash 48 per
        // enemy) so the Won flow presents the battle.c 1022-1031 exp/cash settlement box.
        enemy["experience"] = 26;
        enemy["cash"] = 48;
        var second = (JsonNode)enemy.DeepClone();
        second["id"] = 8;
        team["enemies"]!.AsArray().Add(second);
        var players = data["players"]!.AsArray();
        players[0]!["friendDeathScript"] = 0xA9B5;
        if (hiddenCurrentGlyph) players[0]!["nameObjectId"] = 37;
        var roleOne = (JsonNode)players[0]!.DeepClone();
        if (hiddenCurrentGlyph) roleOne["nameObjectId"] = 0;
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
        // Won-only controlled threshold: two 52-XP victories cannot trigger random
        // formal/hidden stat growth, keeping independent HP/MP closure literals exact.
        // Lost retains the original table and hook/enemy conditions are unchanged.
        if (!lost)
            growth[0]!["levelUpExperience"] = new JsonArray(Enumerable.Range(0, 100).Select(_ => (JsonNode?)1000).ToArray());
        var growthOne = (JsonNode)growth[0]!.DeepClone();
        growthOne["roleId"] = 1;
        growth.Add(growthOne);
        if (forceCurrentDying)
        {
            // Independent authored control, not retail values. The five optional role panel
            // words remain absent; combat uses the explicit player panels, as in the Story control.
            for (int i = 0; i < 2; i++)
            {
                roles[i]!["hp"] = i == 0 ? 100 : 60;
                players[i]!["hp"] = i == 0 ? 100 : 60;
                roles[i]!["maxHp"] = i == 0 ? 200 : 240;
                players[i]!["maxHp"] = i == 0 ? 200 : 240;
                roles[i]!["mp"] = i == 0 ? 20 : 10;
                roles[i]!["maxMp"] = i == 0 ? 50 : 240;
                roles[i]!["coveredByRoleId"] = i == 0 ? 2 : 0;
                players[i]!["coveredByRoleId"] = i == 0 ? 2 : 0;
                players[i]!["attack"] = i == 0 ? 40 : 20;
                players[i]!["defense"] = i == 0 ? 20 : 0;
                players[i]!["dexterity"] = 100;
                var foe = team["enemies"]!.AsArray()[i]!;
                foe["hp"] = i == 0 ? 43 : 1;
                foe["level"] = 0;
                // Optional second real enemy strike: 57 -> 13 (Dying), then 13 -> 0
                // (the surviving coverer's FriendDeath). Default closure remains unchanged.
                foe["attack"] = i == 0 || forceCurrentFriendDeath ? 8 : 0;
                foe["defense"] = 0;
                foe["dexterity"] = i == 0 || forceCurrentFriendDeath ? 500 : 0;
                foe["experience"] = 0;
                foe["cash"] = 0;
            }
        }
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
            FormatVersion = 2,
            SourceIdentity = "Synthetic first-battle closure visual over authored leaves; not retail data.",
            Players =
            [
                new PalBattleVisualPlayer { RoleId = 0, PlayerSpriteUri = Localize("Leaves/F0.tres"),
                    PlayerPosition = new Vector2I(200, 176),
                    AttackEffectSpriteUri = Localize("Leaves/AttackEffect.tres"), AttackEffectIndex = 0 },
                new PalBattleVisualPlayer { RoleId = 1, PlayerSpriteUri = Localize("Leaves/F1.tres"),
                    PlayerPosition = new Vector2I(256, 152),
                    AttackEffectSpriteUri = Localize("Leaves/AttackEffect.tres"), AttackEffectIndex = 0 }
            ],
            PaletteUri = Localize("Leaves/Palette.tres"),
            NumberSpriteUri = Localize("Leaves/Numbers.tres"),
        };
        var team = new PalBattleVisualTeam
        {
            TeamId = 18, TargetId = -1,
            BackgroundUri = Localize("Leaves/Background.tres"),
            EnemyPosition = new Vector2I(int.MinValue, int.MinValue),
            EnemySlots =
            [
                new PalBattleVisualEnemySlot { TargetId = 7, EnemyPosition = new Vector2I(70, 130), EnemySpriteUri = Localize("Leaves/Enemy.tres"),
                    IdleFrames = 1, IdleAnimSpeed = 2, MagicFrames = 1, AttackFrames = 1, ActWaitFrames = 1 },
                new PalBattleVisualEnemySlot { TargetId = 8, EnemyPosition = new Vector2I(140, 106), EnemySpriteUri = Localize("Leaves/Enemy.tres"),
                    IdleFrames = 1, IdleAnimSpeed = 2, MagicFrames = 1, AttackFrames = 1, ActWaitFrames = 1 },
            ],
        };
        visual.Teams = [team];
        foreach (string uri in new[] { visual.Players[0].PlayerSpriteUri, visual.Players[1].PlayerSpriteUri, visual.PaletteUri, team.BackgroundUri,
            visual.Players[0].AttackEffectSpriteUri!, visual.Players[1].AttackEffectSpriteUri!, visual.NumberSpriteUri }.Concat(team.EnemySlots.Select(slot => slot.EnemySpriteUri)).Distinct(StringComparer.Ordinal))
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
