using Godot;

namespace Pal.Game.Tests;

public partial class ExpectedReadyException : Node
{
    public override void _Ready() => throw new System.InvalidOperationException("ISSUE13_EXPECTED_READY_EXCEPTION");
}
