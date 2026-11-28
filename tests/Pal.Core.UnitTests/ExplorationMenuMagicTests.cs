using System.Text.Json.Nodes;
using Xunit;
using Pal.Core.State;
using Pal.Core.Exploration;
using Pal.Core.Battle;
using Pal.Core.Events;
using Pal.Core.Menu;

namespace Pal.Core.Tests;

// Independent oracle: uigame.c PAL_InGameMagicMenu (654-845: alive-only caster select over
// PAL_ReadMenu, apply-to-all magics run straight from the list with the literal caller word 0,
// single-target member selects repeat while the caster can still pay, and the caster's MP is
// deducted only after the on-use AND on-success scripts survived), magicmenu.c
// PAL_MagicSelectionMenu (36-401: the caster's learned magics sorted by object WORD, disabled
// when the cost exceeds the current live MP or the magic is not usable outside battle, enabled
// rows confirm only, moves clamp at both list ends without wrapping) and ui.c PAL_ReadMenu
// (401-660: wrap navigation, enabled-only confirm, Menu cancels). Numbers are hand-authored.
public sealed class ExplorationMenuMagicTests
{
    [Fact]
    public void Authored_magic_menu_context_parses_as_modern_battle_data()
    {
        var content = ModernBattleContent.Parse(MagicContextData().ToJsonString());
        // Bindings keep authored order with their cost and flag words (global.h kMagicFlag
        // kMagicFlagUsableOutsideBattle 1<<0, kMagicFlagApplyToAll 1<<4; lprgMagic cost).
        Assert.Equal([(310, 5, false, true), (311, 4, true, true), (312, 3, false, false), (313, 99, false, true)],
            content.MagicMenu.Select(binding => (binding.ObjectId, binding.CostMp, binding.ApplyToAll,
                binding.UsableOutsideBattle)).ToArray());
        // Legacy data without the section stays valid and empty.
        var legacy = ModernBattleContent.Parse(Data().ToJsonString());
        Assert.Empty(legacy.MagicMenu);
        // Strict domain rejections with authored paths.
        Assert.Contains("$.magicMenu[0].objectId", Reject("objectId0", "expected integer in 1..65535").Message);
        Assert.Contains("$.magicMenu[0].costMp", Reject("costHigh", "expected integer in 0..65535").Message);
        Assert.Contains("unknown field 'costMP'", Reject("typo", "unknown field").Message);
        Assert.Contains("expected boolean", Reject("applyToAllString", "expected boolean").Message);
        Assert.Contains("unique object WORD ids", Reject("duplicate", "unique").Message);
    }

