using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Pal.Game.Profiles;
using System.Threading.Tasks;
using Godot;
using Pal.Core;
using Pal.Game.Resources;

namespace Pal.Game.Tests;

// Normal C# caller of the shipped scene and its readonly runtime state. Input goes through InputMap.
public partial class ExplorationObserver : Node
{
    private Main main = null!;
    private int frame;
    private ExplorationState? last;
    private static readonly Key[] Arrows = [Key.Down, Key.Left, Key.Up, Key.Right];

    public override async void _Ready()
    {
        try
        {
            GD.Print($"OWNED_OBSERVER:pid={OS.GetProcessId()}:executable={OS.GetExecutablePath()}:created={System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime():O}");
            main = ResourceLoader.Load<PackedScene>("res://Scenes/Main.tscn").Instantiate<Main>();
            AddChild(main);
            Check(main.Exploration != null, "Main exposes initialized exploration");
            Trace("start");
            string scenario = OS.GetEnvironment("PAL_EXPLORATION_CASE");
            if (scenario == "integration-scene2") await IntegrationScene2();
            else if (scenario == "default-scene2") await DefaultScene2();
            else if (scenario == "dynamic") await Dynamic();
            else if (scenario.StartsWith("arrow-", StringComparison.Ordinal)) await Arrow(scenario[6..]);
            else if (scenario.StartsWith("raw-", StringComparison.Ordinal)) await RawFrames(int.Parse(scenario[4..]));
            else if (scenario.StartsWith("blocked-", StringComparison.Ordinal)) await Blocked(scenario[8..]);
            else await South();
            GD.Print("PAL_EXPLORATION_OBSERVED:" + (string.IsNullOrEmpty(scenario) ? "south" : scenario));
            GetTree().Quit(0);
        }
        catch (Exception e)
        {
            GD.PrintErr("Exploration observation failed: " + e.Message);
            GetTree().Quit(1);
        }
        finally
        {
            foreach (var key in Arrows) KeyEvent(key, false);
        }
    }

