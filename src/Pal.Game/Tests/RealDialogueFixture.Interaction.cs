using System;
using System.IO;
using System.Linq;
using Godot;
using Pal.Core;
using Pal.Game.Profiles;
using Pal.Game.Resources;

namespace Pal.Game.Tests;

public partial class RealDialogueFixture
{
    private bool _interactionMode;
    private bool _graphical;
    private Main? _main;
    private ExplorationState? _blockedState;
    private ExplorationState? _beforeEcho;
    private ulong _mainFrame;
    private ulong _displayFrame;
    private ulong _handledFrame;
    private int _callsCompleted;
    private bool _completionObserved;
    private uint _completionTicks;
    private Image? _mapBaseline;
    private Guid _lastRequest;
    private int _batchLine;
    private ulong _batchFrame;
    private bool _typingCaptured;

    private void ReadyInteraction()
    {
        _interactionMode = true;
        _graphical = DisplayServer.GetName() != "headless";
        try
        {
            _evidence = OS.GetEnvironment("PAL_DIALOGUE_GPU_EVIDENCE");
            _main = ResourceLoader.Load<PackedScene>("res://Scenes/Main.tscn").Instantiate<Main>();
            AddChild(_main);
            Require(_main.Interaction is not null && _main.Display is not null, "Main must initialize independent interaction mode.");
            _display = _main.Display!;
            _main.UpdateCompleted += ObserveMainUpdate;
            _display.UpdateCompleted += ObserveDisplayUpdate;
            var args = Pal.Game.Map.NativeSceneLoader.Arguments(OS.GetCmdlineUserArgs());
            _languageId = args.Language;
            var profile = NativeNpcInteractionProfile.Load(args.DialogueProfile!);
            string root = profile.DialogueDirectory;
            _language = Load<PalDialogueLanguage>($"{root}/Language.{_languageId}.tres");
            _font = Load<PalDialogueFont>($"{root}/Font.tres");
            _portrait = Load<PalDialogueIndexedImage>($"{root}/Portrait-059.tres");
            _prompts = Enumerable.Range(0, 3).Select(i => Load<PalDialogueIndexedImage>($"{root}/Prompt-{i:00}.tres")).ToArray();
            var scene = ResourceLoader.Load<PackedScene>(profile.StaticResources + "/Scenes/Scene.tscn").Instantiate<PalMapScene>();
            try
            {
                BindInteractionResourceIdentity(args.DialogueProfile!, root, scene.Data.Palette);
                if (_graphical)
                {
                    var frames = scene.Data.Map.Tiles.Frames.Concat(scene.Data.Sprites.SelectMany(s => s.Frames)).ToArray();
                    int rotating = frames.Sum(frame => frame.Indices.Where((index, i) => frame.Alpha[i] != 0 && index is >= 0xF9 and <= 0xFE).Count());
                    Require(rotating == 0, "The real-map restoration oracle requires the independently audited absence of opaque F9-FE scene pixels.");
                    GD.Print($"PAL_INTERACTION_PALETTE_INDEX_AUDIT:frames={frames.Length}:opaqueF9FE={rotating}; all scene textures still receive the full active palette");
                }
                var native = scene.Data.Palette;
                _palette = Enumerable.Range(0, 256).Select(i => new Color(native.Rgb[i * 3] / 255f, native.Rgb[i * 3 + 1] / 255f, native.Rgb[i * 3 + 2] / 255f)).ToArray();
            }
            finally { scene.Free(); }
            var parts = _language.ToCorePackage().Resolve(61, 1).Parts;
            Require(parts.All(p => !p.IsClear && p.Text is not null), "Target messages must retain text parts.");
            bool title = parts[0].Text![^1] is ':' or '\uFF1A' or '\u2236';
            string first = parts[0].Text!;
            _title = title ? (first.Contains('\\') ? System.Text.RegularExpressions.Regex.Replace(first, @"\\(.)|[-'@""$~)(]", m => m.Groups[1].Value) : first) : "";
            int color = 0x4F, delay = 24;
            _lines = parts.Skip(title ? 1 : 0).Select(part =>
            {
                var line = DialogueTextTokenizer.TokenizeLine(part.Text!, color, delay);
                color = line.EndColor; delay = line.EndDelayMilliseconds; return line;
            }).ToArray();
            ProcessPriority = 100;
            _started = Time.GetTicksMsec();
            if (_graphical)
            {
                Require(Directory.Exists(_evidence), "GPU evidence must be a new explicit directory.");
                RenderingServer.FramePostDraw += ObserveRenderedFrame;
                _subscribed = true;
            }
            GD.Print($"PAL_INTERACTION_BEGIN:language={_languageId}:renderer={DisplayServer.GetName()}");
        }
        catch (Exception error) { Fail(error); }
    }

