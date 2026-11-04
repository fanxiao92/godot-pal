using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Pal.Game.Dialogue;
using Pal.Game.Resources;

namespace Pal.Game.Tests;

/// <summary>M4 #54 unit C real-GPU acceptance entry (never selected by Main or the headless
/// suite; _Ready requires a graphical renderer and explicit environment selection). Modes:
/// "real" drives the staged private closure profile (static scene, pre-battle dialogue pages,
/// @09FE battle view, bounded real damage rounds); "synthetic-won"/"synthetic-lost" drive the
/// same production binding/host chain through the fast synthetic closure to the victory
/// settlement box and the lost red fade. Pixel checks follow the M3 GPU precedent: independently
/// computed expectations from the bound indexed resources (background compositing, glyph/HUD
/// palette colors, palette.c PAL_FadeToRed convergence) plus region sampling; every capture
/// keeps its SHA-256 in fixture-result.json for cross-language/operator comparison. Programmatic
/// input only; physical-keyboard acceptance is M4PhysicalDesktopFixture.</summary>
public partial class M4FirstBattleGpuFixture : Node
{
    private string evidence = "";
    private string mode = "";
    private string language = "zh-Hans";
    private string profileUri = "";
    private Main? main;
    private readonly List<object> checks = [];
    private readonly Dictionary<string, string> hashes = [];
    private int failures;
    private bool finished;
    private ulong started;
    private ulong lastCaptureFrame = ulong.MaxValue;
    private int phase; // 0 wait first update, 1 dialogue, 2 battle, 3 done
    private readonly List<int> enemyHpTrajectory = [];
    private int engagementTotal = -1;
    private int dialogueCaptures;
    private ulong lastDialogueCaptureAt;
    private int waitingFrames;
    private bool advanceAfterCapture;
    private bool searchPressed;
    private int downBefore = -1;
    private int downHeldFrames;
    private PalPalette? palette;
    private Color textColor; // dialogue glyph color: index 0x4F of the active palette
    private Color hudColor;  // battle HUD/settlement text: palette index 79
    // Reference battle.c:27-31 g_rgPlayerPos bottom-center columns for a party of two.
    private static readonly Vector2I[] ReferencePlayerOrigins = [new(200, 176), new(256, 152)];
    private PalDialogueIndexedImage? battleBackground;
    private Rect2I[] enemyRects = [];
    private Rect2I[] playerRects = [];
    // Upper-half map sample points for the red-fade oracle (the lost page's lower box and any
    // preserved-color text stay outside this band).
    private readonly (int X, int Y)[] fadePoints =
        [(16, 12), (64, 28), (112, 10), (160, 40), (208, 16), (256, 32), (300, 8), (40, 52), (96, 44), (152, 58), (220, 50), (288, 44)];
    private Color[]? preFadeSamples;
    private int midFadeStep = -1;
    private readonly System.Diagnostics.Stopwatch roundWatch = new();
    private int rounds;
    private int updates;
    private SyntheticFirstBattleFixture? synthetic;
    private NativeProfileFixture? syntheticStatics;
    // capture wants (observed in _Process, consumed on the next rendered frame)
    private bool staticCaptured, idleCaptured, feedbackCaptured, settlementCaptured, postBattleCaptured, fadeEndDone;
    private bool wantStatic, wantDialogue, wantIdle, wantFeedback, wantSettlement, wantPostBattle, wantFadePre, wantFadeMid, wantFadeEnd;