    private async Task Arrow(string direction)
    {
        // Literal PAL direction/position/frame examples, not a second movement algorithm.
        var expected = direction switch
        {
            "south" => (Key.Down, 0, 304, 328, 288, 336, 272, 344, 144, 216, 1, 0, 2),
            "west" => (Key.Left, 1, 304, 312, 288, 304, 272, 296, 144, 200, 4, 3, 5),
            "north" => (Key.Up, 2, 336, 312, 352, 304, 368, 296, 176, 200, 7, 6, 8),
            "east" => (Key.Right, 3, 336, 328, 352, 336, 368, 344, 176, 216, 10, 9, 11),
            _ => throw new InvalidOperationException("unknown direction")
        };
        var (key, facing, x1, y1, x2, y2, x3, y3, cameraX, cameraY, frame1, frame2, frame3) = expected;
        string action = "exploration_" + direction;
        var bindings = InputMap.ActionGetEvents(action);
        Check(bindings.Count == 1 && bindings[0] is InputEventKey binding && binding.Keycode == key,
            "dedicated project action contains only the specified arrow");
        KeyEvent(key, true);
        await NextFrame(); // ParseInputEvent is buffered until the engine flushes input.
        Check(Input.IsActionPressed(action), "ParseInputEvent resolves project InputMap binding");
        await Until(() => main.Exploration!.IsWalking, "arrow must walk");
        var first = main.Exploration!;
        Check((first.Leader.X, first.Leader.Y, first.Leader.Direction, first.Leader.StepPhase, first.Plan.LeaderFrameIndex)
            == (x1, y1, facing, 1, frame1), "first arrow literal state");
        Check((first.Plan.CameraX, first.Plan.CameraY) == (cameraX, cameraY), "first arrow literal camera");
        Check((first.Leader.X - first.Plan.CameraX, first.Leader.Y - first.Plan.CameraY) == (160, 112), "fixed screen anchor");
        ulong firstTick = Time.GetTicksMsec();
        int shortFrames = 0;
        for (int i = 0; i < 3; i++)
        {
            KeyEvent(key, true, echo: true);
            await NextFrame();
            if (Time.GetTicksMsec() - firstTick < 70)
            {
                Check(ReferenceEquals(first, main.Exploration), "render frames/echo below deadline cannot step");
                shortFrames++;
            }
        }
        Check(shortFrames > 0, "observed actual render frame below 100ms deadline");
        await Until(() => !ReferenceEquals(first, main.Exploration), "held next step");
        var second = main.Exploration!;
        Check(Time.GetTicksMsec() - firstTick >= 70, "held step uses real clock rather than render frequency");
        Check((second.Leader.X, second.Leader.Y, second.Leader.StepPhase, second.Plan.LeaderFrameIndex)
            == (x2, y2, 2, frame2), "held second literal state");
        await Until(() => !ReferenceEquals(second, main.Exploration), "held third step");
        Check((main.Exploration!.Leader.X, main.Exploration.Leader.Y, main.Exploration.Leader.StepPhase,
            main.Exploration.Plan.LeaderFrameIndex) == (x3, y3, 3, frame3), "held third literal state");
        KeyEvent(key, false);
        await NextFrame();
        Check(!Input.IsActionPressed(action), "release clears project action");
        await Until(() => !main.Exploration!.IsWalking, "release standing");
        var stopped = main.Exploration!;
        Check((stopped.Leader.X, stopped.Leader.Y, stopped.Leader.StepPhase, stopped.Plan.LeaderFrameIndex)
            == (x3, y3, 0, frame2), "stop preserves position and uses standing frame/phase");
        await Until(() => !ReferenceEquals(stopped, main.Exploration), "idle due update");
        Check((main.Exploration!.Leader.X, main.Exploration.Leader.Y, main.Exploration.Plan.LeaderFrameIndex)
            == (x3, y3, frame2), "standing does not animate while idle");
        Check(main.Exploration.Leader.WalkFramesWord == 0, "raw WORD0 remains unmodified");
        KeyEvent(key, true);
        await Until(() => main.Exploration!.IsWalking, "arrow restart");
        var restart = direction switch
        {
            "south" => (256, 352), "west" => (256, 288), "north" => (384, 288), "east" => (384, 352),
            _ => throw new InvalidOperationException("unknown direction")
        };
        Check((main.Exploration!.Leader.X, main.Exploration.Leader.Y) == restart
            && main.Exploration.Leader.StepPhase == 3 && main.Exploration.Plan.LeaderFrameIndex == frame3,
            "restart uses idle-retained phase and literal position/frame");
        KeyEvent(key, false);
    }

    private async Task Blocked(string kind)
    {
        bool map = kind == "map";
        var initial = main.Exploration!;
        KeyEvent(map ? Key.Left : Key.Down, true);
        await Until(() => !ReferenceEquals(initial, main.Exploration), "blocked due update");
        var state = main.Exploration!;
        Check((state.Leader.X, state.Leader.Y, state.Leader.Direction, state.Leader.StepPhase, state.Plan.LeaderFrameIndex)
            == (320, 320, map ? 1 : 0, 2, map ? 3 : 0), "blocked turns standing without displacement");
        Check(!state.IsWalking && (state.Plan.CameraX, state.Plan.CameraY) == (160, 208), "blocked camera and walking state");
        await Until(() => !ReferenceEquals(state, main.Exploration), "blocked held second update");
        Check((main.Exploration!.Leader.X, main.Exploration.Leader.Y, main.Exploration.Leader.StepPhase)
            == (320, 320, 0), "blocked held stays put without sliding and alternates stop phase");
        Check(main.Exploration.Plan.Commands.All(c => c.Source != SceneDrawSource.Object), "hidden spriteless blocker has no draw command");
        KeyEvent(map ? Key.Left : Key.Down, false);
    }

