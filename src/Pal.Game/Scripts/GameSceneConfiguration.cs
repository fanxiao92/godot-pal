using Pal.Game.Map;
using System;
using System.IO;
using System.Linq;
using System.Globalization;
using Pal.Game.Profiles;

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

    internal string? StoryProfile { get; private init; }
    internal string? VisualProfile { get; private init; }
    /// <summary>The #52 no-combat dynamic-scene selection: a checked story profile run through
    /// DynamicEventSession instead of the battle story flow.</summary>
    internal string? DynamicProfile { get; private init; }
    internal ulong? BattleSeed { get; private init; }
    internal string Root { get; }
    internal string Profile { get; }
    internal bool StaticAcceptance { get; }
    internal string? DialogueProfile { get; }
    internal string? BattleProfile { get; }
    internal string Language { get; }

    public static GameSceneConfiguration FromArguments(params string[] arguments)
    {
        if (arguments.Any(a => a.StartsWith("--dynamic-", StringComparison.Ordinal)))
        {
            string? dynamicProfile = null, language = null;
            for (int i = 0; i < arguments.Length; i += 2)
            {
                if (i + 1 == arguments.Length || arguments[i + 1].StartsWith("--", StringComparison.Ordinal))
                    throw new InvalidDataException("Dynamic scene: missing value for " + arguments[i]);
                switch (arguments[i])
                {
                    case "--dynamic-profile" when dynamicProfile is null: dynamicProfile = arguments[i + 1]; break;
                    case "--story-language" when language is null: language = arguments[i + 1]; break;
                    default: throw new InvalidDataException("Dynamic scene: duplicate, mixed, or unknown " + arguments[i]);
                }
            }
            if (dynamicProfile is null) throw new InvalidDataException("Dynamic scene requires --dynamic-profile");
            NativeExplorationProfile.ValidateUri(dynamicProfile);
            if (language is not (null or "zh-Hans" or "en")) throw new InvalidDataException("Story language must be zh-Hans or en");
            return new("", "", false, null, null, language ?? "zh-Hans") { DynamicProfile = dynamicProfile };
        }
        if (arguments.Any(a => a.StartsWith("--story-", StringComparison.Ordinal) || a.StartsWith("--battle-", StringComparison.Ordinal)))
        {
            string? story = null, visual = null, language = null, seedText = null;
            for (int i = 0; i < arguments.Length; i += 2)
            {
                if (i + 1 == arguments.Length || arguments[i + 1].StartsWith("--", StringComparison.Ordinal))
                    throw new InvalidDataException("Story: missing value for " + arguments[i]);
                switch (arguments[i])
                {
                    case "--story-profile" when story is null: story = arguments[i + 1]; break;
                    case "--battle-visual-profile" when visual is null: visual = arguments[i + 1]; break;
                    case "--story-language" when language is null: language = arguments[i + 1]; break;
                    case "--battle-seed" when seedText is null: seedText = arguments[i + 1]; break;
                    default: throw new InvalidDataException("Story: duplicate, mixed, or unknown " + arguments[i]);
                }
            }
            if (story is null || visual is null) throw new InvalidDataException("Story requires both --story-profile and --battle-visual-profile");
            NativeExplorationProfile.ValidateUri(story); NativeExplorationProfile.ValidateUri(visual);
            if (language is not (null or "zh-Hans" or "en")) throw new InvalidDataException("Story language must be zh-Hans or en");
            ulong? seed = null;
            if (seedText is not null)
            {
                if (!ulong.TryParse(seedText, NumberStyles.None, CultureInfo.InvariantCulture, out ulong value))
                    throw new InvalidDataException("Battle seed must be an unsigned decimal ulong");
                seed = value;
            }
            return new("", "", false, null, null, language ?? "zh-Hans") { StoryProfile = story, VisualProfile = visual, BattleSeed = seed };
        }
        var selected = NativeSceneLoader.Arguments(arguments);
        return new(selected.Root, selected.Profile, selected.StaticAcceptance, selected.DialogueProfile, selected.BattleProfile, selected.Language);
    }
}
