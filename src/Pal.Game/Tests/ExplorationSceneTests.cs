using System;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using Pal.Core;
using static GdUnit4.Assertions;

namespace Pal.Game.Tests;

[TestSuite, RequireGodotRuntime]
public class ExplorationSceneTests
{
    [TestCase("south", "arrow", Timeout = 20000)]
    [TestCase("west", "arrow", Timeout = 20000)]
    [TestCase("north", "arrow", Timeout = 20000)]
    [TestCase("east", "arrow", Timeout = 20000)]
    [TestCase("south", "wasd", Timeout = 20000)]
    [TestCase("west", "wasd", Timeout = 20000)]
    [TestCase("north", "wasd", Timeout = 20000)]
    [TestCase("east", "wasd", Timeout = 20000)]
    public async Task LogicalBindingsMoveHoldReleaseAndRestartActualMain(string direction, string layout)
    {
        // Independent literal oracle retained from ExplorationObserver.Arrow, not calculated movement.
        var expected = direction switch
        {
            "south" => new Route(Key.Down, Key.S, 0, 304, 328, 288, 336, 272, 344, 144, 216, 1, 0, 2, 256, 352),
            "west" => new Route(Key.Left, Key.A, 1, 304, 312, 288, 304, 272, 296, 144, 200, 4, 3, 5, 256, 288),
            "north" => new Route(Key.Up, Key.W, 2, 336, 312, 352, 304, 368, 296, 176, 200, 7, 6, 8, 384, 288),
            "east" => new Route(Key.Right, Key.D, 3, 336, 328, 352, 336, 368, 344, 176, 216, 10, 9, 11, 384, 352),
            _ => throw new InvalidOperationException("unknown direction")
        };
        var bindings = InputMap.ActionGetEvents("exploration_" + direction);
        AssertThat(bindings.Count).IsEqual(2);
        AssertThat(bindings.All(e => e is InputEventKey k && k.PhysicalKeycode == 0)).IsTrue();
        AssertThat(bindings.Cast<InputEventKey>().Select(k => k.Keycode).OrderBy(k => k)
            .SequenceEqual(new[] { expected.Arrow, expected.Letter }.OrderBy(k => k))).IsTrue();
        Key key = layout == "wasd" ? expected.Letter : expected.Arrow;
        using var fixture = new ExplorationFixture();
        fixture.Native.SavePacked();
        string uri = fixture.Native.ResourceRoot + "/Scenes/Scene.tscn";
        string sceneHash = Godot.FileAccess.GetSha256(uri);
        string profileHash = Godot.FileAccess.GetSha256(fixture.Native.Uri);
        using (var scene = new ExplorationScene(fixture.Native, direction, key, expected))
            await scene.Run();
        AssertThat(Input.IsActionPressed("exploration_" + direction)).IsFalse();
        // New node, same cached URI: prior movement must not mutate the shared native resources.
        using (var next = new ExplorationScene(fixture.Native, direction, key, expected))
            await next.Run(firstStepOnly: true);
        AssertThat(Input.IsActionPressed("exploration_" + direction)).IsFalse();
        AssertThat(fixture.Native.Profile.Leader.WalkFramesWord).IsEqual(0);
        AssertThat(fixture.Native.Scene.Sprites[0].FrameCount).IsEqual(12);
        AssertThat(Godot.FileAccess.GetSha256(uri)).IsEqual(sceneHash);
        AssertThat(Godot.FileAccess.GetSha256(fixture.Native.Uri)).IsEqual(profileHash);
    }

    private sealed record Route(Key Arrow, Key Letter, int Facing, int X1, int Y1, int X2, int Y2,
        int X3, int Y3, int CameraX, int CameraY, int Frame1, int Frame2, int Frame3, int RestartX, int RestartY);