    [Fact]
    public void Magic_page_selects_caster_lists_filtered_magics_and_navigates_the_reference_way()
    {
        var (menu, roles) = Menu();
        menu.Open();
        menu.Press(ExplorationMenuKey.Down);
        menu.Press(ExplorationMenuKey.Confirm);
        Assert.True(menu.Mode is ExplorationMenuMode.MagicCasters or ExplorationMenuMode.MagicList,
            "PAL_MAGIC_RED: reference uigame.c PAL_InGameMagicMenu - confirming the magic entry must open the caster select or the single-member magic list");
        Assert.Equal(ExplorationMenuMode.MagicCasters, menu.Mode);
        // The caster rows list the whole party; dead members stay listed but disabled.
        Assert.Equal([(0, "大侠", true), (1, "小侠", true)],
            menu.MagicCastersView().Select(row => (row.RoleId, row.Name, row.Alive)).ToArray());
        // ui.c PAL_ReadMenu: the caster cursor wraps both ways.
        menu.Press(ExplorationMenuKey.Up);
        Assert.Equal(1, menu.SelectedMagicCaster);
        menu.Press(ExplorationMenuKey.Down);
        Assert.Equal(0, menu.SelectedMagicCaster);
        // A dead member stays reachable by cursor, but Confirm does nothing (fEnabled).
        var dead = Menu(role1Hp: 0);
        dead.menu.Open();
        dead.menu.Press(ExplorationMenuKey.Down);
        dead.menu.Press(ExplorationMenuKey.Confirm);
        Assert.Equal([(0, "大侠", true), (1, "小侠", false)],
            dead.menu.MagicCastersView().Select(row => (row.RoleId, row.Name, row.Alive)).ToArray());
        dead.menu.Press(ExplorationMenuKey.Down);
        dead.menu.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(ExplorationMenuMode.MagicCasters, dead.menu.Mode); // no half-selected caster
        // kKeyMenu cancels the caster select; PAL_InGameMagicMenu returns and uigame.c case 2
        // 'goto out' closes the whole menu. The static caster cursor survives closes.
        dead.menu.Press(ExplorationMenuKey.Cancel);
        Assert.Equal(ExplorationMenuMode.Closed, dead.menu.Mode);
        dead.menu.Open();
        dead.menu.Press(ExplorationMenuKey.Down);
        dead.menu.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(1, dead.menu.SelectedMagicCaster);
        // An alive caster confirms into the magic list.
        menu.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(ExplorationMenuMode.MagicList, menu.Mode);
        // magicmenu.c PAL_MagicSelectionMenuInit: the caster's learned magics (authored here in
        // slot order 313, 311, 310, 312) sorted by object WORD; enabled only when the magic is
        // usable outside battle and its cost fits the caster's current live MP.
        Assert.Equal([(310, "回春术", 5, true), (311, "清风扇", 4, true), (312, "烈焰咒", 3, false), (313, "天雷诀", 99, false)],
            menu.MagicListView().Select(row => (row.ObjectId, row.Name, row.CostMp, row.Enabled)).ToArray());
        Assert.Equal(310, menu.SelectedMagic); // wMagic resets to the first row each entry
        // PAL_MagicSelectionMenuUpdate: moves clamp at both list ends without wrapping.
        menu.Press(ExplorationMenuKey.Up);
        Assert.Equal(310, menu.SelectedMagic);
        menu.Press(ExplorationMenuKey.Left);
        Assert.Equal(310, menu.SelectedMagic);
        menu.Press(ExplorationMenuKey.Right);
        Assert.Equal(311, menu.SelectedMagic);
        menu.Press(ExplorationMenuKey.Down);
        Assert.Equal(312, menu.SelectedMagic);
        menu.Press(ExplorationMenuKey.Down);
        Assert.Equal(313, menu.SelectedMagic);
        menu.Press(ExplorationMenuKey.Down);
        Assert.Equal(313, menu.SelectedMagic); // past the last row stays put
        menu.Press(ExplorationMenuKey.Left);
        Assert.Equal(312, menu.SelectedMagic);
        menu.Press(ExplorationMenuKey.Up);
        Assert.Equal(311, menu.SelectedMagic);
        // Disabled rows never confirm (cost above live MP / battle-only flags).
        menu.Press(ExplorationMenuKey.Down); // 313: cost 99 over MP 20
        menu.Press(ExplorationMenuKey.Down);
        menu.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(ExplorationMenuMode.MagicList, menu.Mode);
        Assert.Null(menu.PendingMagicUse);
        // A single-target magic opens the member select at member 0 (fresh wPlayer local).
        menu.Press(ExplorationMenuKey.Up);
        menu.Press(ExplorationMenuKey.Up);
        menu.Press(ExplorationMenuKey.Up); // back to 310
        menu.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(ExplorationMenuMode.MagicTargets, menu.Mode);
        Assert.Equal((310, 0), (menu.SelectedMagic, menu.SelectedMagicTarget));
        var view = menu.MagicTargetsView();
        Assert.Equal((310, "回春术", "生命", "法力"), (view.ObjectId, view.Name, view.HpLabel, view.MpLabel));
        Assert.Equal([("大侠", 100, 150, 20, 50), ("小侠", 95, 150, 30, 50)],
            view.Targets.Select(target => (target.Name, target.Hp, target.MaxHp, target.Mp, target.MaxMp)).ToArray());
        // uigame.c 783-803: the target cursor steps without wrapping at both party ends.
        menu.Press(ExplorationMenuKey.Up);
        Assert.Equal(0, menu.SelectedMagicTarget);
        menu.Press(ExplorationMenuKey.Down);
        Assert.Equal(1, menu.SelectedMagicTarget);
        menu.Press(ExplorationMenuKey.Down);
        Assert.Equal(1, menu.SelectedMagicTarget);
        // kKeyMenu breaks the target loop; PAL_MagicSelectionMenu reopens at the same magic.
        menu.Press(ExplorationMenuKey.Cancel);
        Assert.Equal(ExplorationMenuMode.MagicList, menu.Mode);
        Assert.Equal(310, menu.SelectedMagic);
        // kKeyMenu in the list returns 0: the magic menu exits and 'goto out' closes everything.
        menu.Press(ExplorationMenuKey.Cancel);
        Assert.Equal(ExplorationMenuMode.Closed, menu.Mode);
        // The next entry resets the magic cursor but keeps the caster and entry cursors
        // (static words): the remembered main-menu cursor is still on the magic entry.
        menu.Open();
        menu.Press(ExplorationMenuKey.Confirm);
        menu.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(ExplorationMenuMode.MagicList, menu.Mode);
        Assert.Equal((0, 310), (menu.SelectedMagicCaster, menu.SelectedMagic));
        // uigame.c 662-665: a single-member party skips the caster select entirely
        // ('goto start_magicmenu'); even a dead sole member is the caster, mirroring the
        // reference (magicmenu.c checks cost and flags, never HP).
        var lone = Menu(singleMember: true, role0Hp: 0).menu;
        lone.Open();
        lone.Press(ExplorationMenuKey.Down); // move the main-menu cursor to the magic entry
        lone.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(ExplorationMenuMode.MagicList, lone.Mode);
        Assert.Equal(0, lone.SelectedMagicCaster);
        Assert.Equal([true, true, false, false], lone.MagicListView().Select(row => row.Enabled).ToArray());
        // Magic-entry construction requires the whole authored magic context up front.
        Assert.Contains("requires authored magic-menu bindings", Assert.Throws<InvalidDataException>(
            () => Menu(noMagicMenu: true).menu).Message);
        Assert.Contains("unique object WORD ids", Assert.Throws<InvalidDataException>(
            () => Menu(duplicateBinding: true).menu).Message);
        Assert.Contains("314", Assert.Throws<InvalidDataException>(
            () => Menu(missingMagicWord: true).menu).Message);
        Assert.Throws<InvalidOperationException>(() => Menu().menu.MagicCastersView());
        Assert.Throws<InvalidOperationException>(() => Menu().menu.MagicListView());
        Assert.Throws<InvalidOperationException>(() => Menu().menu.MagicTargetsView());
    }