    public override void _Ready()
    {
        try
        {
            Require(DisplayServer.GetName() != "headless", "This fixture requires a graphical renderer.");
            evidence = RequiredEnvironment("PAL_M4_GPU_EVIDENCE");
            Require(Directory.Exists(evidence), "Evidence directory must already exist.");
            mode = RequiredEnvironment("PAL_M4_GPU_MODE");
            Require(mode is "real" or "synthetic-won" or "synthetic-lost", "Mode must be real, synthetic-won or synthetic-lost.");
            if (mode == "real")
            {
                language = RequiredEnvironment("PAL_M4_GPU_LANGUAGE");
                Require(language is "zh-Hans" or "en", "Real mode language must be zh-Hans or en.");
                profileUri = RequiredEnvironment("PAL_M4_GPU_PROFILE");
                Require(profileUri.StartsWith("res://", StringComparison.Ordinal), "Real mode requires a res:// profile URI.");
                LoadOracle(LoadRealVisual());
            }
            else
            {
                profileUri = BuildSyntheticProfile(mode == "synthetic-lost");
                LoadOracle(LoadSyntheticVisual());
            }
            var colors = Enumerable.Range(0, 256).Select(i => new Color(palette!.Rgb[i * 3] / 255f,
                palette.Rgb[i * 3 + 1] / 255f, palette.Rgb[i * 3 + 2] / 255f)).ToArray();
            textColor = colors[0x4F];
            hudColor = colors[79];
            main = new Main();
            main.Failed += m => Fail(new InvalidOperationException("Main failed: " + m));
            main.UpdateCompleted += () => updates++;
            main.Configure(mode == "real"
                ? GameSceneConfiguration.FromArguments("--m4-first-battle-profile", profileUri, "--m4-first-battle-language", language)
                : GameSceneConfiguration.FromArguments("--m4-first-battle-profile", profileUri));
            AddChild(main);
            started = Time.GetTicksMsec();
            RenderingServer.FramePostDraw += ObserveFrame;
            GD.Print($"PAL_M4_GPU_BEGIN:mode={mode}:language={language}:renderer={DisplayServer.GetName()}");
        }
        catch (Exception error) { Fail(error); }
    }

    private PalBattleVisualProfile LoadRealVisual()
    {
        var profile = ResourceLoader.Load<PalFirstMandatoryBattleProfile>(profileUri,
            cacheMode: ResourceLoader.CacheMode.IgnoreDeep)
            ?? throw new InvalidDataException("The selected real profile could not be loaded.");
        palette = ResourceLoader.Load<PalPalette>(VisualOf(profile).PaletteUri,
            cacheMode: ResourceLoader.CacheMode.IgnoreDeep)
            ?? throw new InvalidDataException("The visual palette could not be loaded.");
        return VisualOf(profile);
    }

    private static PalBattleVisualProfile VisualOf(PalFirstMandatoryBattleProfile profile) =>
        ResourceLoader.Load<PalBattleVisualProfile>(profile.VisualUri, cacheMode: ResourceLoader.CacheMode.IgnoreDeep)
            ?? throw new InvalidDataException("The profile's visual binding could not be loaded.");

    private PalBattleVisualProfile LoadSyntheticVisual()
    {
        var profile = ResourceLoader.Load<PalFirstMandatoryBattleProfile>(profileUri,
            cacheMode: ResourceLoader.CacheMode.IgnoreDeep)
            ?? throw new InvalidDataException("The synthetic closure profile could not be loaded.");
        var visual = VisualOf(profile);
        palette = ResourceLoader.Load<PalPalette>(visual.PaletteUri, cacheMode: ResourceLoader.CacheMode.IgnoreDeep)!;
        return visual;
    }

    private void LoadOracle(PalBattleVisualProfile visual)
    {
        var team = visual.Teams?.SingleOrDefault(t => t.TeamId == 18)
            ?? throw new InvalidDataException("The visual binding must carry team 18.");
        battleBackground = ResourceLoader.Load<PalDialogueIndexedImage>(team.BackgroundUri,
            cacheMode: ResourceLoader.CacheMode.IgnoreDeep)
            ?? throw new InvalidDataException("The bound battlefield background could not be loaded.");
        Require(team.EnemySlots is { Length: 2 }, "The team must bind exactly two enemy slots.");
        Require(visual.PlayerPosition == new Vector2I(200, 176),
            "Independent battle.c g_rgPlayerPos[1][0] expects player column zero at (200,176).");
        Require(team.EnemySlots[0].EnemyPosition == new Vector2I(70, 130)
            && team.EnemySlots[1].EnemyPosition == new Vector2I(140, 106),
            "Independent team18 slot layout expects (70,130) and (140,106).");
        var enemySprite = ResourceLoader.Load<PalSprite>(team.EnemySpriteUri,
            cacheMode: ResourceLoader.CacheMode.IgnoreDeep)!;
        int enemyWidth = enemySprite.Frames.Max(f => f.Width), enemyHeight = enemySprite.Frames.Max(f => f.Height);
        enemyRects = team.EnemySlots.Select(s => RectFor(s.EnemyPosition, enemyWidth, enemyHeight)).ToArray();
        var playerSprite = ResourceLoader.Load<PalSprite>(visual.PlayerSpriteUri,
            cacheMode: ResourceLoader.CacheMode.IgnoreDeep)!;
        int playerWidth = playerSprite.Frames.Max(f => f.Width), playerHeight = playerSprite.Frames.Max(f => f.Height);
        playerRects = ReferencePlayerOrigins.Select(o => RectFor(o, playerWidth, playerHeight)).ToArray();
    }