    // This host only controls/observes the real Main; it has no game assembly or time advancement.
    private sealed class ExplorationScene : IDisposable
    {
        private enum Boundary { First, Second, Third, Stop, Idle, Restart, Complete }
        private readonly ISceneRunner? runner;
        private readonly Main Main = new();
        private readonly Window window = ((SceneTree)Engine.GetMainLoop()).Root;
        private readonly Vector2I minSize;
        private readonly Vector2I size;
        private readonly double timeScale;
        private readonly int physicsTicks;
        private readonly Control? focus;
        private readonly string action;
        private readonly Key key;
        private readonly Route route;
        private Boundary boundary;
        private ExplorationState? previous;
        private Exception? assertionFailure;
        private string? sceneFailure;
        private ulong boundaryStarted;
        private bool firstStepOnly;
        private bool running;
        private bool disposed;
        private int actualUpdates;

        public ExplorationScene(NativeProfileFixture fixture, string direction, Key key, Route route)
        {
            this.key = key; this.route = route; action = "exploration_" + direction;
            minSize = window.MinSize; size = window.Size;
            timeScale = Engine.TimeScale; physicsTicks = Engine.PhysicsTicksPerSecond;
            focus = window.GuiGetFocusOwner();
            try
            {
                Main.Configure(GameSceneConfiguration.FromArguments("--m2-resources", fixture.ResourceRoot,
                    "--m2-profile", fixture.Uri));
                Main.Failed += OnFailed;
                Main.UpdateCompleted += OnUpdated;
                runner = ISceneRunner.Load(Main, autoFree: true);
                Check(Main.Failure is null, "initialization: " + Main.Failure);
                var initial = Main.Exploration!;
                Check((initial.Leader.X, initial.Leader.Y, initial.Leader.StepPhase) == (320, 320, 0),
                    "independent scene starts at literal fixture position/phase");
            }
            catch
            {
                try { runner?.Dispose(); }
                finally { Cleanup(); }
                throw;
            }
        }

        private void OnFailed(string message) => sceneFailure = message;

        public async Task Run(bool firstStepOnly = false)
        {
            this.firstStepOnly = firstStepOnly;
            running = true;
            boundaryStarted = Time.GetTicksMsec();
            SendKey(true);
            Check(Input.IsActionPressed(action), "press resolves project InputMap action");
            try
            {
                while (boundary != Boundary.Complete && assertionFailure is null && sceneFailure is null)
                {
                    if (Time.GetTicksMsec() - boundaryStarted > 650)
                        throw new TimeoutException("Required actual Main.UpdateCompleted boundary not observed: " + boundary);
                    // Yield only. OnUpdated, not this continuation, samples and controls every transition.
                    await runner!.AwaitIdleFrame();
                }
                if (assertionFailure is not null) ExceptionDispatchInfo.Capture(assertionFailure).Throw();
                Check(sceneFailure is null && Main.Failure is null, "unexpected scene failure: " + (sceneFailure ?? Main.Failure));
                GD.Print($"PAL_EXPLORATION_SCENE_OBSERVED:{action}:{key}:firstOnly={firstStepOnly}:actualUpdates={actualUpdates}");
            }
            finally { running = false; SendKey(false); }
        }

