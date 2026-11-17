using System;
using System.IO;
using System.Linq;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Godot;
using Pal.Game.Editor;

namespace Pal.Game.Tests;

internal sealed class AuthoredStoryFixture : IDisposable
{
    private readonly string input = Path.Combine(Path.GetTempPath(), "pal-story-c-" + Guid.NewGuid().ToString("N"));
    internal string Root { get; } = "res://Assets/RuntimeTests/story-c-" + Guid.NewGuid().ToString("N");
    internal string InputDirectory => input;
    internal string StoryUri { get; }
    internal string VisualUri { get; }
    private string? numericInput;
    internal AuthoredStoryFixture(string scenario, string? missingPresentation = null, int? enemyHp = null,
        bool removeDualAttack = false, int? playerHp = null, int? partyHpMpPrefixAmount = null,
        bool singlePlayerTwoEnemies = false, bool heterogeneousEnemies = false, bool threePlayer = false,
        bool zeroHpPuppetEntry = false, bool mixedEmptySlots = false)
    {
        if (heterogeneousEnemies && scenario != "PartyWin")
            throw new ArgumentException("Heterogeneous enemy art requires the two-slot PartyWin author.", nameof(heterogeneousEnemies));
        if (singlePlayerTwoEnemies && scenario != "PartyWin")
            throw new ArgumentException("The single-player/two-enemy derivation requires PartyWin.", nameof(singlePlayerTwoEnemies));
        if (threePlayer && scenario != "PartyWin")
            throw new ArgumentException("The three-player derivation requires the two-slot PartyWin author.", nameof(threePlayer));
        if (zeroHpPuppetEntry && scenario != "PartyLoss")
            throw new ArgumentException("The zero-HP puppet entry derivation requires PartyLoss.", nameof(zeroHpPuppetEntry));
        if (mixedEmptySlots && scenario != "PartyWin")
            throw new ArgumentException("The mixed empty-slot derivation requires PartyWin.", nameof(mixedEmptySlots));
        if (mixedEmptySlots && (heterogeneousEnemies || singlePlayerTwoEnemies || zeroHpPuppetEntry))
            throw new ArgumentException("The mixed empty-slot derivation is its own five-slot author shape.", nameof(mixedEmptySlots));
        if (partyHpMpPrefixAmount is { } amount)
        {
            if (scenario != "PartyWin")
                throw new ArgumentException("The HP/MP prefix is supported only for PartyWin.", nameof(scenario));
            if (amount is < short.MinValue or > short.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(partyHpMpPrefixAmount), amount, "The HP/MP prefix amount must be a SHORT.");
        }
        string repository = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "../.."));
        string source = Path.Combine(repository, "samples/authored-battle", scenario);
        Directory.CreateDirectory(input);
        string output = ProjectSettings.GlobalizePath(Root);
        Directory.CreateDirectory(Path.Combine(output, "Static/Scenes"));
        try
        {
            // A scenario may own its static arena (new/empty targets only); shared scenarios keep
            // the common authored arena and its profile.
            string scenarioStatic = Path.Combine(repository, "samples/authored-battle", scenario, "Static");
            bool ownedStatic = Directory.Exists(scenarioStatic);
            File.Copy(ownedStatic ? Path.Combine(scenarioStatic, "Scenes/Scene.tscn")
                : ProjectSettings.GlobalizePath("res://Samples/AuthoredBattle/Static/Scenes/Scene.tscn"), Path.Combine(output, "Static/Scenes/Scene.tscn"));
            if (ownedStatic) File.Copy(Path.Combine(scenarioStatic, "Exploration.tres"), Path.Combine(output, "Static/Exploration.tres"));
            string numbers = Path.Combine(source, "Numbers");
            if (enemyHp is { } hp || playerHp is { } || removeDualAttack || singlePlayerTwoEnemies || threePlayer || zeroHpPuppetEntry || mixedEmptySlots)
            {
                // Owned sibling, not an extra directory in strict Story input. Original author bytes stay read-only.
                numericInput = Path.Combine(Path.GetTempPath(), "pal-story-numeric-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(numericInput);
                foreach (string path in Directory.GetFiles(numbers)) File.Copy(path, Path.Combine(numericInput, Path.GetFileName(path)));
                string dataPath = Path.Combine(numericInput, "battle-data.json");
                var data = JsonNode.Parse(File.ReadAllText(dataPath, System.Text.Encoding.UTF8))!;
                var team = System.Linq.Enumerable.Single(data["teams"]!.AsArray(), t => (int)t!["teamId"]! == 18)!;
                if (enemyHp is { } enemyHpValue)
                {
                    var enemy = System.Linq.Enumerable.Single(team["enemies"]!.AsArray(), e => (int)e!["id"]! == 7)!;
                    enemy["hp"] = enemyHpValue;
                }
                if (playerHp is { } memberHp)
                {
                    // Both the role snapshot and its player must keep matching HP words.
                    data["roles"]![0]!["hp"] = memberHp;
                    data["players"]![0]!["hp"] = memberHp;
                }
                if (singlePlayerTwoEnemies)
                {
                    // TriggerBattleContent requires matching RoleId sets in all three sections.
                    foreach (string section in new[] { "roles", "players", "growth" })
                        data[section]!.AsArray().RemoveAt(1);
                }
                if (threePlayer)
                {
                    // #137 test-owned third member: role2 clones role1's authored numeric row so
                    // roles/players/growth keep matching RoleId sets in party order.
                    foreach (string section in new[] { "roles", "players", "growth" })
                    {
                        var third = data[section]![1]!.DeepClone();
                        third["roleId"] = 2;
                        data[section]!.AsArray().Add(third);
                    }
                }
                if (zeroHpPuppetEntry)
                {
                    // #139 test-owned dead entry: member 0 authors a zero HP word with the puppet
                    // status (kStatusPuppet, statuses slot 4) in both the role snapshot and its
                    // matching player; the formal battle start must revive it at 1 HP and clear
                    // the puppet (battle.c 1568-1583), never the preflight.
                    data["roles"]![0]!["hp"] = 0;
                    data["players"]![0]!["hp"] = 0;
                    data["roles"]![0]!["statuses"]![4] = 1;
                }
                if (mixedEmptySlots)
                {
                    // #141 (T13c) test-owned mixed runtime slots [empty, 7, empty, 7, empty]: the
                    // empty rows are #140's canonical all-zero isEmpty spelling; loaded neighbours
                    // stay duplicate id7 with distinct authored rewards.
                    JsonNode EmptyRow()
                    {
                        var row = team["enemies"]![0]!.DeepClone();
                        foreach (string field in new[] { "id", "level", "hp", "attack", "defense", "dexterity",
                            "physicalResistance", "turnStartScript", "readyScript", "battleEndScript", "magicId",
                            "dualMove", "attackEquivItem", "attackEquivItemRate", "experience", "cash" })
                            row[field] = 0;
                        row["statuses"] = new JsonArray(0, 0, 0, 0, 0, 0, 0, 0, 0);
                        row["poisons"] = new JsonArray(0);
                        row["isEmpty"] = true;
                        return row;
                    }
                    var first = team["enemies"]![0]!;
                    var second = team["enemies"]![1]!;
                    first["experience"] = 3; first["cash"] = 5;
                    second["experience"] = 3; second["cash"] = 5;
                    team["enemies"] = new JsonArray(EmptyRow(), first.DeepClone(), EmptyRow(), second.DeepClone(), EmptyRow());
                }
                if (removeDualAttack)
                {
                    foreach (string section in new[] { "roles", "players" })
                        data[section]![1]!["equipment"]![3] = 0;
                    data["roleEquipment"]![1]![3] = 0;
                }
                File.WriteAllText(dataPath, data.ToJsonString(), System.Text.Encoding.UTF8);
                numbers = numericInput;
            }
            string numeric = PrepareBattleData.Prepare(numbers, Path.Combine(output, "Numbers"));
            Directory.CreateDirectory(Path.Combine(input, "events"));
            foreach (string path in Directory.GetFiles(Path.Combine(source, "Story/events"))) File.Copy(path, Path.Combine(input, "events", Path.GetFileName(path)));
            var metadata = JsonNode.Parse(File.ReadAllText(Path.Combine(source, "Story/story-package.json")))!;
            if (singlePlayerTwoEnemies)
            {
                // The visual binding shrinks with the numeric party: one typed player binding
                // at the single-player reference column, never a leftover second role.
                metadata["visual"]!["players"]!.AsArray().RemoveAt(1);
                metadata["visual"]!["players"]![0]!["playerPosition"] = new JsonArray(240, 170);
                metadata["sourceIdentity"] = metadata["sourceIdentity"]!.GetValue<string>()
                    + " Test-owned one-player/two-enemy derivation; both duplicate id7 enemy slots retained, not retail data.";
            }
            if (threePlayer)
            {
                // The whole party switches to the reference g_rgPlayerPos[2] columns (battle.c:27-31);
                // role2 shares the authored player sprite by authored design, like the base pair.
                var players = metadata["visual"]!["players"]!.AsArray();
                players[0]!["playerPosition"] = new JsonArray(180, 180);
                players[1]!["playerPosition"] = new JsonArray(234, 170);
                var third = players[1]!.DeepClone();
                third["roleId"] = 2;
                third["playerPosition"] = new JsonArray(270, 146);
                players.Add(third);
                metadata["sourceIdentity"] = metadata["sourceIdentity"]!.GetValue<string>()
                    + " Test-owned three-player/two-enemy derivation; role2 clones role1's authored numeric row and shares the player sprite; not retail data.";
            }
            if (heterogeneousEnemies)
            {
                // Owned native leaves are inputs to formal Prepare, never edits of its output.
                // Match the existing synthetic 97/98 frame construction; Source is not authentication.
                for (int slot = 0; slot < 2; slot++)
                {
                    int id = 97 + slot, count = slot == 0 ? 3 : 4;
                    var sprite = new Pal.Game.Resources.PalSprite { SpriteId = id, FrameCount = count, DecodedByteLength = 200,
                        Source = new Godot.Collections.Dictionary { ["archive"] = "test-owned heterogeneous art, not authenticated", ["chunk"] = id } };
                    for (int frame = 0; frame < count; frame++)
                    {
                        int width = slot == 0 ? 2 : 3, height = slot == 0 ? 1 : (frame + 1) * 3;
                        sprite.Frames.Add(new Pal.Game.Resources.PalFrame { Index = frame, SourceOffset = 2 + frame * 4,
                            Width = width, Height = height,
                            Indices = System.Linq.Enumerable.Repeat((byte)(4 + slot), width * height).ToArray(),
                            Alpha = System.Linq.Enumerable.Repeat((byte)255, width * height).ToArray() });
                    }
                    string uri = Root + $"/Enemy{id}.tres";
                    NativeProfileFixture.SaveResource(sprite, uri);
                    var enemy = metadata["visual"]!["teams"]![0]!["enemies"]![slot]!;
                    enemy["enemySpriteUri"] = uri;
                    enemy["idleFrames"] = slot == 0 ? 1 : 2;
                    enemy["idleAnimSpeed"] = slot == 0 ? 0 : 3;
                    enemy["magicFrames"] = slot == 0 ? 1 : 0;
                    enemy["attackFrames"] = slot == 0 ? 1 : 2;
                    enemy["actWaitFrames"] = slot == 0 ? 0 : 2;
                }
                metadata["sourceIdentity"] = metadata["sourceIdentity"]!.GetValue<string>()
                    + " Test-owned heterogeneous 97/98 native art and per-slot metadata; not retail data.";
            }
            if (mixedEmptySlots)
            {
                // The visual binding keeps one overlay entry per runtime slot: empty entries
                // declare only targetId/enemyPosition; loaded entries keep the full group.
                var enemies = metadata["visual"]!["teams"]![0]!["enemies"]!.AsArray();
                var loaded0 = enemies[0]!.DeepClone();
                var loaded1 = enemies[1]!.DeepClone();
                loaded0!["enemyPosition"] = new JsonArray(100, 120);
                loaded1!["enemyPosition"] = new JsonArray(156, 88);
                metadata["visual"]!["teams"]![0]!["enemies"] = new JsonArray(
                    new JsonObject { ["targetId"] = 0, ["enemyPosition"] = new JsonArray(72, 136) },
                    loaded0, new JsonObject { ["targetId"] = 0, ["enemyPosition"] = new JsonArray(128, 104) },
                    loaded1, new JsonObject { ["targetId"] = 0, ["enemyPosition"] = new JsonArray(184, 72) });
                metadata["sourceIdentity"] = metadata["sourceIdentity"]!.GetValue<string>()
                    + " Test-owned mixed empty-slot five-slot derivation (T13c #141); empty runtime slots are authored, not retail data.";
            }
            if (partyHpMpPrefixAmount is { } prefixAmount) AddPartyHpMpPrefix(prefixAmount, metadata);
            if (missingPresentation == "effect")
                foreach (var player in metadata["visual"]!["players"]!.AsArray())
                {
                    RemoveRequired(player!.AsObject(), "attackEffectSpriteUri");
                    RemoveRequired(player.AsObject(), "attackEffectIndex");
                }
            if (missingPresentation == "number") RemoveRequired(metadata["visual"]!.AsObject(), "numberSpriteUri");
            if (missingPresentation == "idle") RemoveRequired(metadata["visual"]!["teams"]![0]!["enemies"]![0]!.AsObject(), "idleAnimSpeed");
            if (enemyHp is { } hp2)
                metadata["sourceIdentity"] = metadata["sourceIdentity"]!.GetValue<string>()
                    + $" Test-owned numeric copy: team18/enemy7 hp={hp2} only; not retail data.";            metadata["staticResources"] = Root + "/Static";
            if (ownedStatic) metadata["staticProfile"] = Root + "/Static/Exploration.tres";
            metadata["battleContentUri"] = numeric;
            File.WriteAllText(Path.Combine(input, "story-package.json"), metadata.ToJsonString());
            var prepared = PrepareStoryPackage.Prepare(input, Path.Combine(output, "Story"));
            StoryUri = prepared.StoryUri; VisualUri = prepared.VisualUri;
        }
        catch { Dispose(); throw; }
    }
    private static void RemoveRequired(JsonObject owner, string field)
    {
        if (!owner.Remove(field)) throw new InvalidOperationException($"Fixture mutation requires existing field: {field}");
    }
    private void AddPartyHpMpPrefix(int amount, JsonNode metadata)
    {
        string arenaPath = Path.Combine(input, "events/arena.json");
        string indexPath = Path.Combine(input, "events/index.json");
        var arena = JsonNode.Parse(File.ReadAllText(arenaPath))!;
        var index = JsonNode.Parse(File.ReadAllText(indexPath))!;
        // Only this owned authored shape is relocatable. In particular raw0's opaque WORD8
        // is not an address: never replace arbitrary WORD values or infer new references.
        var expectedArena = JsonNode.Parse("""
            {"formatVersion":1,"profile":"faithful","fileId":"arena",
             "entries":[{"id":"challenge","owner":{"type":"eventObject","id":1},"slot":"wTriggerScript","kind":"trigger","target":"L0001","sourceAddress":1}],
             "commands":[
              {"label":"L0000","op":"raw","next":"L0001","source":{"index":0,"opcode":134,"operands":[0,65535,8]}},
              {"label":"L0001","op":"setDialog","next":"L0002","source":{"index":1,"opcode":60,"operands":[0,0,0]},"box":"upper","portraitId":0,"color":0,"rngWord":0},
              {"label":"L0002","op":"showDialog","next":"L0003","source":{"index":2,"opcode":65535,"operands":[1,0,0]},"messageId":1,"descriptionLineWord":0},
              {"label":"L0003","op":"startBattle","next":"L0004","source":{"index":3,"opcode":7,"operands":[18,7,0]},"enemyTeamId":18,"lossTarget":"L0007","isBoss":true,"fleeTarget":null},
              {"label":"L0004","op":"setDialog","next":"L0005","source":{"index":4,"opcode":60,"operands":[0,0,0]},"box":"upper","portraitId":0,"color":0,"rngWord":0},
              {"label":"L0005","op":"showDialog","next":"L0006","source":{"index":5,"opcode":65535,"operands":[2,0,0]},"messageId":2,"descriptionLineWord":0},
              {"label":"L0006","op":"end","next":"L0007","source":{"index":6,"opcode":0,"operands":[0,0,0]},"mode":"keep"},
              {"label":"L0007","op":"setDialog","next":"L0008","source":{"index":7,"opcode":60,"operands":[0,0,0]},"box":"upper","portraitId":0,"color":0,"rngWord":0},
              {"label":"L0008","op":"showDialog","next":"L0009","source":{"index":8,"opcode":65535,"operands":[3,0,0]},"messageId":3,"descriptionLineWord":0},
              {"label":"L0009","op":"end","next":null,"source":{"index":9,"opcode":0,"operands":[0,0,0]},"mode":"keep"}]}
            """);
        var expectedIndex = JsonNode.Parse("""
            {"formatVersion":1,"profile":"faithful","source":{"archive":"SSS.MKF","chunk":4,"byteOrder":"little","instructionCount":10,"byteLength":80,"sha256":"0ad42c6e02646a69b76934eb478f4a917ac11e81436f61c35cd91f65e61f7ca8"},"files":["arena"]}
            """);
        if (!JsonNode.DeepEquals(arena, expectedArena) || !JsonNode.DeepEquals(index, expectedIndex)
            || metadata["entryFileId"]!.GetValue<string>() != "arena" || metadata["entryId"]!.GetValue<string>() != "challenge"
            || metadata["ownerId"]!.GetValue<int>() != 1 || metadata["entryAddress"]!.GetValue<int>() != 1)
            throw new InvalidDataException("PartyWin HP/MP prefix requires the known ten-instruction authored arena shape.");
        var commands = arena["commands"]!.AsArray();
        for (int address = 1; address < 10; address++)
        {
            var command = commands[address]!;
            command["label"] = $"L{address + 1:D4}";
            command["source"]!["index"] = address + 1;
            command["next"] = address == 9 ? null : $"L{address + 2:D4}";
        }
        commands[3]!["lossTarget"] = "L0008";
        commands[3]!["source"]!["operands"]![1] = 8;
        commands.Insert(1, new JsonObject { ["label"] = "L0001", ["op"] = "changeHpMp", ["next"] = "L0002",
            ["scope"] = 1, ["amount"] = amount, ["source"] = new JsonObject { ["index"] = 1, ["opcode"] = 0x001D,
                ["operands"] = new JsonArray(1, (int)unchecked((ushort)amount), 0) } });
        var bytes = new byte[88];
        for (int address = 0; address < commands.Count; address++)
        {
            var source = commands[address]!["source"]!;
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(address * 8), checked((ushort)source["opcode"]!.GetValue<int>()));
            for (int word = 0; word < 3; word++)
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(address * 8 + 2 + word * 2), checked((ushort)source["operands"]![word]!.GetValue<int>()));
        }
        index["source"]!["instructionCount"] = 11;
        index["source"]!["byteLength"] = bytes.Length;
        index["source"]!["sha256"] = Convert.ToHexStringLower(SHA256.HashData(bytes));
        metadata["sourceIdentity"] = metadata["sourceIdentity"]!.GetValue<string>()
            + $" Test-owned PartyWin changeHpMp prefix: scope=1, amount={amount}; original authored stream derived, not retail SSS.MKF.";
        File.WriteAllText(arenaPath, arena.ToJsonString());
        File.WriteAllText(indexPath, index.ToJsonString());
    }

    internal string[] Snapshot() => System.Linq.Enumerable.Order(
        Directory.GetFiles(input, "*", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(ProjectSettings.GlobalizePath(Root), "*", SearchOption.AllDirectories)), StringComparer.Ordinal)
        .Select(path => path + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))))
        .ToArray();

    public void Dispose()
    {
        if (Directory.Exists(input)) Directory.Delete(input, true);
        if (numericInput is not null && Directory.Exists(numericInput)) Directory.Delete(numericInput, true);
        string output = ProjectSettings.GlobalizePath(Root);
        if (Directory.Exists(output)) Directory.Delete(output, true);
    }
}
