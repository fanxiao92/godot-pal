namespace Pal.Core.Events;

public enum OpcodeSupportLevel
{
    Full,
    Partial
}

/// <summary>Code-derived runtime capability metadata for one SDLPal script opcode.</summary>
public sealed record OpcodeCapability(int Opcode, string Verb, OpcodeSupportLevel Level, string? Note = null)
{
    public string FormatCell() => Level == OpcodeSupportLevel.Full
        ? (string.IsNullOrEmpty(Note) ? "✅" : $"✅ ({Note})")
        : (string.IsNullOrEmpty(Note) ? "⚠️" : $"⚠️ {Note}");
}
