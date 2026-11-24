using Godot;

namespace Pal.Game.Resources;

/// <summary>One party-position player binding: the battle sprite follows the RoleId at that
/// party slot (battle.c:856 addresses g_rgParty[i].wPlayerRole), never the head role, the
/// party position alone or the sprite number. Resources may be shared between bindings;
/// per-member identity, poses, hit/death state and effect addressing are not merged.</summary>
[GlobalClass]
public partial class PalBattleVisualPlayer : Resource
{
    [Export] public int RoleId { get; set; } = -1;
    [Export] public string PlayerSpriteUri { get; set; } = "";
    // Resolved column 1 of the effect table for this member's actual battle sprite
    // (fight.c:2029), not RoleId; absent means paired-empty for this member only.
    [Export] public string AttackEffectSpriteUri { get; set; } = "";
    [Export] public int AttackEffectIndex { get; set; } = -1;
    // Explicit bottom-center column i of g_rgPlayerPos[party-1] (battle.c:27-31).
    [Export] public Vector2I PlayerPosition { get; set; } = new(int.MinValue, int.MinValue);
}
