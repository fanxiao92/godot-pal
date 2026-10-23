namespace Pal.Core;

/// <summary>Tokenizes the control subset used by SDLPal TEXT_DisplayText.</summary>
public static class DialogueTextTokenizer
{
    public const int DefaultColor = 0x4F;
    public const int YellowColor = 0x2D;
    public const int RedColor = 0x1A;
    public const int CyanColor = 0x8D;
    public const int AlternateRedColor = 0x17;
    public const int DefaultDelayMilliseconds = 24;

    public static int GetPromptFrame(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        int frame = 0;
        for (int index = 0; index < text.Length; index++)
        {
            if (text[index] == '\\' && index + 1 < text.Length) { index++; continue; }
            if (text[index] == ')') frame = 1;
            else if (text[index] == '(') frame = 2;
        }
        return frame;
    }

    public static IReadOnlyList<DialogueTextToken> Tokenize(string text, int initialColor) =>
        TokenizeLine(text, initialColor, DefaultDelayMilliseconds).Tokens;

    public static DialogueTextLine TokenizeLine(string text, int initialColor, int initialDelayMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (initialColor is < 0 or > 255) throw new ArgumentOutOfRangeException(nameof(initialColor));
        if (initialDelayMilliseconds < 0) throw new ArgumentOutOfRangeException(nameof(initialDelayMilliseconds));
        var tokens = new List<DialogueTextToken>();
        int color = initialColor;
        int delay = initialDelayMilliseconds;
        for (int index = 0; index < text.Length;)
        {
            char value = text[index++];
            if (value == '\\')
            {
                if (index < text.Length) tokens.Add(new DialogueTextToken(text[index++].ToString(), color, delay));
                continue;
            }
            switch (value)
            {
                case '-': color = Toggle(color, CyanColor); continue;
                case '\'': color = Toggle(color, RedColor); continue;
                case '@': color = Toggle(color, AlternateRedColor); continue;
                case '"': color = Toggle(color, YellowColor); continue;
                case ')':
                case '(': continue;
                case '~': throw new InvalidDataException("The '~' timed early-return control is not supported by this dialogue display slice.");
                case '$':
                    if (index + 1 >= text.Length || !char.IsAsciiDigit(text[index]) || !char.IsAsciiDigit(text[index + 1]))
                        throw new InvalidDataException("The '$' delay control requires exactly two decimal digits.");
                    int sourceDelay = (text[index] - '0') * 10 + text[index + 1] - '0';
                    index += 2;
                    delay = sourceDelay * 10 / 7 * 8;
                    continue;
                default:
                    tokens.Add(new DialogueTextToken(value.ToString(), color, delay)); continue;
            }
        }
        return new DialogueTextLine(Array.AsReadOnly(tokens.ToArray()), color, delay, GetPromptFrame(text));
    }

    private static int Toggle(int current, int selected) => current == selected ? DefaultColor : selected;
}

public sealed record DialogueTextToken(string Text, int Color, int DelayMilliseconds);
public sealed record DialogueTextLine(IReadOnlyList<DialogueTextToken> Tokens, int EndColor, int EndDelayMilliseconds, int PromptFrame);
public sealed record DialogueVisibleRun(string Text, int Color);
