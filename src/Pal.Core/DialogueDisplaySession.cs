namespace Pal.Core;

public enum DialogueDisplayPhase
{
    Typing,
    WaitingForAdvance,
    Completed
}

/// <summary>Deterministic progression of one resolved dialogue block, isolated from Godot and I/O.</summary>
public sealed class DialogueDisplaySession
{
    private readonly DialogueMessageBlock _block;
    private int _partIndex;
    private int _visibleLength;
    private int _lineIndex;
    private int _promptFrame;
    private int _currentTextColor;
    private int _currentDelayMilliseconds = DialogueTextTokenizer.DefaultDelayMilliseconds;
    private uint _elapsed;
    private uint _waitElapsed;
    private bool _pendingAdvance;
    private bool _skip;
    private bool _pendingSkip;
    private int _paletteRotation;
    private IReadOnlyList<DialogueTextToken> _tokens = Array.Empty<DialogueTextToken>();
    private readonly List<DialogueTextToken> _visible = [];
    private readonly List<DialogueVisibleLine> _completedLines = [];
    private string _titleText = "";
    private bool _clearAfterWait;
    private bool _consumeClearEntry;
    private DialogueDisplaySnapshot _current;

    public DialogueDisplaySnapshot Current => _current;
    /// <summary>Text control state to carry to the next display after completion, including trailing controls.</summary>
    public int TextColor => _currentTextColor;
    public int DelayMilliseconds => _currentDelayMilliseconds;

    public DialogueDisplaySession(DialogueMessageBlock block, int inheritedColor, int commandColor,
        int initialDelayMilliseconds = DialogueTextTokenizer.DefaultDelayMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(block);
        if (inheritedColor is < 0 or > 255 || commandColor is < 0 or > 255)
            throw new ArgumentOutOfRangeException(nameof(commandColor), "Dialogue colors must be palette indices.");
        if (initialDelayMilliseconds < 0) throw new ArgumentOutOfRangeException(nameof(initialDelayMilliseconds));
        _currentDelayMilliseconds = initialDelayMilliseconds;
        _block = block;
        _currentTextColor = commandColor == 0 ? inheritedColor : commandColor;
        bool clearDisplay = SetNextLine();
        _current = _partIndex >= _block.Parts.Count
            ? Snapshot(DialogueDisplayPhase.Completed, clearDisplay: true)
            : Snapshot(DialogueDisplayPhase.Typing, clearDisplay);
    }

    /// <summary>Advance is a fresh mapped wait press; acceleration is a fresh Search/Menu press, never held state.</summary>
    public void Update(uint elapsedMilliseconds, bool advancePressed, bool acceleratePressed)
    {
        if (_current.Phase == DialogueDisplayPhase.Completed) return;
        if (_current.Phase == DialogueDisplayPhase.WaitingForAdvance)
        {
            ulong waiting = (ulong)_waitElapsed + elapsedMilliseconds;
            _paletteRotation = (_paletteRotation + (int)(waiting / 100)) % 6;
            _waitElapsed = (uint)(waiting % 100);
            _pendingAdvance |= advancePressed;
            if (waiting >= 100 && _pendingAdvance) AdvancePastWait();
            else _current = Snapshot(DialogueDisplayPhase.WaitingForAdvance, _current.ClearDisplay);
            return;
        }

        if (_lineIndex > 3)
        {
            _clearAfterWait = true;
            _consumeClearEntry = false;
            _current = BeginWait();
            return;
        }

        DialogueMessagePart part = _block.Parts[_partIndex];
        if (part.IsClear)
        {
            _clearAfterWait = _lineIndex > 0;
            _consumeClearEntry = _clearAfterWait;
            if (_clearAfterWait)
            {
                _current = BeginWait();
            }
            else
            {
                _partIndex++;
                _visible.Clear();
                SetNextLine();
                _current = _partIndex >= _block.Parts.Count
                    ? Snapshot(DialogueDisplayPhase.Completed, clearDisplay: true)
                    : Snapshot(DialogueDisplayPhase.Typing, clearDisplay: true);
            }
            return;
        }

        if (_visibleLength == 0 && _visible.Count > 0) _visible.Clear();
        _pendingSkip |= acceleratePressed;
        if (_skip)
        {
            _visibleLength = _tokens.Count;
            _visible.Clear();
            _visible.AddRange(_tokens);
            _elapsed = 0;
        }
        else
        {
            // text.c draws first, then delays after that glyph, including the last one.
            if (_visibleLength == 0 && _tokens.Count > 0)
                _visible.Add(_tokens[_visibleLength++]);
            ulong accumulated = (ulong)_elapsed + elapsedMilliseconds;
            bool lastDelayCompleted = _tokens.Count == 0;
            while (_visibleLength > 0)
            {
                int delay = _tokens[_visibleLength - 1].DelayMilliseconds;
                if (accumulated < (uint)delay) break;
                accumulated -= (uint)delay;
                if (_pendingSkip)
                {
                    _skip = true;
                    _pendingSkip = false;
                    while (_visibleLength < _tokens.Count) _visible.Add(_tokens[_visibleLength++]);
                    lastDelayCompleted = true;
                    break;
                }
                if (_visibleLength == _tokens.Count) { lastDelayCompleted = true; break; }
                _visible.Add(_tokens[_visibleLength++]);
            }
            _elapsed = (uint)Math.Min(accumulated, uint.MaxValue);
            if (!lastDelayCompleted)
            {
                _current = Snapshot(DialogueDisplayPhase.Typing);
                return;
            }
        }

        if (_visibleLength >= _tokens.Count)
        {
            _completedLines.Add(new DialogueVisibleLine(_lineIndex, VisibleRuns()));
            _partIndex++;
            _lineIndex++;
            _visibleLength = 0;
            _elapsed = 0;
            if (_partIndex == _block.Parts.Count)
                _current = BeginWait();
            else
            {
                DialogueMessagePart next = _block.Parts[_partIndex];
                if (next.IsClear)
                {
                    _clearAfterWait = true;
                    _consumeClearEntry = true;
                    _current = BeginWait();
                }
                else if (_lineIndex > 3)
                {
                    _clearAfterWait = true;
                    _consumeClearEntry = false;
                    _promptFrame = 0;
                    _current = BeginWait();
                }
                else
                {
                    _visible.Clear();
                    SetNextLine();
                    _current = Snapshot(DialogueDisplayPhase.Typing);
                }
            }
        }
        else _current = Snapshot(DialogueDisplayPhase.Typing);
    }

