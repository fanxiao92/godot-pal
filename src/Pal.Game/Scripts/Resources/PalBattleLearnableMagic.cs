using Godot;

namespace Pal.Game.Resources;

[GlobalClass]
public partial class PalBattleLearnableMagic : Resource
{
    [Export] public int Level { get; set; } = 0;
    [Export] public int MagicId { get; set; } = 0;
}
