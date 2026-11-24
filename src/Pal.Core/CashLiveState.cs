namespace Pal.Core;

/// <summary>Host-owned DWORD cash balance, explicitly seeded once. Binding to trigger calls,
/// accepted battle receipts and native starting contexts belongs to the host.</summary>
public sealed class CashLiveState
{
    public uint Cash { get; private set; }

    public CashLiveState(uint initialCash) => Cash = initialCash;

    /// <summary>Applies a signed WORD delta (script.c 0x001E). Insufficient cash returns false
    /// without mutation; the caller owns the shortage branch, not this state.</summary>
    public bool TryChange(int amount)
    {
        if (amount is < -32768 or > 32767) throw new ArgumentOutOfRangeException(nameof(amount));
        if (amount < 0 && Cash < (uint)-amount) return false;
        Cash = unchecked(Cash + (uint)amount);
        return true;
    }

    /// <summary>Unsigned integer division by two (script.c 0x008F).</summary>
    public void Halve() => Cash /= 2;

    /// <summary>Adds an already-authorized delta with DWORD wrap (battle.c PAL_BattleWon).
    /// This arithmetic operation does not authenticate or deduplicate completion receipts.</summary>
    public void Credit(uint amount) => Cash = unchecked(Cash + amount);
}
