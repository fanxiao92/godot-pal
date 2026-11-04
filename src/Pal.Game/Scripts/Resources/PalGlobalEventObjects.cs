using Godot;

namespace Pal.Game.Resources;

/// <summary>The closure-bound global event-object table: records outside the static bundle that
/// the verified @09C3 closure's object verbs actually address. Runtime never reads raw SSS.</summary>
[GlobalClass]
public partial class PalGlobalEventObjects : Resource
{
    [Export] public int FormatVersion { get; set; } = -1;
    [Export] public string SourceIdentity { get; set; } = "";
    [Export] public PalGlobalEventObject[] Objects { get; set; } = [];
}
