using Pal.Game.Map;

namespace Pal.Game;

/// <summary>Validated, immutable selection shared by the process entry and embedded scenes.</summary>
public sealed class GameSceneConfiguration
{
    private GameSceneConfiguration(string root, string profile, bool staticAcceptance, string? dialogueProfile,
        string? battleProfile, string language)
    {
        Root = root; Profile = profile; StaticAcceptance = staticAcceptance;
        DialogueProfile = dialogueProfile; BattleProfile = battleProfile; Language = language;
    }

    internal string Root { get; }
    internal string Profile { get; }
    internal bool StaticAcceptance { get; }
    internal string? DialogueProfile { get; }
    internal string? BattleProfile { get; }
    internal string Language { get; }

    public static GameSceneConfiguration FromArguments(params string[] arguments)
    {
        var selected = NativeSceneLoader.Arguments(arguments);
        return new(selected.Root, selected.Profile, selected.StaticAcceptance, selected.DialogueProfile, selected.BattleProfile, selected.Language);
    }
}
