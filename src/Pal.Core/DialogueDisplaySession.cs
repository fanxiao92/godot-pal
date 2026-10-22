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
    private int _paletteRotation;
    private IReadOnlyList<DialogueTextToken> _tokens = Array.Empty<DialogueTextToken>();
    private readonly List<DialogueTextToken> _visible = [];
    private readonly List<DialogueVisibleLine> _completedLines = [];
    private string _titleText = "";
    private bool _clearAfterWait;
    private bool _consumeClearEntry;
    private DialogueDisplaySnapshot _current;

    public DialogueDisplaySnapshot Current => _current;

    public DialogueDisplaySession(DialogueMessageBlock block, int inheritedColor, int commandColor)
    {
        ArgumentNullException.ThrowIfNull(block);
        if (inheritedColor is < 0 or > 255 || commandColor is < 0 or > 255)
            throw new ArgumentOutOfRangeException(nameof(commandColor), "Dialogue colors must be palette indices.");
        _block = block;
        _currentTextColor = commandColor == 0 ? inheritedColor : commandColor;
        SetNextLine();
        _current = _partIndex >= _block.Parts.Count
            ? Snapshot(DialogueDisplayPhase.Completed, clearDisplay: true)
            : Snapshot(DialogueDisplayPhase.Typing);
    }

    public void Update(uint elapsedMilliseconds, bool advancePressed, bool accelerateHeld)
    {
        if (_current.Phase == DialogueDisplayPhase.Completed) return;
        if (_current.Phase == DialogueDisplayPhase.WaitingForAdvance)
        {
            ulong waiting = (ulong)_waitElapsed + elapsedMilliseconds;
            _paletteRotation = (_paletteRotation + (int)(waiting / 100)) % 6;
            _waitElapsed = (uint)(waiting % 100);
            if (advancePressed) AdvancePastWait();
            else _current = Snapshot(DialogueDisplayPhase.WaitingForAdvance, _current.ClearDisplay);
            return;
        }

        if (_lineIndex > 3)
        {
            _clearAfterWait = true;
            _consumeClearEntry = false;
            _current = Snapshot(DialogueDisplayPhase.WaitingForAdvance);
            if (advancePressed) AdvancePastWait();
            return;
        }

        DialogueMessagePart part = _block.Parts[_partIndex];
        if (part.IsClear)
        {
            _clearAfterWait = _lineIndex > 0;
            _consumeClearEntry = _clearAfterWait;
            if (_clearAfterWait)
            {
                _current = Snapshot(DialogueDisplayPhase.WaitingForAdvance);
                if (advancePressed) AdvancePastWait();
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
        if (accelerateHeld || advancePressed)
        {
            _visibleLength = _tokens.Count;
            _visible.Clear();
            _visible.AddRange(_tokens);
            _elapsed = 0;
        }
        else
        {
            ulong accumulated = (ulong)_elapsed + elapsedMilliseconds;
            while (_visibleLength < _tokens.Count)
            {
                int delay = _tokens[_visibleLength].DelayMilliseconds;
                if (accumulated < (uint)delay) break;
                accumulated -= (uint)delay;
                _visible.Add(_tokens[_visibleLength++]);
            }
            _elapsed = (uint)Math.Min(accumulated, uint.MaxValue);
        }

        if (_visibleLength >= _tokens.Count)
        {
            _completedLines.Add(new DialogueVisibleLine(_lineIndex, VisibleRuns()));
            _partIndex++;
            _lineIndex++;
            _visibleLength = 0;
            _elapsed = 0;
            if (_partIndex == _block.Parts.Count)
                _current = Snapshot(DialogueDisplayPhase.WaitingForAdvance);
            else
            {
                DialogueMessagePart next = _block.Parts[_partIndex];
                if (next.IsClear)
                {
                    _clearAfterWait = true;
                    _consumeClearEntry = true;
                    _current = Snapshot(DialogueDisplayPhase.WaitingForAdvance);
                }
                else if (_lineIndex > 3)
                {
                    _clearAfterWait = true;
                    _consumeClearEntry = false;
                    _promptFrame = 0;
                    _current = Snapshot(DialogueDisplayPhase.WaitingForAdvance);
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

    private void SetNextLine()
    {
        // PAL_ShowDialogText treats a colon-terminated message before the first body line
        // as an immediate title. It changes neither body color/delay nor the body line count.
        while (_lineIndex == 0 && _partIndex < _block.Parts.Count
            && _block.Parts[_partIndex] is { IsClear: false, Text: { Length: > 0 } text }
            && text[^1] is ':' or '\uFF1A' or '\u2236')
        {
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
    }

    private void AdvancePastWait()
    {
        _paletteRotation = 0;
        _waitElapsed = 0;
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
