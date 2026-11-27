using Pal.Core;
using Xunit;
using Pal.Core.State;
using Pal.Core.Battle;
using Pal.Core.Menu;
using Pal.Core.Save;

namespace Pal.Core.Tests;

/// <summary>M6-C leaf #227 (parent #148): the formal system-menu load flow on the exploration
/// menu session. Independent oracles are the frozen #221/#223 contracts (five manual slots,
/// request identity), the fixed reference shape (uigame.c PAL_SystemMenu's SAVE/LOAD item rows
/// over the shared PAL_SaveSlotMenu, ui.c PAL_ReadMenu wrap, gpGlobals->iCurSystemMenuItem)
/// and hand-written literal expectations — never values produced by the session under test.</summary>
public sealed class ExplorationMenuLoadTests
{
    [Fact]
    public void System_items_layer_lists_save_and_load_and_opens_the_slot_list_per_intent()
    {
        var menu = Menu();
        menu.Open();
        menu.Press(ExplorationMenuKey.Down);
        menu.Press(ExplorationMenuKey.Confirm);
        // #227: the reference system menu (uigame.c PAL_SystemMenu) lists its SAVE/LOAD rows and
        // only then opens the shared PAL_SaveSlotMenu per chosen intent — #224's collapsed
        // save-only slot layer cannot express a load intent at all.
        Assert.True(menu.Mode != ExplorationMenuMode.SystemMenu,
            "SYSTEM_LOAD_RED: the system entry must open the reference system-item layer (uigame.c PAL_SystemMenu's SAVE/LOAD rows) before the shared five-slot list, so a load intent can be chosen");
        Assert.Equal(ExplorationMenuMode.SystemItems, menu.Mode);

        // The item rows resolve their prepared words; the remembered cursor starts at SAVE and
        // browsing (both direction pairs, wrap included) only moves it — never a request.
        Assert.Equal(2, menu.SystemItemCount);
        Assert.Equal("存档", menu.SystemItemLabel(0));
        Assert.Equal("读取", menu.SystemItemLabel(1));
        Assert.Equal(0, menu.SelectedSystemItem);
        Assert.Null(menu.PendingSave);
        Assert.Null(menu.PendingLoad);
        menu.Press(ExplorationMenuKey.Up);
        Assert.Equal(1, menu.SelectedSystemItem);
        menu.Press(ExplorationMenuKey.Up);
        Assert.Equal(0, menu.SelectedSystemItem);
        menu.Press(ExplorationMenuKey.Down);
        Assert.Equal(1, menu.SelectedSystemItem);
        menu.Press(ExplorationMenuKey.Left);
        Assert.Equal(0, menu.SelectedSystemItem);
        menu.Press(ExplorationMenuKey.Right);
        Assert.Equal(1, menu.SelectedSystemItem);
        Assert.Null(menu.PendingSave);
        Assert.Null(menu.PendingLoad);

        // Confirm on SAVE opens the shared slot layer with the save intent (#224's flow); the
        // remembered slot cursor starts at the first manual slot.
        menu.Press(ExplorationMenuKey.Up); // back to SAVE
        menu.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(ExplorationMenuMode.SystemMenu, menu.Mode);
        Assert.Equal(ExplorationSystemIntent.Save, menu.SystemIntent);
        Assert.Equal(PlayerSaveContract.FirstSlot, menu.SelectedSaveSlot);

        // kKeyMenu returns from the slot layer to the system items; the remembered item cursor
        // survives, and Confirm on LOAD reopens the slot layer with the load intent.
        menu.Press(ExplorationMenuKey.Cancel);
        Assert.Equal(ExplorationMenuMode.SystemItems, menu.Mode);
        Assert.Equal(0, menu.SelectedSystemItem);
        menu.Press(ExplorationMenuKey.Down);
        menu.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(ExplorationMenuMode.SystemMenu, menu.Mode);
        Assert.Equal(ExplorationSystemIntent.Load, menu.SystemIntent);

        // kKeyMenu leaves the load-intent slot layer back to the system items, then the main
        // menu; a reopen keeps the remembered system-item cursor (iCurSystemMenuItem).
        menu.Press(ExplorationMenuKey.Cancel);
        Assert.Equal(ExplorationMenuMode.SystemItems, menu.Mode);
        menu.Press(ExplorationMenuKey.Cancel);
        Assert.Equal(ExplorationMenuMode.MainMenu, menu.Mode);
        menu.Press(ExplorationMenuKey.Cancel);
        Assert.Equal(ExplorationMenuMode.Closed, menu.Mode);
        menu.Open();
        Assert.Equal(1, menu.SelectedEntry);
        menu.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(ExplorationMenuMode.SystemItems, menu.Mode);
        Assert.Equal(1, menu.SelectedSystemItem);
        menu.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(ExplorationMenuMode.SystemMenu, menu.Mode);
        Assert.Equal(ExplorationSystemIntent.Load, menu.SystemIntent);
    }