    private async Task RawFrames(int raw)
    {
        KeyEvent(Key.Down, true);
        await Until(() => main.Exploration!.IsWalking, "raw first step");
        var first = main.Exploration!;
        Check((first.Leader.X, first.Leader.Y, first.Leader.StepPhase, first.Plan.LeaderFrameIndex) == (304, 328, 1, 1), "raw first frame");
        await Until(() => !ReferenceEquals(first, main.Exploration), "raw second step");
        var second = main.Exploration!;
        Check((second.Leader.X, second.Leader.Y, second.Leader.StepPhase, second.Plan.LeaderFrameIndex)
            == (288, 336, 2, raw == 4 ? 2 : 0), "raw second three/four frame branch");
        KeyEvent(Key.Down, false);
        await Until(() => !main.Exploration!.IsWalking, "raw release");
        Check((main.Exploration!.Leader.StepPhase, main.Exploration.Plan.LeaderFrameIndex) == (0, 0), "raw stop resets high phase to zero");
        KeyEvent(Key.Down, true);
        await Until(() => main.Exploration!.IsWalking, "raw restart");
        Check((main.Exploration!.Leader.X, main.Exploration.Leader.Y, main.Exploration.Leader.StepPhase,
            main.Exploration.Plan.LeaderFrameIndex) == (272, 344, 1, 1), "raw restart uses retained stop phase");
        KeyEvent(Key.Down, false);
        Check(main.Exploration.Leader.WalkFramesWord == raw, "raw metadata never rewritten");
    }

    private async Task Dynamic()
    {
        bool gpu = !string.IsNullOrEmpty(OS.GetEnvironment("PAL_EXPLORATION_GPU_EVIDENCE"));
        if (gpu)
        {
            using var initial = await Readback("dynamic-start");
            Pixels(initial, [(155, 99, Colors.Blue), (164, 99, Colors.Green), (159, 101, Colors.Magenta),
                (158, 101, Colors.Red), (172, 101, Colors.Blue), (178, 108, Colors.Magenta)]);
        }
        // Observe a real idle deadline, so first walking phase is known without controlling the clock.
        var before = main.Exploration!;
        await Until(() => !ReferenceEquals(before, main.Exploration), "dynamic idle boundary");
        int phase = main.Exploration!.Leader.StepPhase;
        KeyEvent(Key.Down, true);
        await Until(() => main.Exploration!.IsWalking, "dynamic south walk");
        KeyEvent(Key.Down, false);
        var walked = main.Exploration!;
        Check((walked.Leader.X, walked.Leader.Y, walked.Plan.CameraX, walked.Plan.CameraY) == (304, 328, 144, 216), "dynamic literal movement/camera");
        int expectedFrame = phase == 0 ? 1 : 2;
        Check(walked.Plan.LeaderFrameIndex == expectedFrame, "dynamic three-frame literal branch");
        var actor = walked.Plan.Commands.Single(c => c.Source == SceneDrawSource.Leader);
        Check((actor.Left, actor.Top) == (154, expectedFrame == 1 ? 96 : 95), "native walking frame dimensions determine top-left");
        var obj = walked.Plan.Commands.Single(c => c.Source == SceneDrawSource.Object);
        Check((obj.ObjectId, obj.FrameIndex, obj.Left, obj.Top) == (1, 0, 187, 92), "object world position and frame remain unchanged while camera moves");
        if (gpu)
        {
            using var moved = await Readback("dynamic-south");
            Color walking = expectedFrame == 1 ? Colors.Cyan : Colors.Yellow;
            Pixels(moved, [(155, 99, walking), (164, 99, walking), (154, 115, walking),
                (153, 115, Colors.Red), (188, 93, Colors.Blue), (180, 91, Colors.Green),
                (188, 100, Colors.Magenta), (197, 100, Colors.Magenta), (198, 100, Colors.Blue)]);
        }
        await Until(() => !main.Exploration!.IsWalking, "dynamic standing");
        if (gpu)
        {
            using var standing = await Readback("dynamic-release");
            Pixels(standing, [(155, 97, Colors.Magenta), (158, 101, Colors.Red), (154, 115, Colors.Red),
                (165, 115, Colors.Magenta), (166, 115, Colors.Red)]);
            await AtomicRenderer();
            GD.Print("PAL_GPU_DYNAMIC:independent literal colors/alpha/cover/object anchors and atomic update passed");
        }
        Check(main.Exploration!.Leader.WalkFramesWord == 0, "dynamic metadata raw word stays zero");
    }

