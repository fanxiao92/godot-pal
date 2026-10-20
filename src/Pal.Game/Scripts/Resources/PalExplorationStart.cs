using Godot;

namespace Pal.Game.Resources;

[GlobalClass]
public partial class PalExplorationStart : Resource
{
    // Coordinates accept signed SHORTs or their raw WORD representation, never an omitted zero.
    [Export] public int X { get; set; } = int.MinValue;
    [Export] public int Y { get; set; } = int.MinValue;
    [Export] public int Direction { get; set; } = -1;
    [Export] public int Layer { get; set; } = -1;
    [Export] public int StepPhase { get; set; } = -1;
}