    [Fact]
    public void Load_confirm_parks_exactly_one_request_and_only_the_owned_receipt_completes()
    {
        var menu = Menu();
        menu.Open();
        EnterLoadSlotLayer(menu);
        menu.Press(ExplorationMenuKey.Down);
        menu.Press(ExplorationMenuKey.Down); // browse to slot 3

        // Confirm issues exactly one pending load request for the browsed slot and parks the
        // layer — never a save request; a duplicate confirm, a move and a cancel all reject
        // without ever issuing a second or altering the parked request (#148 Stories 12/31).
        menu.Press(ExplorationMenuKey.Confirm);
        var pending = menu.PendingLoad;
        Assert.NotNull(pending);
        Assert.Equal(3, pending!.Slot);
        Assert.NotEqual(default, pending.Id);
        Assert.Null(menu.PendingSave);
        Assert.Throws<InvalidOperationException>(() => menu.Press(ExplorationMenuKey.Confirm));
        Assert.Throws<InvalidOperationException>(() => menu.Press(ExplorationMenuKey.Up));
        Assert.Throws<InvalidOperationException>(() => menu.Press(ExplorationMenuKey.Cancel));
        Assert.Same(pending, menu.PendingLoad);

        // Only the owned request id completes the parking; foreign and stale ids reject, so an
        // old session's receipt can never complete anything on a kept or rebuilt session.
        Assert.Throws<InvalidOperationException>(() => menu.CompleteLoad(Guid.NewGuid(), succeeded: false));
        Assert.Same(pending, menu.PendingLoad);
        Assert.Throws<InvalidOperationException>(() => menu.CompleteSave(pending.Id, succeeded: true));
        Assert.Same(pending, menu.PendingLoad);

        // A failed read (the host's storage/validation outcome) returns the layer to browsing
        // identically; the session keeps no outcome of its own — the host owns the report and
        // the recovery slot, and the load intent stays for another confirmed read.
        menu.CompleteLoad(pending.Id, succeeded: false);
        Assert.Null(menu.PendingLoad);
        Assert.Equal(ExplorationMenuMode.SystemMenu, menu.Mode);
        Assert.Equal(ExplorationSystemIntent.Load, menu.SystemIntent);
        Assert.Equal(3, menu.SelectedSaveSlot);

        // A second confirmed read is a new request with a fresh identity for another slot.
        menu.Press(ExplorationMenuKey.Up);
        menu.Press(ExplorationMenuKey.Confirm);
        var second = menu.PendingLoad;
        Assert.NotNull(second);
        Assert.Equal(2, second!.Slot);
        Assert.NotEqual(pending.Id, second.Id);
        menu.CompleteLoad(second.Id, succeeded: true);
        Assert.Null(menu.PendingLoad);
    }

