using Pal.Core;
using Xunit;

namespace Pal.Core.Tests;

/// <summary>M6-C leaf #224 (parent #148): the system-menu layer's five-slot save flow on the
/// exploration menu session. Independent oracles are the frozen #221/#223 contracts (five
/// manual slots, request identity bound to one committed boundary), the fixed reference shape
/// (uigame.c PAL_SystemMenu/PAL_SaveSlotMenu, ui.c PAL_ReadMenu wrap) and hand-written literal
/// expectations — never values produced by the session under test.</summary>
public sealed class ExplorationMenuSaveTests
{
    [Fact]
    public void Slot_browsing_moves_only_the_remembered_cursor_and_confirms_exactly_one_request()
    {
        var menu = Menu();
        menu.Open();
        menu.Press(ExplorationMenuKey.Down);
        menu.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(ExplorationMenuMode.SystemMenu, menu.Mode);

        // Stories 3/4: browsing (every direction pair, both wraps included) only moves the
        // remembered slot cursor — never a request, never a write, never a switch.
        string before = Describe(menu);
        Assert.Equal(PlayerSaveContract.FirstSlot, menu.SelectedSaveSlot);
        menu.Press(ExplorationMenuKey.Up);
        Assert.Equal(PlayerSaveContract.LastSlot, menu.SelectedSaveSlot);
        menu.Press(ExplorationMenuKey.Down);
        Assert.Equal(PlayerSaveContract.FirstSlot, menu.SelectedSaveSlot);
        menu.Press(ExplorationMenuKey.Down);
        Assert.Equal(PlayerSaveContract.FirstSlot + 1, menu.SelectedSaveSlot);
        menu.Press(ExplorationMenuKey.Up);
        Assert.Equal(PlayerSaveContract.FirstSlot, menu.SelectedSaveSlot);
        menu.Press(ExplorationMenuKey.Left);
        Assert.Equal(PlayerSaveContract.LastSlot, menu.SelectedSaveSlot);
        menu.Press(ExplorationMenuKey.Right);
        Assert.Equal(PlayerSaveContract.FirstSlot, menu.SelectedSaveSlot);
        Assert.Null(menu.PendingSave);
        Assert.Equal(before, Describe(menu));

        // Story 12: Confirm issues exactly one pending save request for the browsed slot and
        // parks the layer — a duplicate confirm, a move and a cancel all reject without ever
        // issuing a second or altering the parked request.
        menu.Press(ExplorationMenuKey.Down);
        menu.Press(ExplorationMenuKey.Confirm);
        var pending = menu.PendingSave;
        Assert.NotNull(pending);
        Assert.Equal(PlayerSaveContract.FirstSlot + 1, pending!.Slot);
        Assert.NotEqual(default, pending.Id);
        Assert.Throws<InvalidOperationException>(() => menu.Press(ExplorationMenuKey.Confirm));
        Assert.Throws<InvalidOperationException>(() => menu.Press(ExplorationMenuKey.Down));
        Assert.Throws<InvalidOperationException>(() => menu.Press(ExplorationMenuKey.Cancel));
        Assert.Same(pending, menu.PendingSave);

        // Only the owned request id completes the parking; a foreign id rejects.
        Assert.Throws<InvalidOperationException>(() => menu.CompleteSave(Guid.NewGuid(), succeeded: true));
        Assert.Same(pending, menu.PendingSave);
    }

    [Fact]
    public void CompleteSave_receipts_return_browsing_for_both_outcomes_and_the_cursor_survives_closes()
    {
        var menu = Menu();
        menu.Open();
        menu.Press(ExplorationMenuKey.Down);
        menu.Press(ExplorationMenuKey.Confirm);
        menu.Press(ExplorationMenuKey.Down); // browse to slot 2
        menu.Press(ExplorationMenuKey.Down); // browse to slot 3
        menu.Press(ExplorationMenuKey.Confirm);
        var first = menu.PendingSave!;
        Assert.Equal(3, first.Slot);

        // A failed write (the host's storage outcome) returns the layer to browsing identically;
        // the session keeps no outcome of its own — the host owns the report and the recovery slot.
        menu.CompleteSave(first.Id, succeeded: false);
        Assert.Null(menu.PendingSave);
        Assert.Equal(ExplorationMenuMode.SystemMenu, menu.Mode);
        Assert.Equal(3, menu.SelectedSaveSlot);

        // A second confirmed operation is a new request with a fresh identity for another slot.
        menu.Press(ExplorationMenuKey.Up);
        menu.Press(ExplorationMenuKey.Up); // browse back to slot 1
        menu.Press(ExplorationMenuKey.Confirm);
        var second = menu.PendingSave!;
        Assert.Equal(1, second.Slot);
        Assert.NotEqual(first.Id, second.Id);
        menu.CompleteSave(second.Id, succeeded: true);
        Assert.Null(menu.PendingSave);

        // kKeyMenu returns to the main menu; the remembered slot cursor survives closes exactly
        // like the main-menu cursor (gpGlobals->iCurMainMenuItem), which still sits on the
        // system entry — a re-entered layer confirms the same slot without a move.
        menu.Press(ExplorationMenuKey.Cancel);
        Assert.Equal(ExplorationMenuMode.MainMenu, menu.Mode);
        menu.Press(ExplorationMenuKey.Cancel);
        Assert.Equal(ExplorationMenuMode.Closed, menu.Mode);
        menu.Open();
        Assert.Equal(1, menu.SelectedEntry);
        menu.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(ExplorationMenuMode.SystemMenu, menu.Mode);
        Assert.Equal(1, menu.SelectedSaveSlot);
        menu.Press(ExplorationMenuKey.Confirm);
        var third = menu.PendingSave!;
        Assert.Equal(1, third.Slot);
        menu.CompleteSave(third.Id, succeeded: true);
    }

    /// <summary>Every public word the save flow reads, in one comparable string, for the
    /// browsing-changes-nothing assertion.</summary>
    private static string Describe(ExplorationMenuSession menu) =>
        string.Join(";", menu.Mode, menu.SelectedEntry, menu.SelectedSaveSlot, menu.Entries.Count);

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

    /// <summary>Minimal real session over hand-written literal owners: one status entry plus the
    /// formal #222/#224 system entry over one shared live role.</summary>
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
}
