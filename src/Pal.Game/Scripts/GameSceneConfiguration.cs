using Pal.Game.Map;

namespace Pal.Game;

/// <summary>Validated, immutable selection shared by the process entry and embedded scenes.</summary>
public sealed class GameSceneConfiguration
{
    private GameSceneConfiguration(string root, string profile, bool staticAcceptance, string? dialogueProfile, string language)
    {
        Root = root; Profile = profile; StaticAcceptance = staticAcceptance;
        DialogueProfile = dialogueProfile; Language = language;
    }

    internal string Root { get; }
    internal string Profile { get; }
    internal bool StaticAcceptance { get; }
    internal string? DialogueProfile { get; }
    internal string Language { get; }

    public static GameSceneConfiguration FromArguments(params string[] arguments)
    {
        var (root, profile, staticAcceptance, dialogueProfile, language) = NativeSceneLoader.Arguments(arguments);
        return new(root, profile, staticAcceptance, dialogueProfile, language);
    }
}
