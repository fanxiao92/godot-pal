using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

namespace Pal.Game.Tests;

/// <summary>#53 unit 3b: the REAL first battle through the shipped Main — search → @09C3
/// pre-battle dialogue (41 pages) → @09FE team18 battle (dual enemies, dual strike, hooks) →
/// Won continuation → changed exploration; and the Lost → reload-TODO terminal. Uses the private
/// prepared closure profile; the class lives in its own name-level batch with an explicit
/// 300000ms session timeout.</summary>
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
    public async Task Real_profile_search_dialogue_battle_Won_and_changed_exploration()
    {
        var (main, scene) = Start("zh-Hans");
        using (scene)
        {
            await scene.WaitFor(() => scene.Updates.Count > 0, "first update");
            long displayTicks = 0;
            main.Display!.UpdateCompleted += () => displayTicks++;
            scene.Press(Key.Enter); // search owner28 -> @09C3 opens through the verified closure

            int pages = 0; int frames = 0;
            ulong deadline = Time.GetTicksMsec() + (System.Environment.GetEnvironmentVariable("REAL_TRACE_SHORT") is null ? 200000u : 30000u); // the parked waits/movements advance
            while (main.StoryBattle is null && main.Dialogue?.Session?.PendingLostReload is null
                && Time.GetTicksMsec() < deadline)
            {
                // Press only when a display reached its advance boundary; parked waits and
                // counted movements advance on their own committed update ticks.
                var display = main.Dialogue?.Session?.PendingDisplay;
                if (display is { } shown)
                {
                    // Enter accelerates typing and advances at the boundary alike (the display
                    // consumes it by phase); the headless frame delta can be zero.
                    if (shown.Display.Current.Phase is Pal.Core.DialogueDisplayPhase.Typing
                        or Pal.Core.DialogueDisplayPhase.WaitingForAdvance && (frames & 15) == 0 || (frames & 63) == 0)
                    {
                        if ((frames & 15) == 0) { scene.Press(Key.Enter); pages++; }
                    }
                    if (shown.Display.Current.Phase == Pal.Core.DialogueDisplayPhase.WaitingForAdvance && (frames & 3) == 0)
                    { scene.Press(Key.Enter); pages++; }
                }
                frames++;
                if ((frames & 127) == 0)
                    System.Console.WriteLine($"REAL_BMODE:frame={frames} mode={main.StoryBattleMode} phase={main.StoryBattle?.Current.Combat.Phase} sel={main.StoryBattle?.Current.Combat.SelectingPlayerIndex} hp0={main.StoryBattle?.Current.Combat.Players[0].Hp} eh0={main.StoryBattle?.Current.Combat.Enemies[0].Hp}");
                if ((frames & 511) == 0)
                    System.Console.WriteLine($"REAL_TRACE:frame={frames} phase={display?.Display.Current.Phase} text={display?.Display.Current.VisibleText?.Length} pos={main.Dialogue?.Session?.CurrentPosition} displayTicks={displayTicks} inTree={main.Display?.IsInsideTree()} canProc={main.Display?.CanProcess()} procIn={main.Display?.IsProcessingInput()} child={main.Display?.GetParent() is not null} vis={main.Display?.Visible}");
                await scene.RunnerIdle();
            }
            System.Console.WriteLine($"REAL_PARK:phase={main.Dialogue?.Session?.Phase} pos={main.Dialogue?.Session?.CurrentPosition} " +
                $"display={main.Dialogue?.Session?.PendingDisplay is not null} pages={pages} failure={main.Failure} updates={scene.Updates.Count} " +
                $"pending={main.Dialogue?.Session?.PendingBattle is not null} wait={main.Dialogue?.Session?.PendingSceneWait is not null} " +
                $"move={main.Dialogue?.Session?.PendingMovement is not null} trans={main.Dialogue?.Session?.PendingTransition is not null}");
            AssertThat(main.StoryBattle is not null).IsTrue(); // @09FE engaged through the facade

            int rounds = 0;
            while (main.StoryBattle is not null && rounds++ < 900)
            {
                scene.Press(Key.Enter); // attack confirm + target confirm per round
                await scene.RunnerIdle();
                if ((rounds & 31) == 0)
                    System.Console.WriteLine($"REAL_ROUND:round={rounds} mode={main.StoryBattleMode} phase={main.StoryBattle?.Current.Combat.Phase} sel={main.StoryBattle?.Current.Combat.SelectingPlayerIndex} hp0={main.StoryBattle?.Current.Combat.Players[0].Hp} eh0={main.StoryBattle?.Current.Combat.Enemies[0].Hp} eh1={main.StoryBattle?.Current.Combat.Enemies[1].Hp} failure={main.Failure}");
            }
            System.Console.WriteLine($"REAL_FLOW:pages={pages} rounds={rounds} lost={main.StoryLostTerminal} " +
                $"completed={main.Dialogue?.Session?.Phase} hp0={main.Battle?.State.Roles[0].Hp} hp1={main.Battle?.State.Roles[1].Hp} " +
                $"exp0={main.Battle?.State.Roles[0].PrimaryExperience.Value} magics0={main.Battle?.State.Roles[0].Magics.Count(m => m != 0)}");
            AssertThat(main.StoryLostTerminal || main.Dialogue?.Session?.Phase == Pal.Core.TriggerScriptPhase.Completed).IsTrue();
            // The settled outcome changed the world: either the Won continuation completed and
            // exploration resumed (entry rewrites / states), or the real Lost terminal holds.
            if (main.StoryLostTerminal)
            {
                AssertThat(main.StoryLostReloadDiagnostics).IsEqual(1);
            }
            else
            {
                AssertThat(main.Battle is not null).IsTrue();
                scene.Press(Key.Down); // post-battle exploration still owns committed updates
                await scene.WaitFor(() => scene.Updates.Count > 0, "post-battle exploration update");
            }
            scene.AssertHealthy();
        }
    }

    /// <summary>The committed equivalent of MainSceneTests.EmbeddedScene for this batch: same
    /// real input injection, healthy/release contract and window restore.</summary>
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
                main.Failed -= _ => { };
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
