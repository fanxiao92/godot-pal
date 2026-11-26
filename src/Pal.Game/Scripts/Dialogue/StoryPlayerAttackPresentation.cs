using System.Collections.Generic;
using Godot;

namespace Pal.Game.Dialogue;

/// <summary>Single-target, no-dual-attack physical animation, fight.c2008–2292/3670–3754.
/// The local x/y subtraction before FX never assigns player.pos. No combat or RNG is advanced.</summary>
public sealed class StoryPlayerAttackPresentation(Vector2 player, Vector2 enemy,
    int beforeEnemyHp, int afterEnemyHp, int playerFrame) : IStoryPlayerStrikePresentation
{
    public const int Duration = 16;
    public StoryPlayerAttackSnapshot At(int pose, int enemyHeight)
    {
        int frame = pose < 4 ? 7 : pose < 7 ? 8 : pose < 13 ? 9 : playerFrame;
        Vector2 pp = pose < 4 || pose >= 13 ? player : enemy + (pose < 6 ? new Vector2(64, 20)
            : pose < 9 ? new Vector2(54, 18) : new Vector2(56, 19));
        Vector2 ep = enemy + (pose == 10 ? new Vector2(-8, -4) : pose == 11 ? new Vector2(-4, -2)
            : pose == 12 ? new Vector2(-6, -3) : Vector2.Zero);
        int? effect = pose is >= 7 and <= 9 ? pose - 7 : null;
        Vector2 effectPosition = enemy + new Vector2(-16 * (effect ?? 0), -enemyHeight / 3 + 10 + 16 * (effect ?? 0));
        return new(frame, pp, ep, pose is 8 or 9 ? 6 : 0, effect, effectPosition,
            pose >= 7 ? afterEnemyHp : beforeEnemyHp, pose >= 7);
    }
}

/// <summary>The all-target physical animation, fight.c 2036–2256 with sTarget -1: the actor walks
/// the single-target offsets toward the fixed (150,100) center while a dual attack-all actor stays
/// at its home column (fight.c 2080–2101: first strike −8/−4, second −20/−12); the three FX frames
/// blit at every struck enemy without the single-target per-frame offset (fight.c 2173–2190), and
/// every struck enemy color-shifts and shakes horizontally together (fight.c 2198–2256, x-only).</summary>
public sealed class StoryPlayerAttackAllPresentation(Vector2 player, IReadOnlyList<Vector2> enemies,
    IReadOnlyList<int> targetSlots, IReadOnlyList<int> beforeHp, IReadOnlyList<int> afterHp,
    int playerFrame, bool dual, bool secondStrike)
    : IStoryPlayerStrikePresentation
{
    private static readonly Vector2 Center = new(150, 100);
    public const int Duration = 16;
    public StoryPlayerAttackSnapshot At(int pose, int enemyHeight)
    {
        int frame = pose < 4 ? 7 : pose < 7 ? 8 : pose < 13 ? 9 : playerFrame;
        Vector2 home = player + (secondStrike ? new Vector2(-20, -12) : new Vector2(-8, -4));
        Vector2 pp = dual ? home
            : pose < 4 || pose >= 13 ? player
            : Center + (pose < 6 ? new Vector2(64, 20) : pose < 9 ? new Vector2(54, 18) : new Vector2(56, 19));
        int? effect = pose is >= 7 and <= 9 ? pose - 7 : null;
        var struck = new StoryStruckEnemy[enemies.Count];
        for (int i = 0; i < enemies.Count; i++)
        {
            Vector2 shake = pose == 10 ? new Vector2(-8, 0) : pose == 11 ? new Vector2(-4, 0)
                : pose == 12 ? new Vector2(-6, 0) : Vector2.Zero;
            struck[i] = new(targetSlots[i], enemies[i] + shake, pose is 8 or 9 ? 6 : 0,
                pose >= 7 ? afterHp[i] : beforeHp[i], pose >= 7, enemies[i]);
        }
        return new(frame, pp, struck.Length == 0 ? Vector2.Zero : struck[0].Position,
            struck.Length == 0 ? 0 : struck[0].ColorShift, effect,
            struck.Length == 0 ? Vector2.Zero : struck[0].EffectPosition,
            struck.Length == 0 ? 0 : struck[0].DisplayedHp, struck.Length != 0 && pose >= 7)
        { StruckEnemies = struck };
    }
}

/// <summary>One player strike's pose sampling; the attack-all variant ignores the single-target
/// enemy height (its FX anchors at each struck enemy's own position).</summary>
public interface IStoryPlayerStrikePresentation
{
    StoryPlayerAttackSnapshot At(int pose, int enemyHeight);
}

public readonly record struct StoryPlayerAttackSnapshot(int PlayerFrame, Vector2 PlayerPosition,
    Vector2 EnemyPosition, int EnemyColorShift, int? EffectFrame, Vector2 EffectPosition,
    int DisplayedEnemyHp, bool ResultVisible)
{
    /// <summary>Per-struck-enemy state of one all-target strike (fight.c 2173–2256); null keeps
    /// the legacy single-target scalar fields.</summary>
    public IReadOnlyList<StoryStruckEnemy>? StruckEnemies { get; init; }
}

/// <summary>One struck enemy of an all-target strike: its combat slot index (PalStoryBattleView
/// keys the per-slot enemy draw on it, so it is the target's enemy slot, not the hit-list ordinal),
/// shaken position, color shift, displayed HP/result visibility and its own FX anchor.</summary>
public readonly record struct StoryStruckEnemy(int Slot, Vector2 Position, int ColorShift,
    int DisplayedHp, bool ResultVisible, Vector2 EffectPosition);

/// <summary>One battle's live enemy gesture, shared by UpdateFighters, TRUE delays and manual FX.
/// WORD pre-decrement deliberately permits 0→65535; active FALSE poses assign frame but never reset counter.</summary>
public sealed class StoryEnemyIdleState(int idleFrames, int speed)
{
    public int Frame { get; private set; }
    public ushort Counter { get; private set; } = (ushort)speed;
    public void SetFrame(int frame) => Frame = frame;
    public void Advance()
    {
        Counter = unchecked((ushort)(Counter - 1));
        if (Counter == 0) { Frame++; Counter = (ushort)speed; }
        if (Frame >= idleFrames) Frame = 0;
    }
}