    private async Task AtomicRenderer()
    {
        using var image = Image.CreateEmpty(4, 4, false, Image.Format.Rgba8);
        image.Fill(Colors.Cyan);
        using var cyan = ImageTexture.CreateFromImage(image);
        image.Fill(Colors.Green);
        using var green = ImageTexture.CreateFromImage(image);
        var scene = new PalMapScene();
        try
        {
            Texture2D Resolve(SceneDrawCommand c) => c.FrameIndex switch
            {
                0 => cyan, 1 => green, _ => throw new InvalidOperationException("literal missing texture")
            };
            StaticScenePlan Plan(params SceneDrawCommand[] commands) => new(0, 0, 0, 0, commands);
            scene.InitializeScene(Plan(new SceneDrawCommand(SceneDrawSource.Leader, 17, 0, 10, 10, 999, 0)), Resolve);
            AddChild(scene);
            // Deliberately reversed SortY: renderer must consume final array order and exact Left/Top.
            scene.UpdateScene(Plan(new(SceneDrawSource.Leader, 17, 0, 20, 20, 999, 0),
                new(SceneDrawSource.Cover, 12, 1, 20, 20, -999, 0)));
            using var before = await Readback("renderer-before-reject");
            Pixels(before, [(20, 20, Colors.Green), (23, 23, Colors.Green), (10, 10, Colors.Red)]);
            bool rejected = false;
            try
            {
                scene.UpdateScene(Plan(new(SceneDrawSource.Leader, 17, 0, 40, 40, 0, 0),
                    new(SceneDrawSource.Cover, 12, 99, 50, 50, 0, 0)));
            }
            catch (InvalidOperationException e) when (e.Message == "literal missing texture") { rejected = true; }
            Check(rejected, "missing texture update must throw publicly");
            using var after = await Readback("renderer-after-reject");
            Check(before.GetData().SequenceEqual(after.GetData()), "all 64000 GPU pixels retained after partial resolution failure");
        }
        finally { scene.Free(); }
    }

    private static void Pixels(Image image, (int X, int Y, Color Color)[] samples)
    {
        foreach (var (x, y, color) in samples)
            Check(image.GetPixel(x, y).IsEqualApprox(color), $"literal GPU pixel({x},{y}) expected {color} got {image.GetPixel(x, y)}");
        GD.Print($"PAL_GPU_LITERAL:{samples.Length} independent color samples passed");
    }

