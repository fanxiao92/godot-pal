namespace Pal.Core;

public enum DialogueDisplayPhase
{
    Typing,
    WaitingForAdvance,
    Completed
}

/// <summary>Deterministic display progression for one resolved message block; no rendering or input device access.</summary>
public sealed class DialogueDisplaySession
{
    public const uint DefaultCharacterIntervalMilliseconds = 24;

    private readonly DialogueMessageBlock _block;
    private readonly int _color;
    private int _partIndex;
    private int _visibleLength;
    private int _lineIndex;
    private uint _elapsed;
    private uint _waitElapsed;
    private int _paletteRotation;
    private string _renderedText = string.Empty;
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
        _color = commandColor == 0 ? inheritedColor : commandColor;
        _current = Snapshot(DialogueDisplayPhase.Typing);
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
                _renderedText = string.Empty;
                _visibleLength = 0;
                _current = _partIndex >= _block.Parts.Count
                    ? Snapshot(DialogueDisplayPhase.Completed, clearDisplay: true)
                    : Snapshot(DialogueDisplayPhase.Typing, clearDisplay: true);
            }
            return;
        }

        string text = part.Text!;
        if (accelerateHeld || advancePressed)
        {
            _visibleLength = text.Length;
            _elapsed = 0;
            _renderedText = text;
        }
        else
        {
            ulong accumulated = (ulong)_elapsed + elapsedMilliseconds;
            int added = (int)Math.Min((ulong)(text.Length - _visibleLength), accumulated / DefaultCharacterIntervalMilliseconds);
            _visibleLength += added;
            _elapsed = (uint)(accumulated % DefaultCharacterIntervalMilliseconds);
            _renderedText = text[.._visibleLength];
        }

        if (_visibleLength >= text.Length)
        {
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
                else
                    _current = Snapshot(DialogueDisplayPhase.Typing);
            }
        }
        else
        {
            _current = Snapshot(DialogueDisplayPhase.Typing);
        }
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
        _renderedText = string.Empty;
        _visibleLength = 0;
        _lineIndex = 0;
        _elapsed = 0;
        _current = _partIndex >= _block.Parts.Count
            ? Snapshot(DialogueDisplayPhase.Completed, clearDisplay: true)
            : Snapshot(DialogueDisplayPhase.Typing, clearDisplay: true);
    }

    private DialogueDisplaySnapshot Snapshot(DialogueDisplayPhase phase, bool clearDisplay = false)
    {
        string visible = phase == DialogueDisplayPhase.Completed || clearDisplay ? string.Empty : _renderedText;
        return new DialogueDisplaySnapshot(phase, visible, _color, _lineIndex, _paletteRotation, clearDisplay);
    }
}

public sealed record DialogueDisplaySnapshot(
    DialogueDisplayPhase Phase,
    string VisibleText,
    int Color,
    int LineIndex,
    int PaletteRotation,
    bool ClearDisplay);
