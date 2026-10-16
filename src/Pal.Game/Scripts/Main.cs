using Godot;

namespace Pal.Game;

public partial class Main : Control
{
    public override void _Ready()
    {
        var status = GetNode<Label>("Content/Status");
        status.Text = Pal.Core.Startup.Status;
        GD.Print($"PAL_GAME_READY:{status.Text}");
    }
}
