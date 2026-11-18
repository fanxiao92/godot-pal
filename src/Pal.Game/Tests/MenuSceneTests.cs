using System;
using System.Linq;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using Pal.Core;
using Pal.Game.Dialogue;
using static GdUnit4.Assertions;

namespace Pal.Game.Tests;

/// <summary>#213 exploration menu host + status page on the formal story Main (SceneRunner,
/// real input events, real update-completion boundaries). Independent numeric expectations come
/// from the committed authored input samples/authored-battle/PartyWin/Numbers/battle-data.json
/// (role0 level2 HP100/150 MP20/50 primary exp0, player panels 40/-/20/100/-, growth threshold
/// 100 at level 2; role1 HP95 panels 35/-/18/100/-) and the reference Won half-recovery
/// (battle.c PAL_BattleWon: (150-100)/2 -> 125). No test-only host or state shortcut exists.</summary>
[TestSuite, RequireGodotRuntime]
public class MenuSceneTests
{
    [TestCase("zh-Hans", Timeout = 60000)]
    [TestCase("en", Timeout = 60000)]
    public async Task MenuOpensFromExplorationShowsLiveStatusAndCancelsBack(string language)
    {
        bool zh = language == "zh-Hans";
        using var fixture = new AuthoredStoryFixture("PartyWin");
        var selection = GameSceneConfiguration.FromArguments("--story-profile", fixture.StoryUri,
            "--battle-visual-profile", fixture.VisualUri, "--battle-seed", "1", "--story-language", language);
        using var host = new MenuHost(selection);
        host.AssertHealthy();
        await host.Wait(() => host.Updates.Count > 0);
        AssertThat(host.Main.Menu).IsNotNull();
        AssertThat(host.Main.Menu!.Mode).IsEqual(ExplorationMenuMode.Closed);

        // input.c kKeyMenu (Escape) opens the in-game menu at a real update boundary (play.c 551).
        host.Tap(Key.Escape);
        await host.Wait(() => host.Main.Menu!.Mode == ExplorationMenuMode.MainMenu);
        AssertThat(host.Main.MenuView).IsNotNull();
        AssertThat(host.Main.Menu!.EntryLabel(0)).IsEqual(zh ? "状态" : "STATUS");

        // While the menu owns input the exploration world stays frozen: held direction presses
        // never reach the exploration session across several real callbacks.
        var frozen = host.Main.Exploration!;
        host.Press(Key.Down);
        await host.AwaitUpdates(3);
        AssertThat(ReferenceEquals(host.Main.Exploration, frozen)).IsTrue();
        host.Release(Key.Down);

        // Search enters the status page at member 0 with live shared-state values.
        host.Tap(Key.Enter);
        await host.Wait(() => host.Main.Menu!.Mode == ExplorationMenuMode.Status);
        var view = host.Main.Menu!.StatusView();
        AssertThat(view.Name).IsEqual(zh ? "大侠" : "HERO");
        AssertThat((view.Level, view.Hp, view.MaxHp, view.Mp, view.MaxMp, view.Experience, view.NextLevelExperience))
            .IsEqual((2, 100, 150, 20, 50, 0, 100));
        AssertThat((view.Attack, view.MagicPower, view.Defense, view.Dexterity, view.FleeRate))
            .IsEqual((40, 0, 20, 100, 0));
        AssertThat(view.Equipment.Count).IsEqual(0);
        AssertThat(view.IsDead).IsFalse();
        AssertThat((view.LevelLabel, view.HpLabel, view.MpLabel, view.ExperienceLabel)).IsEqual(zh
            ? ("等级", "生命", "法力", "经验") : ("LEVEL", "HP", "MP", "EXP"));
        AssertThat((view.AttackLabel, view.MagicPowerLabel, view.DefenseLabel, view.DexterityLabel, view.FleeRateLabel))
            .IsEqual(zh ? ("攻击力", "灵力", "防御", "身法", "吉运")
                : ("ATTACK POWER", "MAGIC POWER", "DEFENSE", "DEXTERITY", "FLEE RATE"));

        // uigame.c PAL_PlayerStatus: Right advances to the next party member.
        host.Tap(Key.Right);
        await host.Wait(() => host.Main.Menu!.StatusMemberIndex == 1);
        var second = host.Main.Menu!.StatusView();
        AssertThat(second.Name).IsEqual(zh ? "小侠" : "HERO TWO");
        AssertThat((second.Hp, second.MaxHp, second.Attack, second.Defense)).IsEqual((95, 150, 35, 18));

        // Confirm past the last member closes the whole menu; the closing Search press must not
        // leak into the owner1 NPC trigger (uigame.c 'goto out').
        host.Tap(Key.Enter);
        await host.Wait(() => host.Main.Menu!.Mode == ExplorationMenuMode.Closed);
        AssertThat(host.Main.MenuView).IsNull();
        AssertThat(host.Main.Interaction!.PendingTrigger).IsNull();
        AssertThat(host.Main.Interaction!.Phase).IsEqual(NpcInteractionPhase.Exploring);

        // Exploration input resumes on the next real update and the search works normally again.
        var before = host.Main.Exploration!;
        host.Press(Key.Down);
        await host.Wait(() => !ReferenceEquals(host.Main.Exploration, before));
        host.Release(Key.Down);
        host.Tap(Key.Enter);
        await host.Wait(() => host.Main.Interaction!.PendingTrigger is not null);
        AssertThat(host.Main.Menu!.Mode).IsEqual(ExplorationMenuMode.Closed);
    }