    [Fact]
    public void Magic_use_deducts_MP_only_after_both_scripts_succeed_and_repeats_or_exits_by_cost()
    {
        var (menu, roles) = Menu();
        menu.Open();
        menu.Press(ExplorationMenuKey.Down);
        menu.Press(ExplorationMenuKey.Confirm); // caster select
        menu.Press(ExplorationMenuKey.Confirm); // member 0 is alive → magic list
        // The apply-to-all magic commits straight from the list with the literal caller word 0
        // (uigame.c 742-743); the item flow's 0xFFFF keep-caller word never applies here.
        menu.Press(ExplorationMenuKey.Right); // cursor 311
        menu.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(ExplorationMenuMode.MagicOnUse, menu.Mode);
        var all = menu.PendingMagicUse!;
        Assert.Equal((311, true, 0, 0), (all.ObjectId, all.ApplyToAll, all.CasterRoleId, all.TargetRoleId));
        Assert.Throws<InvalidOperationException>(() => menu.Press(ExplorationMenuKey.Cancel));
        Assert.Throws<InvalidOperationException>(() => menu.CompleteMagicSuccess(all.Id, true));
        Assert.Throws<InvalidOperationException>(() => menu.CompleteMagicUse(Guid.NewGuid(), true));
        // A failed on-use skips the on-success run and the deduction; the apply-to-all branch
        // returns to the magic selection loop (uigame.c 740-758, the menu stays open).
        menu.CompleteMagicUse(all.Id, false);
        Assert.Equal(ExplorationMenuMode.MagicList, menu.Mode);
        Assert.Null(menu.PendingMagicUse);
        Assert.Null(menu.PendingMagicSuccess);
        Assert.Equal(20, roles.Roles[0].Mp);
        // On-use success advances to the on-success phase before any MP moves.
        menu.Press(ExplorationMenuKey.Confirm); // cursor still 311
        var all2 = menu.PendingMagicUse!;
        menu.CompleteMagicUse(all2.Id, true);
        Assert.Equal(ExplorationMenuMode.MagicOnSuccess, menu.Mode);
        Assert.Null(menu.PendingMagicUse);
        Assert.Equal(all2.Id, menu.PendingMagicSuccess!.Id);
        Assert.Throws<InvalidOperationException>(() => menu.CompleteMagicUse(all2.Id, true));
        menu.CompleteMagicSuccess(all2.Id, true);
        Assert.Equal(ExplorationMenuMode.MagicList, menu.Mode);
        Assert.Equal(16, roles.Roles[0].Mp); // 20 − 4 only after both gates survived
        // A failed on-success also keeps the MP (no unified refund; the gates simply never ran).
        menu.Press(ExplorationMenuKey.Confirm);
        var all3 = menu.PendingMagicUse!;
        menu.CompleteMagicUse(all3.Id, true);
        menu.CompleteMagicSuccess(all3.Id, false);
        Assert.Equal(ExplorationMenuMode.MagicList, menu.Mode);
        Assert.Equal(16, roles.Roles[0].Mp);
        // Single-target magic 310 (cost 5): the member select commits the target's role id as
        // the script caller word (uigame.c 810-818).
        menu.Press(ExplorationMenuKey.Left); // cursor 310
        menu.Press(ExplorationMenuKey.Confirm);
        menu.Press(ExplorationMenuKey.Confirm); // member 0
        Assert.Equal(ExplorationMenuMode.MagicOnUse, menu.Mode);
        var one = menu.PendingMagicUse!;
        Assert.Equal((310, false, 0, 0), (one.ObjectId, one.ApplyToAll, one.CasterRoleId, one.TargetRoleId));
        menu.CompleteMagicUse(one.Id, false);
        Assert.Equal(ExplorationMenuMode.MagicTargets, menu.Mode); // failed run reopens the select
        Assert.Equal(16, roles.Roles[0].Mp);
        menu.Press(ExplorationMenuKey.Confirm);
        var two = menu.PendingMagicUse!;
        menu.CompleteMagicUse(two.Id, true);
        menu.CompleteMagicSuccess(two.Id, false); // failed on-success: still no MP
        Assert.Equal(ExplorationMenuMode.MagicTargets, menu.Mode);
        Assert.Equal(16, roles.Roles[0].Mp);
        menu.Press(ExplorationMenuKey.Confirm);
        var three = menu.PendingMagicUse!;
        menu.CompleteMagicUse(three.Id, true);
        menu.CompleteMagicSuccess(three.Id, true);
        // 16 − 5 = 11 still covers the cost, so the target select reopens for another use.
        Assert.Equal(ExplorationMenuMode.MagicTargets, menu.Mode);
        Assert.Equal(11, roles.Roles[0].Mp);
        // The wPlayer cursor keeps its position across the repeat (uigame.c keeps wPlayer).
        menu.Press(ExplorationMenuKey.Down); // member 1
        menu.Press(ExplorationMenuKey.Confirm);
        var four = menu.PendingMagicUse!;
        Assert.Equal(1, four.TargetRoleId);
        menu.CompleteMagicUse(four.Id, true);
        menu.CompleteMagicSuccess(four.Id, true);
        Assert.Equal(ExplorationMenuMode.MagicTargets, menu.Mode); // 6 >= 5
        Assert.Equal(6, roles.Roles[0].Mp);
        menu.Press(ExplorationMenuKey.Confirm); // member 1 again
        var five = menu.PendingMagicUse!;
        menu.CompleteMagicUse(five.Id, true);
        menu.CompleteMagicSuccess(five.Id, true);
        // uigame.c 824-834: the caster can no longer pay, so the target loop ends at the list.
        Assert.Equal(ExplorationMenuMode.MagicList, menu.Mode);
        Assert.Equal(1, roles.Roles[0].Mp);
        // The reopened list recomputes from the live MP: every magic is now disabled.
        Assert.Equal([false, false, false, false], menu.MagicListView().Select(row => row.Enabled).ToArray());
        // kKeyMenu closes the whole menu; the executed casts' MP stays committed (no rollback).
        menu.Press(ExplorationMenuKey.Cancel);
        Assert.Equal(ExplorationMenuMode.Closed, menu.Mode);
        Assert.Equal(1, roles.Roles[0].Mp);
        Assert.Equal((95, 30), (roles.Roles[1].Hp, roles.Roles[1].Mp)); // targets untouched by the menu
    }

