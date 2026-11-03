using System;
using System.Collections.Generic;
using Godot;
using Pal.Core;

namespace Pal.Game.Dialogue;

/// <summary>Active single-enemy physical sequence, fight.c4987–5135. No RNG or combat advancement.
/// Main supplies the persistent gesture for TRUE tail segments; this timeline owns active FALSE frames.</summary>
internal sealed class StoryEnemyAttackPresentation
{
    private readonly List<(long End, StoryEnemyAttackSnapshot Snapshot)> segments = new();
    private readonly StoryEnemyAttackSnapshot returned;
    internal long Duration { get; private set; }
    internal StoryEnemyAttackSnapshot At(int pose)
    {
        foreach (var segment in segments) if (pose < segment.End) return segment.Snapshot;
        return returned;
    }

    internal StoryEnemyAttackPresentation(int idle, int magic, int attack, int wait,
        Vector2 player, Vector2 enemy, int beforeHp, int afterHp, int maxHp, ClassicBattleAction action, int initialEnemyFrame = 0)
    {
        int pf = RestFrame(beforeHp, maxHp), ef = initialEnemyFrame;
        Vector2 pp = player, ep = enemy;
        bool impact = false;
        int shift = 0;
        void Hold(int duration)
        {
            if (duration == 0) return;
            Duration += duration;
            segments.Add((Duration, new(pf, ef, pp, ep, shift, impact ? afterHp : beforeHp, impact)));
        }
        for (int i = 0; i < magic; i++) { ef = idle + i; Hold(2); }
        for (int i = 0; i < 3 - magic; i++) { ep -= new Vector2(2, 1); Hold(1); }
        Hold(1);
        ep = player + new Vector2(-44, -16);
        if (action.Evaded) pf = 3;
        if (attack == 0) { ef = idle - 1; Hold(2); }
        else
            // Inclusive loop and -1 are intentional; wait=0 adds no timed pose.
            for (int i = 0; i <= attack; i++) { ef = idle + magic + i - 1; Hold(wait); }
        impact = true;
        if (!action.Evaded) { pf = 4; shift = 6; }
        Hold(1);
        shift = 0; pp += new Vector2(8, 4); Hold(1);
        pp += new Vector2(2, 1); Hold(3);
        ep = enemy; ef = 0; Hold(1);
        pf = RestFrame(afterHp, maxHp); Hold(5);
        returned = new(pf, 0, player, enemy, 0, afterHp, true);
    }
    internal static int RestFrame(int hp, int maxHp) => hp == 0 ? 2 : hp < Math.Min(100, maxHp / 5) ? 1 : 0;
    internal static byte ShiftIndex(byte index, int shift) => (byte)((index & 0xf0) | Math.Clamp((index & 15) + shift, 0, 15));
}

internal readonly record struct StoryEnemyAttackSnapshot(int PlayerFrame, int EnemyFrame,
    Vector2 PlayerPosition, Vector2 EnemyPosition, int PlayerColorShift, int DisplayedPlayerHp, bool ResultVisible);