    private static Rect2I RectFor(Vector2I bottomCenter, int width, int height) =>
        new(bottomCenter.X - width / 2, bottomCenter.Y - height, width, height);

    private string BuildSyntheticProfile(bool lost)
    {
        // Same production chain as the committed SyntheticFirstBattleClosureTests cases.
        var battleFixture = new NativeBattleProfileFixture();
        syntheticStatics = battleFixture.Static;
        var statics = battleFixture.Static;
        foreach (var frame in statics.MapScene.Data.Map.Tiles.Frames)
        {
            frame.Indices = Array.Empty<byte>().Concat(Enumerable.Repeat((byte)0, frame.Width * frame.Height)).ToArray();
            frame.Alpha = Array.Empty<byte>().Concat(Enumerable.Repeat((byte)255, frame.Width * frame.Height)).ToArray();
        }
        foreach (var sprite in statics.MapScene.Data.Sprites)
            foreach (var frame in sprite.Frames)
            {
                frame.Indices = Array.Empty<byte>().Concat(Enumerable.Repeat((byte)0, frame.Width * frame.Height)).ToArray();
                frame.Alpha = Array.Empty<byte>().Concat(Enumerable.Repeat((byte)255, frame.Width * frame.Height)).ToArray();
            }
        statics.MapScene.Data.Palette = (PalPalette)ResourceLoader.Load("res://Samples/AuthoredBattle/Static/Palette.tres",
            cacheMode: ResourceLoader.CacheMode.IgnoreDeep)!;
        statics.SavePacked();
        string staticProfile = statics.ResourceRoot + "/Profile.tres";
        NativeProfileFixture.SaveResource(statics.Profile, staticProfile);
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(statics.ResourceRoot + "/Palettes"));
        File.Copy(ProjectSettings.GlobalizePath("res://Samples/AuthoredBattle/Static/Palette.tres"),
            ProjectSettings.GlobalizePath(statics.ResourceRoot + "/Palettes/Palette.tres"));
        synthetic = new SyntheticFirstBattleFixture(statics, statics.ResourceRoot, staticProfile, lost);
        return synthetic.ProfileUri;
    }

    public override void _Process(double delta)
    {
        if (finished) return;
        ulong now = Time.GetTicksMsec();
        if (now - started > 300000u)
        {
            Fail(new TimeoutException($"mode={mode} phase={phase} did not complete in the 300s liveness bound."));
            return;
        }
        if (main is null || main.Failure is not null) return;
        try
        {
            switch (phase)
            {
                case 0:
                    if (updates == 0) return;
                    if (mode == "real") { phase = 1; wantStatic = true; return; } // capture the static scene first
                    InputTap(Key.Enter); // search owner28 through the verified closure
                    searchPressed = true;
                    phase = 1;
                    return;
                case 1:
                {
                    if (mode == "real" && !searchPressed && staticCaptured)
                    {
                        searchPressed = true;
                        InputTap(Key.Enter);
                        return;
                    }
                    if (main.StoryBattle is not null || main.Dialogue?.Session?.PendingLostReload is not null)
                    {
                        phase = 2;
                        roundWatch.Start();
                        return;
                    }
                    var display = main.Dialogue?.Session?.PendingDisplay;
                    if (display is not null)
                    {
                        bool waiting = display.Display.Current.Phase == Pal.Core.DialogueDisplayPhase.WaitingForAdvance;
                        // Capture only a stably-waiting page: the display may lag the session by a
                        // frame, and capturing an in-transition frame photographs the bare map.
                        waitingFrames = waiting ? waitingFrames + 1 : 0;
                        if (advanceAfterCapture)
                        {
                            advanceAfterCapture = false;
                            waitingFrames = 0;
                            InputTap(Key.Enter); // advance to the next page after a capture
                            return;
                        }
                        if (mode == "real" && waitingFrames >= 6 && dialogueCaptures < 4 && now - lastDialogueCaptureAt > 350) wantDialogue = true;
                        // Hold the page (no Enter) until it is captured, so a stable waiting
                        // page cannot be advanced away mid-capture (real-mode dialogue evidence
                        // only; the synthetic pages are not part of this acceptance).
                        if (!(mode == "real" && dialogueCaptures < 4 && waitingFrames > 0)
                            && ((Engine.GetProcessFrames() & 7) == 0 || waiting)) InputTap(Key.Enter);
                    }
                    else waitingFrames = 0;
                    return;
                }
                case 2:
                {
                    var battle = main.StoryBattle;
                    if (battle is null) { phase = 3; return; }
                    if (engagementTotal < 0) engagementTotal = battle.Current.Combat.Enemies.Sum(e => e.Hp);
                    if (!idleCaptured && main.StoryBattleMode == StoryBattlePresentation.AttackMenu) wantIdle = true;
                    if (mode == "real")
                    {
                        if (rounds >= 300 || roundWatch.Elapsed.TotalSeconds >= 90) { phase = 3; return; }
                        InputTap(Key.Enter);
                        rounds++;
                        if ((rounds & 15) == 0) enemyHpTrajectory.Add(battle.Current.Combat.Enemies.Sum(e => e.Hp));
                        if (!feedbackCaptured && main.StoryDrawSnapshot is { ResultVisible: true }) wantFeedback = true;
                        return;
                    }
                    if (main.StoryBattleMode == StoryBattlePresentation.Settlement && !settlementCaptured) wantSettlement = true;
                    InputTap(Key.Enter);
                    rounds++;
                    return;
                }
                case 3:
                    if (mode == "real") { if (idleCaptured) Finish(); return; }
                    if (mode == "synthetic-won")
                    {
                        var session = main.Dialogue?.Session;
                        if (session is null) return;
                        if (session.PendingDisplay is not null && (Engine.GetProcessFrames() & 7) == 0) InputTap(Key.Enter);
                        if (session.Phase == Pal.Core.TriggerScriptPhase.Completed)
                        {
                            if (!postBattleCaptured) { wantPostBattle = true; return; }
                            if (downBefore < 0)
                            {
                                downBefore = updates;
                                downHeldFrames = 14;
                                InputKey(Key.Down, true); // hold: a discrete 100ms step needs the key held across ticks
                                return;
                            }
                            if (downHeldFrames > 0)
                            {
                                if (--downHeldFrames == 0) InputKey(Key.Down, false);
                                return;
                            }
                            if (updates > downBefore) Finish();
                        }
                        return;
                    }
                    // synthetic-lost: park into the red-fade terminal and sample the convergence.
                    if (main.StoryLostTerminal)
                    {
                        if (preFadeSamples is null && main.StoryRedFadeStep is -1 or 0) { wantFadePre = true; return; }
                        if (preFadeSamples is not null && main.StoryRedFadeStep is > 1 and < 24 && midFadeStep < 0) { wantFadeMid = true; return; }
                        if (preFadeSamples is not null && main.StoryRedFadeStep >= 30) { wantFadeEnd = true; if (fadeEndDone) Finish(); return; }
                    }
                    InputTap(Key.Enter);
                    return;
            }
        }
        catch (Exception error) { Fail(error); }
    }

    private static void InputKey(Key key, bool pressed)
    {
        using var input = new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed };
        Input.ParseInputEvent(input);
        Input.FlushBufferedEvents();
    }

    private static void InputTap(Key key)
    {
        InputKey(key, true);
        InputKey(key, false);
    }

    private void ObserveFrame()
    {
        if (finished) return;
        try
        {
            ulong frame = Engine.GetProcessFrames();
            if (frame == lastCaptureFrame) return;
            lastCaptureFrame = frame;
            if (wantStatic)
            {
                wantStatic = false;
                staticCaptured = true;
                Capture("static-scene", CheckStatic);
                return;
            }
            if (wantDialogue)
            {
                wantDialogue = false;
                lastDialogueCaptureAt = Time.GetTicksMsec();
                dialogueCaptures++;
                advanceAfterCapture = true;
                Capture($"dialogue-page{dialogueCaptures}", CheckDialogue);
                return;
            }
            if (wantIdle)
            {
                wantIdle = false;
                idleCaptured = true;
                Capture("battle-idle", CheckBattleIdle);
                return;
            }
            if (wantFeedback)
            {
                wantFeedback = false;
                feedbackCaptured = true;
                Capture("battle-feedback", CheckFeedback);
                return;
            }
            if (wantSettlement)
            {
                wantSettlement = false;
                settlementCaptured = true;
                Capture("settlement-box", CheckSettlement);
                return;
            }
            if (wantPostBattle)
            {
                wantPostBattle = false;
                postBattleCaptured = true;
                Capture("post-battle-map", CheckPostBattle);
                return;
            }
            if (wantFadePre)
            {
                wantFadePre = false;
                preFadeSamples = CollectFadeSamples();
                return;
            }
            if (wantFadeMid)
            {
                wantFadeMid = false;
                midFadeStep = main!.StoryRedFadeStep;
                Capture($"red-fade-mid{midFadeStep}", image => CheckFade(image, midFadeStep));
                return;
            }
            if (wantFadeEnd)
            {
                wantFadeEnd = false;
                fadeEndDone = true;
                Capture($"red-fade-converged{main!.StoryRedFadeStep}", image => CheckFade(image, main.StoryRedFadeStep));
            }
        }
        catch (Exception error) { Fail(error); }
    }

    // ---- checks ------------------------------------------------------------

    private void CheckStatic(Image image)
    {
        int distinct = DistinctColors(image);
        Add("static-scene-rendered", distinct >= 32 && NonBlank(image), $"distinctColors={distinct}");
        // The real scene2/map12 static render keeps its identity hash here; an independent full
        // map compositor stays out of scope (M3 map precedent: restoration, not full-map proof).
    }

    private void CheckDialogue(Image image)
    {
        // Glyph pixels: palette index 0x4F text color in the box text band (the zh and en glyph
        // sets differ; per-language counts land in fixture-result.json for cross-run comparison).
        int glyphPixels = CountNear(image, new Rect2I(80, 4, 236, 132), textColor);
        Add($"dialogue-glyphs-{language}", glyphPixels >= 30, $"palette0x4FPixels={glyphPixels}");
        int covered = PortraitZoneDifference(image);
        Add("dialogue-portrait-zone", covered >= 30, $"pixelsDifferingFromStatic={covered}");
    }

    /// <summary>Difference between the dialogue frame and the captured static scene inside the
    /// portrait zone (text.c (48 - w/2, 55 - h/2)); proves the page overlay actually drew.</summary>
    private int PortraitZoneDifference(Image image)
    {
        string staticPath = Path.Combine(evidence, "static-scene.png");
        if (!File.Exists(staticPath)) return -1;
        using var before = Image.LoadFromFile(staticPath);
        if (before.GetSize() != image.GetSize()) return -1;
        return DiffCount(image, before, new Rect2I(6, 4, 84, 104));
    }

    private static int DiffCount(Image image, Image before, Rect2I rect)
    {
        int count = 0;
        ForRect(image, rect, c => { });
        int x0 = Math.Clamp(rect.Position.X, 0, 320), x1 = Math.Clamp(rect.End.X, 0, 320);
        int y0 = Math.Clamp(rect.Position.Y, 0, 200), y1 = Math.Clamp(rect.End.Y, 0, 200);
        for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++)
            if (ColorDistance(image.GetPixel(x, y), before.GetPixel(x, y)) > 0.05f) count++;
        return count;
    }

    private void CheckBattleIdle(Image image)
    {
        var combat = main!.StoryBattle!.Current.Combat;
        Add("battle-two-live-enemies", combat.Enemies.Count == 2 && combat.Enemies.All(e => e.Hp > 0),
            $"enemies={combat.Enemies.Count} hp=[{string.Join(",", combat.Enemies.Select(e => e.Hp))}] players={combat.Players.Count}");
        // Independent background composite: every sampled pixel outside the sprite/HUD rects
        // must equal palette[background.PaletteIndices[y*320+x]] (the bound indexed planes).
        var expected = ExpectedBackground();
        var rects = enemyRects.Concat(playerRects).Append(new Rect2I(0, 172, 320, 28)).ToArray();
        int sampled = 0, mismatches = 0, firstX = -1, firstY = -1;
        uint seed = 20261104;
        for (int i = 0; i < 900 && sampled < 220; i++)
        {
            seed = seed * 1664525u + 1013904223u;
            int x = (int)(seed % 320);
            seed = seed * 1664525u + 1013904223u;
            int y = (int)(seed % 200);
            if (rects.Any(r => r.HasPoint(new Vector2I(x, y)))) continue;
            sampled++;
            if (ColorDistance(image.GetPixel(x, y), expected[y * 320 + x]) > 1.1f / 255f)
            {
                mismatches++;
                if (firstX < 0) { firstX = x; firstY = y; }
            }
        }
        Add("battle-background-independent", mismatches == 0,
            $"sampled={sampled} mismatches={mismatches} first={firstX},{firstY}");
        foreach (var (rect, slot) in enemyRects.Zip(new[] { 1, 2 }))
        {
            int anchor = rect.Position.Y * 320 + rect.Position.X;
            int sprite = CountInRect(image, rect, c => ColorDistance(c, expected[anchor]) > 0.05f);
            Add($"battle-enemy{slot}-sprite", sprite >= 30, $"differingPixels={sprite} rect={rect}");
        }
        foreach (var (rect, member) in playerRects.Zip(new[] { 1, 2 }))
        {
            int sprite = CountInRect(image, rect, c => c.A > 0.1f);
            Add($"battle-player{member}-sprite", sprite >= 20, $"pixels={sprite} rect={rect}");
        }
        int hudText = CountNear(image, new Rect2I(0, 174, 320, 26), hudColor);
        Add("battle-hp-hud-text", hudText >= 20, $"palette79Pixels={hudText}");
        float dark = FractionInRect(image, new Rect2I(0, 174, 320, 26), c => (c.R + c.G + c.B) / 3f < 0.16f);
        Add("battle-hud-panel", dark >= 0.5f, $"darkFraction={dark:F2}");
    }

    private void CheckFeedback(Image image)
    {
        // A visible-result feedback frame (damage rides the 40ms digit clock). Digit textures
        // are a binding-coverage question: the engine-error scan in the launcher surfaces any
        // missing number/effect frames as errors rather than this pixel check deciding it.
        var snapshot = main!.StoryDrawSnapshot;
        Add("battle-feedback-visible-result", snapshot is { ResultVisible: true },
            $"resultVisible={snapshot?.ResultVisible} digits={snapshot?.NumberDigits.Count}");
    }

    private void CheckSettlement(Image image)
    {
        string? line = main!.StorySettlementLine;
        Add("settlement-line-present", !string.IsNullOrEmpty(line), $"line='{line}'");
        float dark = FractionInRect(image, new Rect2I(20, 62, 280, 28), c => (c.R + c.G + c.B) / 3f < 0.16f);
        Add("settlement-box-dark", dark >= 0.85f, $"darkFraction={dark:F2}");
        int text = CountNear(image, new Rect2I(28, 64, 264, 24), hudColor);
        Add("settlement-box-text", text >= 15, $"palette79Pixels={text}");
    }

    private void CheckPostBattle(Image image)
    {
        int distinct = DistinctColors(image);
        var expected = ExpectedBackground();
        // The map is restored after the battle view releases; the battlefield composite must
        // not still cover the sampled frame.
        int battleBgMatches = 0;
        for (int i = 0; i < 40; i++)
        {
            int x = (i * 7 + 13) % 320, y = (i * 5 + 31) % 160;
            if (ColorDistance(image.GetPixel(x, y), expected[y * 320 + x]) <= 1.1f / 255f) battleBgMatches++;
        }
        // The synthetic static scene is uniform-by-construction (zero index planes), so the
        // restored-map proof is that the battlefield composite no longer covers the frame.
        Add("post-battle-map-restored", distinct >= 1 && battleBgMatches <= 8,
            $"distinctColors={distinct} battlefieldMatches={battleBgMatches}/40");
    }

    private Color[] CollectFadeSamples()
    {
        using var image = CaptureNow("red-fade-pre");
        var samples = fadePoints.Select(p => image.GetPixel(p.X, p.Y)).ToArray();
        Add("red-fade-pre-sampled", samples.Length == fadePoints.Length,
            $"step={main!.StoryRedFadeStep} points={fadePoints.Length}");
        return samples;
    }

    private void CheckFade(Image image, int step)
    {
        if (preFadeSamples is null) { Add("red-fade-oracle", false, "pre-fade samples missing"); return; }
        int compared = 0, off = 0, worst = 0;
        foreach (var (point, before) in fadePoints.Zip(preFadeSamples))
        {
            Color want = FadeToRed(before, step);
            Color got = image.GetPixel(point.X, point.Y);
            int delta = Math.Max(Math.Abs(got.R8 - want.R8), Math.Max(Math.Abs(got.G8 - want.G8), Math.Abs(got.B8 - want.B8)));
            worst = Math.Max(worst, delta);
            compared++;
            if (delta > 2) off++;
        }
        Add($"red-fade-step{step}", off == 0, $"compared={compared} offByOracle={off} worstChannelDelta={worst}");
        if (step >= 30)
        {
            int atTarget = preFadeSamples.Count(s =>
            {
                Color want = FadeToRed(s, 32);
                return want.G8 == 0 && want.B8 == 0 && want.R8 == (s.R8 + s.G8 + s.B8) / 4 + 64;
            });
            Add("red-fade-convergence-formula", atTarget == preFadeSamples.Length,
                $"pointsAtConvergedTarget={atTarget}/{preFadeSamples.Length}");
        }
    }

    /// <summary>Independent palette.c PAL_FadeToRed (script.c 0x004F game-over fade): every
    /// entry except the preserved 0x4F text color moves red toward (r+g+b)/4+64 by 8 per step
    /// for 32 steps of 75ms; green/blue fall by 8 per step. Written from the reference
    /// semantics, not from Main's implementation.</summary>
    private static Color FadeToRed(Color before, int step)
    {
        if (step <= 0) return before;
        int target = (before.R8 + before.G8 + before.B8) / 4 + 64;
        int red = before.R8;
        if (red > target) red -= Math.Min(8 * step, red - target);
        else if (red < target) red += Math.Min(8 * step, target - red);
        return Color.Color8((byte)red, (byte)Math.Max(0, before.G8 - 8 * step), (byte)Math.Max(0, before.B8 - 8 * step));
    }

    // ---- helpers -----------------------------------------------------------

    private Color[] ExpectedBackground()
    {
        var bg = battleBackground!;
        var colors = Enumerable.Range(0, 256).Select(i => new Color(palette!.Rgb[i * 3] / 255f,
            palette.Rgb[i * 3 + 1] / 255f, palette.Rgb[i * 3 + 2] / 255f)).ToArray();
        var expected = new Color[320 * 200];
        for (int i = 0; i < 320 * 200; i++) expected[i] = colors[bg.PaletteIndices[i]];
        return expected;
    }

    private Image CaptureNow(string name)
    {
        var actual = GetViewport().GetTexture().GetImage();
        Require(actual is not null && actual.GetSize() == new Vector2I(320, 200), "GPU readback must be a non-null 320x200 image.");
        Require(actual!.SavePng(Path.Combine(evidence, name + ".png")) == Error.Ok, "Saving GPU evidence failed.");
        using var sha = SHA256.Create();
        hashes[name] = Convert.ToHexStringLower(sha.ComputeHash(File.ReadAllBytes(Path.Combine(evidence, name + ".png"))));
        SaveResult(completed: false);
        GD.Print($"PAL_M4_GPU_CHECK:stage={name}:hash={hashes[name][..12]}");
        return actual;
    }

    private void Capture(string name, Action<Image> check)
    {
        using var image = CaptureNow(name);
        check(image);
    }

    private static void ForRect(Image image, Rect2I rect, Action<Color> visit)
    {
        int x0 = Math.Clamp(rect.Position.X, 0, 320), x1 = Math.Clamp(rect.End.X, 0, 320);
        int y0 = Math.Clamp(rect.Position.Y, 0, 200), y1 = Math.Clamp(rect.End.Y, 0, 200);
        for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++) visit(image.GetPixel(x, y));
    }

    private static int CountInRect(Image image, Rect2I rect, Func<Color, bool> predicate)
    {
        int count = 0;
        ForRect(image, rect, c => { if (predicate(c)) count++; });
        return count;
    }

    private static float FractionInRect(Image image, Rect2I rect, Func<Color, bool> predicate)
    {
        int total = 0, hit = 0;
        ForRect(image, rect, c => { total++; if (predicate(c)) hit++; });
        return total == 0 ? 0f : hit / (float)total;
    }

    private static int CountNear(Image image, Rect2I rect, Color want)
    {
        int count = 0;
        ForRect(image, rect, c => { if (ColorDistance(c, want) <= 2.5f / 255f) count++; });
        return count;
    }

    private static float ColorDistance(Color a, Color b) =>
        Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));

    private static int DistinctColors(Image image)
    {
        var set = new HashSet<int>();
        ForRect(image, new Rect2I(0, 0, 320, 200), c => set.Add((int)c.ToAbgr32()));
        return set.Count;
    }

    private static bool NonBlank(Image image)
    {
        Color first = image.GetPixel(0, 0);
        for (int y = 0; y < 200; y += 7) for (int x = 0; x < 320; x += 11)
            if (ColorDistance(image.GetPixel(x, y), first) > 0.05f) return true;
        return false;
    }

    private void Add(string name, bool passed, string details)
    {
        checks.Add(new { name, passed, details });
        if (!passed) failures++;
        GD.Print($"PAL_M4_GPU_ASSERT:{(passed ? "PASS" : "FAIL")}:{name}:{details}");
    }

    private void Finish()
    {
        finished = true;
        RenderingServer.FramePostDraw -= ObserveFrame;
        if (mode == "real")
        {
            var battle = main!.StoryBattle;
            int final = battle is null ? 0 : battle.Current.Combat.Enemies.Sum(e => e.Hp);
            Add("real-damage-trajectory", enemyHpTrajectory.Count > 0 && final < engagementTotal,
                $"engagement={engagementTotal} trajectory=[{string.Join(",", enemyHpTrajectory)}] final={final} rounds={rounds}");
        }
        SaveResult(completed: true);
        GD.Print($"PAL_M4_GPU_END:mode={mode}:language={language}:failures={failures}:completed=true");
        GetTree().Quit(failures == 0 ? 0 : 1);
    }

    private void Fail(Exception error)
    {
        finished = true;
        RenderingServer.FramePostDraw -= ObserveFrame;
        if (Directory.Exists(evidence)) SaveResult(completed: false, error.GetType().Name + ": " + error.Message);
        GD.PrintErr($"PAL_M4_GPU_FAILED:{error.GetType().Name}:{error.Message}");
        GetTree().Quit(1);
    }

    private void SaveResult(bool completed, string? failure = null)
    {
        var payload = new
        {
            mode, language, profileUri, failures, completed, failure, checks, hashes,
            scope = mode == "real"
                ? "Real staged closure profile on a real GPU window: static scene, pre-battle dialogue pages (bilingual glyph color sampling), @09FE battle view (independent battlefield composite, dual enemy/player sprites, HP HUD), one visible-result feedback frame and a bounded real damage trajectory. Retail whole-campaign Won/Lost stays the documented human-speed boundary (#53 ruling b); settlement box and red fade are covered by the synthetic-won/lost GPU runs of the same production chain. Programmatic input; not physical-keyboard/manual acceptance."
                : "Synthetic closure on a real GPU window through the same production binding/host chain: full Won flow with victory settlement box and restored post-battle map, or full Lost flow into the red-fade terminal with an independent palette.c convergence oracle. Programmatic input; not physical-keyboard/manual acceptance."
        };
        File.WriteAllText(Path.Combine(evidence, "fixture-result.json"),
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
    }

    public override void _ExitTree()
    {
        RenderingServer.FramePostDraw -= ObserveFrame;
        synthetic?.Dispose();
        syntheticStatics?.Dispose();
    }

    private static string RequiredEnvironment(string name) => OS.GetEnvironment(name) is { Length: > 0 } value
        ? value : throw new InvalidDataException($"Explicit {name} is required; no fallback or preparation is performed.");
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