    [Fact]
    public void Menu_cast_changes_flow_through_shared_owners_into_the_next_battle_opening()
    {
        // Story 75: the menu cast's script effects (through the world-bound trigger session the
        // host runs) and the menu's own MP deduction land in the shared live owners, and the
        // next formal battle opening consumes both — no second state copy anywhere.
        var owner = new RoleLiveState([Role(0, 100, mp: 20, magics: [310]), Role(1, 70, mp: 10)]);
        var (menu, _) = Menu(roles: owner);
        var fixture = new EventFixture();
        // L0001: the on-use script — script.c 0x001D heals the caller word (the target's role).
        fixture.Commands[1] = ChangeHpMp(1, 30);
        fixture.Commands[2] = End(2);
        fixture.Commands[3] = Battle(3);
        fixture.Files["shared"]!["entries"]!.AsArray().Add(JsonNode.Parse(
            """{"id":"success","owner":{"type":"eventObject","id":60},"slot":"wTriggerScript","kind":"trigger","target":"L0004","sourceAddress":4}""")!);
        fixture.Files["shared"]!["entries"]!.AsArray().Add(JsonNode.Parse(
            """{"id":"battle","owner":{"type":"eventObject","id":60},"slot":"wTriggerScript","kind":"trigger","target":"L0003","sourceAddress":3}""")!);
        var content = new TriggerBattleContent(
            [Role(0, 100, mp: 20, magics: [310]), Role(1, 70, mp: 10)],
            [new ClassicBattlePlayer(0, 100, 150, 40, 20, 100), new ClassicBattlePlayer(1, 70, 150, 40, 20, 100)],
            new Dictionary<int, ClassicBattleGrowthData> { [0] = Growth(), [1] = Growth() },
            new Dictionary<int, IReadOnlyList<ClassicBattleEnemy>> { [18] = [new ClassicBattleEnemy(7, 0, 43, 0, 0, 0, 1)] });
        var script = TriggerScriptSession.ForSharedRoles(fixture.Load(), TriggerScriptSessionTests.Language(),
            content, new BindingRandom(), owner, SharedWorld(owner));
        menu.Open();
        menu.Press(ExplorationMenuKey.Down);
        menu.Press(ExplorationMenuKey.Confirm); // caster select
        menu.Press(ExplorationMenuKey.Confirm); // member 0 → magic list (310 enabled at MP 20)
        menu.Press(ExplorationMenuKey.Confirm); // single-target → member select
        menu.Press(ExplorationMenuKey.Down);
        menu.Press(ExplorationMenuKey.Confirm); // member 1 commits the cast
        var cast = menu.PendingMagicUse!;
        Assert.Equal((310, false, 0, 1), (cast.ObjectId, cast.ApplyToAll, cast.CasterRoleId, cast.TargetRoleId));
        // Host runs the on-use script with the request's caller word; the changeHpMp verb heals
        // through the shared world into the shared owner (PAL_IncreaseHPMP raises HP and MP).
        script.Start("shared", "npc", cast.TargetRoleId);
        Assert.Equal(TriggerScriptPhase.Completed, script.Phase);
        Assert.True(script.ScriptSuccess);
        Assert.Equal((100, 40), (owner.Roles[1].Hp, owner.Roles[1].Mp));
        menu.CompleteMagicUse(cast.Id, script.ScriptSuccess);
        Assert.Equal(ExplorationMenuMode.MagicOnSuccess, menu.Mode);
        // Host runs the on-success script; both gates survived, so the menu deducts the caster.
        script.Start("shared", "success", cast.TargetRoleId);
        Assert.True(script.ScriptSuccess);
        menu.CompleteMagicSuccess(cast.Id, script.ScriptSuccess);
        Assert.Equal(ExplorationMenuMode.MagicTargets, menu.Mode); // 15 still covers cost 5
        Assert.Equal((100, 15), (owner.Roles[0].Hp, owner.Roles[0].Mp));
        // The subsequent formal battle opens from the same shared owners: the healed HP and the
        // menu-deducted MP are the battle's opening state (Story 75's cross-menu consumption).
        script.Start("shared", "battle", 0);
        var request = script.PendingBattle!;
        Assert.Equal((100, 15), (request.Lifecycle.Current.Roles[0].Hp, request.Lifecycle.Current.Roles[0].Mp));
        Assert.Equal((100, 40), (request.Lifecycle.Current.Roles[1].Hp, request.Lifecycle.Current.Roles[1].Mp));
    }

