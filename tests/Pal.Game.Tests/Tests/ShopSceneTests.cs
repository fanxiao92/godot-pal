using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using Pal.Core;
using Pal.Game.Dialogue;
using static GdUnit4.Assertions;
using Pal.Core.Exploration;
using Pal.Core.Dialogue;
using Pal.Core.Events;
using Pal.Core.Menu;
using Pal.Game.Battle;

namespace Pal.Game.Tests;

/// <summary>#217 (M6-B T05): the shop buy/sell chain on the formal story Main (SceneRunner,
/// real input events, real update-completion boundaries) — script.c 0x0026/0x0027 park the
/// trigger inside uigame.c PAL_BuyMenu/PAL_SellMenu; the menus run over the shared
/// cash/inventory/role owners, and the menu->battle continuity consumes the same live state.
/// Independent numeric expectations come from the committed authored inputs (the test-owned
/// menuShop derivation over samples/authored-battle/PartyEquip): cash seed 600, prices
/// 300/120, 199/250, 320/999, 197/40, 210/90 with sale prices 20/125/45/60, and the #214
/// equip rows (199's role0 row attack 3/defense 2). No test-only host or state shortcut
/// exists.</summary>
[TestSuite, RequireGodotRuntime]
[TestCategory("Shop")]
public class ShopSceneTests
{
    [TestCase(Timeout = 300000)]
    public async Task ShopBuysAndSellsThroughTheRealTriggerContextAndFeedsTheNextBattle()
    {
        using var fixture = new AuthoredStoryFixture("PartyEquip", menuEquip: true, menuShop: true);
        var selection = GameSceneConfiguration.FromArguments("--story-profile", fixture.StoryUri,
            "--battle-visual-profile", fixture.VisualUri, "--battle-seed", "1", "--story-language", "zh-Hans");
        using var host = new ShopHost(selection);
        host.AssertHealthy();
        await host.Wait(() => host.Updates.Count > 0);
        var menu = host.Main.Menu!;
        AssertThat(host.Main.Shop).IsNotNull();
        AssertThat(host.Main.Shop!.Mode).IsEqual(ExplorationShopMode.Closed);

        // The NPC trigger parks on its first dialog; after it completes the buy menu owns input
        // (script.c blocks inside PAL_BuyMenu while the world freezes).
        host.Tap(Key.Enter);
        await host.Wait(() => host.Main.Dialogue!.Session!.PendingDisplay is not null);
        await host.FinishDisplay();
        await host.Wait(() => host.Main.Shop!.Mode == ExplorationShopMode.BuyList);
        var shop = host.Main.Shop;
        AssertThat(host.Main.ShopView).IsNotNull();
        AssertThat(menu.Mode).IsEqual(ExplorationMenuMode.Closed);

        // The frozen world: held direction presses never move the party while the shop is open.
        // The open shop owns the direction press (uigame.c menu loops), so the check also proves
        // the buy cursor consumed it — row 199 owns exactly one bag copy — and the choreography
        // restores the fresh cursor before the status panel assertions read row 300 again.
        var frozen = host.Main.Exploration!;
        host.Press(Key.Down);
        await host.AwaitUpdates(3);
        AssertThat(ReferenceEquals(host.Main.Exploration, frozen)).IsTrue();
        AssertThat(shop.SelectedBuyItem).IsEqual(1);
        host.Release(Key.Down);
        host.Tap(Key.Up);
        await host.AwaitUpdates(1);
        AssertThat(shop.SelectedBuyItem).IsEqual(0);

        // The store row lists its authored order with prices; the panel reads the shared wallet.
        AssertThat(string.Join(";", shop.BuyRowsView().Select(row => $"{row.ObjectId}:{row.Name}:{row.Price}")))
            .IsEqual("300:生命提升:120;199:LEATHER CAP:250;320:MEDAL:999");
        var status = shop.BuyStatusView();
        AssertThat((status.Cash, status.Owned, status.CashLabel, status.OwnedLabel)).IsEqual((600u, 0, "现金", "OWNED"));

        // An unaffordable row (999 > 600) ignores Search: no confirm box, no deduction.
        host.Tap(Key.Down);
        host.Tap(Key.Down);
        await host.AwaitUpdates(1); // the queued direction applies at the next real update
        AssertThat(shop.SelectedBuyItem).IsEqual(2);
        host.Tap(Key.Enter);
        await host.AwaitUpdates(2);
        AssertThat(shop.Mode).IsEqual(ExplorationShopMode.BuyList);
        AssertThat(shop.BuyStatusView().Cash).IsEqual(600u);

        // Buy 300: the confirm cancel (kKeyMenu) leaves everything untouched, then No does too.
        host.Tap(Key.Up);
        host.Tap(Key.Up);
        host.Tap(Key.Enter);
        await host.Wait(() => shop.Mode == ExplorationShopMode.BuyConfirm);
        host.Tap(Key.Escape);
        await host.Wait(() => shop.Mode == ExplorationShopMode.BuyList);
        AssertThat(shop.BuyStatusView().Cash).IsEqual(600u);
        host.Tap(Key.Enter);
        await host.Wait(() => shop.Mode == ExplorationShopMode.BuyConfirm);
        host.Tap(Key.Enter); // No is the default cursor (uigame.c PAL_SelectionMenu nDefault 0)
        await host.Wait(() => shop.Mode == ExplorationShopMode.BuyList);
        AssertThat(shop.BuyStatusView().Cash).IsEqual(600u);

        // The confirmed purchase debits first and adds the copy; the owned panel follows the bag.
        host.Tap(Key.Enter);
        await host.Wait(() => shop.Mode == ExplorationShopMode.BuyConfirm);
        host.Tap(Key.Down);
        host.Tap(Key.Enter);
        await host.Wait(() => shop.Mode == ExplorationShopMode.BuyList);
        AssertThat(shop.BuyStatusView().Cash).IsEqual(480u);
        AssertThat(shop.BuyStatusView().Owned).IsEqual(1);

        // Buy 199 (the continuity input: one copy equips on 大侠, one stays in the bag).
        host.Tap(Key.Down);
        host.Tap(Key.Enter);
        await host.Wait(() => shop.Mode == ExplorationShopMode.BuyConfirm);
        host.Tap(Key.Down);
        host.Tap(Key.Enter);
        await host.Wait(() => shop.Mode == ExplorationShopMode.BuyList);
        AssertThat((shop.BuyStatusView().Cash, shop.BuyStatusView().Owned)).IsEqual((230u, 2));

        // kKeyMenu cancels the buy loop; the next command parks the sell menu over the same bag.
        host.Tap(Key.Escape);
        await host.Wait(() => shop.Mode == ExplorationShopMode.SellList);
        AssertThat(string.Join(";", shop.SellRowsView()
            .Select(row => $"{row.ObjectId}:{row.Name}:{row.Amount}:{row.Price}:{row.Sellable}")))
            .IsEqual("197:HEADBAND:2:20:True;199:LEATHER CAP:2:125:True;210:CAPE:1:45:False;300:生命提升:1:60:True");
        var sellStatus = shop.SellStatusView();
        AssertThat((sellStatus.Cash, sellStatus.Price, sellStatus.CashLabel, sellStatus.PriceLabel))
            .IsEqual((230u, 20, "现金", "PRICE"));

        // The owned-but-unsellable 210 row never confirms (itemmenu.c's kItemFlagSellable gate).
        host.Tap(Key.Down);
        host.Tap(Key.Down);
        await host.AwaitUpdates(1); // the queued direction applies at the next real update
        AssertThat(shop.SelectedSellItem).IsEqual(2);
        host.Tap(Key.Enter);
        await host.AwaitUpdates(2);
        AssertThat(shop.Mode).IsEqual(ExplorationShopMode.SellList);
        AssertThat(shop.SellStatusView().Cash).IsEqual(230u);

        // Sell one 197: the No default leaves the copy; Yes removes it first, then credits 20.
        host.Tap(Key.Up);
        host.Tap(Key.Up);
        host.Tap(Key.Enter);
        await host.Wait(() => shop.Mode == ExplorationShopMode.SellConfirm);
        host.Tap(Key.Enter);
        await host.Wait(() => shop.Mode == ExplorationShopMode.SellList);
        AssertThat(shop.SellStatusView().Cash).IsEqual(230u);
        host.Tap(Key.Enter);
        await host.Wait(() => shop.Mode == ExplorationShopMode.SellConfirm);
        host.Tap(Key.Down);
        host.Tap(Key.Enter);
        await host.Wait(() => shop.Mode == ExplorationShopMode.SellList);
        AssertThat(shop.SellStatusView().Cash).IsEqual(250u);

        // Closing the sell menu resumes the trigger into battle #1 (the authored boss team).
        host.Tap(Key.Escape);
        await host.Wait(() => host.Main.StoryBattleMode == StoryBattlePresentation.AttackMenu, timeoutMs: 30000);
        var first = host.Main.StoryBattle!.Current.Combat;
        AssertThat((first.Players[0].Attack, first.Players[0].Defense)).IsEqual((40, 20));
        AssertThat(first.Players[1].Defense).IsEqual(52); // authored gear, no shop change yet
        await FinishBattle();
        await host.Wait(() => host.Main.Interaction!.Phase == NpcInteractionPhase.Exploring, timeoutMs: 30000);

        // The menu consumes the purchased items on the same shared owners: equip 199 on 大侠
        // (the bag copy), then use 300 on 小侠 through its checked on-use script entry.
        host.Tap(Key.Escape);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.MainMenu);
        AssertThat(menu.Entries.Count).IsEqual(4); // 状态/防身/ITEMS/系统（#222/#224 恒有系统项）
        host.Tap(Key.Down);
        host.Tap(Key.Enter);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.EquipItems);
        AssertThat(string.Join(";", menu.EquipItemsView().Select(row => $"{row.ObjectId}:{row.Name}:{row.Amount}")))
            .IsEqual("197:HEADBAND:1;199:LEATHER CAP:2;210:CAPE:1");
        host.Tap(Key.Down);
        host.Tap(Key.Enter);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.EquipRoles);
        host.Tap(Key.Enter); // 大侠's empty head takes 199 (wLastUnequippedItem 0)
        await host.Wait(() => menu.Mode == ExplorationMenuMode.EquipItems);
        AssertThat(string.Join(";", menu.EquipItemsView().Select(row => $"{row.ObjectId}:{row.Name}:{row.Amount}")))
            .IsEqual("197:HEADBAND:1;199:LEATHER CAP:1;210:CAPE:1");
        host.Tap(Key.Escape);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.MainMenu);
        host.Tap(Key.Escape);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.Closed);

        host.Tap(Key.Escape);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.MainMenu);
        host.Tap(Key.Down); // ITEMS — the remembered cursor sits on 防身 (1) from the equip flow
        host.Tap(Key.Enter);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.Items);
        AssertThat(string.Join(";", menu.ItemsView().Select(row => $"{row.ObjectId}:{row.Name}:{row.Amount}")))
            .IsEqual("300:生命提升:1");
        host.Tap(Key.Enter);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.ItemTargets);
        // The battle engine's seeded rolls own the absolute settled HP (independently tested
        // by the M6-A chain); this ticket pins the menu continuity over it — use300 authors
        // +30 HP/MP (changeHpMp caps each word at max, global.c PAL_IncreaseHPMP).
        var targetBefore = menu.ItemTargetsView().Targets[1];
        host.Tap(Key.Down); // 小侠
        host.Tap(Key.Enter);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.ItemScript);
        await host.Wait(() => host.Main.Dialogue!.Session!.PendingDisplay is not null);
        await host.FinishDisplay();
        await host.Wait(() => menu.Mode == ExplorationMenuMode.ItemTargets);
        host.Tap(Key.Escape); // the consumed last copy returns straight to the item list
        await host.Wait(() => menu.Mode == ExplorationMenuMode.Items);
        AssertThat(menu.ItemsView().Count).IsEqual(0);
        host.Tap(Key.Escape);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.Closed);

        // The status page reads the same live owners: 大侠 defense 20+2 from the equipped 199,
        // 小侠 healed to 118 with capped MP.
        host.Tap(Key.Escape);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.MainMenu);
        host.Tap(Key.Up);
        host.Tap(Key.Up); // 状态
        host.Tap(Key.Enter);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.Status);
        var hero = menu.StatusView();
        AssertThat((hero.Attack, hero.Defense)).IsEqual((43, 22)); // 40+3 / 20+2 from 199's role0 row
        host.Tap(Key.Right);
        await host.Wait(() => menu.StatusMemberIndex == 1);
        var second = menu.StatusView();
        AssertThat((second.Hp, second.Mp))
            .IsEqual((targetBefore.Hp + 30, Math.Min(targetBefore.Mp + 30, second.MaxMp)));
        host.Tap(Key.Escape);
        await host.Wait(() => menu.Mode == ExplorationMenuMode.Closed);

        // Interaction #2: the shop reopens over the changed shared state — the owned panel now
        // counts 大侠's equipped 199 copy with the remaining bag copy, and the sell list no
        // longer offers the consumed 300. The follow-up battle #2 opens on the menu's state.
        host.Tap(Key.Enter);
        await host.Wait(() => host.Main.Dialogue!.Session!.PendingDisplay is not null);
        await host.FinishDisplay();
        await host.Wait(() => host.Main.Shop!.Mode == ExplorationShopMode.BuyList);
        host.Tap(Key.Down); // 199
        await host.AwaitUpdates(1); // the queued direction applies at the next real update
        AssertThat((shop.BuyStatusView().Cash, shop.BuyStatusView().Owned)).IsEqual((250u, 2)); // bag 1 + equipped 1
        host.Tap(Key.Escape);
        await host.Wait(() => shop.Mode == ExplorationShopMode.SellList);
        AssertThat(string.Join(";", shop.SellRowsView()
            .Select(row => $"{row.ObjectId}:{row.Name}:{row.Amount}")))
            .IsEqual("197:HEADBAND:1;199:LEATHER CAP:1;210:CAPE:1");
        host.Tap(Key.Escape);
        await host.Wait(() => host.Main.StoryBattleMode == StoryBattlePresentation.AttackMenu, timeoutMs: 30000);
        var next = host.Main.StoryBattle!.Current.Combat;
        AssertThat(next.Players[0].Defense).IsEqual(22); // the equipped 199 feeds the battle
        var roles = host.Main.StoryBattle!.Current.Roles;
        // Story 75's continuity: the battle opens on the menu's live state — 大侠 unchanged,
        // 小侠 exactly the healed status-page value (the seed's rolls stay the engine's domain).
        AssertThat((roles[0].Hp, roles[1].Hp)).IsEqual((hero.Hp, second.Hp));
        await FinishBattle();
        await host.Wait(() => host.Main.Interaction!.Phase == NpcInteractionPhase.Exploring, timeoutMs: 30000);
        return;

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
        }
    }

    [TestCase("zh-Hans", Timeout = 120000)]
    [TestCase("en", Timeout = 120000)]
    public async Task ShopPagesLocalizeLabelsNumbersAndBitmaps(string language)
    {
        // Story 85's content consistency: the shop consumes the same prepared language package
        // and BALL chain as the bag pages — names localize, numbers and bitmap identities stay.
        using var fixture = new AuthoredStoryFixture("PartyEquip", menuEquip: true, menuShop: true);
        var selection = GameSceneConfiguration.FromArguments("--story-profile", fixture.StoryUri,
            "--battle-visual-profile", fixture.VisualUri, "--battle-seed", "1", "--story-language", language);
        using var host = new ShopHost(selection);
        host.AssertHealthy();
        await host.Wait(() => host.Updates.Count > 0);
        bool zh = language == "zh-Hans";

        host.Tap(Key.Enter);
        await host.Wait(() => host.Main.Dialogue!.Session!.PendingDisplay is not null);
        await host.FinishDisplay();
        await host.Wait(() => host.Main.Shop!.Mode == ExplorationShopMode.BuyList);
        var shop = host.Main.Shop!;
        AssertThat(string.Join(";", shop.BuyRowsView().Select(row => $"{row.ObjectId}:{row.Name}:{row.Price}")))
            .IsEqual(zh ? "300:生命提升:120;199:LEATHER CAP:250;320:MEDAL:999"
                : "300:VITAL PILL:120;199:LEATHER CAP:250;320:MEDAL:999");
        var status = shop.BuyStatusView();
        AssertThat((status.CashLabel, status.OwnedLabel)).IsEqual((zh ? "现金" : "CASH", "OWNED"));

        // uigame.c PAL_BuyMenu_OnItemChange draws the selected row's BALL chunk; the shop-only
        // 320 carries its own chunk 41, and 199's empty-chunk word 0 draws no picture.
        AssertThat(host.Main.ShopView!.PresentedItemBitmap).IsEqual((300, 37, 4, 3));
        host.Tap(Key.Down);
        await host.Wait(() => host.Main.ShopView!.PresentedItemBitmap is null);
        host.Tap(Key.Down);
        await host.Wait(() => host.Main.ShopView!.PresentedItemBitmap is { } chunk41 && chunk41.Bitmap == 41);
        AssertThat(host.Main.ShopView!.PresentedItemBitmap).IsEqual((320, 41, 2, 2));

        // The confirm box and the sell panel localize through the same word contract.
        host.Tap(Key.Enter); // 320 is unaffordable at seed 600 -> inert, stays on the list
        await host.AwaitUpdates(2);
        host.Tap(Key.Up);
        host.Tap(Key.Up);
        host.Tap(Key.Enter);
        await host.Wait(() => shop.Mode == ExplorationShopMode.BuyConfirm);
        AssertThat((shop.ConfirmView().NoLabel, shop.ConfirmView().YesLabel, shop.ConfirmView().SelectedIndex))
            .IsEqual(("NO", "YES", 0));
        host.Tap(Key.Down); // to Yes: the purchase lands 300 in the bag (600 -> 480)
        host.Tap(Key.Enter);
        await host.Wait(() => shop.Mode == ExplorationShopMode.BuyList);
        host.Tap(Key.Escape);
        await host.Wait(() => shop.Mode == ExplorationShopMode.SellList);
        AssertThat(shop.SellStatusView().PriceLabel).IsEqual("PRICE");
        AssertThat(host.Main.ShopView!.PresentedItemBitmap is null).IsTrue(); // 197's empty-chunk word 0
        host.Tap(Key.Down);
        host.Tap(Key.Down);
        host.Tap(Key.Down); // 300
        await host.Wait(() => host.Main.ShopView!.PresentedItemBitmap is { } chunk37 && chunk37.Bitmap == 37);
        AssertThat(host.Main.ShopView!.PresentedItemBitmap).IsEqual((300, 37, 4, 3));
        host.Tap(Key.Escape);
        await host.Wait(() => host.Main.Shop!.Mode == ExplorationShopMode.Closed);
        AssertThat(host.Main.ShopView).IsNull();
    }

    [TestCase(Timeout = 120000)]
    public async Task ShopSellMenuPresentsTheEmptyBagListThroughTheRealHost()
    {
        // Review fix (shop-sell-empty-list-view-crash): itemmenu.c counts an empty compressed
        // bag without rejecting and uigame.c PAL_SellMenu opens over it — the empty sell page
        // stays presentable in the formal host (the cash panel with the reference's empty
        // price box, no rows, no picture); Search/directions stay inert and only cancel
        // closes it, resuming the parked trigger into the battle like any other close.
        using var fixture = new AuthoredStoryFixture("PartyEquip", menuEquip: true, menuShop: true, menuShopEmptyBag: true);
        var selection = GameSceneConfiguration.FromArguments("--story-profile", fixture.StoryUri,
            "--battle-visual-profile", fixture.VisualUri, "--battle-seed", "1", "--story-language", "zh-Hans");
        using var host = new ShopHost(selection);
        host.AssertHealthy();
        await host.Wait(() => host.Updates.Count > 0);
        host.Tap(Key.Enter);
        await host.Wait(() => host.Main.Dialogue!.Session!.PendingDisplay is not null);
        await host.FinishDisplay();
        await host.Wait(() => host.Main.Shop!.Mode == ExplorationShopMode.BuyList);
        var shop = host.Main.Shop!;
        host.Tap(Key.Escape); // the buy loop cancels into the next shop command (0x0027)
        await host.Wait(() => shop.Mode == ExplorationShopMode.SellList);
        AssertThat(shop.SellRowsView().Count).IsEqual(0);
        AssertThat(shop.SelectedSellItem).IsEqual(0);
        // Presenting the empty page must not fail the host: the panel is cash-only
        // (OnItemChange's cash box; object 0 leaves the price box empty) and the absent row
        // draws no picture — the previously thrown status projection killed the update loop.
        var status = shop.SellStatusView();
        AssertThat((status.Cash, status.CashLabel, status.PriceLabel)).IsEqual((600u, "现金", ""));
        AssertThat(host.Main.ShopView is not null).IsTrue();
        AssertThat(host.Main.ShopView!.PresentedItemBitmap is null).IsTrue();
        host.Tap(Key.Enter); // Search never confirms the empty list
        await host.AwaitUpdates(1);
        host.Tap(Key.Down); // moves clamp inside the empty list
        await host.AwaitUpdates(1);
        AssertThat(shop.Mode).IsEqual(ExplorationShopMode.SellList);
        AssertThat(shop.SellRowsView().Count).IsEqual(0);
        host.Tap(Key.Escape);
        await host.Wait(() => host.Main.Shop!.Mode == ExplorationShopMode.Closed);
        AssertThat(host.Main.ShopView).IsNull();
    }

    [TestCase(Timeout = 30000)]
    public void StoryLoadRejectsAShopBitmapWithoutItsPreparedNativeBinding()
    {
        // #217 fail-closed: a shop object's nonzero bitmap word without its prepared native
        // item bitmap rejects at the strict story boundary (prepare/load) — no placeholder
        // image and no silent skip can ever reach the shop pages.
        AuthoredStoryFixture? fixture = null;
        string failure = "";
        try { fixture = new AuthoredStoryFixture("PartyEquip", menuEquip: true, menuShop: true, menuShopBitmapsMissingBinding: true); }
        catch (InvalidDataException error) { failure = error.Message; }
        finally { fixture?.Dispose(); }
        AssertThat(failure.Contains("shop object 320 bitmap 41")).IsTrue();
        AssertThat(failure.Contains("has no prepared native item bitmap")).IsTrue();
    }

    [TestCase(Timeout = 30000)]
    public void PreparedLanguagesCoverEveryShopWordGlyphInBothLanguages()
    {
        // The shop label words join the menu word contract: both languages carry them and every
        // rune has a visible glyph, so no run can show mixed or missing shop lettering.
        var font = ResourceLoader.Load<Pal.Game.Resources.PalDialogueFont>("res://Samples/AuthoredBattle/Dialogue/Font.tres")!;
        AssertThat(font).IsNotNull();
        int[] words =
        [
            ExplorationShopSession.ConfirmNoWord, ExplorationShopSession.ConfirmYesWord,
            ExplorationShopSession.ShopOwnedWord, ExplorationShopSession.ShopPriceWord,
            ExplorationShopSession.CashWord, 320,
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
                    var glyph = font.GetGlyph(rune.Value);
                    AssertThat(rune.Value == ' ' || glyph.Bitmap.Any(value => value != 0)).IsTrue();
                }
            }
        }
        foreach (var rune in "0123456789:/".EnumerateRunes())
            AssertThat(font.GetGlyph(rune.Value).Bitmap.Any(value => value != 0)).IsTrue();
    }

    // Narrow host: it only drives and observes the real Main through SceneRunner and real input
    // events; no game assembly, time advancement or state shortcut exists here.
    private sealed class ShopHost : IDisposable
    {
        internal Main Main { get; } = new();
        private readonly ISceneRunner runner;
        private readonly Control? focus = ((SceneTree)Engine.GetMainLoop()).Root.GuiGetFocusOwner();
        internal System.Collections.Generic.List<ExplorationState> Updates { get; } = [];
        internal System.Collections.Generic.List<string> Failures { get; } = [];
        internal int UpdatesCount { get; private set; }

        public ShopHost(GameSceneConfiguration selection)
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
                throw new InvalidOperationException("Shop host failed: " + (Main.Failure ?? Failures[0]));
        }

        public void Press(Key key) => Send(key, true);
        public void Release(Key key) => Send(key, false);
        public void Tap(Key key) { Send(key, true); Send(key, false); }

        private void Send(Key key, bool pressed)
        {
            if (pressed) runner.SimulateKeyPress(key);
            else runner.SimulateKeyRelease(key);
        }

        public async Task Wait(Func<bool> predicate, int timeoutMs = 15000)
        {
            ulong start = Time.GetTicksMsec();
            int before = UpdatesCount;
            while (!predicate() || UpdatesCount == before)
            {
                AssertHealthy();
                if (Time.GetTicksMsec() - start > (ulong)timeoutMs)
                    throw new TimeoutException("Shop production boundary not observed.");
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
                    throw new TimeoutException("Shop host updates did not advance.");
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