        private void OnUpdated()
        {
            if (!running || boundary == Boundary.Complete || assertionFailure is not null) return;
            actualUpdates++;
            try
            {
                var state = Main.Exploration!;
                // The FIRST completion is unconditional: never skip a missed press by waiting for walking.
                if (boundary != Boundary.First && ReferenceEquals(previous, state)) return;
                switch (boundary)
                {
                    case Boundary.First:
                        State(state, route.X1, route.Y1, 1, route.Frame1, true, "first actual update");
                        Check((state.Plan.CameraX, state.Plan.CameraY) == (route.CameraX, route.CameraY), "first literal camera");
                        Check((state.Leader.X - state.Plan.CameraX, state.Leader.Y - state.Plan.CameraY) == (160, 112), "fixed screen anchor");
                        if (firstStepOnly)
                        {
                            SendKey(false);
                            Advance(Boundary.Complete, state);
                            return;
                        }
                        for (int i = 0; i < 3; i++) SendKey(true, echo: true);
                        Check(ReferenceEquals(state, Main.Exploration), "echo does not update state outside production update");
                        Check(Input.IsActionPressed(action), "echo retains held project action");
                        Advance(Boundary.Second, state);
                        break;
                    case Boundary.Second:
                        State(state, route.X2, route.Y2, 2, route.Frame2, true, "held second update");
                        Check(Input.IsActionPressed(action), "second update still holds action");
                        Advance(Boundary.Third, state);
                        break;
                    case Boundary.Third:
                        State(state, route.X3, route.Y3, 3, route.Frame3, true, "held third update");
                        SendKey(false);
                        Check(!Input.IsActionPressed(action), "release clears project action");
                        Advance(Boundary.Stop, state);
                        break;
                    case Boundary.Stop:
                        State(state, route.X3, route.Y3, 0, route.Frame2, false, "release standing update");
                        Advance(Boundary.Idle, state);
                        break;
                    case Boundary.Idle:
                        State(state, route.X3, route.Y3, 2, route.Frame2, false, "idle does not animate");
                        Check(state.Leader.WalkFramesWord == 0, "raw WORD0 remains unmodified");
                        SendKey(true);
                        Check(Input.IsActionPressed(action), "restart resolves project action");
                        Advance(Boundary.Restart, state);
                        break;
                    case Boundary.Restart:
                        State(state, route.RestartX, route.RestartY, 3, route.Frame3, true, "restart retains idle phase");
                        SendKey(false);
                        Check(!Input.IsActionPressed(action), "restart key released");
                        Advance(Boundary.Complete, state);
                        break;
                }
            }
            catch (Exception error)
            {
                // Do not throw through Main._Process: that would misclassify a test assertion as game failure.
                assertionFailure = error;
                SendKey(false);
            }
        }

        private void State(ExplorationState state, int x, int y, int phase, int frame, bool walking, string label) =>
            Check((state.Leader.X, state.Leader.Y, state.Leader.Direction, state.Leader.StepPhase,
                state.Plan.LeaderFrameIndex, state.IsWalking) == (x, y, route.Facing, phase, frame, walking),
                $"{label}: actual=({state.Leader.X},{state.Leader.Y},{state.Leader.Direction},{state.Leader.StepPhase}," +
                $"{state.Plan.LeaderFrameIndex},{state.IsWalking}); expected=({x},{y},{route.Facing},{phase},{frame},{walking}); actualUpdates={actualUpdates}");

        private void Advance(Boundary next, ExplorationState state)
        {
            previous = state;
            boundary = next;
            boundaryStarted = Time.GetTicksMsec();
        }

        private void SendKey(bool pressed, bool echo = false)
        {
            using var input = new InputEventKey { Keycode = key, Pressed = pressed, Echo = echo };
            Input.ParseInputEvent(input);
            Input.FlushBufferedEvents();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try
            {
                running = false;
                SendKey(false);
                Main.Failed -= OnFailed;
                Main.UpdateCompleted -= OnUpdated;
                runner!.Dispose();
                AssertThat(GodotObject.IsInstanceValid(Main))
                    .OverrideFailureMessage("SceneRunner.Dispose must free oldMain; disposed Main is still valid")
                    .IsFalse();
            }
            finally { Cleanup(); }
        }

        private void Cleanup()
        {
            try
            {
                SendKey(false);
                Main.Failed -= OnFailed;
                Main.UpdateCompleted -= OnUpdated;
                if (GodotObject.IsInstanceValid(Main)) Main.Free();
            }
            finally
            {
                window.MinSize = minSize; window.Size = size;
                Engine.TimeScale = timeScale; Engine.PhysicsTicksPerSecond = physicsTicks;
                if (GodotObject.IsInstanceValid(focus)) focus!.GrabFocus();
            }
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