    [Fact]
    public void Failed_on_success_reopens_the_target_select_even_when_the_executed_script_spent_the_casters_mp()
    {
        // uigame.c 807-841: the exhaustion cancel (wPlayer = MENUITEM_VALUE_CANCELLED) is nested
        // inside the on-success branch's deduction. When the executed on-use script already
        // spent the caster's MP below the cost AND the on-success script fails, the reference
        // reopens the target select; only a successful, deducted cast can exit by exhaustion.
        var owner = new RoleLiveState([Role(0, 100, mp: 20, magics: [310]), Role(1, 95, mp: 30)]);
        var (menu, _) = Menu(roles: owner);
        var fixture = new EventFixture();
        // L0001: the on-use script — script.c 0x001D spends the caller (here the caster itself,
        // a self-cast): signed amount -18 moves the shared live MP 20 -> 2 (below cost 5).
        fixture.Commands[1] = ChangeHpMp(1, -18);
        fixture.Commands[2] = End(2);
        // L0003: the on-success script — script.c 0x0041 marks the run failed.
        fixture.Commands[3] = MarkFailed(3);
        fixture.Files["shared"]!["entries"]!.AsArray().Add(JsonNode.Parse(
            """{"id":"success","owner":{"type":"eventObject","id":60},"slot":"wTriggerScript","kind":"trigger","target":"L0003","sourceAddress":3}""")!);
        var content = new TriggerBattleContent(
            [Role(0, 100, mp: 20, magics: [310]), Role(1, 95, mp: 30)],
            [new ClassicBattlePlayer(0, 100, 150, 40, 20, 100), new ClassicBattlePlayer(1, 95, 150, 35, 18, 100)],
            new Dictionary<int, ClassicBattleGrowthData> { [0] = Growth(), [1] = Growth() },
            new Dictionary<int, IReadOnlyList<ClassicBattleEnemy>> { [18] = [new ClassicBattleEnemy(7, 0, 43, 0, 0, 0, 1)] });
        var script = TriggerScriptSession.ForSharedRoles(fixture.Load(), TriggerScriptSessionTests.Language(),
            content, new BindingRandom(), owner, SharedWorld(owner));
        menu.Open();
        menu.Press(ExplorationMenuKey.Down);
        menu.Press(ExplorationMenuKey.Confirm); // caster select
        menu.Press(ExplorationMenuKey.Confirm); // member 0 is alive → magic list (310 at MP 20)
        menu.Press(ExplorationMenuKey.Confirm); // single-target → member select
        menu.Press(ExplorationMenuKey.Confirm); // member 0 commits the self-cast
        var cast = menu.PendingMagicUse!;
        Assert.Equal((310, false, 0, 0), (cast.ObjectId, cast.ApplyToAll, cast.CasterRoleId, cast.TargetRoleId));
        // Host runs the on-use script with the target's role as the caller word (the caster
        // here); the script's signed changeHpMp spends the caster's shared live MP to 2.
        script.Start("shared", "npc", cast.TargetRoleId);
        Assert.Equal(TriggerScriptPhase.Completed, script.Phase);
        Assert.True(script.ScriptSuccess);
        Assert.Equal(2, owner.Roles[0].Mp);
        menu.CompleteMagicUse(cast.Id, script.ScriptSuccess);
        // Host runs the on-success script; it marks the run failed.
        script.Start("shared", "success", cast.TargetRoleId);
        Assert.False(script.ScriptSuccess);
        menu.CompleteMagicSuccess(cast.Id, script.ScriptSuccess);
        Assert.True(menu.Mode == ExplorationMenuMode.MagicTargets,
            "PAL_MAGIC_SUCCESS_EXIT_RED: uigame.c 807-841 - a failed on-success reopens the target select; the exhaustion exit belongs only to the deducted successful branch");
        Assert.Equal(2, owner.Roles[0].Mp); // no deduction, and the script's spend stays committed
    }

