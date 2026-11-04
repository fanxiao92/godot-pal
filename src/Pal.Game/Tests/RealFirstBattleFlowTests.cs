using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

namespace Pal.Game.Tests;

/// <summary>#53 unit 3b phased real assertions (parent ruling (b)): the retail profile's
/// full campaign cannot finish inside an automated session (real wall-clock pacing: 2x350HP
/// enemies vs the frozen party), so this committed case proves the real phases — search →
/// pre-battle pages → @09FE engagement → real bounded damage trajectory — with the phases
/// chained by trajectory assertions. The continuous full flow (Won/Lost end-to-end) is
/// covered by the committed synthetic full-closure fixture case; retail full-campaign stays
/// optional human-desktop acceptance for #54. Private real resources only.</summary>
[TestSuite, RequireGodotRuntime]
public class RealFirstBattleFlowTests
{
    private const string ProfileUri = "res://Assets/M4RealBattle53Closure/Battle/Profile.tres";

    private static (Main Main, RealFlowScene Scene) Start(string language, long? seed = null)
    {
        var arguments = new[] { "--m4-first-battle-profile", ProfileUri, "--m4-first-battle-language", language };
        var selection = seed is { } value ? GameSceneConfiguration.FromArguments(
            arguments[0], arguments[1], arguments[2], arguments[3], "--battle-seed", value.ToString())
            : GameSceneConfiguration.FromArguments(arguments);
        var main = new Main();
        main.Configure(selection);
        var scene = new RealFlowScene(main);
        scene.AssertHealthy();
        return (main, scene);
    }

    [TestCase(Timeout = 280000)]
    public async Task Real_phases_search_pages_engagement_and_bounded_damage_trajectory()
    {
        var (main, scene) = Start("zh-Hans");
        using (scene)
        {
            await scene.WaitFor(() => scene.Updates.Count > 0, "first update");
            scene.Press(Key.Enter); // search owner28 -> @09C3 opens through the verified closure

            // Phase 1 — the real pre-battle performance advances through the production
            // display; parked waits/movements advance on their own committed update ticks.
            int pages = 0; int frames = 0;
            ulong deadline = Time.GetTicksMsec() + 200000;
            while (main.StoryBattle is null && main.Dialogue?.Session?.PendingLostReload is null
                && Time.GetTicksMsec() < deadline)
            {
                AdvanceDisplay(scene, main, ref pages, ref frames);
                await scene.RunnerIdle();
            }
            AssertThat(pages).IsGreater(0); // real dialogue pages advanced

            // Phase 2 — @09FE engagement: the verified facade hands the real team18 request
            // to the production battle host with both enemy slots alive.
            AssertThat(main.StoryBattle is not null).IsTrue();
            AssertThat(main.StoryBattle!.Current.Combat.Enemies.Count).IsEqual(2);
            AssertThat(main.StoryBattle.Current.Combat.Enemies.All(enemy => enemy.Hp > 0)).IsTrue();

            // Phase 3 — bounded real progression: fight a fixed number of presentation-paced
            // rounds and assert the real damage trajectory (strictly more total damage than at
            // engagement; enemies never heal above their engagement HP).
            int engagementTotal = main.StoryBattle.Current.Combat.Enemies.Sum(enemy => enemy.Hp);
            int rounds = 0;
            int lastTotal = engagementTotal;
            while (main.StoryBattle is not null && rounds < 600)
            {
                scene.Press(Key.Enter);
                await scene.RunnerIdle();
                rounds++;
                if ((rounds & 63) == 0 && main.StoryBattle is not null)
                {
                    int total = main.StoryBattle.Current.Combat.Enemies.Sum(enemy => enemy.Hp);
                    AssertThat(total).IsLessEqual(lastTotal); // no healing
                    lastTotal = total;
                }
            }
            int finalTotal = main.StoryBattle is null ? 0 : main.StoryBattle.Current.Combat.Enemies.Sum(enemy => enemy.Hp);
            AssertThat(finalTotal).IsLess(engagementTotal); // real damage landed
            System.Console.WriteLine($"REAL_PHASES:pages={pages} rounds={rounds} engagementTotal={engagementTotal} finalTotal={finalTotal} lost={main.StoryLostTerminal}");
            scene.AssertHealthy();
        }
    }

