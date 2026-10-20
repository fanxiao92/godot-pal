using Godot;
using Godot.Collections;

namespace Pal.Game.Resources;

[GlobalClass]
public partial class PalPartyMember : Resource
{
    [Export] public int RoleId { get; set; } = 0;
    [Export] public int WSpriteNum { get; set; } = 0;
    [Export] public PalSprite Sprite { get; set; } = null!;
    [Export] public Dictionary Source { get; set; } = new();
}
