using System;
using Godot;
using Pal.Core;
using Pal.Core.Battle;

namespace Pal.Game.Dialogue;

/// <summary>Per-scene entropy; a seed is a diagnostic input, not SDL sequence compatibility.</summary>
public sealed class GodotBattleRandom : IBattleRandom, IDisposable
{
    private readonly RandomNumberGenerator random = new();
    public GodotBattleRandom(ulong? seed)
    {
        if (seed is { } value) random.Seed = value;
        else random.Randomize();
    }
    public (ulong Seed, ulong State) CurrentState => (random.Seed, random.State);
    public bool IsDisposed => !GodotObject.IsInstanceValid(random);

    /// <summary>The caller owns the native fork. Seed must precede State: assigning Seed
    /// resets Godot's state, whereas State restores the current position in the stream.</summary>
    public GodotBattleRandom Fork()
    {
        var fork = new GodotBattleRandom(random.Seed);
        try { fork.random.State = random.State; return fork; }
        catch { fork.Dispose(); throw; }
    }

    public int NextInclusive(int from, int to) => to <= from ? from : random.RandiRange(from, to);
    public float NextInclusive(float from, float to) => to <= from ? from : random.RandfRange(from, to);
    public void Dispose() => random.Dispose();
}
