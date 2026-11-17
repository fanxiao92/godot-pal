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
            ExplorationMenuSession.RoleNameWordBase + 1,
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
