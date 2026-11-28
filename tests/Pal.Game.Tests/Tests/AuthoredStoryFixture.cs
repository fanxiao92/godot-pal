using System;
using System.IO;
using System.Linq;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Godot;
using Pal.Game.Editor;
using Pal.Core.Battle;

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
        bool zeroHpPuppetEntry = false, bool mixedEmptySlots = false, bool attackAllMember0 = false,
        bool fleeEscape = false, bool menuEquip = false, bool menuItems = false, bool menuMagic = false,
        bool menuItemsBitmaps = false, bool menuItemsBitmapsMissingBinding = false,
        bool menuShop = false, bool menuShopBitmapsMissingBinding = false, bool menuShopEmptyBag = false,
        bool controlFlow = false)
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
        if (attackAllMember0 && (!mixedEmptySlots || scenario != "PartyWin"))
            throw new ArgumentException("The attack-all derivation is defined on the mixed empty-slot PartyWin shape.", nameof(attackAllMember0));
        if (fleeEscape && (scenario != "LostReload" || playerHp is null))
            throw new ArgumentException("The flee-escape derivation is defined on the LostReload author with an explicit playerHp word.", nameof(fleeEscape));
        if (menuEquip && scenario != "PartyEquip")
            throw new ArgumentException("The menu-equip context derivation requires the PartyEquip author.", nameof(menuEquip));
        if (menuItems && scenario != "PartyWin")
            throw new ArgumentException("The menu-items context derivation requires the PartyWin author.", nameof(menuItems));
        if (menuMagic && scenario != "PartyWin")
            throw new ArgumentException("The menu-magic context derivation requires the PartyWin author.", nameof(menuMagic));
        if (menuMagic && menuItems)
            throw new ArgumentException("The menu-magic and menu-items derivations are separate owned story graphs.", nameof(menuMagic));
        if (menuItemsBitmaps && (!menuItems || scenario != "PartyWin" || menuMagic))
            throw new ArgumentException("The item-bitmaps derivation requires the PartyWin menu-items author.", nameof(menuItemsBitmaps));
        if (menuItemsBitmapsMissingBinding && (!menuItems || scenario != "PartyWin" || menuMagic))
            throw new ArgumentException("The missing item-bitmap binding derivation requires the PartyWin menu-items author.", nameof(menuItemsBitmapsMissingBinding));
        if (menuItemsBitmaps && menuItemsBitmapsMissingBinding)
            throw new ArgumentException("The item-bitmaps derivations are separate owned story graphs.");
        if (menuShop && (scenario != "PartyEquip" || !menuEquip))
            throw new ArgumentException("The shop derivation requires the PartyEquip menu-equip author (buy -> equip -> battle continuity).", nameof(menuShop));
        if (menuShopBitmapsMissingBinding && !menuShop)
            throw new ArgumentException("The missing shop-bitmap binding derivation requires the shop author.", nameof(menuShopBitmapsMissingBinding));
        if (menuShopEmptyBag && !menuShop)
            throw new ArgumentException("The empty-bag shop derivation requires the shop author (the sell menu of an empty bag).", nameof(menuShopEmptyBag));
        if (controlFlow && scenario != "Midbattle")
            throw new ArgumentException("The T03 control-flow derivation requires the Midbattle author.", nameof(controlFlow));
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
                : GameProjectMount.GameSamplePath("Samples/AuthoredBattle/Static/Scenes/Scene.tscn"), Path.Combine(output, "Static/Scenes/Scene.tscn"));
            if (ownedStatic) File.Copy(Path.Combine(scenarioStatic, "Exploration.tres"), Path.Combine(output, "Static/Exploration.tres"));
            string numbers = Path.Combine(source, "Numbers");
            if (enemyHp is { } hp || playerHp is { } || removeDualAttack || singlePlayerTwoEnemies || threePlayer || zeroHpPuppetEntry || mixedEmptySlots || attackAllMember0 || fleeEscape || menuEquip || menuItems || menuMagic || menuShop || controlFlow)
            {
                // Owned sibling, not an extra directory in strict Story input. Original author bytes stay read-only.
                numericInput = Path.Combine(Path.GetTempPath(), "pal-story-numeric-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(numericInput);
                foreach (string path in Directory.GetFiles(numbers)) File.Copy(path, Path.Combine(numericInput, Path.GetFileName(path)));
                string dataPath = Path.Combine(numericInput, "battle-data.json");
                var data = JsonNode.Parse(File.ReadAllText(dataPath, System.Text.Encoding.UTF8))!;
                var team = System.Linq.Enumerable.Single(data["teams"]!.AsArray(), t => (int)t!["teamId"]! == 18)!;
                if (controlFlow)
                {
                    // #234 (T03 production chain): the dying role hook starts with the appended
                    // counted root goto (owner = the hook's RoleId participant word) and the
                    // second enemy slot owns an empty battle-end script, so the settled event
                    // continuation's 0xFFFF call runs with the battle-owned last caller word.
                    data["players"]![1]!["dyingScript"] = 78;
                    team["enemies"]![1]!["battleEndScript"] = 77;
                };
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
                if (fleeEscape)
                {
                    // #145 (T17): fleeRate 999 stays above the authored dex500 defense sum (524)
                    // under every draw, so the non-boss battle's escape always succeeds; the role
                    // row keeps its authored panels (no declared-panel flowback here).
                    data["players"]![0]!["fleeRate"] = 999;
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
                if (attackAllMember0)
                {
                    // #143 (T15) test-owned authored capability: member 0 carries the authored
                    // attack-all word (PLAYERROLES rgwAttackAll) — the transportable all-target
                    // declaration through the formal native chain; member 1 keeps single targets.
                    // The numeric words are hand-checked against the reference math: the all-target
                    // strike applies base 42 (attack 40 vs effective defense (0+6)*4 = 24,
                    // resistance 1) divided by the running 1,2,4,8,16 division (fight.c 3686-3735),
                    // so hp 21 dies to both 42/1 and 42/2 exactly, with or without the critical x3;
                    // and dexterity 999 (the reference cap) x [0.9,1.1] always outruns member 1's
                    // 100 x [0.9,1.1] (fight.c 1462-1578), so the frozen hit set is deterministically
                    // {1,3} and the battle settles once in round 1.
                    data["players"]![0]!["attackAll"] = true;
                    data["players"]![0]!["dexterity"] = 999;
                    foreach (int slot in new[] { 1, 3 }) team["enemies"]![slot]!["hp"] = 21;
                }
                if (menuEquip)
                {
                    // #214 (M6-B T02): the test-owned equip context — initial bag rows, a sixth
                    // head object 199 (role0 attack 3/defense 2, role1 defense 4) and per-object
                    // part/eligibility bindings for every worn object. 210 stays role1-only as
                    // the reference's flagged-but-role0-ineligible rejection sentinel.
                    JsonNode EquipRow(int attack, int defense) => new JsonObject
                    {
                        ["attack"] = attack, ["defense"] = defense, ["dexterity"] = 0, ["battleSprite"] = 0,
                        ["statuses"] = new JsonArray(0, 0, 0, 0, 0, 0, 0, 0, 0)
                    };
                    JsonNode ZeroRow() => EquipRow(0, 0);
                    data["equipments"]!.AsArray().Add(new JsonObject
                    {
                        ["objectId"] = 199,
                        ["effects"] = new JsonArray(EquipRow(3, 2), EquipRow(0, 4),
                            ZeroRow(), ZeroRow(), ZeroRow(), ZeroRow())
                    });
                    data["inventory"] = new JsonArray(
                        new JsonObject { ["itemId"] = 197, ["amount"] = 2 },
                        new JsonObject { ["itemId"] = 199, ["amount"] = 1 },
                        new JsonObject { ["itemId"] = 210, ["amount"] = 1 });
                    data["equipMenu"] = new JsonArray(
                        new JsonObject { ["objectId"] = 197, ["part"] = 0, ["equipableBy"] = new JsonArray(0, 1) },
                        new JsonObject { ["objectId"] = 199, ["part"] = 0, ["equipableBy"] = new JsonArray(0, 1) },
                        new JsonObject { ["objectId"] = 210, ["part"] = 2, ["equipableBy"] = new JsonArray(1) },
                        new JsonObject { ["objectId"] = 170, ["part"] = 3, ["equipableBy"] = new JsonArray(0, 1) },
                        new JsonObject { ["objectId"] = 237, ["part"] = 4, ["equipableBy"] = new JsonArray(0, 1) },
                        new JsonObject { ["objectId"] = 248, ["part"] = 5, ["equipableBy"] = new JsonArray(0, 1) });
                }
                if (menuItems)
                {
                    // #215 (M6-B T03): the test-owned item-use context — usable bag rows and
                    // per-object consuming/apply-to-all bindings. 300 heals one member +30 with a
                    // real dialogue line, 301 heals the party +10, 302 marks the script failed;
                    // all three are consuming so the success flag decides every removal. #218
                    // adds the BALL bitmap words: chunks 37/41 draw on the item pages, 302 keeps
                    // the reference's empty-chunk word 0 (no picture).
                    data["inventory"] = new JsonArray(
                        new JsonObject { ["itemId"] = 300, ["amount"] = 2 },
                        new JsonObject { ["itemId"] = 301, ["amount"] = 1 },
                        new JsonObject { ["itemId"] = 302, ["amount"] = 1 });
                    data["useMenu"] = new JsonArray(
                        new JsonObject { ["objectId"] = 300, ["consuming"] = true, ["applyToAll"] = false, ["bitmap"] = 37 },
                        new JsonObject { ["objectId"] = 301, ["consuming"] = true, ["applyToAll"] = true, ["bitmap"] = 41 },
                        new JsonObject { ["objectId"] = 302, ["consuming"] = true, ["applyToAll"] = false, ["bitmap"] = 0 });
                }
                if (menuMagic)
                {
                    // #216 (M6-B T04): the test-owned magic context — role0 learns 310..313 in
                    // slot order and per-object cost/flag bindings. 310 heals one member +30 with
                    // a real dialogue line, 311 heals the party +10 straight from the list with
                    // the caller word 0, 312 is battle-only (listed, never confirmable outside)
                    // and 313's on-success marks the script failed, so its cast never deducts MP.
                    var learned = data["roles"]![0]!["magics"]!.AsArray();
                    learned[0] = 310; learned[1] = 311; learned[2] = 312; learned[3] = 313;
                    data["magicMenu"] = new JsonArray(
                        new JsonObject { ["objectId"] = 310, ["costMp"] = 5, ["applyToAll"] = false, ["usableOutsideBattle"] = true },
                        new JsonObject { ["objectId"] = 311, ["costMp"] = 4, ["applyToAll"] = true, ["usableOutsideBattle"] = true },
                        new JsonObject { ["objectId"] = 312, ["costMp"] = 3, ["applyToAll"] = false, ["usableOutsideBattle"] = false },
                        new JsonObject { ["objectId"] = 313, ["costMp"] = 6, ["applyToAll"] = false, ["usableOutsideBattle"] = true });
                }
                if (menuShop)
                {
                    // #217 (M6-B T05): the test-owned shop context — the explicit cash seed, a
                    // usable heal 300 (consumed through its on-use entry appended below), priced
                    // shop objects (300/120 sellable, 199/250 sellable, 320/999 unaffordable and
                    // unsellable, 197/40 sellable, 210/90 owned-but-never-sellable) and store row
                    // 5 [300, 199, 320]. Independent numbers: cash 600 buys 300+199 (600 -> 230)
                    // and leaves 320 unreachable; selling one 197 credits 20.
                    data["cash"] = 600;
                    data["useMenu"] = new JsonArray(
                        new JsonObject { ["objectId"] = 300, ["consuming"] = true, ["applyToAll"] = false, ["bitmap"] = 37 });
                    data["shopObjects"] = new JsonArray(
                        new JsonObject { ["objectId"] = 300, ["price"] = 120, ["sellable"] = true, ["bitmap"] = 37 },
                        new JsonObject { ["objectId"] = 199, ["price"] = 250, ["sellable"] = true, ["bitmap"] = 0 },
                        new JsonObject { ["objectId"] = 320, ["price"] = 999, ["sellable"] = false, ["bitmap"] = 41 },
                        new JsonObject { ["objectId"] = 197, ["price"] = 40, ["sellable"] = true, ["bitmap"] = 0 },
                        new JsonObject { ["objectId"] = 210, ["price"] = 90, ["sellable"] = false, ["bitmap"] = 0 });
                    data["stores"] = new JsonArray(
                        new JsonObject { ["storeId"] = 5, ["items"] = new JsonArray(300, 199, 320) });
                    if (menuShopEmptyBag)
                    {
                        // Review fix (shop-sell-empty-list-view-crash): itemmenu.c counts an
                        // empty compressed inventory without rejecting, so uigame.c's sell menu
                        // of an empty bag is the empty list — the derivation drops every bag
                        // row while keeping the shop context and its cash seed, and the empty
                        // sell page must stay presentable in the formal host.
                        data["inventory"] = new JsonArray();
                    }
                }
                File.WriteAllText(dataPath, data.ToJsonString(), System.Text.Encoding.UTF8);
                numbers = numericInput;
            }
            string numeric = PrepareBattleData.Prepare(numbers, Path.Combine(output, "Numbers"));
            Directory.CreateDirectory(Path.Combine(input, "events"));
            foreach (string path in Directory.GetFiles(Path.Combine(source, "Story/events"))) File.Copy(path, Path.Combine(input, "events", Path.GetFileName(path)));
            if (fleeEscape) AddFleeEscapeBranch();
            if (menuItems) AddItemUseEntries();
            if (menuMagic) AddMagicUseEntries();
            if (menuShop) AddShopEntries();
            if (controlFlow) AddControlFlowStream();
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
            if (attackAllMember0)
                metadata["sourceIdentity"] = metadata["sourceIdentity"]!.GetValue<string>()
                    + " Test-owned attack-all word, member-0 speed word 999 and loaded-slot HP 21 words (T15 #143); authored capability/numeric declarations, not retail data.";
            if (fleeEscape)
                metadata["sourceIdentity"] = metadata["sourceIdentity"]!.GetValue<string>()
                    + " Test-owned non-boss flee derivation (T17 #145): startBattle operand2 re-points to an appended flee branch and the player declares fleeRate 999; authored stream derived, not retail data.";
            if (menuEquip)
                metadata["sourceIdentity"] = metadata["sourceIdentity"]!.GetValue<string>()
                    + " Test-owned menu equip context (T02 #214): bag rows, head object 199 and part/eligibility bindings; authored inputs, not retail data.";
            if (controlFlow)
                metadata["sourceIdentity"] = metadata["sourceIdentity"]!.GetValue<string>()
                    + " Test-owned T03 production-chain derivation (#234): the selected entry calls an appended nested display, the dying hook starts with a counted root goto and the settled 0xFFFF call owns the battle-end last caller; authored inputs, not retail data.";
            if (menuItems)
                metadata["sourceIdentity"] = metadata["sourceIdentity"]!.GetValue<string>()
                    + " Test-owned menu item-use context (T03 #215): usable bag rows, consuming/apply-to-all bindings and item.wScriptOnUse entries; authored inputs, not retail data.";
            if (menuMagic)
                metadata["sourceIdentity"] = metadata["sourceIdentity"]!.GetValue<string>()
                    + " Test-owned menu magic context (T04 #216): learned magic slots, cost/flag bindings and magic.wScriptOnUse/wScriptOnSuccess entries; authored inputs, not retail data.";
            if (menuShop)
                metadata["sourceIdentity"] = metadata["sourceIdentity"]!.GetValue<string>()
                    + " Test-owned shop context (T05 #217): explicit cash seed, priced shop objects, store row 5 and the showBuyMenu/showSellMenu arena commands; authored inputs, not retail data.";
            if (menuItemsBitmaps || menuItemsBitmapsMissingBinding)
            {
                // #218 (M6-B T03b): test-owned BALL item-bitmap sprites — chunk 37 (object 300)
                // is a 4x3 bitmap and chunk 41 (object 301) a 2x2 one, authored with the ball
                // chain's literal Source identity; object 302 keeps the empty-chunk word 0.
                // The missing-binding derivation saves the sprites but omits the metadata
                // bindings, so the strict story boundary must reject the nonzero bitmap word.
                foreach (var (chunk, width, height) in new[] { (37, 4, 3), (41, 2, 2) })
                {
                    var sprite = new Pal.Game.Resources.PalSprite { SpriteId = chunk, FrameCount = 1,
                        DecodedByteLength = 6 + width * height,
                        Source = new Godot.Collections.Dictionary { ["archive"] = "ball.mkf", ["chunk"] = chunk,
                            ["encoding"] = "uncompressed PAL RLE bitmap",
                            ["frameLayout"] = "single RLE bitmap: optional 0x00000002 header, WORD width/height, RLE runs" } };
                    sprite.Frames.Add(new Pal.Game.Resources.PalFrame { Index = 0, SourceOffset = 6,
                        Width = width, Height = height,
                        Indices = Enumerable.Range(0, width * height).Select(pixel => (byte)(chunk + pixel % 5)).ToArray(),
                        Alpha = Enumerable.Repeat((byte)255, width * height).ToArray() });
                    NativeProfileFixture.SaveResource(sprite, Root + $"/ItemBitmap-{chunk}.tres");
                }
                if (menuItemsBitmaps)
                    metadata["itemBitmaps"] = new JsonArray(
                        new JsonObject { ["objectId"] = 300, ["uri"] = Root + "/ItemBitmap-37.tres" },
                        new JsonObject { ["objectId"] = 301, ["uri"] = Root + "/ItemBitmap-41.tres" });
                metadata["sourceIdentity"] = metadata["sourceIdentity"]!.GetValue<string>()
                    + " Test-owned BALL item-bitmap sprites (T03b #218): chunks 37/41 as authored test art, not retail ball.mkf data.";
            }
            if (menuShop)
            {
                // #217 (T05): the shop pages consume the same prepared BALL chain — chunk 37 for
                // object 300 (shared with its use-menu binding) and chunk 41 for the shop-only
                // object 320. The missing-binding derivation keeps 300's binding and drops 320's,
                // so the strict story boundary must reject the shop-side nonzero bitmap word.
                foreach (var (chunk, width, height) in new[] { (37, 4, 3), (41, 2, 2) })
                {
                    var sprite = new Pal.Game.Resources.PalSprite { SpriteId = chunk, FrameCount = 1,
                        DecodedByteLength = 6 + width * height,
                        Source = new Godot.Collections.Dictionary { ["archive"] = "ball.mkf", ["chunk"] = chunk,
                            ["encoding"] = "uncompressed PAL RLE bitmap",
                            ["frameLayout"] = "single RLE bitmap: optional 0x00000002 header, WORD width/height, RLE runs" } };
                    sprite.Frames.Add(new Pal.Game.Resources.PalFrame { Index = 0, SourceOffset = 6,
                        Width = width, Height = height,
                        Indices = Enumerable.Range(0, width * height).Select(pixel => (byte)(chunk + pixel % 5)).ToArray(),
                        Alpha = Enumerable.Repeat((byte)255, width * height).ToArray() });
                    NativeProfileFixture.SaveResource(sprite, Root + $"/ShopBitmap-{chunk}.tres");
                }
                metadata["itemBitmaps"] = new JsonArray(
                    new JsonObject { ["objectId"] = 300, ["uri"] = Root + "/ShopBitmap-37.tres" });
                if (!menuShopBitmapsMissingBinding)
                    metadata["itemBitmaps"]!.AsArray().Add(
                        new JsonObject { ["objectId"] = 320, ["uri"] = Root + "/ShopBitmap-41.tres" });
                metadata["sourceIdentity"] = metadata["sourceIdentity"]!.GetValue<string>()
                    + " Test-owned shop BALL bitmaps (T05 #217): chunks 37/41 as authored test art, not retail ball.mkf data.";
                if (menuShopEmptyBag)
                    metadata["sourceIdentity"] = metadata["sourceIdentity"]!.GetValue<string>()
                        + " Test-owned empty-bag shop derivation: the bag seed is the empty list for the sell page; authored inputs, not retail data.";
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
            (string StoryUri, string VisualUri) prepared;
            using (GameProjectMount.StageSamplesInTestProject())
                prepared = PrepareStoryPackage.Prepare(input, Path.Combine(output, "Story"));
            StoryUri = prepared.StoryUri; VisualUri = prepared.VisualUri;
        }
        catch { Dispose(); throw; }
    }
    private void AddFleeEscapeBranch()
    {
        // Only this owned authored shape is relocatable (same rule as the HP/MP prefix). The
        // original author bytes stay read-only; the derivation owns a fresh GUID-owned input.
        string arenaPath = Path.Combine(input, "events/arena.json");
        string indexPath = Path.Combine(input, "events/index.json");
        var arena = JsonNode.Parse(File.ReadAllText(arenaPath))!;
        var index = JsonNode.Parse(File.ReadAllText(indexPath))!;
        var commands = arena["commands"]!.AsArray();
        var battle = commands[3]!;
        if (commands.Count != 13 || battle["op"]!.GetValue<string>() != "startBattle"
            || (int)battle["source"]!["operands"]![0]! != 18 || (int)battle["source"]!["operands"]![1]! != 7
            || (int)battle["source"]!["operands"]![2]! != 0 || battle["isBoss"]!.GetValue<bool>() != true
            || battle["fleeTarget"] is not null || battle["lossTarget"]!.GetValue<string>() != "L0007"
            || commands[12]!["op"]!.GetValue<string>() != "end" || commands[12]!["next"] is not null)
            throw new InvalidDataException("The flee-escape derivation requires the known 13-instruction LostReload author arena shape.");
        // script.c 0x0007: operand2 is the flee branch, and a nonzero word also makes
        // fIsBoss = FALSE (PAL_StartBattle(wTeam, !operand2)); the loss target stays L0007.
        battle["isBoss"] = false;
        battle["fleeTarget"] = "L0013";
        battle["source"]!["operands"]![2] = 13;
        // The previous terminal row stops being the stream's end: its next must now chain to
        // the appended successor (the next-successor schema rule covers the whole stream).
        commands[12]!["next"] = "L0013";
        commands.Add(new JsonObject { ["label"] = "L0013", ["op"] = "setDialog", ["next"] = "L0014",
            ["source"] = new JsonObject { ["index"] = 13, ["opcode"] = 60, ["operands"] = new JsonArray(0, 0, 0) },
            ["box"] = "upper", ["portraitId"] = 0, ["color"] = 0, ["rngWord"] = 0 });
        commands.Add(new JsonObject { ["label"] = "L0014", ["op"] = "showDialog", ["next"] = "L0015",
            ["source"] = new JsonObject { ["index"] = 14, ["opcode"] = 65535, ["operands"] = new JsonArray(3, 0, 0) },
            ["messageId"] = 3, ["descriptionLineWord"] = 0 });
        commands.Add(new JsonObject { ["label"] = "L0015", ["op"] = "end", ["next"] = null,
            ["source"] = new JsonObject { ["index"] = 15, ["opcode"] = 0, ["operands"] = new JsonArray(0, 0, 0) },
            ["mode"] = "keep" });
        var bytes = new byte[commands.Count * 8];
        for (int address = 0; address < commands.Count; address++)
        {
            var source = commands[address]!["source"]!;
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(address * 8), checked((ushort)source["opcode"]!.GetValue<int>()));
            for (int word = 0; word < 3; word++)
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(address * 8 + 2 + word * 2), checked((ushort)source["operands"]![word]!.GetValue<int>()));
        }
        index["source"]!["instructionCount"] = commands.Count;
        index["source"]!["byteLength"] = bytes.Length;
        index["source"]!["sha256"] = Convert.ToHexStringLower(SHA256.HashData(bytes));
        File.WriteAllText(arenaPath, arena.ToJsonString());
        File.WriteAllText(indexPath, index.ToJsonString());
    }

    private void AddControlFlowStream()
    {
        // Only this owned authored shape is relocatable (same rule as the flee/item derivations).
        string arenaPath = Path.Combine(input, "events/arena.json");
        string indexPath = Path.Combine(input, "events/index.json");
        var arena = JsonNode.Parse(File.ReadAllText(arenaPath))!;
        var index = JsonNode.Parse(File.ReadAllText(indexPath))!;
        var commands = arena["commands"]!.AsArray();
        if (commands.Count != 65 || commands[1]!["op"]!.GetValue<string>() != "setDialog"
            || commands[3]!["op"]!.GetValue<string>() != "startBattle"
            || commands[6]!["op"]!.GetValue<string>() != "end"
            || commands[10]!["op"]!.GetValue<string>() != "randomJump"
            || commands[64]!["op"]!.GetValue<string>() != "end")
            throw new InvalidDataException("The T03 control-flow derivation requires the known 65-instruction Midbattle author arena shape.");
        // The original terminal row gains the appended continuation as its WORD-order successor.
        commands[64]!["next"] = "L0065";
        // L0001: call the appended nested callee with operand word zero (inherit the event caller).
        commands[1]!["op"] = "call";
        commands[1]!["to"] = "L0065";
        commands[1]!["eventObjectId"] = 0;
        foreach (string field in new[] { "box", "portraitId", "color", "rngWord" }) commands[1]!.AsObject().Remove(field);
        commands[1]!["source"]!["opcode"] = 4;
        commands[1]!["source"]!["operands"] = new JsonArray(65, 0, 0);
        // L0004 (the battle's WORD-order successor): call the appended counted callee with the
        // inherited event caller word (0). The Midbattle friend-death hook can leave last event
        // object 0, whose counted gate is undefined (script.c dereferences only a nonzero object),
        // so the derived settled call inherits the root caller instead of 0xFFFF.
        commands[4]!["op"] = "call";
        commands[4]!["to"] = "L0068";
        commands[4]!["eventObjectId"] = 0;
        foreach (string field in new[] { "box", "portraitId", "color", "rngWord" }) commands[4]!.AsObject().Remove(field);
        commands[4]!["source"]!["opcode"] = 4;
        commands[4]!["source"]!["operands"] = new JsonArray(68, 0, 0);
        // L0005: the callee returns here; 0x0001 publishes its WORD-order successor as the
        // returned entry, which Main writes back to the searched object (play.c 153).
        commands[5]!["op"] = "end";
        commands[5]!["mode"] = "advance";
        foreach (string field in new[] { "messageId", "descriptionLineWord" }) commands[5]!.AsObject().Remove(field);
        commands[5]!["source"]!["opcode"] = 1;
        commands[5]!["source"]!["operands"] = new JsonArray(0, 0, 0);
        void Append(string label, string op, int opcode, JsonArray operands, string? next, string semantics)
        {
            var row = JsonNode.Parse(semantics)!.AsObject();
            row["label"] = label;
            row["op"] = op;
            row["next"] = next is null ? null : JsonValue.Create(next);
            row["source"] = new JsonObject { ["index"] = commands.Count, ["opcode"] = opcode, ["operands"] = operands };
            commands.Add(row);
        }
        Append("L0065", "setDialog", 60, new JsonArray(0, 0, 0), "L0066", """{"box":"upper","portraitId":0,"color":0,"rngWord":0}""");
        Append("L0066", "showDialog", 65535, new JsonArray(4, 0, 0), "L0067", """{"messageId":4,"descriptionLineWord":0}""");
        Append("L0067", "end", 0, new JsonArray(0, 0, 0), "L0068", """{"mode":"keep"}""");
        // The settled continuation's inherited callee: a counted root gate (owner = event caller)
        // then the Won display; the failure arm ends without one.
        Append("L0068", "goto", 3, new JsonArray(70, 4, 0), "L0069", """{"to":"L0070","limit":4}""");
        Append("L0069", "end", 0, new JsonArray(0, 0, 0), "L0070", """{"mode":"keep"}""");
        Append("L0070", "setDialog", 60, new JsonArray(0, 0, 0), "L0071", """{"box":"upper","portraitId":0,"color":0,"rngWord":0}""");
        Append("L0071", "showDialog", 65535, new JsonArray(2, 0, 0), "L0072", """{"messageId":2,"descriptionLineWord":0}""");
        Append("L0072", "end", 0, new JsonArray(0, 0, 0), "L0073", """{"mode":"keep"}""");
        Append("L0073", "end", 0, new JsonArray(0, 0, 0), "L0074", """{"mode":"keep"}""");
        Append("L0074", "end", 0, new JsonArray(0, 0, 0), "L0075", """{"mode":"keep"}""");
        Append("L0075", "end", 0, new JsonArray(0, 0, 0), "L0076", """{"mode":"keep"}""");
        Append("L0076", "end", 0, new JsonArray(0, 0, 0), "L0077", """{"mode":"keep"}""");
        // The second enemy slot's post-victory script (battle.c 1334-1337): empty, so its own
        // invocation leaves the slot word in wLastEventObject and the settled 0xFFFF resolves it.
        Append("L0077", "end", 0, new JsonArray(0, 0, 0), "L0078", """{"mode":"keep"}""");
        // The dying hook's own root counted goto (script.c 3196): owner = the hook participant
        // word; the high limit keeps both the threshold and the death invocation on the target.
        Append("L0078", "goto", 3, new JsonArray(10, 100, 0), "L0079", """{"to":"L0010","limit":100}""");
        Append("L0079", "end", 0, new JsonArray(0, 0, 0), null, """{"mode":"keep"}""");
        var bytes = new byte[commands.Count * 8];
        for (int address = 0; address < commands.Count; address++)
        {
            var source = commands[address]!["source"]!;
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(address * 8), checked((ushort)source["opcode"]!.GetValue<int>()));
            for (int word = 0; word < 3; word++)
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(address * 8 + 2 + word * 2), checked((ushort)source["operands"]![word]!.GetValue<int>()));
        }
        index["source"]!["instructionCount"] = commands.Count;
        index["source"]!["byteLength"] = bytes.Length;
        index["source"]!["sha256"] = Convert.ToHexStringLower(SHA256.HashData(bytes));
        File.WriteAllText(arenaPath, arena.ToJsonString());
        File.WriteAllText(indexPath, index.ToJsonString());
    }

    private void AddItemUseEntries()
    {
        // Only this owned authored shape is relocatable (same rule as the HP/MP prefix). The
        // original author bytes stay read-only; the derivation owns a fresh GUID-owned input.
        string arenaPath = Path.Combine(input, "events/arena.json");
        string indexPath = Path.Combine(input, "events/index.json");
        var arena = JsonNode.Parse(File.ReadAllText(arenaPath))!;
        var index = JsonNode.Parse(File.ReadAllText(indexPath))!;
        var entries = arena["entries"]!.AsArray();
        var commands = arena["commands"]!.AsArray();
        if (entries.Count != 1 || entries[0]!["id"]!.GetValue<string>() != "challenge"
            || commands.Count != 10 || commands[9]!["op"]!.GetValue<string>() != "end"
            || commands[9]!["mode"]!.GetValue<string>() != "keep" || commands[9]!["next"] is not null
            || commands[3]!["op"]!.GetValue<string>() != "startBattle" || commands[3]!["isBoss"]!.GetValue<bool>() != true
            || (int)commands[3]!["source"]!["operands"]![0]! != 18 || commands[3]!["fleeTarget"] is not null
            || commands[3]!["lossTarget"]!.GetValue<string>() != "L0007"
            || (int)index["source"]!["instructionCount"]! != 10)
            throw new InvalidDataException("The menu-items derivation requires the known ten-instruction PartyWin author arena shape.");
        // The previous terminal row stops being the stream's end: its next chains to the first
        // appended item command (end keep ignores it at runtime, the schema keeps WORD order).
        commands[9]!["next"] = "L0010";
        // 300 (single-target heal +30 with a dialogue line): itemmenu.c/uigame.c run the object's
        // wScriptOnUse entry with the selected member's role as the caller.
        entries.Add(new JsonObject { ["id"] = "use300", ["owner"] = new JsonObject { ["type"] = "object", ["id"] = 300 },
            ["slot"] = "item.wScriptOnUse", ["kind"] = "trigger", ["target"] = "L0010", ["sourceAddress"] = 10 });
        commands.Add(new JsonObject { ["label"] = "L0010", ["op"] = "changeHpMp", ["next"] = "L0011",
            ["scope"] = 0, ["amount"] = 30,
            ["source"] = new JsonObject { ["index"] = 10, ["opcode"] = 0x001D, ["operands"] = new JsonArray(0, 30, 0) } });
        commands.Add(new JsonObject { ["label"] = "L0011", ["op"] = "setDialog", ["next"] = "L0012",
            ["source"] = new JsonObject { ["index"] = 11, ["opcode"] = 60, ["operands"] = new JsonArray(0, 0, 0) },
            ["box"] = "upper", ["portraitId"] = 0, ["color"] = 0, ["rngWord"] = 0 });
        commands.Add(new JsonObject { ["label"] = "L0012", ["op"] = "showDialog", ["next"] = "L0013",
            ["source"] = new JsonObject { ["index"] = 12, ["opcode"] = 65535, ["operands"] = new JsonArray(25, 0, 0) },
            ["messageId"] = 25, ["descriptionLineWord"] = 0 });
        commands.Add(new JsonObject { ["label"] = "L0013", ["op"] = "end", ["next"] = "L0014",
            ["source"] = new JsonObject { ["index"] = 13, ["opcode"] = 0, ["operands"] = new JsonArray(0, 0, 0) },
            ["mode"] = "keep" });
        // 301 (apply-to-all heal +10): play.c runs it once with the caller word 0xFFFF.
        entries.Add(new JsonObject { ["id"] = "use301", ["owner"] = new JsonObject { ["type"] = "object", ["id"] = 301 },
            ["slot"] = "item.wScriptOnUse", ["kind"] = "trigger", ["target"] = "L0014", ["sourceAddress"] = 14 });
        commands.Add(new JsonObject { ["label"] = "L0014", ["op"] = "changeHpMp", ["next"] = "L0015",
            ["scope"] = 1, ["amount"] = 10,
            ["source"] = new JsonObject { ["index"] = 14, ["opcode"] = 0x001D, ["operands"] = new JsonArray(1, 10, 0) } });
        commands.Add(new JsonObject { ["label"] = "L0015", ["op"] = "end", ["next"] = "L0016",
            ["source"] = new JsonObject { ["index"] = 15, ["opcode"] = 0, ["operands"] = new JsonArray(0, 0, 0) },
            ["mode"] = "keep" });
        // 302 (failing powder): script.c 0x0041 clears the success flag, so play.c keeps the copy.
        entries.Add(new JsonObject { ["id"] = "use302", ["owner"] = new JsonObject { ["type"] = "object", ["id"] = 302 },
            ["slot"] = "item.wScriptOnUse", ["kind"] = "trigger", ["target"] = "L0016", ["sourceAddress"] = 16 });
        commands.Add(new JsonObject { ["label"] = "L0016", ["op"] = "markFailed", ["next"] = "L0017",
            ["source"] = new JsonObject { ["index"] = 16, ["opcode"] = 0x41, ["operands"] = new JsonArray(0, 0, 0) } });
        commands.Add(new JsonObject { ["label"] = "L0017", ["op"] = "end", ["next"] = null,
            ["source"] = new JsonObject { ["index"] = 17, ["opcode"] = 0, ["operands"] = new JsonArray(0, 0, 0) },
            ["mode"] = "keep" });
        var bytes = new byte[commands.Count * 8];
        for (int address = 0; address < commands.Count; address++)
        {
            var source = commands[address]!["source"]!;
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(address * 8), checked((ushort)source["opcode"]!.GetValue<int>()));
            for (int word = 0; word < 3; word++)
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(address * 8 + 2 + word * 2), checked((ushort)source["operands"]![word]!.GetValue<int>()));
        }
        index["source"]!["instructionCount"] = commands.Count;
        index["source"]!["byteLength"] = bytes.Length;
        index["source"]!["sha256"] = Convert.ToHexStringLower(SHA256.HashData(bytes));
        File.WriteAllText(arenaPath, arena.ToJsonString());
        File.WriteAllText(indexPath, index.ToJsonString());
    }

    private void AddMagicUseEntries()
    {
        // Only this owned authored shape is relocatable (same rule as the item-use derivation).
        // The original author bytes stay read-only; the derivation owns a fresh GUID-owned input.
        string arenaPath = Path.Combine(input, "events/arena.json");
        string indexPath = Path.Combine(input, "events/index.json");
        var arena = JsonNode.Parse(File.ReadAllText(arenaPath))!;
        var index = JsonNode.Parse(File.ReadAllText(indexPath))!;
        var entries = arena["entries"]!.AsArray();
        var commands = arena["commands"]!.AsArray();
        if (entries.Count != 1 || entries[0]!["id"]!.GetValue<string>() != "challenge"
            || commands.Count != 10 || commands[9]!["op"]!.GetValue<string>() != "end"
            || commands[9]!["mode"]!.GetValue<string>() != "keep" || commands[9]!["next"] is not null
            || commands[3]!["op"]!.GetValue<string>() != "startBattle" || commands[3]!["isBoss"]!.GetValue<bool>() != true
            || (int)commands[3]!["source"]!["operands"]![0]! != 18 || commands[3]!["fleeTarget"] is not null
            || commands[3]!["lossTarget"]!.GetValue<string>() != "L0007"
            || (int)index["source"]!["instructionCount"]! != 10)
            throw new InvalidDataException("The menu-magic derivation requires the known ten-instruction PartyWin author arena shape.");
        // The previous terminal row stops being the stream's end: its next chains to the first
        // appended magic command (end keep ignores it at runtime, the schema keeps WORD order).
        commands[9]!["next"] = "L0010";
        // 310 (single-target heal +30 with a dialogue line): uigame.c runs the object's
        // wScriptOnUse entry with the selected member's role as the caller, then its
        // wScriptOnSuccess entry after the first survived.
        entries.Add(new JsonObject { ["id"] = "magic310OnUse", ["owner"] = new JsonObject { ["type"] = "object", ["id"] = 310 },
            ["slot"] = "magic.wScriptOnUse", ["kind"] = "trigger", ["target"] = "L0010", ["sourceAddress"] = 10 });
        commands.Add(new JsonObject { ["label"] = "L0010", ["op"] = "changeHpMp", ["next"] = "L0011",
            ["scope"] = 0, ["amount"] = 30,
            ["source"] = new JsonObject { ["index"] = 10, ["opcode"] = 0x001D, ["operands"] = new JsonArray(0, 30, 0) } });
        commands.Add(new JsonObject { ["label"] = "L0011", ["op"] = "setDialog", ["next"] = "L0012",
            ["source"] = new JsonObject { ["index"] = 11, ["opcode"] = 60, ["operands"] = new JsonArray(0, 0, 0) },
            ["box"] = "upper", ["portraitId"] = 0, ["color"] = 0, ["rngWord"] = 0 });
        commands.Add(new JsonObject { ["label"] = "L0012", ["op"] = "showDialog", ["next"] = "L0013",
            ["source"] = new JsonObject { ["index"] = 12, ["opcode"] = 65535, ["operands"] = new JsonArray(25, 0, 0) },
            ["messageId"] = 25, ["descriptionLineWord"] = 0 });
        commands.Add(new JsonObject { ["label"] = "L0013", ["op"] = "end", ["next"] = "L0014",
            ["source"] = new JsonObject { ["index"] = 13, ["opcode"] = 0, ["operands"] = new JsonArray(0, 0, 0) },
            ["mode"] = "keep" });
        entries.Add(new JsonObject { ["id"] = "magic310OnSuccess", ["owner"] = new JsonObject { ["type"] = "object", ["id"] = 310 },
            ["slot"] = "magic.wScriptOnSuccess", ["kind"] = "trigger", ["target"] = "L0014", ["sourceAddress"] = 14 });
        commands.Add(new JsonObject { ["label"] = "L0014", ["op"] = "end", ["next"] = "L0015",
            ["source"] = new JsonObject { ["index"] = 14, ["opcode"] = 0, ["operands"] = new JsonArray(0, 0, 0) },
            ["mode"] = "keep" });
        // 311 (apply-to-all heal +10): uigame.c 740-743 runs both entries once with the literal
        // caller word 0, straight from the list without a player select.
        entries.Add(new JsonObject { ["id"] = "magic311OnUse", ["owner"] = new JsonObject { ["type"] = "object", ["id"] = 311 },
            ["slot"] = "magic.wScriptOnUse", ["kind"] = "trigger", ["target"] = "L0015", ["sourceAddress"] = 15 });
        commands.Add(new JsonObject { ["label"] = "L0015", ["op"] = "changeHpMp", ["next"] = "L0016",
            ["scope"] = 1, ["amount"] = 10,
            ["source"] = new JsonObject { ["index"] = 15, ["opcode"] = 0x001D, ["operands"] = new JsonArray(1, 10, 0) } });
        entries.Add(new JsonObject { ["id"] = "magic311OnSuccess", ["owner"] = new JsonObject { ["type"] = "object", ["id"] = 311 },
            ["slot"] = "magic.wScriptOnSuccess", ["kind"] = "trigger", ["target"] = "L0016", ["sourceAddress"] = 16 });
        commands.Add(new JsonObject { ["label"] = "L0016", ["op"] = "end", ["next"] = "L0017",
            ["source"] = new JsonObject { ["index"] = 16, ["opcode"] = 0, ["operands"] = new JsonArray(0, 0, 0) },
            ["mode"] = "keep" });
        // 312 is battle-only (kMagicFlagUsableOutsideBattle clear): listed for the caster but
        // never confirmable outside battle; its entries still resolve for the strict profile.
        entries.Add(new JsonObject { ["id"] = "magic312OnUse", ["owner"] = new JsonObject { ["type"] = "object", ["id"] = 312 },
            ["slot"] = "magic.wScriptOnUse", ["kind"] = "trigger", ["target"] = "L0017", ["sourceAddress"] = 17 });
        commands.Add(new JsonObject { ["label"] = "L0017", ["op"] = "end", ["next"] = "L0018",
            ["source"] = new JsonObject { ["index"] = 17, ["opcode"] = 0, ["operands"] = new JsonArray(0, 0, 0) },
            ["mode"] = "keep" });
        entries.Add(new JsonObject { ["id"] = "magic312OnSuccess", ["owner"] = new JsonObject { ["type"] = "object", ["id"] = 312 },
            ["slot"] = "magic.wScriptOnSuccess", ["kind"] = "trigger", ["target"] = "L0018", ["sourceAddress"] = 18 });
        commands.Add(new JsonObject { ["label"] = "L0018", ["op"] = "end", ["next"] = "L0019",
            ["source"] = new JsonObject { ["index"] = 18, ["opcode"] = 0, ["operands"] = new JsonArray(0, 0, 0) },
            ["mode"] = "keep" });
        // 313's on-use ends successfully, but its on-success marks the script failed
        // (script.c 0x0041): the cast's MP deduction gate never opens.
        entries.Add(new JsonObject { ["id"] = "magic313OnUse", ["owner"] = new JsonObject { ["type"] = "object", ["id"] = 313 },
            ["slot"] = "magic.wScriptOnUse", ["kind"] = "trigger", ["target"] = "L0019", ["sourceAddress"] = 19 });
        commands.Add(new JsonObject { ["label"] = "L0019", ["op"] = "end", ["next"] = "L0020",
            ["source"] = new JsonObject { ["index"] = 19, ["opcode"] = 0, ["operands"] = new JsonArray(0, 0, 0) },
            ["mode"] = "keep" });
        entries.Add(new JsonObject { ["id"] = "magic313OnSuccess", ["owner"] = new JsonObject { ["type"] = "object", ["id"] = 313 },
            ["slot"] = "magic.wScriptOnSuccess", ["kind"] = "trigger", ["target"] = "L0020", ["sourceAddress"] = 20 });
        commands.Add(new JsonObject { ["label"] = "L0020", ["op"] = "markFailed", ["next"] = "L0021",
            ["source"] = new JsonObject { ["index"] = 20, ["opcode"] = 0x41, ["operands"] = new JsonArray(0, 0, 0) } });
        commands.Add(new JsonObject { ["label"] = "L0021", ["op"] = "end", ["next"] = null,
            ["source"] = new JsonObject { ["index"] = 21, ["opcode"] = 0, ["operands"] = new JsonArray(0, 0, 0) },
            ["mode"] = "keep" });
        var bytes = new byte[commands.Count * 8];
        for (int address = 0; address < commands.Count; address++)
        {
            var source = commands[address]!["source"]!;
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(address * 8), checked((ushort)source["opcode"]!.GetValue<int>()));
            for (int word = 0; word < 3; word++)
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(address * 8 + 2 + word * 2), checked((ushort)source["operands"]![word]!.GetValue<int>()));
        }
        index["source"]!["instructionCount"] = commands.Count;
        index["source"]!["byteLength"] = bytes.Length;
        index["source"]!["sha256"] = Convert.ToHexStringLower(SHA256.HashData(bytes));
        File.WriteAllText(arenaPath, arena.ToJsonString());
        File.WriteAllText(indexPath, index.ToJsonString());
    }

    private void AddShopEntries()
    {
        // #217 (T05): the test-owned shop arena — the challenge stream gains the two shop
        // commands between the greeting dialog and the battle (script.c 0x0026/0x0027), and the
        // item on-use entry for 300 appends after the terminal row like the #215 derivation.
        // Only this owned authored shape is relocatable; the original author bytes stay
        // read-only and the derivation owns a fresh GUID-owned input.
        string arenaPath = Path.Combine(input, "events/arena.json");
        string indexPath = Path.Combine(input, "events/index.json");
        var arena = JsonNode.Parse(File.ReadAllText(arenaPath))!;
        var index = JsonNode.Parse(File.ReadAllText(indexPath))!;
        var entries = arena["entries"]!.AsArray();
        var commands = arena["commands"]!.AsArray();
        if (entries.Count != 1 || entries[0]!["id"]!.GetValue<string>() != "challenge"
            || commands.Count != 10 || commands[9]!["op"]!.GetValue<string>() != "end"
            || commands[9]!["mode"]!.GetValue<string>() != "keep" || commands[9]!["next"] is not null
            || commands[3]!["op"]!.GetValue<string>() != "startBattle" || commands[3]!["isBoss"]!.GetValue<bool>() != true
            || (int)commands[3]!["source"]!["operands"]![0]! != 18 || commands[3]!["fleeTarget"] is not null
            || commands[3]!["lossTarget"]!.GetValue<string>() != "L0007"
            || (int)index["source"]!["instructionCount"]! != 10)
            throw new InvalidDataException("The shop derivation requires the known ten-instruction PartyEquip author arena shape.");
        // The previous terminal row stops being the stream's end: its next chains to the first
        // appended item command (end keep ignores it at runtime, the schema keeps WORD order).
        commands[9]!["next"] = "L0010";
        // 300 (single-target heal +30 with a dialogue line): the use-menu binding's on-use entry
        // (#215 pattern); its heal is the menu->battle continuity input.
        entries.Add(new JsonObject { ["id"] = "use300", ["owner"] = new JsonObject { ["type"] = "object", ["id"] = 300 },
            ["slot"] = "item.wScriptOnUse", ["kind"] = "trigger", ["target"] = "L0010", ["sourceAddress"] = 10 });
        commands.Add(new JsonObject { ["label"] = "L0010", ["op"] = "changeHpMp", ["next"] = "L0011",
            ["scope"] = 0, ["amount"] = 30,
            ["source"] = new JsonObject { ["index"] = 10, ["opcode"] = 0x001D, ["operands"] = new JsonArray(0, 30, 0) } });
        commands.Add(new JsonObject { ["label"] = "L0011", ["op"] = "setDialog", ["next"] = "L0012",
            ["source"] = new JsonObject { ["index"] = 11, ["opcode"] = 60, ["operands"] = new JsonArray(0, 0, 0) },
            ["box"] = "upper", ["portraitId"] = 0, ["color"] = 0, ["rngWord"] = 0 });
        commands.Add(new JsonObject { ["label"] = "L0012", ["op"] = "showDialog", ["next"] = "L0013",
            ["source"] = new JsonObject { ["index"] = 12, ["opcode"] = 65535, ["operands"] = new JsonArray(25, 0, 0) },
            ["messageId"] = 25, ["descriptionLineWord"] = 0 });
        commands.Add(new JsonObject { ["label"] = "L0013", ["op"] = "end", ["next"] = null,
            ["source"] = new JsonObject { ["index"] = 13, ["opcode"] = 0, ["operands"] = new JsonArray(0, 0, 0) },
            ["mode"] = "keep" });
        // script.c 0x0026/0x0027 insert between the greeting dialog and the battle: the script
        // parks on each shop menu and the host receipts it when the player cancels (uigame.c).
        commands.Insert(3, new JsonObject { ["label"] = "L0003", ["op"] = "showBuyMenu", ["next"] = "L0004",
            ["storeId"] = 5,
            ["source"] = new JsonObject { ["index"] = 3, ["opcode"] = 0x26, ["operands"] = new JsonArray(5, 0, 0) } });
        commands.Insert(4, new JsonObject { ["label"] = "L0004", ["op"] = "showSellMenu", ["next"] = "L0005",
            ["source"] = new JsonObject { ["index"] = 4, ["opcode"] = 0x27, ["operands"] = new JsonArray(0, 0, 0) } });
        // Relabel the shifted stream (the battle and its loss branch moved +2) and rewrite the
        // BYTE index/hash over the final instruction order.
        for (int address = 0; address < commands.Count; address++)
        {
            var command = commands[address]!;
            command["label"] = $"L{address:D4}";
            command["source"]!["index"] = address;
            command["next"] = address == commands.Count - 1 ? null : $"L{address + 1:D4}";
        }
        commands[5]!["lossTarget"] = "L0009";
        // The appended item entry moved +2 with the shop insertions: its target/source address
        // follow the shifted stream (the entry owns the 300 on-use run at the new index 12).
        entries[1]!["target"] = "L0012";
        entries[1]!["sourceAddress"] = 12;
        var bytes = new byte[commands.Count * 8];
        for (int address = 0; address < commands.Count; address++)
        {
            var source = commands[address]!["source"]!;
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(address * 8), checked((ushort)source["opcode"]!.GetValue<int>()));
            for (int word = 0; word < 3; word++)
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(address * 8 + 2 + word * 2), checked((ushort)source["operands"]![word]!.GetValue<int>()));
        }
        index["source"]!["instructionCount"] = commands.Count;
        index["source"]!["byteLength"] = bytes.Length;
        index["source"]!["sha256"] = Convert.ToHexStringLower(SHA256.HashData(bytes));
        File.WriteAllText(arenaPath, arena.ToJsonString());
        File.WriteAllText(indexPath, index.ToJsonString());
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