    private static InvalidDataException Reject(string mutation, string expected)
    {
        var data = MagicContextData();
        switch (mutation)
        {
            case "objectId0": data["magicMenu"]![0]!["objectId"] = 0; break;
            case "costHigh": data["magicMenu"]![0]!["costMp"] = 65536; break;
            case "typo": data["magicMenu"]![0]!["costMP"] = 5; break;
            case "applyToAllString": data["magicMenu"]![0]!["applyToAll"] = "yes"; break;
            case "duplicate": ((JsonArray)data["magicMenu"]!).Add(
                new JsonObject { ["objectId"] = 310, ["costMp"] = 1, ["applyToAll"] = false, ["usableOutsideBattle"] = true }); break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }
        return Assert.Throws<InvalidDataException>(() => ModernBattleContent.Parse(data.ToJsonString()));
    }

    private static (ExplorationMenuSession menu, RoleLiveState roles) Menu(
        RoleLiveState? roles = null, bool noMagicMenu = false, bool duplicateBinding = false,
        bool missingMagicWord = false, int role1Hp = 95, int role0Hp = 100, bool singleMember = false)
    {
        int[] casterMagics = [313, 311, 310, 312]; // authored slot order; the list sorts by WORD
        var owner = roles ?? new RoleLiveState([Role(0, role0Hp, mp: 20, magics: casterMagics), Role(1, role1Hp, mp: 30)]);
        var words = new Dictionary<int, string>
        {
            [ExplorationMenuSession.StatusEntryWord] = "状态",
            [ExplorationMenuSession.MagicEntryWord] = "法术",
            [ExplorationMenuSession.LevelWord] = "等级",
            [ExplorationMenuSession.HpWord] = "生命",
            [ExplorationMenuSession.MpWord] = "法力",
            [60100] = "大侠",
            [60101] = "小侠",
            [310] = "回春术",
            [311] = "清风扇",
            [312] = "烈焰咒",
            [313] = "天雷诀",
        };
        if (missingMagicWord) words.Remove(314);
        List<BattleMagicMenuBinding> boundMagics =
        [
            new(310, 5, false, true),
            new(311, 4, true, true),
            new(312, 3, false, false),
            new(313, 99, false, true),
        ];
        if (missingMagicWord) boundMagics.Add(new BattleMagicMenuBinding(314, 1, false, true));
        IReadOnlyList<BattleMagicMenuBinding>? magicMenu = noMagicMenu ? null
            : duplicateBinding ? [new BattleMagicMenuBinding(310, 5, false, true), new BattleMagicMenuBinding(310, 1, true, true)]
            : boundMagics;
        var party = singleMember
            ? new List<ExplorationMenuMember> { new(0, 60100, false, 40, 6, 20, 100, 3) }
            : [new ExplorationMenuMember(0, 60100, false, 40, 6, 20, 100, 3),
               new ExplorationMenuMember(1, 60101, false, 35, 5, 18, 99, 2)];
        var equipment = BattleEquipmentState.Initialize(
            [new int[6], new int[6], new int[6], new int[6], new int[6], new int[6]],
            new Dictionary<int, BattleEquipmentObject>());
        var session = new ExplorationMenuSession(
            [new(ExplorationMenuPage.Status, ExplorationMenuSession.StatusEntryWord),
             new(ExplorationMenuPage.Magic, ExplorationMenuSession.MagicEntryWord)],
            party, owner, equipment,
            new Dictionary<int, ClassicBattleGrowthData> { [0] = Growth(), [1] = Growth() },
            words, magicMenu: magicMenu);
        return (session, owner);
    }