    // Real native scene2 acceptance caller. The external route contains independent literal positions,
    // never generated from the observed plan; it cannot set Main's state, camera or clock.
    private async Task IntegrationScene2()
    {
        string evidence = OS.GetEnvironment("PAL_EXPLORATION_GPU_EVIDENCE");
        Check(!string.IsNullOrEmpty(evidence), "integration evidence directory required");
        Directory.CreateDirectory(evidence);
        var data = ResourceLoader.Load<PalSceneData>("res://Assets/Scenes/Scene.tres");
        string[] args = OS.GetCmdlineUserArgs();
        int profileArgument = Array.IndexOf(args, "--m2-profile");
        string profileUri = profileArgument < 0 ? NativeExplorationProfile.DefaultUri : args[profileArgument + 1];
        var profile = NativeExplorationProfile.Load(profileUri, data);
        string Objects() => JsonSerializer.Serialize(data.Objects.Select(o => new {
            o.Id, o.X, o.Y, o.SLayer, o.SState, o.SVanishTime, o.WSpriteNum, o.NSpriteFrames,
            o.WDirection, o.WCurrentFrameNum, o.WTriggerScript, o.WAutoScript, o.WTriggerMode,
            o.NScriptIdleFrame, o.WSpritePtrOffset, o.NSpriteFramesAuto, o.WScriptIdleFrameCountAuto,
            o.InitialFrameIndex, o.InitiallyVisible
        }), new JsonSerializerOptions { WriteIndented = true });
        string originalObjects = Objects();
        File.WriteAllText(Path.Combine(evidence, "objects-before.json"), originalObjects);
        File.WriteAllText(Path.Combine(evidence, "checked-start.json"), JsonSerializer.Serialize(new {
            profileUri, profile.Leader.X, profile.Leader.Y, profile.Neighbors
        }, new JsonSerializerOptions { WriteIndented = true }));

        // At most sixteen reachable origins/four candidates each. Query facts are not motion proof.
        var queue = new Queue<(int X, int Y, string Route)>();
        var seen = new HashSet<(int, int)>();
        queue.Enqueue((profile.Leader.X, profile.Leader.Y, ""));
        seen.Add((profile.Leader.X, profile.Leader.Y));
        var queries = new List<object>();
        (int X, int Y)[] deltas = [(-16, 8), (-16, -8), (16, -8), (16, 8)];
        for (int origin = 0; origin < 16 && queue.Count > 0; origin++)
        {
            var from = queue.Dequeue();
            for (int direction = 0; direction < 4; direction++)
            {
                int x = from.X + deltas[direction].X, y = from.Y + deltas[direction].Y;
                bool mapBlocked = profile.Rules.IsBlocked(x, y, checkObjects: false);
                bool blocked = profile.Rules.IsBlocked(x, y);
                queries.Add(new { from, direction, x, y, mapBlocked, blocked,
                    blockers = profile.Rules.Objects.Where(o => o.State >= 2 &&
                        Math.Abs(o.X - x) + Math.Abs(o.Y - y) * 2 < 16)
                        .Select(o => new { o.Id, o.X, o.Y, o.State,
                            distance = Math.Abs(o.X - x) + Math.Abs(o.Y - y) * 2 }).ToArray() });
                if (!blocked && seen.Add((x, y))) queue.Enqueue((x, y, from.Route + direction));
            }
        }
        File.WriteAllText(Path.Combine(evidence, "bounded-query.json"), JsonSerializer.Serialize(queries,
            new JsonSerializerOptions { IncludeFields = true, WriteIndented = true }));
        await IntegrationCapture(evidence, "start");
        string routePath = OS.GetEnvironment("PAL_INTEGRATION_ROUTE");
        if (!string.IsNullOrEmpty(routePath))
        {
            using var route = JsonDocument.Parse(File.ReadAllText(routePath));
            foreach (var leg in route.RootElement.EnumerateArray())
            {
                int direction = leg.GetProperty("direction").GetInt32();
                Check(direction is >= 0 and <= 3, "literal direction range");
                string action = new[] { "exploration_south", "exploration_west", "exploration_north", "exploration_east" }[direction];
                var bindings = InputMap.ActionGetEvents(action);
                Check(bindings.Count == 1 && bindings[0] is InputEventKey binding && binding.Keycode == Arrows[direction],
                    "real project InputMap arrow binding");
                KeyEvent(Arrows[direction], true);
                await NextFrame();
                Check(Input.IsActionPressed(action), "arrow is pressed through actual InputMap");
                foreach (var point in leg.GetProperty("positions").EnumerateArray())
                {
                    int x = point[0].GetInt32(), y = point[1].GetInt32();
                    await Until(() => main.Exploration!.Leader.X == x && main.Exploration.Leader.Y == y
                        && main.Exploration.Leader.Direction == direction && main.Exploration.IsWalking,
                        "actual Main reaches independent literal walking point");
                    IntegrationState(evidence, "walk");
                    // Observe a new state before accepting another held step, even on repeated points.
                    if (point.GetArrayLength() != 2) throw new InvalidOperationException("literal point requires x,y");
                }
                if (leg.TryGetProperty("blocked", out var block))
                {
                    int x = block[0].GetInt32(), y = block[1].GetInt32();
                    var before = main.Exploration;
                    await Until(() => !ReferenceEquals(before, main.Exploration) &&
                        main.Exploration!.Leader.Direction == direction && !main.Exploration.IsWalking,
                        "blocked arrow turns then stands");
                    Check((main.Exploration!.Leader.X, main.Exploration.Leader.Y) == (x, y), "blocked position unchanged");
                    IntegrationState(evidence, "blocked");
                    before = main.Exploration;
                    await Until(() => !ReferenceEquals(before, main.Exploration), "held blocked next due update");
                    Check((main.Exploration!.Leader.X, main.Exploration.Leader.Y) == (x, y)
                        && !main.Exploration.IsWalking, "held block cannot slide");
                    IntegrationState(evidence, "blocked-held");
                }
                KeyEvent(Arrows[direction], false);
                string name = leg.GetProperty("name").GetString()!;
                if (leg.TryGetProperty("capture", out var capture) && capture.GetBoolean())
                    await IntegrationCapture(evidence, name + "-released");
                await NextFrame();
                Check(!Input.IsActionPressed(action), "released arrow clears InputMap");
                await Until(() => !main.Exploration!.IsWalking, "release stands at actual deadline");
                var stopped = main.Exploration!;
                IntegrationState(evidence, name + "-standing");
                Check((stopped.Leader.X - stopped.Plan.CameraX, stopped.Leader.Y - stopped.Plan.CameraY) == (160, 112),
                    "literal fixed anchor after movement/stop/block");
                await Until(() => !ReferenceEquals(stopped, main.Exploration), "idle due update after release");
                Check((main.Exploration!.Leader.X, main.Exploration.Leader.Y, main.Exploration.Plan.CameraX,
                    main.Exploration.Plan.CameraY, main.Exploration.Plan.LeaderFrameIndex) ==
                    (stopped.Leader.X, stopped.Leader.Y, stopped.Plan.CameraX, stopped.Plan.CameraY, stopped.Plan.LeaderFrameIndex),
                    "idle retains position/camera/standing bitmap");
            }
        }
        string finalObjects = Objects();
        File.WriteAllText(Path.Combine(evidence, "objects-after.json"), finalObjects);
        Check(originalObjects == finalObjects, "all native object initial fields remain unchanged");
        GD.Print("PAL_INTEGRATION_SCOPE:actual Main/ParseInputEvent/InputMap/Process; logical GPU; NOT physical OS input/pixels or manual desktop");
    }