    private bool SetNextLine()
    {
        bool clearDisplay = false;
        // PAL_ShowDialogText treats a colon-terminated message before the first body line
        // as an immediate title. It changes neither body color/delay nor the body line count.
        while (_lineIndex == 0 && _partIndex < _block.Parts.Count)
        {
            // No body means no key wait, but script.c still restores the background.
            if (_block.Parts[_partIndex].IsClear) { clearDisplay = true; _partIndex++; continue; }
            if (_block.Parts[_partIndex].Text is not { Length: > 0 } text || text[^1] is not (':' or '\uFF1A' or '\u2236')) break;
            var title = new System.Text.StringBuilder();
            // PAL_UnescapeText returns the original string unless a backslash is present.
            // Once enabled, escaped characters survive and unescaped display controls disappear.
            bool unescape = text.Contains('\\');
            for (int index = 0; index < text.Length; index++)
            {
                if (unescape && text[index] == '\\' && index + 1 < text.Length) index++;
                else if (unescape && text[index] is '-' or '\'' or '@' or '"' or '$' or '~' or ')' or '(') continue;
                title.Append(text[index]);
            }
            _titleText = title.ToString();
            _partIndex++;
        }
        if (_partIndex < _block.Parts.Count && !_block.Parts[_partIndex].IsClear)
        {
            DialogueTextLine line = DialogueTextTokenizer.TokenizeLine(_block.Parts[_partIndex].Text!, _currentTextColor, _currentDelayMilliseconds);
            _tokens = line.Tokens;
            _currentTextColor = line.EndColor;
            _currentDelayMilliseconds = line.EndDelayMilliseconds;
            _promptFrame = line.PromptFrame;
        }
        else _promptFrame = 0;
        return clearDisplay;
    }

    private DialogueDisplaySnapshot BeginWait()
    {
        // PAL_DialogWaitForKey clears transient keys and fUserSkip at the wait boundary.
        _skip = false;
        _pendingSkip = false;
        _pendingAdvance = false;
        _waitElapsed = 0;
        return Snapshot(DialogueDisplayPhase.WaitingForAdvance);
    }

    private void AdvancePastWait()
    {
        _paletteRotation = 0;
        _waitElapsed = 0;
        _pendingAdvance = false;
        _pendingSkip = false;
        _skip = false;
        if (!_clearAfterWait)
        {
            _current = Snapshot(DialogueDisplayPhase.Completed, clearDisplay: true);
            return;
        }

        if (_consumeClearEntry) _partIndex++;
        _clearAfterWait = false;
        _consumeClearEntry = false;
        _visible.Clear();
        _completedLines.Clear();
        _visibleLength = 0;
        _lineIndex = 0;
        _elapsed = 0;
        SetNextLine();
        _current = _partIndex >= _block.Parts.Count
            ? Snapshot(DialogueDisplayPhase.Completed, clearDisplay: true)
            : Snapshot(DialogueDisplayPhase.Typing, clearDisplay: true);
    }

    private DialogueDisplaySnapshot Snapshot(DialogueDisplayPhase phase, bool clearDisplay = false)
    {
        IReadOnlyList<DialogueVisibleRun> runs = clearDisplay || phase == DialogueDisplayPhase.Completed ? [] : VisibleRuns();
        var lines = new List<DialogueVisibleLine>();
        if (!clearDisplay && phase != DialogueDisplayPhase.Completed)
        {
            lines.AddRange(_completedLines);
            if (phase == DialogueDisplayPhase.Typing && _visibleLength > 0)
                lines.Add(new DialogueVisibleLine(_lineIndex, runs));
        }
        return new DialogueDisplaySnapshot(phase, string.Concat(runs.Select(run => run.Text)),
            runs.LastOrDefault()?.Color ?? _currentTextColor, _lineIndex, _paletteRotation, clearDisplay, _promptFrame, runs)
        {
            TitleText = phase == DialogueDisplayPhase.Completed ? "" : _titleText,
            VisibleLines = lines.AsReadOnly()
        };
    }

    private IReadOnlyList<DialogueVisibleRun> VisibleRuns() => _visible
        .Aggregate(new List<DialogueVisibleRun>(), (list, token) =>
        {
            if (list.Count > 0 && list[^1].Color == token.Color)
                list[^1] = list[^1] with { Text = list[^1].Text + token.Text };
            else list.Add(new DialogueVisibleRun(token.Text, token.Color));
            return list;
        }).AsReadOnly();
}

public sealed record DialogueDisplaySnapshot(
    DialogueDisplayPhase Phase,
    string VisibleText,
    int Color,
    int LineIndex,
    int PaletteRotation,
    bool ClearDisplay,
    int PromptFrame,
    IReadOnlyList<DialogueVisibleRun> VisibleRuns)
{
    public string TitleText { get; init; } = "";
    public IReadOnlyList<DialogueVisibleLine> VisibleLines { get; init; } = [];
}

public sealed record DialogueVisibleLine(int LineIndex, IReadOnlyList<DialogueVisibleRun> Runs);
