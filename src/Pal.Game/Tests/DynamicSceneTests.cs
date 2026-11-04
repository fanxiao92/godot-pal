using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using Pal.Core;
using Pal.Game.Dialogue;
using static GdUnit4.Assertions;

namespace Pal.Game.Tests;

/// <summary>#52 no-combat dynamic-scene chain: real input starts the authored event, the scene
/// updating wait drives the auto object's counted movement and hide, the event itself walks the
/// searched NPC, and exploration resumes with every change retained.</summary>
[TestSuite, RequireGodotRuntime]
public class DynamicSceneTests
{
    private static GameSceneConfiguration DynamicSelection(AuthoredStoryFixture fixture) =>
        GameSceneConfiguration.FromArguments("--dynamic-profile", fixture.StoryUri, "--story-language", "zh-Hans");

    [TestCase]
    public async Task DynamicEventRunsDialogueCountedMovementHideAndRetainsExploration()
    {
        using var fixture = new AuthoredStoryFixture("Dynamic");
        using var host = new DynamicHost(DynamicSelection(fixture));
        var world = host.Main.Dynamic!.World;
        var obj1 = world.Objects.Single(o => o.Id == 1);
        var obj2 = world.Objects.Single(o => o.Id == 2);
        AssertThat(obj1.X).IsEqual(304);
        AssertThat(obj2.X).IsEqual(352);
        AssertThat(obj2.State).IsEqual(0); // the auto walker starts hidden

        // Real search input starts the event; the first dialogue parks the call.
        host.Tap(Key.Enter);
        await host.Wait(() => host.Main.Dynamic!.Trigger?.PendingDisplay is not null);
        AssertThat(host.Main.Dynamic.Current.Leader.X).IsEqual(320); // frozen world under the display

        // Direction keys during the dialogue never leak into exploration.
        host.Tap(Key.Right);
        host.Tap(Key.Left);
        await host.Wait(() => host.Updates > 3);
        AssertThat(host.Main.Dynamic.Current.Leader.X).IsEqual(320);
        AssertThat(host.Main.Dynamic.Current.Leader.Y).IsEqual(320);
        await host.AdvanceDialogue(); // the world stays frozen until the display completes
        await host.Wait(() => host.Main.Dynamic!.Trigger?.PendingSceneWait is not null);

        // The scene-updating wait drives the auto walker one step per real update tick: the
        // observer must see gradual movement, never a jump or a teleport to the end.
        List<(int X, int Y)> walked = [];
        host.Movement += position => walked.Add(position);
        await host.Wait(() => host.Main.Dynamic!.Trigger?.PendingDisplay is not null);
        AssertThat(obj2.AutoEntry).IsEqual(21); // the wait's last tick ran hide, end keep parked
        AssertThat(obj2.State).IsEqual(-1);      // 0x0052 flipped the state negative
        AssertThat(walked.Count).IsGreater(3);
        for (int i = 1; i < walked.Count; i++)
        {
            int stepX = Math.Abs(walked[i].X - walked[i - 1].X), stepY = Math.Abs(walked[i].Y - walked[i - 1].Y);
            // One speed-3 PAL_NPCWalkTo step per tick; only the final near-target snap may jump
            // both words at once (script.c snaps when either offset is under speed*2).
            bool snap = i == walked.Count - 1 && (walked[i].X, walked[i].Y) == (288, 288);
            AssertBool(snap ? stepX <= 12 && stepY <= 6 : stepX <= 6 && stepY <= 3).IsTrue();
        }
        AssertThat(walked[0].X).IsEqual(346); // first counted West step from (352,320)
        AssertThat(walked[0].Y).IsEqual(317);
        AssertThat(walked[^1].X).IsEqual(288); // nine steps plus the near-target snap
        AssertThat(walked[^1].Y).IsEqual(288);
        await host.AdvanceDialogue();

        // The event's own counted movement advances the searched NPC per update, then rewrites.
        await host.Wait(() => host.Main.Dynamic!.Trigger?.PendingTransition is not null);
        AssertThat(obj1.X).IsEqual(288);
        AssertThat(obj1.Y).IsEqual(336);
        AssertThat(obj1.Direction).IsEqual(0);
        AssertThat(obj1.CurrentFrame).IsEqual(0);

        // The redraw boundary then the closing dialogue end the event; exploration resumes.
        await host.Wait(() => host.Main.Dynamic!.Trigger?.PendingDisplay is not null);
        await host.AdvanceDialogue();
        await host.Wait(() => host.Main.Dynamic!.Trigger is null && host.Main.Dynamic.Phase == DynamicEventPhase.Exploring);
        AssertThat(host.Main.Dynamic.LastScriptSuccess).IsTrue();

        // Retention: object state/vanish, trigger mode and entry, PC and rewrites stay saved.
        AssertThat(obj2.State).IsEqual(-1);       // 0x0052 hide kept the state negative
        AssertThat(obj2.VanishTime).IsLess(800);  // the retained exploration keeps counting it down
        AssertThat(obj2.AutoEntry).IsEqual(21);   // end keep parked the auto entry
        AssertThat(obj1.TriggerEntry).IsEqual(1); // the return entry was saved
        AssertThat(obj1.TriggerMode).IsEqual(0);  // 0x0040 rewrite kept
        AssertThat(world.Leader.X).IsEqual(320);  // 0x0046 PC position kept
        AssertThat(world.Leader.Y).IsEqual(320);
        AssertThat(world.Leader.Direction).IsEqual(3); // 0x0015 party facing kept
        AssertThat(world.SceneScripts[1].Item2).IsEqual(18); // 0x006D scene teleport entry kept
        AssertThat(world.ObjectScripts[(777, 4)]).IsEqual(18); // 0x0090 cross-scene slot kept
        // The hidden walker left the plan; the searched NPC is still drawn at its walked tile.
        AssertBool(host.ObjectVisible(2)).IsFalse();
        AssertBool(host.ObjectVisible(1)).IsTrue();

        // Exploration still answers real input after the event; nothing restarts on its own.
        int updates = host.Updates;
        await host.Wait(() => host.Updates > updates + 1);
        AssertThat(host.Main.Dynamic.PendingTrigger).IsNull();
    }

