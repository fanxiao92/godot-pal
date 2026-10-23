namespace Pal.Core;

/// <summary>Explicit dialogue-language choice shared by a content reference set; it never merges packages.</summary>
public sealed class DialogueLanguageSelection
{
    public const string DefaultLanguage = "zh-Hans";
    public string Language { get; }

    public DialogueLanguageSelection(string language = DefaultLanguage)
    {
        if (language is not ("zh-Hans" or "en"))
            throw new ArgumentException("Dialogue language must be zh-Hans or en.", nameof(language));
        Language = language;
    }

    public DialogueLanguagePackage Select(DialogueLanguagePackage simplifiedChinese, DialogueLanguagePackage english)
    {
        ArgumentNullException.ThrowIfNull(simplifiedChinese);
        ArgumentNullException.ThrowIfNull(english);
        if (simplifiedChinese.Language != "zh-Hans" || english.Language != "en")
            throw new InvalidDataException("Language package identities do not match the selected language set.");
        return Language == "zh-Hans" ? simplifiedChinese : english;
    }
}