    private static DynamicEventWorld SharedWorld(RoleLiveState owner) => DynamicEventWorld.ForHost(
        new StaticSceneRules(new SceneMap(2, new uint[16384], [new SceneFrame(0, 32, 15)]), []),
        new StaticLeader(320, 320, 0, 0, 0, 3,
            new SceneSprite(17, Enumerable.Range(0, 12).Select(i => new SceneFrame(i, 20, 30)))), 2, 0, 0, owner);

    private static JsonObject ChangeHpMp(int address, int amount) => new()
    {
        ["label"] = $"L{address:X4}", ["op"] = "changeHpMp", ["scope"] = 0, ["amount"] = amount,
        ["next"] = $"L{address + 1:X4}",
        ["source"] = new JsonObject { ["index"] = address, ["opcode"] = 0x1d,
            ["operands"] = new JsonArray(0, unchecked((ushort)amount), 0) }
    };

    private static JsonObject End(int address) => new()
    {
        ["label"] = $"L{address:X4}", ["op"] = "end", ["mode"] = "keep", ["next"] = $"L{address + 1:X4}",
        ["source"] = new JsonObject { ["index"] = address, ["opcode"] = 0, ["operands"] = new JsonArray(0, 0, 0) }
    };

    private static JsonObject MarkFailed(int address) => new()
    {
        ["label"] = $"L{address:X4}", ["op"] = "markFailed", ["next"] = $"L{address + 1:X4}",
        ["source"] = new JsonObject { ["index"] = address, ["opcode"] = 0x41, ["operands"] = new JsonArray(0, 0, 0) }
    };

    private static JsonObject Battle(int address, int team = 18, string? loss = "L0004", bool boss = true) => new()
    {
        ["label"] = $"L{address:X4}", ["op"] = "startBattle", ["enemyTeamId"] = team, ["lossTarget"] = loss,
        ["isBoss"] = boss, ["fleeTarget"] = null, ["next"] = $"L{address + 1:X4}",
        ["source"] = new JsonObject { ["index"] = address, ["opcode"] = 7, ["operands"] = new JsonArray(99, 3, 2) }
    };

