using Pal.Game.Map;

namespace Pal.Game;

/// <summary>Validated immutable native selection shared by process entry and embedded scenes.</summary>
public sealed class GameSceneConfiguration
{
    private GameSceneConfiguration(string root, string profile) { Root = root; Profile = profile; }
    internal string Root { get; }
    internal string Profile { get; }

    public static GameSceneConfiguration FromArguments(params string[] arguments)
    {
        var (root, profile) = NativeSceneLoader.Arguments(arguments);
        return new(root, profile);
    }
}