    [TestCase(Timeout = 120000)]
    public async Task MenuIsUnreachableDuringDialogueAndBattleUntilExplorationResumes()
    {
        using var fixture = new AuthoredStoryFixture("PartyWin");
        var selection = GameSceneConfiguration.FromArguments("--story-profile", fixture.StoryUri,
            "--battle-visual-profile", fixture.VisualUri, "--battle-seed", "1");
        using var host = new MenuHost(selection);
        host.AssertHealthy();
        await host.Wait(() => host.Updates.Count > 0);

        // Active dialogue owns Search/Menu (text.c wait loop): the menu key never opens a menu.
        host.Tap(Key.Enter);
        await host.Wait(() => host.Main.Dialogue!.Session!.PendingDisplay is not null);
        host.Tap(Key.Escape);
        await host.AwaitUpdates(3);
        AssertThat(host.Main.Menu!.Mode).IsEqual(ExplorationMenuMode.Closed);
        await host.FinishDisplay();

        // The battle owns kKeyMenu as its own cancel (uibattle.c); the exploration menu stays
        // closed through target menus and the whole owned battle, including its result path.
        await host.Wait(() => host.Main.StoryBattleMode == StoryBattlePresentation.AttackMenu);
        host.Tap(Key.Escape);
        await host.AwaitUpdates(2);
        AssertThat(host.Main.Menu!.Mode).IsEqual(ExplorationMenuMode.Closed);
        AssertThat(host.Main.StoryBattleMode).IsEqual(StoryBattlePresentation.AttackMenu);
        await host.ConfirmBattleMenu(StoryBattlePresentation.TargetMenu);
        // Target cancel returns to the move menu (uibattle.c 1512); the menu stays closed.
        host.Tap(Key.Escape);
        await host.Wait(() => host.Main.StoryBattleMode == StoryBattlePresentation.AttackMenu);
        AssertThat(host.Main.Menu!.Mode).IsEqual(ExplorationMenuMode.Closed);
        await host.ConfirmBattleMenu(StoryBattlePresentation.TargetMenu);
        await host.ConfirmBattleMenu(StoryBattlePresentation.AttackMenu); // member 1 selects
        await host.ConfirmBattleMenu(StoryBattlePresentation.TargetMenu);
        await host.ConfirmBattleMenu(StoryBattlePresentation.Resolving);
        // Seed-1 PartyWin cannot settle in one round (member 1's authored hit is 33..38 against
        // 43hp slots; the cycling precedent finishes slot 1 in round 2), so every further round
        // selects through the real target menus until the owned battle settles exactly once.
        var script = host.Main.Dialogue!.Session!;
        while (script.PendingBattle is not null)
        {
            await host.Wait(() => host.Main.StoryBattleMode == StoryBattlePresentation.AttackMenu
                || script.PendingBattle is null);
            if (script.PendingBattle is null) break;
            await host.ConfirmBattleMenu(StoryBattlePresentation.TargetMenu);
            // The target-confirm Enter either starts the next alive member's selection or, for
            // the final selecting member, builds the queue and enters Resolving.
            var combat = host.Main.StoryBattle!.Current.Combat;
            int selecting = combat.SelectingPlayerIndex
                ?? throw new InvalidOperationException("Target menu has no selecting member.");
            bool final = Enumerable.Range(selecting + 1, combat.Players.Count - selecting - 1)
                .All(index => combat.Players[index].Hp == 0);
            await host.ConfirmBattleMenu(final
                ? StoryBattlePresentation.Resolving : StoryBattlePresentation.AttackMenu);
        }
        // The won display parks over the result path.
        await host.Wait(() => host.Main.StoryBattleMode is null && host.Main.Dialogue!.Session!.PendingDisplay is not null,
            timeoutMs: 90000);
        host.Tap(Key.Escape);
        await host.AwaitUpdates(2);
        AssertThat(host.Main.Menu!.Mode).IsEqual(ExplorationMenuMode.Closed);
        await host.FinishDisplay();
        await host.Wait(() => host.Main.Interaction!.Phase == NpcInteractionPhase.Exploring, timeoutMs: 30000);

        // After the result returns to exploration the menu opens again and reads the LIVE state:
        // the Won half-recovery raised role0 HP 100 -> 125 (battle.c PAL_BattleWon).
        host.Tap(Key.Escape);
        await host.Wait(() => host.Main.Menu!.Mode == ExplorationMenuMode.MainMenu);
        host.Tap(Key.Enter);
        await host.Wait(() => host.Main.Menu!.Mode == ExplorationMenuMode.Status);
        AssertThat(host.Main.Menu!.StatusView().Hp).IsEqual(125);
        host.Tap(Key.Escape);
        await host.Wait(() => host.Main.Menu!.Mode == ExplorationMenuMode.Closed);
    }

