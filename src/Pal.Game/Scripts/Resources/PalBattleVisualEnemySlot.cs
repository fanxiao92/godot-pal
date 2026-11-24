using Godot;

namespace Pal.Game.Resources;

/// <summary>One enemy slot binding of a team. Slot order is the story team's enemy slot
/// order; duplicate enemy definition IDs across slots stay separate slots.</summary>
[GlobalClass]
public partial class PalBattleVisualEnemySlot : Resource
{
    [Export] public int TargetId { get; set; } = -1;
    [Export] public string EnemySpriteUri { get; set; } = "";
    [Export] public Vector2I EnemyPosition { get; set; } = new(int.MinValue, int.MinValue);
    [Export] public int IdleFrames { get; set; } = -1;
    [Export] public int IdleAnimSpeed { get; set; } = -1;
    [Export] public int MagicFrames { get; set; } = -1;
    [Export] public int AttackFrames { get; set; } = -1;
    [Export] public int ActWaitFrames { get; set; } = -1;
}