    private static void AdvanceDisplay(RealFlowScene scene, Main main, ref int pages, ref int frames)
    {
        var display = main.Dialogue?.Session?.PendingDisplay;
        if (display is not { } shown) return;
        // Enter accelerates typing and advances at the boundary alike (the headless frame
        // delta can be zero, so input-driven advancement is the reliable route).
        if ((frames & 15) == 0 || shown.Display.Current.Phase == Pal.Core.DialogueDisplayPhase.WaitingForAdvance && (frames & 3) == 0)
        {
            scene.Press(Key.Enter);
            pages++;
        }
        frames++;
    }

    /// <summary>The committed equivalent of MainSceneTests.EmbeddedScene: real input injection,
    /// healthy/release contract and window restore.</summary>
    internal sealed class RealFlowScene : IDisposable
    {
        private readonly ISceneRunner runner;
        private readonly Window window = ((SceneTree)Engine.GetMainLoop()).Root;
        private readonly Vector2I minSize;
        private readonly Vector2I size;
        private readonly double timeScale;
        private readonly int physicsTicks;
        private readonly Control? focus;
        private readonly System.Collections.Generic.HashSet<Key> keys = [];
        public Main Main { get; }
        public System.Collections.Generic.List<string> Failures { get; } = [];
        public System.Collections.Generic.List<Pal.Core.ExplorationState> Updates { get; } = [];

        public RealFlowScene(Main main)
        {
            Main = main;
            minSize = window.MinSize; size = window.Size;
            timeScale = Engine.TimeScale; physicsTicks = Engine.PhysicsTicksPerSecond;
            focus = window.GuiGetFocusOwner();
            main.Failed += m => Failures.Add(m);
            main.UpdateCompleted += () => Updates.Add(Main.Exploration!);
            try { runner = ISceneRunner.Load(main, autoFree: true); }
            catch
            {
                runner?.Dispose();
                if (GodotObject.IsInstanceValid(main)) main.Free();
                RestoreGlobals();
                throw;
            }
        }

        public void AssertHealthy()
        {
            AssertThat(Main.Failure).IsNull();
            AssertThat(Failures.Count).IsEqual(0);
        }

        public void Press(Key key)
        {
            keys.Add(key);
            SendKey(key, true);
        }

        private void Release(Key key)
        {
            SendKey(key, false);
            keys.Remove(key);
        }

        private static void SendKey(Key key, bool pressed)
        {
            using var input = new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed };
            Input.ParseInputEvent(input);
            Input.FlushBufferedEvents();
        }

        public async Task RunnerIdle() => await runner.AwaitIdleFrame();

        public async Task WaitFor(Func<bool> predicate, string boundary) => await WaitFor(predicate, boundary, 2000);

        public async Task WaitFor(Func<bool> predicate, string boundary, uint timeoutMs)
        {
            ulong start = Time.GetTicksMsec();
            while (!predicate())
            {
                if (Time.GetTicksMsec() - start > timeoutMs)
                    throw new TimeoutException("Required production boundary not observed: " + boundary + "; failure=" + Main.Failure);
                await runner.AwaitIdleFrame();
            }
        }

        private void RestoreGlobals()
        {
            window.MinSize = minSize; window.Size = size;
            Engine.TimeScale = timeScale; Engine.PhysicsTicksPerSecond = physicsTicks;
            if (GodotObject.IsInstanceValid(focus)) focus!.GrabFocus();
        }

        public void Dispose()
        {
            try
            {
                foreach (Key key in keys.ToArray()) Release(key);
                runner.Dispose();
                AssertThat(GodotObject.IsInstanceValid(Main)).IsFalse();
            }
            finally
            {
                if (GodotObject.IsInstanceValid(Main)) Main.Free();
                RestoreGlobals();
            }
        }
    }
}