    [TestCase(Timeout = 180000)]
    public async Task EquipmentPageEquipsFromStockReturnsOldItemAndFeedsTheNextBattle()
    {
        // #214: the equip loop over the shared live owners on the formal Main. Independent
        // numbers: PartyEquip authors role0 panels 40/20/100 bare and role1 wearing
        // [197 head, 0, 210 shoulder, 170 hand, 237 feet, 248 wear] (authored effect rows
        // 197: role0 0/0 and role1 attack 6/defense 2, 210: dexterity 8, 170: 5/2/5 + dual attack,
        // 237: defense 30, 248: dexterity 6); the test-owned menuEquip derivation adds head
        // object 199 (role0 attack 3/defense 2, role1 defense 4), bag rows 197x2/199x1/210x1
        // and part/eligibility bindings (210 stays role1-only as the rejection sentinel).
        using var fixture = new AuthoredStoryFixture("PartyEquip", menuEquip: true);
        var selection = GameSceneConfiguration.FromArguments("--story-profile", fixture.StoryUri,
            "--battle-visual-profile", fixture.VisualUri, "--battle-seed", "1", "--story-language", "zh-Hans");
        using var host = new MenuHost(selection);
        host.AssertHealthy();
        await host.Wait(() => host.Updates.Count > 0);
        var menu = host.Main.Menu!;

        // Battle #1 baseline: the boss trigger consumes the authored gear (the M6-A chain).
        host.Tap(Key.Enter);
        await host.Wait(() => host.Main.Dialogue!.Session!.PendingDisplay is not null);
        await host.FinishDisplay();
        await host.Wait(() => host.Main.StoryBattleMode == StoryBattlePresentation.AttackMenu, timeoutMs: 30000);
        var first = host.Main.StoryBattle!.Current.Combat;
        AssertThat((first.Players[0].Attack, first.Players[0].Defense)).IsEqual((40, 20));
        AssertThat(first.Players[1].Defense).IsEqual(52); // authored 18 + head 2 + hand 2 + feet 30
        await FinishBattle();

        // The menu gains the equipment entry exactly for the authored equip context.
        host.Tap(Key.Escape);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.MainMenu);
        AssertThat(menu.Entries.Count).IsEqual(2);
        AssertThat(menu.EntryLabel(1)).IsEqual("防身");
        host.Tap(Key.Down);
        host.Tap(Key.Enter);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.EquipItems);
        AssertRows(menu, [(197, "HEAD", "HEADBAND", 2), (199, "HEAD", "LEATHER CAP", 1), (210, "SHOULDER", "CAPE", 1)]);

        // Eligibility bits per member and the member's current gear (uigame.c player box).
        host.Tap(Key.Down);
        host.Tap(Key.Down); // 210: role1-only
        host.Tap(Key.Enter);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.EquipRoles);
        var page = menu.EquipRolesView();
        AssertThat((page.Name, page.PartLabel)).IsEqual(("CAPE", "SHOULDER"));
        AssertThat(page.Roles[0].Eligible).IsFalse();
        AssertThat(page.Roles[1].Eligible).IsTrue();
        AssertThat(string.Join(",", page.Roles[1].Gear)).IsEqual("HEADBAND,CAPE,WOODEN SWORD,SANDALS,AMULET");

        // Confirm on the ineligible member never equips (no half change; uigame.c flag gate).
        host.Tap(Key.Enter);
        await host.AwaitUpdates(2);
        AssertThat(menu.Mode).IsEqual(ExplorationMenuMode.EquipRoles);
        AssertThat(menu.EquipSelection).IsEqual(210);

        // Same-object equip on 小侠's shoulder slot: registers rewritten, stock unmoved.
        host.Tap(Key.Down);
        host.Tap(Key.Enter);
        await host.AwaitUpdates(2);
        AssertThat(menu.Mode).IsEqual(ExplorationMenuMode.EquipRoles); // wLastUnequippedItem 210
        AssertThat(menu.EquipSelection).IsEqual(210);
        host.Tap(Key.Escape);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.EquipItems);
        AssertRows(menu, [(197, "HEAD", "HEADBAND", 2), (199, "HEAD", "LEATHER CAP", 1), (210, "SHOULDER", "CAPE", 1)]);

        // 197 onto 大侠's empty head: one copy consumed, back to the item list.
        host.Tap(Key.Escape);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.MainMenu);
        host.Tap(Key.Escape);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.Closed);
        host.Tap(Key.Escape);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.MainMenu);
        host.Tap(Key.Enter);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.EquipItems);
        host.Tap(Key.Down); // wrap the remembered item cursor 210 -> 197 (itemmenu.c iCurInvMenuItem)
        host.Tap(Key.Enter);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.EquipRoles);
        host.Tap(Key.Enter);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.EquipItems); // wLastUnequippedItem 0
        AssertRows(menu, [(197, "HEAD", "HEADBAND", 1), (199, "HEAD", "LEATHER CAP", 1), (210, "SHOULDER", "CAPE", 1)]);

        // 199 onto 小侠's head: the displaced 197 returns and stays selected (old-item flow).
        host.Tap(Key.Down);
        host.Tap(Key.Enter);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.EquipRoles);
        host.Tap(Key.Down);
        host.Tap(Key.Enter);
        await host.AwaitUpdates(2);
        AssertThat(menu.Mode).IsEqual(ExplorationMenuMode.EquipRoles); // wLastUnequippedItem 197
        AssertThat(menu.EquipSelection).IsEqual(197);
        host.Tap(Key.Escape);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.EquipItems);
        AssertRows(menu, [(197, "HEAD", "HEADBAND", 2), (210, "SHOULDER", "CAPE", 1)]);
        host.Tap(Key.Escape);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.MainMenu);
        host.Tap(Key.Escape);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.Closed);

        // The status page reads the same live owners: 大侠 40/20 (197's authored role0
        // row adds nothing), 小侠 35+5/18+2+4+30.
        host.Tap(Key.Escape);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.MainMenu);
        host.Tap(Key.Up);
        host.Tap(Key.Enter);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.Status);
        var hero = menu.StatusView();
        AssertThat((hero.Attack, hero.Defense)).IsEqual((40, 20));
        host.Tap(Key.Right);
        await host.Wait(() => menu.StatusMemberIndex == 1);
        var second = menu.StatusView();
        AssertThat((second.Attack, second.Defense)).IsEqual((40, 54));
        AssertThat(string.Join(",", second.Equipment.Select(gear => gear.Name))).IsEqual(
            "LEATHER CAP,CAPE,WOODEN SWORD,SANDALS,AMULET");
        host.Tap(Key.Escape);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.Closed);

        // Battle #2 consumes the current equipment through the shared owner (#187 binding).
        host.Tap(Key.Enter);
        await host.Wait(() => host.Main.Dialogue!.Session!.PendingDisplay is not null);
        await host.FinishDisplay();
        await host.Wait(() => host.Main.StoryBattleMode == StoryBattlePresentation.AttackMenu, timeoutMs: 30000);
        var next = host.Main.StoryBattle!.Current.Combat;
        AssertThat((next.Players[0].Attack, next.Players[0].Defense)).IsEqual((40, 20));
        AssertThat(next.Players[1].Defense).IsEqual(54);
        await FinishBattle();
        return;

        void AssertRows(ExplorationMenuSession session,
            (int ObjectId, string Part, string Name, int Amount)[] expected) =>
            AssertThat(string.Join(";", session.EquipItemsView()
                .Select(row => $"{row.ObjectId}:{row.PartLabel}:{row.Name}:{row.Amount}")))
                .IsEqual(string.Join(";", expected.Select(row => $"{row.ObjectId}:{row.Part}:{row.Name}:{row.Amount}")));

        async Task FinishBattle()
        {
            var script = host.Main.Dialogue!.Session!;
            while (script.PendingBattle is not null)
            {
                await host.Wait(() => host.Main.StoryBattleMode == StoryBattlePresentation.AttackMenu
                    || script.PendingBattle is null);
                if (script.PendingBattle is null) break;
                await host.ConfirmBattleMenu(StoryBattlePresentation.TargetMenu);
                var combat = host.Main.StoryBattle!.Current.Combat;
                int selecting = combat.SelectingPlayerIndex
                    ?? throw new InvalidOperationException("Target menu has no selecting member.");
                bool final = Enumerable.Range(selecting + 1, combat.Players.Count - selecting - 1)
                    .All(index => combat.Players[index].Hp == 0);
                await host.ConfirmBattleMenu(final
                    ? StoryBattlePresentation.Resolving : StoryBattlePresentation.AttackMenu);
            }
            await host.Wait(() => host.Main.StoryBattleMode is null
                && host.Main.Dialogue!.Session!.PendingDisplay is not null, timeoutMs: 90000);
            await host.FinishDisplay();
            await host.Wait(() => host.Main.Interaction!.Phase == NpcInteractionPhase.Exploring, timeoutMs: 30000);
        }
    }

    [TestCase(Timeout = 120000)]
    public async Task ItemPageUsesHealsFailsAndConsumesThroughTheRealScriptContext()
    {
        // #215: the exploration item-use chain on the formal story Main (play.c PAL_GameUseItem
        // over itemmenu.c PAL_ItemSelectMenu(kItemFlagUsable) and uigame.c PAL_ItemUseMenu).
        // Independent numbers: PartyWin authors role0 HP100/MP20, role1 HP95/MP20 (max 150/50);
        // the test-owned scripts heal +30 (single, with a real dialogue line), +10 party-wide and
        // a markFailed no-op; consumption follows kItemFlagConsuming AND script success only.
        using var fixture = new AuthoredStoryFixture("PartyWin", menuItems: true);
        var selection = GameSceneConfiguration.FromArguments("--story-profile", fixture.StoryUri,
            "--battle-visual-profile", fixture.VisualUri, "--battle-seed", "1", "--story-language", "zh-Hans");
        using var host = new MenuHost(selection);
        host.AssertHealthy();
        await host.Wait(() => host.Updates.Count > 0);
        var menu = host.Main.Menu!;

        // The authored use context gains the items entry; the list shows the usable stock rows
        // with available amounts in binding order (itemmenu.c compresses the rest away).
        host.Tap(Key.Escape);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.MainMenu);
        AssertThat(menu.Entries.Count).IsEqual(2);
        AssertThat(menu.EntryLabel(1)).IsEqual("ITEMS");
        host.Tap(Key.Down);
        host.Tap(Key.Enter);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.Items);
        AssertRows(menu, [(300, "生命提升", 2), (301, "护大家", 1), (302, "别倒", 1)]);

        // 300 on 小侠: the target page reads the live shared vitals (uigame.c stat box); the
        // committed run executes the checked on-use script through the real Dialogue host.
        host.Tap(Key.Enter);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.ItemTargets);
        var targets = menu.ItemTargetsView();
        AssertThat((targets.ObjectId, targets.Name)).IsEqual((300, "生命提升"));
        AssertThat(string.Join(",", targets.Targets.Select(target => target.Name))).IsEqual("大侠,小侠");
        AssertThat((targets.Targets[1].Hp, targets.Targets[1].Mp)).IsEqual((95, 20));
        host.Tap(Key.Down);
        host.Tap(Key.Enter);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.ItemScript);
        await host.Wait(() => host.Main.Dialogue!.Session!.PendingDisplay is not null);
        await host.FinishDisplay();
        // play.c: the single-target loop reopens the player select for another use.
        await host.Wait(() => menu.Mode == ExplorationMenuMode.ItemTargets);
        var healed = menu.ItemTargetsView();
        AssertThat((healed.Targets[0].Hp, healed.Targets[1].Hp)).IsEqual((100, 125));
        AssertThat(healed.Targets[1].Mp).IsEqual(50); // changeHpMp writes HP and MP together
        // Cancel breaks back to the item list without a second run; one copy is gone.
        host.Tap(Key.Escape);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.Items);
        AssertRows(menu, [(300, "生命提升", 1), (301, "护大家", 1), (302, "别倒", 1)]);

        // The second 300 on 大侠 (remembered player cursor stays on the last selection): the
        // copy count reaches zero and the row leaves the compressed list.
        host.Tap(Key.Enter);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.ItemTargets);
        AssertThat(menu.SelectedUseRole).IsEqual(1);
        host.Tap(Key.Up); // PAL_ItemUseMenu wraps: member 1 -> 0
        host.Tap(Key.Enter);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.ItemScript);
        await host.Wait(() => host.Main.Dialogue!.Session!.PendingDisplay is not null);
        await host.FinishDisplay();
        await host.Wait(() => menu.Mode == ExplorationMenuMode.ItemTargets);
        host.Tap(Key.Escape);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.Items);
        AssertRows(menu, [(301, "护大家", 1), (302, "别倒", 1)]);

        // 301 is apply-to-all: no target page — the run commits straight from the list with the
        // caller word 0xFFFF and heals both members; PAL_GameUseItem then returns and uigame.c
        // PAL_InGameMenu case 3 'goto out' closes the whole menu after the one run.
        host.Tap(Key.Enter);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.ItemScript);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.Closed);
        AssertThat(host.Main.MenuView).IsNull();

        // 302 marks the script failed: the reopened items page (remembered entries and inventory
        // cursors) runs it once; the target loop reopens but the consuming copy stays.
        host.Tap(Key.Escape);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.MainMenu);
        host.Tap(Key.Enter);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.Items);
        AssertRows(menu, [(302, "别倒", 1)]);
        host.Tap(Key.Enter);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.ItemTargets);
        host.Tap(Key.Enter);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.ItemScript);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.ItemTargets);
        host.Tap(Key.Escape);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.Items);
        AssertRows(menu, [(302, "别倒", 1)]);

        // The item-select cancel ends the whole use flow (itemmenu.c kKeyMenu returns 0,
        // play.c PAL_GameUseItem returns, uigame.c case 3 'goto out'): the menu closes and
        // the exploration state resumes without leaking the closing cancel into the NPC
        // trigger. The status page then reads the same shared live owners the scripts wrote:
        // 100+30+10 / 95+30+10 HP and capped 50 MP.
        host.Tap(Key.Escape);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.Closed);
        host.Tap(Key.Escape);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.MainMenu);
        // The remembered main-menu cursor stayed on the items entry (iCurMainMenuItem survives
        // closes); step back to the status entry before confirming.
        host.Tap(Key.Up);
        host.Tap(Key.Enter);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.Status);
        var hero = menu.StatusView();
        AssertThat((hero.Hp, hero.Mp)).IsEqual((140, 50));
        host.Tap(Key.Right);
        await host.Wait(() => menu.StatusMemberIndex == 1);
        var second = menu.StatusView();
        AssertThat((second.Hp, second.Mp)).IsEqual((135, 50));
        host.Tap(Key.Escape);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.Closed);
        AssertThat(host.Main.Interaction!.Phase).IsEqual(NpcInteractionPhase.Exploring);
        host.Tap(Key.Enter);
        await host.Wait(() => host.Main.Interaction!.PendingTrigger is not null);
        AssertThat(menu.Mode).IsEqual(ExplorationMenuMode.Closed);

        void AssertRows(ExplorationMenuSession session,
            (int ObjectId, string Name, int Amount)[] expected) =>
            AssertThat(string.Join(";", session.ItemsView()
                .Select(row => $"{row.ObjectId}:{row.Name}:{row.Amount}")))
                .IsEqual(string.Join(";", expected.Select(row => $"{row.ObjectId}:{row.Name}:{row.Amount}")));
    }

    [TestCase(Timeout = 180000)]
    public async Task MagicPageCastsThroughTheRealScriptContextAndFeedsTheNextBattle()
    {
        // #216: the exploration magic-cast chain on the formal story Main (uigame.c
        // PAL_InGameMagicMenu over magicmenu.c PAL_MagicSelectionMenu). Independent numbers:
        // PartyWin authors role0 HP100/MP20, role1 HP95/MP20 (max 150/50); the test-owned
        // scripts heal +30 (single, with a real dialogue line), +10 party-wide and a markFailed
        // no-op on-success; the caster's MP pays the cost only after both script gates
        // survived (never battle's deduct-first order).
        using var fixture = new AuthoredStoryFixture("PartyWin", menuMagic: true);
        var selection = GameSceneConfiguration.FromArguments("--story-profile", fixture.StoryUri,
            "--battle-visual-profile", fixture.VisualUri, "--battle-seed", "1", "--story-language", "zh-Hans");
        using var host = new MenuHost(selection);
        host.AssertHealthy();
        await host.Wait(() => host.Updates.Count > 0);
        var menu = host.Main.Menu!;

        // The authored magic context gains the magic entry; the caster select lists the whole
        // party (uigame.c PAL_ReadMenu items; dead members stay listed but disabled).
        host.Tap(Key.Escape);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.MainMenu);
        AssertThat(menu.Entries.Count).IsEqual(2);
        AssertThat(menu.EntryLabel(1)).IsEqual("法术");
        host.Tap(Key.Down);
        host.Tap(Key.Enter);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.MagicCasters);
        AssertThat(string.Join(",", menu.MagicCastersView().Select(row => $"{row.RoleId}:{row.Name}:{row.Alive}")))
            .IsEqual("0:大侠:True,1:小侠:True");
        host.Tap(Key.Enter);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.MagicList);

        // magicmenu.c: the caster's learned magics sorted by object WORD; enabled only when the
        // magic is usable outside battle and its cost fits the caster's current live MP.
        AssertRows(menu, [(310, "回生术", 5, true), (311, "大回生术", 4, true), (312, "快攻术", 3, false), (313, "吉运术", 6, true)]);
        AssertThat(menu.SelectedMagic).IsEqual(310);

        // A battle-only row stays reachable by the cursor but never confirms
        // (kMagicFlagUsableOutsideBattle clear).
        host.Tap(Key.Down);
        host.Tap(Key.Down); // 312
        host.Tap(Key.Enter);
        await host.AwaitUpdates(2);
        AssertThat(menu.Mode).IsEqual(ExplorationMenuMode.MagicList);
        AssertThat(menu.PendingMagicUse).IsNull();

        // 310 on 小侠: the target page reads the live shared vitals (uigame.c stat box); the
        // committed cast runs the checked on-use script (with its real dialogue line) through
        // the real Dialogue host, then the on-success entry; only then does the caster's MP
        // pay the cost (uigame.c 810-834, never deduct-first).
        host.Tap(Key.Up);
        host.Tap(Key.Up); // 312 -> 311 -> 310
        host.Tap(Key.Enter);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.MagicTargets);
        var targets = menu.MagicTargetsView();
        AssertThat((targets.ObjectId, targets.Name)).IsEqual((310, "回生术"));
        AssertThat((targets.Targets[1].Hp, targets.Targets[1].Mp)).IsEqual((95, 20));
        host.Tap(Key.Down);
        host.Tap(Key.Enter);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.MagicOnUse);
        await host.Wait(() => host.Main.Dialogue!.Session!.PendingDisplay is not null);
        await host.FinishDisplay();
        // The on-success entry (no dialogue) completes on the next real boundaries; the target
        // select reopens for another cast while the caster can still pay (20-5=15 >= 5).
        await host.Wait(() => menu.Mode == ExplorationMenuMode.MagicOnSuccess);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.MagicTargets);
        var healed = menu.MagicTargetsView();
        AssertThat((healed.Targets[0].Hp, healed.Targets[1].Hp)).IsEqual((100, 125));
        AssertThat((healed.Targets[0].Mp, healed.Targets[1].Mp)).IsEqual((15, 50)); // caster paid 5; 小侠 +30 MP capped
        // kKeyMenu breaks the target loop; the list reopens at the same magic.
        host.Tap(Key.Escape);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.MagicList);
        AssertThat(menu.SelectedMagic).IsEqual(310);

        // 313's on-use ends successfully, but its on-success marks the script failed: the
        // deduction gate never opens, and per uigame.c the exhaustion exit belongs to the
        // successful branch only — the target select reopens even below the cost.
        host.Tap(Key.Down);
        host.Tap(Key.Down);
        host.Tap(Key.Down); // 313
        host.Tap(Key.Enter);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.MagicTargets);
        host.Tap(Key.Enter); // member 0 (大侠) commits the cast
        await host.Wait(() => menu.Mode == ExplorationMenuMode.MagicOnUse);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.MagicOnSuccess);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.MagicTargets);
        AssertThat(menu.MagicTargetsView().Targets[0].Mp).IsEqual(15); // markFailed: no deduction
        host.Tap(Key.Escape);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.MagicList);

        // 311 is apply-to-all: no target page — the cast commits straight from the list with
        // the literal caller word 0 (never the item flow's 0xFFFF), heals the party once and
        // the loop returns to the list (the menu stays open, unlike the item apply-to-all).
        host.Tap(Key.Up);
        host.Tap(Key.Up); // 311
        host.Tap(Key.Enter);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.MagicOnUse);
        var all = menu.PendingMagicUse!;
        AssertThat((all.ObjectId, all.ApplyToAll, all.CasterRoleId, all.TargetRoleId)).IsEqual((311, true, 0, 0));
        await host.Wait(() => menu.Mode == ExplorationMenuMode.MagicList);
        // 100+10 HP / 15+10-4 = 21 caster MP; 小侠 125+10 HP, MP stays capped; the live MP
        // re-enables every exploration-usable row against the new cost frontier.
        AssertRows(menu, [(310, "回生术", 5, true), (311, "大回生术", 4, true), (312, "快攻术", 3, false), (313, "吉运术", 6, true)]);

        // kKeyMenu closes the whole menu; the executed casts' MP and the scripts' heals stay
        // committed in the shared live owners (no rollback).
        host.Tap(Key.Escape);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.Closed);
        AssertThat(host.Main.MenuView).IsNull();
        host.Tap(Key.Escape);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.MainMenu);
        // The remembered main-menu cursor stayed on the magic entry (iCurMainMenuItem survives
        // closes); step back to the status entry before confirming.
        host.Tap(Key.Up);
        host.Tap(Key.Enter);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.Status);
        var hero = menu.StatusView();
        AssertThat((hero.Hp, hero.Mp)).IsEqual((110, 21));
        host.Tap(Key.Right);
        await host.Wait(() => menu.StatusMemberIndex == 1);
        var second = menu.StatusView();
        AssertThat((second.Hp, second.Mp)).IsEqual((135, 50));
        host.Tap(Key.Escape);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.Closed);

        // Story 75: the subsequent formal battle opens from the same shared owners — the menu
        // heals and the menu-deducted MP are the battle's opening state (cross-menu
        // consumption; no second state copy anywhere).
        host.Tap(Key.Enter);
        await host.Wait(() => host.Main.Dialogue!.Session!.PendingDisplay is not null);
        await host.FinishDisplay();
        await host.Wait(() => host.Main.StoryBattleMode == StoryBattlePresentation.AttackMenu, timeoutMs: 30000);
        var roles = host.Main.StoryBattle!.Current.Roles;
        AssertThat((roles[0].Hp, roles[0].Mp)).IsEqual((110, 21));
        AssertThat((roles[1].Hp, roles[1].Mp)).IsEqual((135, 50));

        void AssertRows(ExplorationMenuSession session,
            (int ObjectId, string Name, int CostMp, bool Enabled)[] expected) =>
            AssertThat(string.Join(";", session.MagicListView()
                .Select(row => $"{row.ObjectId}:{row.Name}:{row.CostMp}:{row.Enabled}")))
                .IsEqual(string.Join(";", expected.Select(row => $"{row.ObjectId}:{row.Name}:{row.CostMp}:{row.Enabled}")));
    }

    [TestCase(Timeout = 30000)]
    public void PreparedLanguagesCoverEveryMenuWordGlyphInBothLanguages()
    {
        // The selected language is preflighted at host initialization; this pin keeps the
        // unselected language equally complete so no run can show mixed or missing glyphs.
        var font = ResourceLoader.Load<Pal.Game.Resources.PalDialogueFont>("res://Samples/AuthoredBattle/Dialogue/Font.tres")!;
        AssertThat(font).IsNotNull();
        int[] words =
        [
            ExplorationMenuSession.StatusEntryWord, ExplorationMenuSession.LevelWord,
            ExplorationMenuSession.HpWord, ExplorationMenuSession.MpWord, ExplorationMenuSession.ExperienceWord,
            ExplorationMenuSession.AttackPowerWord, ExplorationMenuSession.MagicPowerWord,
            ExplorationMenuSession.DefenseWord, ExplorationMenuSession.DexterityWord,
            ExplorationMenuSession.FleeRateWord, ExplorationMenuSession.RoleNameWordBase,
            ExplorationMenuSession.RoleNameWordBase + 1, ExplorationMenuSession.EquipmentEntryWord,
            ExplorationMenuSession.ItemsEntryWord, ExplorationMenuSession.MagicEntryWord,
            ExplorationMenuSession.PartWordBase, ExplorationMenuSession.PartWordBase + 1,
            ExplorationMenuSession.PartWordBase + 2, ExplorationMenuSession.PartWordBase + 3,
            ExplorationMenuSession.PartWordBase + 4, ExplorationMenuSession.PartWordBase + 5,
            170, 197, 199, 210, 237, 248, 300, 301, 302, 310, 311, 312, 313,
        ];
        foreach (string language in new[] { "zh-Hans", "en" })
        {
            var resource = ResourceLoader.Load<Pal.Game.Resources.PalDialogueLanguage>(
                $"res://Samples/AuthoredBattle/Dialogue/Language.{language}.tres")!;
            AssertThat(resource).IsNotNull();
            foreach (int word in words)
            {
                AssertThat(resource.Words.ContainsKey(word)).IsTrue();
                string text = resource.Words[word];
                AssertThat(text.Length > 0).IsTrue();
                foreach (var rune in text.EnumerateRunes())
                {
                    // GetGlyph rejects missing/duplicate glyphs; word glyphs must be visible
                    // (the space glyph is the only legitimate blank).
                    var glyph = font.GetGlyph(rune.Value);
                    AssertThat(rune.Value == ' ' || glyph.Bitmap.Any(value => value != 0)).IsTrue();
                }
            }
        }
        // Numeric values and separators the menu composes are covered too.
        foreach (var rune in "0123456789:/".EnumerateRunes())
            AssertThat(font.GetGlyph(rune.Value).Bitmap.Any(value => value != 0)).IsTrue();
    }

    // Narrow host: it only drives and observes the real Main through SceneRunner and real input
    // events; no game assembly, time advancement or state shortcut exists here.
    private sealed class MenuHost : IDisposable
    {
        internal Main Main { get; } = new();
        private readonly ISceneRunner runner;
        private readonly Control? focus = ((SceneTree)Engine.GetMainLoop()).Root.GuiGetFocusOwner();
        internal System.Collections.Generic.List<ExplorationState> Updates { get; } = [];
        internal System.Collections.Generic.List<string> Failures { get; } = [];
        internal int UpdatesCount { get; private set; }

        public MenuHost(GameSceneConfiguration selection)
        {
            Main.Configure(selection);
            Main.Failed += failure => Failures.Add(failure);
            Main.UpdateCompleted += () => { UpdatesCount++; Updates.Add(Main.Exploration!); };
            var container = new Node();
            container.AddChild(Main);
            runner = ISceneRunner.Load(container, autoFree: true);
        }

        public void AssertHealthy()
        {
            if (Failures.Count > 0 || Main.Failure is not null)
                throw new InvalidOperationException("Menu host failed: " + (Main.Failure ?? Failures[0]));
        }

        public void Press(Key key) => Send(key, true);
        public void Release(Key key) => Send(key, false);
        public void Tap(Key key) { Send(key, true); Send(key, false); }

        private static void Send(Key key, bool pressed)
        {
            using var input = new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed };
            Input.ParseInputEvent(input);
            Input.FlushBufferedEvents();
        }

        public async Task Wait(Func<bool> predicate, int timeoutMs = 15000)
        {
            ulong start = Time.GetTicksMsec();
            int before = UpdatesCount;
            while (!predicate() || UpdatesCount == before)
            {
                AssertHealthy();
                if (Time.GetTicksMsec() - start > (ulong)timeoutMs)
                    throw new TimeoutException("Menu production boundary not observed.");
                await runner.AwaitIdleFrame();
            }
            AssertHealthy();
        }

        public async Task AwaitUpdates(int count, int timeoutMs = 15000)
        {
            int target = UpdatesCount + count;
            ulong start = Time.GetTicksMsec();
            while (UpdatesCount < target)
            {
                AssertHealthy();
                if (Time.GetTicksMsec() - start > (ulong)timeoutMs)
                    throw new TimeoutException("Menu host updates did not advance.");
                await runner.AwaitIdleFrame();
            }
            AssertHealthy();
        }

        public async Task FinishDisplay()
        {
            var display = Main.Dialogue!.Session!.PendingDisplay!;
            await Wait(() => display.Display.Current.Phase == DialogueDisplayPhase.WaitingForAdvance, timeoutMs: 30000);
            Tap(Key.Enter);
            await Wait(() => Main.Dialogue!.Session!.PendingDisplay?.Id != display.Id, timeoutMs: 30000);
        }

        internal async Task ConfirmBattleMenu(StoryBattlePresentation expected)
        {
            bool observed = false;
            void Capture()
            {
                Main.UpdateCompleted -= Capture;
                if (Main.StoryBattleMode == expected) observed = true;
            }
            Main.UpdateCompleted += Capture;
            try { Tap(Key.Enter); await Wait(() => observed, timeoutMs: 30000); }
            finally { Main.UpdateCompleted -= Capture; }
            AssertThat(Main.StoryBattleMode).IsEqual(expected);
        }

        public void Dispose()
        {
            try
            {
                Release(Key.Enter); Release(Key.Down); Release(Key.Escape);
                runner.Dispose();
            }
            finally
            {
                if (GodotObject.IsInstanceValid(Main)) Main.Free();
                if (GodotObject.IsInstanceValid(focus)) focus!.GrabFocus();
            }
            AssertThat(GodotObject.IsInstanceValid(Main)).IsFalse();
        }
    }
}
