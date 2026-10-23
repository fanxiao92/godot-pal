using Pal.Core;
using Xunit;

namespace Pal.Core.Tests;

public class DialogueMessageTests
{
    [Fact]
    public void Resolves_message_block_by_original_start_and_span_without_collapsing_controls_or_blank_lines()
    {
        var package = new DialogueLanguagePackage("zh-Hans", [
            new DialogueMessageBlock(61, 62, [
                DialogueMessagePart.FromText("line one"),
                DialogueMessagePart.Clear,
                DialogueMessagePart.FromText(string.Empty),
                DialogueMessagePart.FromText("line two")])]);

        var resolved = package.Resolve(61, 1);

        Assert.Equal(61, resolved.StartMessageId);
        Assert.Equal(1, resolved.Span);
        Assert.Collection(resolved.Parts,
            part => Assert.Equal(DialogueMessagePart.FromText("line one"), part),
            part => Assert.Equal(DialogueMessagePart.Clear, part),
            part => Assert.Equal(DialogueMessagePart.FromText(string.Empty), part),
            part => Assert.Equal(DialogueMessagePart.FromText("line two"), part));
    }

    [Fact]
    public void Missing_or_invalid_selected_language_content_fails_instead_of_falling_back()
    {
        var package = new DialogueLanguagePackage("en", [new DialogueMessageBlock(61, 62, [DialogueMessagePart.FromText("english")])]);

        Assert.Throws<InvalidDataException>(() => package.Resolve(61, 0));
        Assert.Throws<InvalidDataException>(() => new DialogueLanguagePackage("en", [
            new DialogueMessageBlock(61, 62, [DialogueMessagePart.FromText("valid")]),
            new DialogueMessageBlock(61, 62, [DialogueMessagePart.FromText("duplicate")])]));
        Assert.Throws<ArgumentException>(() => new DialogueLanguagePackage("fr", []));
    }

    [Fact]
    public void Language_selection_defaults_to_simplified_chinese_and_selects_english_only_when_explicit()
    {
        var chinese = new DialogueLanguagePackage("zh-Hans", [new DialogueMessageBlock(61, 61, [DialogueMessagePart.FromText("zh")])]);
        var english = new DialogueLanguagePackage("en", [new DialogueMessageBlock(61, 61, [DialogueMessagePart.FromText("en")])]);

        Assert.Equal("zh-Hans", new DialogueLanguageSelection().Select(chinese, english).Language);
        Assert.Equal("en", new DialogueLanguageSelection("en").Select(chinese, english).Language);
        Assert.Throws<InvalidDataException>(() => new DialogueLanguageSelection("en").Select(english, chinese));
    }

    [Fact]
    public void Message_identifier_inside_block_does_not_rebase_or_change_original_span()
    {
        var package = new DialogueLanguagePackage("en", [
            new DialogueMessageBlock(61, 62, [DialogueMessagePart.FromText("first"), DialogueMessagePart.FromText("second")]),
            new DialogueMessageBlock(62, 62, [DialogueMessagePart.FromText("internal entry")])]);

        var internalEntry = package.Resolve(62, 0);

        Assert.Equal(62, internalEntry.StartMessageId);
        Assert.Equal(0, internalEntry.Span);
        Assert.Equal(DialogueMessagePart.FromText("internal entry"), Assert.Single(internalEntry.Parts));
    }
}
