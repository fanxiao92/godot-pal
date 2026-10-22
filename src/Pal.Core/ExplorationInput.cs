namespace Pal.Core;

// Adjacent aliases share a reference direction: south=0, west=1, north=2, east=3.
public enum ExplorationKey
{
    SouthPrimary, SouthAlternate,
    WestPrimary, WestAlternate,
    NorthPrimary, NorthAlternate,
    EastPrimary, EastAlternate
}

// Owns advancement of the supplied session; callers must not update it separately.
public sealed class ExplorationInput
{
    private readonly ExplorationSession session;
    // Oldest to newest direction groups, not individual keys; at most four entries.
    private readonly List<int> directionOrder = new(4);
    private int heldKeys;
    public ExplorationState Current => session.Current;

    public ExplorationInput(ExplorationSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        this.session = session;
    }

    public void Press(ExplorationKey key, bool repeat = false)
    {
        ValidateKey(key);
        int keyMask = 1 << (int)key;
        if (repeat || (heldKeys & keyMask) != 0) return;
        heldKeys |= keyMask;
        int direction = (int)key / 2;
        directionOrder.Remove(direction);
        directionOrder.Add(direction);
    }

    public void Release(ExplorationKey key)
    {
        ValidateKey(key);
        heldKeys &= ~(1 << (int)key);
        int direction = (int)key / 2;
        if ((heldKeys & (3 << (direction * 2))) == 0) directionOrder.Remove(direction);
    }

    public bool Update(uint ticksMilliseconds) => session.Update(ticksMilliseconds,
        directionOrder.Count == 0 ? null : directionOrder[^1]);

    private static void ValidateKey(ExplorationKey key)
    {
        if (key is < ExplorationKey.SouthPrimary or > ExplorationKey.EastAlternate)
            throw new ArgumentOutOfRangeException(nameof(key));
    }
}