    private void IntegrationState(string evidence, string label)
    {
        var s = main.Exploration!;
        var state = new { label, observerProcessSignals = frame, ticks = Time.GetTicksMsec(), s.Leader.X, s.Leader.Y,
            s.Leader.Direction, s.Leader.StepPhase, s.Leader.WalkFramesWord, s.Leader.Layer, s.IsWalking,
            s.Plan.CameraX, s.Plan.CameraY, s.Plan.LeaderFrameIndex, s.Plan.Commands };
        File.AppendAllText(Path.Combine(evidence, "actual-state.jsonl"), JsonSerializer.Serialize(state) + System.Environment.NewLine);
        Trace("integration-" + label);
    }

    private async Task IntegrationCapture(string evidence, string name)
    {
        using var image = await Readback(name);
        IntegrationState(evidence, "capture-" + name);
    }

    private async Task DefaultScene2()
    {
        Check((main.Exploration!.Leader.X, main.Exploration.Leader.Y) == (736, 688), "default profile scene2 start");
        await Capture("scene2-start");
        KeyEvent(Key.Down, true);
        await Until(() => main.Exploration!.IsWalking, "default south arrow walk");
        var s = main.Exploration!;
        Check((s.Leader.X, s.Leader.Y) == (720, 696), "default first successful south step");
        Check((s.Plan.CameraX, s.Plan.CameraY) == (560, 584), "default south camera");
        Check((s.Leader.X - s.Plan.CameraX, s.Leader.Y - s.Plan.CameraY) == (160, 112), "default leader screen anchor");
        KeyEvent(Key.Down, false);
        await Capture("scene2-south");
        await Until(() => !main.Exploration!.IsWalking, "default release standing");
        Check((main.Exploration!.Leader.X, main.Exploration.Leader.Y) == (720, 696), "default release retains position");
        await Capture("scene2-release");
        GD.Print("PAL_GPU_SCOPE:programmatic ParseInputEvent arrows; logical GPU readback; not physical OS input/pixels or manual desktop acceptance");
    }