    private void BindInteractionResourceIdentity(string profileUri, string dialogueDirectory, PalPalette palette)
    {
        // Ordinary headless interaction runs have no GPU evidence contract. GPU runs
        // must bind the script's protected paths to the typed resources actually loaded.
        if (string.IsNullOrEmpty(_evidence)) return;
        Require(Directory.Exists(_evidence), "GPU evidence must be a new explicit directory.");
        Require(!string.IsNullOrEmpty(palette.ResourcePath) && !palette.ResourcePath.Contains("::", StringComparison.Ordinal),
            "PalettePath identity requires the loaded scene palette to be an external native resource.");
        string actualDialogue = IdentityPath(dialogueDirectory);
        string actualPalette = IdentityPath(palette.ResourcePath);
        string providedDialogue = IdentityPath(RequiredEnvironment("PAL_DIALOGUE_NATIVE_ROOT"));
        string providedPalette = IdentityPath(RequiredEnvironment("PAL_DIALOGUE_PALETTE"));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        Require(string.Equals(providedDialogue, actualDialogue, comparison),
            $"NativeDialogueDirectory identity mismatch: provided='{providedDialogue}'; actual='{actualDialogue}'.");
        Require(string.Equals(providedPalette, actualPalette, comparison),
            $"PalettePath identity mismatch: provided='{providedPalette}'; actual='{actualPalette}'.");
        File.WriteAllText(Path.Combine(_evidence, "resource-identity.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            InteractionProfileUri = profileUri, NativeDialogueDirectory = actualDialogue, PalettePath = actualPalette
        }));
    }

    private static string IdentityPath(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(ProjectSettings.GlobalizePath(path)));

    private void ObserveMainUpdate()
    {
        _mainFrame = Engine.GetProcessFrames();
        // A no-wait call completes synchronously inside Main's Start, not via a display
        // update. Observe that actual Main callback, without manufacturing an update.
        if (_lines.Length == 0 && !_completionObserved && _main!.Interaction!.Phase == NpcInteractionPhase.Returning)
        {
            _observed = _display.Current;
            ObserveCompletion();
        }
    }

    private void ObserveDisplayUpdate()
    {
        _observed = _display.Current;
        _displayFrame = Engine.GetProcessFrames();
        if (_observed?.Phase == DialogueDisplayPhase.Completed) ObserveCompletion();
    }

    private void ObserveCompletion()
    {
        try
        {
            Require(_main!.Dialogue!.Session!.Phase == TriggerScriptPhase.Completed, "Adapter must receipt actual completed display.");
            Require(_main.Interaction!.Phase == NpcInteractionPhase.Returning, "Main must enter Returning at actual display completion, not a later observer frame.");
            Require(_main.Dialogue.Session.ReturnEntry == 0x0188 && _main.Dialogue.Session.Caller == 60, "Real trigger must return owner60 entry0188, not PC018B.");
            Require(_observed is { Phase: DialogueDisplayPhase.Completed, PaletteRotation: 0, ClearDisplay: true }, "Completion must restore palette/background first.");
            _completionObserved = true;
            _completionTicks = unchecked((uint)Time.GetTicksMsec());
            _callsCompleted++;
            // Deliver a search at the actual Returning boundary, never at a delayed
            // observer/capture frame which could already be past the50ms deadline.
            KeyEvent(true); KeyEvent(false);
            GD.Print($"PAL_INTERACTION_COMPLETE:call={_callsCompleted}:frame={_displayFrame}:ticks={_completionTicks}:return=0188:phase=Returning");
        }
        catch (Exception error) { Fail(error); }
    }

    private void ProcessInteraction()
    {
        if (_finished) return;
        if (Time.GetTicksMsec() - _started > 10000) { Fail(new TimeoutException($"Independent interaction liveness bound: stage={_stage}, mainFrame={_mainFrame}, displayFrame={_displayFrame}, phase={_observed?.Phase}, line={_observed?.LineIndex}.")); return; }
        if (!_graphical) ObserveInteraction();
    }

    private void ObserveInteraction()
    {
        if (_finished || _mainFrame == _handledFrame || _main?.Interaction is not { } npc) return;
        _handledFrame = _mainFrame;
        _updateFrame = _displayFrame;
        try
        {
            switch (_stage)
            {
                case 0:
                    Require(npc.Objects.Count == 32 && npc.Current.Leader is { X: 1232, Y: 1336, Direction: 0 }, "All objects and independent start must be retained.");
                    _beforeEcho = npc.Current;
                    SendInteractionKey(Key.Space, true, true); SendInteractionKey(Key.Space, false);
                    _stage = 1;
                    break;
                case 1 when !ReferenceEquals(_beforeEcho, npc.Current):
                    Require(npc.Phase == NpcInteractionPhase.Exploring && _display.Session is null, "Echo must not initiate search with Windows key-repeat disabled.");
                    KeyEvent(true); _stage = 2;
                    break;
                case 2 when npc.PendingTrigger is { } request:
                    KeyEvent(false);
                    Require(request is { Caller: 60, SourceAddress: 0x0188, SampleIndex: 1 }, "Actual search must hit owner60 at sample1.");
                    Require(request.Id != _lastRequest, "Second search must create a fresh complete call.");
                    _lastRequest = request.Id;
                    var owner = npc.Objects.Single(o => o.Id == 60);
                    Require(owner.Direction == 2 && owner.CurrentFrame == 0 && owner.TriggerEntry == 0x0188,
                        "NPC must face North/stand before trigger starts while retaining entry.");
                    _blockedState = npc.Current;
                    _typingCaptured = false;
                    if (_graphical) CaptureInteractionBaseline();
                    _stage = 3;
                    break;
                case 3 when _lines.Length == 0 && _completionObserved:
                    Require(_main.Dialogue!.Session!.CurrentPosition!.SourceAddress == 0x018B, "No-wait content must still execute both original message instructions.");
                    _stage = 7;
                    break;
                case 3 when _observed is { Phase: DialogueDisplayPhase.Typing or DialogueDisplayPhase.WaitingForAdvance }:
                    Require(ReferenceEquals(_main.Dialogue!.Session!.PendingDisplay!.Display, _display.Session), "One real request.Display must own updates.");
                    Require(_main.Dialogue.Session.CurrentPosition!.SourceAddress == 0x018B, "Both original messages must execute to PC018B before wait.");
                    Require(ReferenceEquals(_blockedState, npc.Current), "Ordinary dialogue must block exploration updates.");
                    SendInteractionKey(Key.Down, true);
                    if (_observed.Phase == DialogueDisplayPhase.WaitingForAdvance)
                    {
                        // A short legal line may finish on its first actual update. It does
                        // not provide an observable Typing/acceleration claim.
                        AssertCompleteBody();
                        _stage = 5;
                        break;
                    }
                    if (_graphical && _observed.VisibleText.Length == 0) break;
                    if (_graphical && !_typingCaptured) { CaptureInteraction("typing", _observed); _typingCaptured = true; }
                    _batchLine = _observed.LineIndex; _batchFrame = _displayFrame;
                    SendInteractionKey(Key.Space, true); SendInteractionKey(Key.Enter, true);
                    _stage = 4;
                    break;
                case 4 when _displayFrame > _batchFrame:
                    KeyEvent(false); SendInteractionKey(Key.Enter, false);
                    Require(_observed!.LineIndex == _batchLine + 1, "One actual update may complete only the current body part.");
                    string completed = string.Concat(_observed.VisibleLines[_batchLine].Runs.Select(r => r.Text));
                    Require(completed == string.Concat(_lines[_batchLine].Tokens.Select(t => t.Text)), "Batch must finish exactly the independently resolved current line.");
                    if (_batchLine + 1 < _lines.Length)
                    {
                        Require(_observed is { Phase: DialogueDisplayPhase.Typing, VisibleText.Length: 0 }, "Batch must not flow into the next body part.");
                        _stage = 3;
                        break;
                    }
                    Require(_observed.Phase == DialogueDisplayPhase.WaitingForAdvance, "Batch must leave the final advance wait unconsumed.");
                    AssertCompleteBody();
                    if (_graphical) CaptureInteraction("accelerated-body", _observed);
                    _stage = 5;
                    break;
                case 5 when _observed is { Phase: DialogueDisplayPhase.WaitingForAdvance } snapshot:
                    Require(ReferenceEquals(_blockedState, npc.Current), "Scene must stay blocked while waiting.");
                    if (_graphical) CaptureInteraction("waiting", snapshot);
                    _waitingRotation = snapshot.PaletteRotation; _stage = 6;
                    break;
                case 6 when _observed is { Phase: DialogueDisplayPhase.WaitingForAdvance } snapshot && snapshot.PaletteRotation != _waitingRotation:
                    if (_graphical) CaptureInteraction("waiting-rotated", snapshot);
                    SendInteractionKey(Key.Down, false);
                    SendInteractionKey(Key.Space, true); SendInteractionKey(Key.Enter, true);
                    _stage = 7;
                    break;
                case 7 when _completionObserved:
                    KeyEvent(false); SendInteractionKey(Key.Enter, false);
                    Require(npc.Objects.Single(o => o.Id == 60).TriggerEntry == 0x0188, "Returned entry must remain0188.");
                    if (_graphical) CaptureInteraction("completed-restored", _observed!);
                    _stage = 8;
                    break;
                case 8 when npc.Phase == NpcInteractionPhase.Exploring && !ReferenceEquals(_blockedState, npc.Current):
                    Require(npc.Current.Leader.X == 1232 && npc.Current.Leader.Y == 1336 && !npc.Current.IsWalking,
                        "Released direction must not stick through dialogue; ending input must not reopen it.");
                    Require(_main.Dialogue!.Session!.Phase == TriggerScriptPhase.Completed, "No transient search may leak into exploration.");
                    if (_callsCompleted == 2)
                    {
                        GD.Print($"PAL_INTERACTION_END:language={_languageId}:calls=2:entry=0188:completed=true");
                        if (_graphical) Finish();
                        else
                        {
                            _finished = true;
                            if (Directory.Exists(_evidence)) SaveResult(completed: true);
                            GetTree().Quit(0);
                        }
                        break;
                    }
                    _completionObserved = false; _observed = null;
                    KeyEvent(true); _stage = 2;
                    break;
            }
        }
        catch (Exception error) { Fail(error); }
    }

    private void AssertCompleteBody()
    {
        Require(_observed!.VisibleLines.Count == _lines.Length, "All original body parts must be retained at wait.");
        for (int line = 0; line < _lines.Length; line++)
            Require(string.Concat(_observed.VisibleLines[line].Runs.Select(r => r.Text)) == string.Concat(_lines[line].Tokens.Select(t => t.Text)),
                "Waiting body must match each independently resolved language part.");
    }

    private static void SendInteractionKey(Key key, bool pressed, bool echo = false)
    {
        using var input = new InputEventKey { Keycode = key, Pressed = pressed, Echo = echo };
        Input.ParseInputEvent(input); Input.FlushBufferedEvents();
    }

    private void CaptureInteractionBaseline()
    {
        _mapBaseline?.Dispose();
        _mapBaseline = GetViewport().GetTexture().GetImage();
        Require(_mapBaseline.GetSize() == new Vector2I(320, 200), "Map baseline must be320x200.");
        _mapBaseline.SavePng(Path.Combine(_evidence, $"call{_callsCompleted + 1}-map-before-dialogue.png"));
    }

    private void CaptureInteraction(string stage, DialogueDisplaySnapshot snapshot)
    {
        Capture($"call{_callsCompleted + (snapshot.Phase == DialogueDisplayPhase.Completed ? 0 : 1)}-{stage}", Expected(snapshot), snapshot);
    }
}
