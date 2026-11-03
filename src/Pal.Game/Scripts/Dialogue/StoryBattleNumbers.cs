using System;
using System.Collections.Generic;
using Godot;
using Pal.Core;

namespace Pal.Game.Dialogue;

internal enum StoryNumberColor { Yellow, Blue, Cyan }
internal readonly record struct StoryNumberDigit(int Slot, int Digit, StoryNumberColor Color, Vector2 Position, uint BornAt);

/// <summary>Battle-owned UI slots (uibattle.c1748–1808), not a pose clock or combat owner.</summary>
internal sealed class StoryBattleNumbers
{
    private sealed record Number(ushort Value, Vector2 Position, StoryNumberColor Color, uint BornAt, bool Pending);
    private readonly Number?[] slots = new Number?[16];
    private (int Word, uint BornAt, bool Pending)? label;

    internal void BeginAction() => label = null;
    internal void EnqueueAction(int beforeHp, ClassicBattleAction action, Vector2 target, uint now, bool pending)
    {
        // Player caller subtracts SHORT damage from WORD HP before stat-change; do not use clamped combat HP.
        ushort after = unchecked((ushort)(beforeHp - action.Damage));
        short difference = unchecked((short)(after - beforeHp));
        if (!action.Evaded && difference != 0)
            Enqueue((ushort)Math.Abs((int)difference), target - new Vector2(9, 0),
                difference < 0 ? StoryNumberColor.Blue : StoryNumberColor.Yellow, now, pending);
        if (action.Evaded || action.Critical)
            label = (action.Evaded ? PalStoryBattleView.MissWord : PalStoryBattleView.CriticalWord, now, pending);
    }
    // Position is the caller's stat-change anchor (already y-clamped), before ShowNum's x-15.
    internal bool Enqueue(ushort value, Vector2 position, StoryNumberColor color, uint now, bool pending = false)
    {
        if (value == 0) return false;
        for (int i = 0; i < slots.Length; i++)
            if (slots[i] is null)
            { slots[i] = new(value, position - new Vector2(15, 0), color, now, pending); return true; }
        return false;
    }
    internal void RevealPending()
    {
        for (int i = 0; i < slots.Length; i++) if (slots[i] is { Pending: true } n) slots[i] = n with { Pending = false };
        if (label is { } l) label = (l.Word, l.BornAt, false);
    }
    internal int? LabelAt(uint now)
    {
        if (label is not { } l) return null;
        if (unchecked(now - l.BornAt) / 40 > 10) { label = null; return null; }
        return l.Pending ? null : l.Word;
    }
    internal IReadOnlyList<StoryNumberDigit> At(uint now)
    {
        var digits = new List<StoryNumberDigit>();
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] is not { } n) continue;
            uint age = unchecked(now - n.BornAt) / 40;
            if (age > 10) { slots[i] = null; continue; }
            if (n.Pending) continue;
            int value = n.Value, x = 24;
            do
            {
                digits.Add(new(i, value % 10, n.Color, n.Position + new Vector2(x, -(int)age), n.BornAt));
                value /= 10; x -= 6;
            } while (value > 0 && x >= 0);
        }
        return digits.AsReadOnly();
    }
    internal void Clear() { Array.Clear(slots); label = null; }
}
