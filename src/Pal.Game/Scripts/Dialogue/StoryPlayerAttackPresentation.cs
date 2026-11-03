using Godot;

namespace Pal.Game.Dialogue;

/// <summary>Single-target, no-dual-attack physical animation, fight.c2008–2292/3670–3754.
/// The local x/y subtraction before FX never assigns player.pos. No combat or RNG is advanced.</summary>
internal sealed class StoryPlayerAttackPresentation(Vector2 player, Vector2 enemy,
    int beforeEnemyHp, int afterEnemyHp, int playerFrame)
{
    internal const int Duration = 16;
    internal StoryPlayerAttackSnapshot At(int pose, int enemyHeight)
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

internal readonly record struct StoryPlayerAttackSnapshot(int PlayerFrame, Vector2 PlayerPosition,
    Vector2 EnemyPosition, int EnemyColorShift, int? EffectFrame, Vector2 EffectPosition,
    int DisplayedEnemyHp, bool ResultVisible);

/// <summary>One battle's live enemy gesture, shared by UpdateFighters, TRUE delays and manual FX.
/// WORD pre-decrement deliberately permits 0→65535; active FALSE poses assign frame but never reset counter.</summary>
internal sealed class StoryEnemyIdleState(int idleFrames, int speed)
{
    internal int Frame { get; private set; }
    internal ushort Counter { get; private set; } = (ushort)speed;
    internal void SetFrame(int frame) => Frame = frame;
    internal void Advance()
    {
        Counter = unchecked((ushort)(Counter - 1));
        if (Counter == 0) { Frame++; Counter = (ushort)speed; }
        if (Frame >= idleFrames) Frame = 0;
    }
}