    private static BattleRoleSnapshot Role(int roleId, int hp, int mp = 20, int[]? magics = null)
    {
        var learned = new int[32];
        if (magics is not null) Array.Copy(magics, learned, magics.Length);
        return new(roleId, 2, hp, 150, mp, 50, new BattleExperienceSnapshot(77, 3, 12), Hidden(),
            new int[6], learned, new int[9], 0, 0, 0, 0, 0, 0);
    }

    private static ClassicBattleGrowthData Growth() => new(Enumerable.Repeat(100, 100).ToArray(), []);

    private static IReadOnlyDictionary<string, BattleExperienceSnapshot> Hidden() =>
        new Dictionary<string, BattleExperienceSnapshot>
        {
            ["health"] = new(0, 1, 0), ["magic"] = new(0, 1, 0), ["attack"] = new(0, 1, 0),
            ["magicPower"] = new(0, 1, 0), ["defense"] = new(0, 1, 0), ["dexterity"] = new(0, 1, 0),
            ["flee"] = new(0, 1, 0),
        };

    private static JsonObject MagicContextData()
    {
        var data = Data();
        data["magicMenu"] = new JsonArray(
            new JsonObject { ["objectId"] = 310, ["costMp"] = 5, ["applyToAll"] = false, ["usableOutsideBattle"] = true },
            new JsonObject { ["objectId"] = 311, ["costMp"] = 4, ["applyToAll"] = true, ["usableOutsideBattle"] = true },
            new JsonObject { ["objectId"] = 312, ["costMp"] = 3, ["applyToAll"] = false, ["usableOutsideBattle"] = false },
            new JsonObject { ["objectId"] = 313, ["costMp"] = 99, ["applyToAll"] = false, ["usableOutsideBattle"] = true });
        return data;
    }

    private static JsonObject Data()
    {
        var data = JsonNode.Parse("""
        {"formatVersion":1,"roles":[{
          "roleId":0,"level":2,"hp":100,"maxHp":150,"mp":20,"maxMp":50,
          "primaryExperience":{"value":5,"level":2,"count":17},
          "hiddenExperience":{
            "health":{"value":7,"level":3,"count":55},"magic":{"value":7,"level":3,"count":55},
            "attack":{"value":7,"level":3,"count":55},"magicPower":{"value":7,"level":3,"count":55},
            "defense":{"value":7,"level":3,"count":55},"dexterity":{"value":7,"level":3,"count":55},
            "flee":{"value":7,"level":3,"count":55}},
          "equipment":[0,0,0,0,0,0],"magics":[],"statuses":[0,0,0,0,0,0,0,0,0],"coveredByRoleId":0}],
         "players":[{"roleId":0,"hp":100,"maxHp":150,"attack":40,"defense":20,"dexterity":100,
          "equipment":[0,0,0,0,0,0],"statuses":[0,0,0,0,0,0,0,0,0],"poisons":[0],
          "dyingScript":0,"friendDeathScript":0,"coveredByRoleId":null,"attackAll":false}],
         "growth":[{"roleId":0,"levelUpExperience":[],"levelUpMagics":[]}],
         "teams":[{"teamId":18,"enemies":[{"id":7,"level":0,"hp":43,"attack":0,"defense":0,"dexterity":0,"physicalResistance":1,
          "statuses":[0,0,0,0,0,0,0,0,0],"poisons":[0],"turnStartScript":0,"readyScript":0,"battleEndScript":0,
          "magicId":0,"dualMove":0,"attackEquivItem":0,"attackEquivItemRate":0,"experience":0,"cash":0}]}]}
        """)!.AsObject();
        data["growth"]![0]!["levelUpExperience"] = new JsonArray(
            Enumerable.Repeat(0, 100).Select(_ => (JsonNode?)JsonValue.Create(100)).ToArray());
        data["roles"]![0]!["magics"] = new JsonArray(
            Enumerable.Repeat(0, 32).Select(_ => (JsonNode?)JsonValue.Create(0)).ToArray());
        return data;
    }

    // External random seam copied from the battle-binding tests: unit multipliers.
    private sealed class BindingRandom : IBattleRandom
    {
        public int NextInclusive(int from, int to) => from == 0 && to == 5 ? 1 : from;
        public float NextInclusive(float from, float to) => 1;
    }
}
