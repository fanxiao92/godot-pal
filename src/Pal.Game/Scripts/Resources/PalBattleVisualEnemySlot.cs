using Godot;

namespace Pal.Game.Resources;

/// <summary>One enemy slot binding of a team. Slot order is the story team's enemy slot
/// order; duplicate enemy definition IDs across slots stay separate slots.</summary>
[GlobalClass]
public partial class PalBattleVisualEnemySlot : Resource
{
    [Export] public int TargetId { get; set; } = -1;
    [Export] public Vector2I EnemyPosition { get; set; } = new(int.MinValue, int.MinValue);
}