    [TestCase]
    public async Task SameUriSecondSceneStartsFromTheAuthoredInitialState()
    {
        using var fixture = new AuthoredStoryFixture("Dynamic");
        using var first = new DynamicHost(DynamicSelection(fixture));
        first.Tap(Key.Enter);
        await first.Wait(() => first.Main.Dynamic!.Trigger?.PendingDisplay is not null);
        await first.AdvanceDialogue();
        await first.Wait(() => first.Main.Dynamic!.Trigger?.PendingSceneWait is not null);

        // A new scene instance at the same prepared URI never inherits the first run's world.
        using var second = new DynamicHost(DynamicSelection(fixture));
        second.Tap(Key.Enter);
        await second.Wait(() => second.Main.Dynamic!.PendingTrigger is not null);
        var world = second.Main.Dynamic!.World;
        AssertThat(world.Objects.Single(o => o.Id == 2).State).IsEqual(0);
        AssertThat(world.Objects.Single(o => o.Id == 2).VanishTime).IsEqual(0);
        AssertThat(world.Objects.Single(o => o.Id == 1).X).IsEqual(304);
        AssertThat(world.Leader.X).IsEqual(320);
        AssertThat(world.Leader.Direction).IsEqual(0);
        // The first host's event keeps running without leaking into the second scene.
        AssertThat(first.Main.Dynamic!.World.Objects.Single(o => o.Id == 2).State).IsEqual(1);
    }

    internal sealed class DynamicHost : IDisposable
    {
        internal Main Main { get; } = new();
        private readonly ISceneRunner runner;
        internal int Updates { get; private set; }
        internal event Action<(int X, int Y)>? Movement;
        private (int X, int Y)? lastWalker;

        internal DynamicHost(GameSceneConfiguration selection)
        {
            Main.Configure(selection);
            Main.UpdateCompleted += Updated;
            var container = new Node();
            container.AddChild(Main);
            runner = ISceneRunner.Load(container, autoFree: true);
        }

        private void Updated()
        {
            Updates++;
            var walker = Main.Dynamic?.World.Objects.SingleOrDefault(o => o.Id == 2);
            if (walker is null) return;
            var position = (walker.X, walker.Y);
            if (lastWalker != position)
            {
                lastWalker = position;
                Movement?.Invoke(position);
            }
        }

        internal bool ObjectVisible(int objectId) =>
            Main.Dynamic!.Current.Plan.Commands.Any(c => c.Source == SceneDrawSource.Object && c.ObjectId == objectId);

        internal void Tap(Key key) { Send(key, true); Send(key, false); }

        /// <summary>A dialogue advance only lands once the wait has accumulated its 100ms
        /// window (text.c), so the host delivers fresh search presses until the display moves.</summary>
        internal async Task AdvanceDialogue()
        {
            await Wait(() => Main.Display!.Session!.Current.Phase == DialogueDisplayPhase.WaitingForAdvance);
            ulong start = Time.GetTicksMsec();
            while (Main.Display!.Session!.Current.Phase == DialogueDisplayPhase.WaitingForAdvance)
            {
                if (Main.Failure is not null) throw new InvalidOperationException(Main.Failure);
                Tap(Key.Enter);
                await runner.AwaitIdleFrame();
                if (Time.GetTicksMsec() - start > 8000) throw new TimeoutException("dialogue advance never landed");
            }
        }

        private static void Send(Key key, bool pressed)
        {
            using var input = new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed };
            Input.ParseInputEvent(input);
            Input.FlushBufferedEvents();
        }

        internal async Task Wait(Func<bool> predicate)
        {
            ulong start = Time.GetTicksMsec();
            int before = Updates;
            while (!predicate() || Updates == before)
            {
                if (Main.Failure is not null) throw new InvalidOperationException(Main.Failure);
                if (Time.GetTicksMsec() - start > 8000)
                    throw new TimeoutException("Dynamic production boundary not observed; phase=" + Main.Dynamic?.Phase);
                await runner.AwaitIdleFrame();
            }
            if (Main.Failure is not null) throw new InvalidOperationException(Main.Failure);
        }

        public void Dispose() => Main.UpdateCompleted -= Updated;
    }
}