    [Fact]
    public void Both_intents_share_the_remembered_slot_cursor_and_never_leak_requests()
    {
        var menu = Menu();
        menu.Open();
        EnterLoadSlotLayer(menu);
        menu.Press(ExplorationMenuKey.Down);
        menu.Press(ExplorationMenuKey.Down);
        menu.Press(ExplorationMenuKey.Down);
        menu.Press(ExplorationMenuKey.Down); // browse to slot 5 (uigame.c PAL_SaveSlotMenu wrap)

        // The load intent confirms slot 5; cancelling back and reopening through the SAVE item
        // re-enters the same shared slot layer with the slot cursor still remembered — exactly
        // the reference's one PAL_SaveSlotMenu over both system items.
        menu.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(5, menu.PendingLoad!.Slot);
        menu.CompleteLoad(menu.PendingLoad.Id, succeeded: false);
        menu.Press(ExplorationMenuKey.Cancel); // slot layer -> system items
        menu.Press(ExplorationMenuKey.Up);     // remembered item cursor is LOAD; wrap to SAVE
        menu.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(ExplorationSystemIntent.Save, menu.SystemIntent);
        Assert.Equal(5, menu.SelectedSaveSlot);
        Assert.Null(menu.PendingLoad);

        // The save intent confirms the remembered slot as a save request, never a load one.
        menu.Press(ExplorationMenuKey.Confirm);
        Assert.NotNull(menu.PendingSave);
        Assert.Null(menu.PendingLoad);
        Assert.Equal(5, menu.PendingSave!.Slot);
        Assert.Throws<InvalidOperationException>(() => menu.CompleteLoad(menu.PendingSave.Id, succeeded: false));
        menu.CompleteSave(menu.PendingSave.Id, succeeded: true);
        Assert.Null(menu.PendingSave);
        Assert.Null(menu.PendingLoad);
    }

    /// <summary>Drives the formal entry path into the load-intent slot layer: main menu → system
    /// entry → the system items' LOAD row (#227's reference structure).</summary>
    private static void EnterLoadSlotLayer(ExplorationMenuSession menu)
    {
        menu.Press(ExplorationMenuKey.Down);
        menu.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(ExplorationMenuMode.SystemItems, menu.Mode);
        menu.Press(ExplorationMenuKey.Down); // the LOAD row
        menu.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(ExplorationMenuMode.SystemMenu, menu.Mode);
        Assert.Equal(ExplorationSystemIntent.Load, menu.SystemIntent);
    }

    /// <summary>Minimal real session over hand-written literal owners: one status entry plus the
    /// formal #222/#224 system entry over one shared live role, with the system-item words the
    /// reference's SAVE/LOAD rows resolve to.</summary>
    private static ExplorationMenuSession Menu() => new(
        [new ExplorationMenuEntry(ExplorationMenuPage.Status, ExplorationMenuSession.StatusEntryWord),
            new ExplorationMenuEntry(ExplorationMenuPage.System, ExplorationMenuSession.SystemEntryWord)],
        [new ExplorationMenuMember(0, 60100, false, 40, 6, 20, 100, 3)],
        new RoleLiveState([Role(0)]),
        BattleEquipmentState.Initialize(
            Enumerable.Range(0, 6).Select(_ => (IReadOnlyList<int>)new int[6]).ToArray(),
            new Dictionary<int, BattleEquipmentObject>()),
        new Dictionary<int, ClassicBattleGrowthData> { [0] = new([9, 10, 11, 111], []) },
        new Dictionary<int, string>
        {
            [ExplorationMenuSession.StatusEntryWord] = "状态",
            [ExplorationMenuSession.SystemEntryWord] = "系统",
            [ExplorationMenuSession.SaveItemWord] = "存档",
            [ExplorationMenuSession.LoadItemWord] = "读取",
            [ExplorationMenuSession.HpWord] = "生命",
            [ExplorationMenuSession.ExperienceWord] = "经验",
            [ExplorationMenuSession.LevelWord] = "等级",
            [ExplorationMenuSession.MpWord] = "法力",
            [ExplorationMenuSession.AttackPowerWord] = "攻击力",
            [ExplorationMenuSession.MagicPowerWord] = "灵力",
            [ExplorationMenuSession.DefenseWord] = "防御",
            [ExplorationMenuSession.DexterityWord] = "身法",
            [ExplorationMenuSession.FleeRateWord] = "吉运",
            [60100] = "大侠",
        });

    private static BattleRoleSnapshot Role(int roleId) => new(roleId, 2, 100, 150, 20, 50,
        new BattleExperienceSnapshot(77, 3, 12), Hidden(), new int[6], new int[32], new int[9], 0,
        40, 6, 20, 100, 3);

    private static IReadOnlyDictionary<string, BattleExperienceSnapshot> Hidden() =>
        new Dictionary<string, BattleExperienceSnapshot>
        {
            ["health"] = new(0, 1, 0), ["magic"] = new(0, 1, 0), ["attack"] = new(0, 1, 0),
            ["magicPower"] = new(0, 1, 0), ["defense"] = new(0, 1, 0), ["dexterity"] = new(0, 1, 0),
            ["flee"] = new(0, 1, 0),
        };
}