    private async Task Capture(string name)
    {
        string evidence = OS.GetEnvironment("PAL_EXPLORATION_GPU_EVIDENCE");
        if (string.IsNullOrEmpty(evidence)) return;
        using var image = await Readback(name);
    }

    private async Task<Image> Readback(string name)
    {
        string evidence = OS.GetEnvironment("PAL_EXPLORATION_GPU_EVIDENCE");
        Check(DisplayServer.GetName() != "headless", "GPU evidence requires non-headless rendering");
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = GetViewport().GetTexture().GetImage();
        Check(image.GetSize() == new Vector2I(320, 200), "logical GPU readback is 320x200");
        System.IO.Directory.CreateDirectory(evidence);
        Check(image.SavePng(System.IO.Path.Combine(evidence, name + ".png")) == Error.Ok, "save GPU evidence");
        Trace("gpu-" + name);
        return image;
    }

    private async Task South()
    {
        var start = main.Exploration!;
        Check((start.Leader.X, start.Leader.Y) == (320, 320), "literal fixture start");
        KeyEvent(Key.Down, true);
        await Until(() => main.Exploration!.IsWalking, "south arrow must walk via actual Main.Process");
        var state = main.Exploration!;
        Check((state.Leader.X, state.Leader.Y, state.Leader.Direction) == (304, 328, 0), "south first literal position/facing");
        Check((state.Plan.CameraX, state.Plan.CameraY, state.Plan.LeaderFrameIndex) == (144, 216, 1), "south literal camera/frame");
        KeyEvent(Key.Down, false);
        await Until(() => !main.Exploration!.IsWalking, "release must stand");
        Check((main.Exploration!.Leader.X, main.Exploration.Leader.Y) == (304, 328), "release retains position");
    }

    private async Task Until(Func<bool> predicate, string message)
    {
        ulong start = Time.GetTicksMsec();
        while (!predicate() && Time.GetTicksMsec() - start < 650) await NextFrame();
        Check(predicate(), message);
    }

    private async Task NextFrame()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        frame++;
        if (!ReferenceEquals(last, main.Exploration)) Trace("state");
    }

    private void Trace(string kind)
    {
        last = main.Exploration;
        var s = last!;
        GD.Print($"TRACE:{kind}:render={frame}:ticks={Time.GetTicksMsec()}:xy={s.Leader.X},{s.Leader.Y}:dir={s.Leader.Direction}:phase={s.Leader.StepPhase}:walking={s.IsWalking}:camera={s.Plan.CameraX},{s.Plan.CameraY}:frame={s.Plan.LeaderFrameIndex}");
    }

    private void KeyEvent(Key key, bool pressed, bool echo = false)
    {
        using var input = new InputEventKey { Keycode = key, Pressed = pressed, Echo = echo };
        Input.ParseInputEvent(input);
        GD.Print($"INPUT:render={frame}:ticks={Time.GetTicksMsec()}:key={key}:pressed={pressed}:echo={echo}");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
