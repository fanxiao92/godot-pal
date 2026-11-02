using Pal.Core;
using Xunit;

namespace Pal.Core.Tests;

public class DialogueDisplaySessionTests
{
    [Theory]
    [InlineData("NPC:")]
    [InlineData("NPC\uFF1A")]
    [InlineData("NPC\u2236")]
    public void Speaker_title_is_immediate_and_does_not_consume_body_line_or_typing_time(string title)
    {
        var session = new DialogueDisplaySession(new DialogueMessageBlock(61, 61, [
            DialogueMessagePart.FromText(title), DialogueMessagePart.FromText("AB")]), 7, 0);

        Assert.Equal(title, session.Current.TitleText);
        Assert.Equal(0, session.Current.LineIndex);
        Assert.Empty(session.Current.VisibleText);
        Assert.Empty(session.Current.VisibleLines);
        session.Update(23, false, false);
        Assert.Equal("A", session.Current.VisibleText);
        session.Update(1, false, false);
        Assert.Equal("AB", session.Current.VisibleText);
        Assert.Equal(0, session.Current.LineIndex);
        Assert.Equal(7, session.Current.Color);
        Assert.Equal(title, session.Current.TitleText);
        Assert.Equal(0, Assert.Single(session.Current.VisibleLines).LineIndex);
    }

    [Theory]
    [InlineData("N\\-\"'$99@:", "N-99:")]
    [InlineData("N-\"'$99@~():", "N-\"'$99@~():")]
    [InlineData("N\\-\\'\\@\\\"\\$\\~\\)\\(:", "N-'@\"$~)(:")]
    [InlineData("N\\A-'@\"$~)(:", "NA:")]
    public void Title_unescape_matches_reference_and_does_not_change_body_color_or_delay(string source, string expected)
    {
        var session = new DialogueDisplaySession(new DialogueMessageBlock(61, 61, [
            DialogueMessagePart.FromText(source), DialogueMessagePart.FromText("A")]), 7, 0);

        Assert.Equal(expected, session.Current.TitleText);
        session.Update(24, false, false);
        Assert.Equal("A", session.Current.VisibleText);
        Assert.Equal(7, session.Current.Color);
        Assert.Equal(DialogueDisplayPhase.WaitingForAdvance, session.Current.Phase);
    }

    [Fact]
    public void Title_and_completed_body_lines_remain_visible_until_completion_and_snapshots_are_stable()
    {
        var session = new DialogueDisplaySession(new DialogueMessageBlock(61, 61, [
            DialogueMessagePart.FromText("NPC:"), DialogueMessagePart.FromText("A"), DialogueMessagePart.FromText("B")]), 7, 0);
        session.Update(24, false, false);
        var firstLine = session.Current;
        Assert.Empty(firstLine.VisibleText);
        Assert.Equal("A", Assert.Single(Assert.Single(firstLine.VisibleLines).Runs).Text);
        session.Update(24, false, false);
        Assert.Equal(DialogueDisplayPhase.WaitingForAdvance, session.Current.Phase);
        Assert.Equal("NPC:", session.Current.TitleText);
        Assert.Collection(session.Current.VisibleLines,
            line => { Assert.Equal(0, line.LineIndex); Assert.Equal("A", Assert.Single(line.Runs).Text); },
            line => { Assert.Equal(1, line.LineIndex); Assert.Equal("B", Assert.Single(line.Runs).Text); });
        Assert.Single(firstLine.VisibleLines);
        session.Update(100, true, false);
        Assert.Equal(DialogueDisplayPhase.Completed, session.Current.Phase);
        Assert.Empty(session.Current.TitleText);
        Assert.Empty(session.Current.VisibleLines);
    }

    [Fact]
    public void Clear_restores_title_but_discards_body_lines_before_the_next_page()
    {
        var session = new DialogueDisplaySession(new DialogueMessageBlock(61, 61, [
            DialogueMessagePart.FromText("NPC:"), DialogueMessagePart.FromText("A"),
            DialogueMessagePart.Clear, DialogueMessagePart.FromText("B")]), 7, 0);
        session.Update(24, false, false);
        session.Update(100, true, false);
        Assert.Equal("NPC:", session.Current.TitleText);
        Assert.True(session.Current.ClearDisplay);
        Assert.Empty(session.Current.VisibleLines);
        session.Update(24, false, false);
        var line = Assert.Single(session.Current.VisibleLines);
        Assert.Equal(0, line.LineIndex);
        Assert.Equal("B", Assert.Single(line.Runs).Text);
    }

    [Fact]
    public void Colon_after_a_body_line_is_not_a_speaker_title()
    {
        var session = new DialogueDisplaySession(new DialogueMessageBlock(61, 61, [
            DialogueMessagePart.FromText("A"), DialogueMessagePart.FromText("B:")]), 7, 0);
        session.Update(24, false, false);
        session.Update(48, false, false);
        Assert.Empty(session.Current.TitleText);
        Assert.Equal("B:", session.Current.VisibleText);
        Assert.Equal(2, session.Current.VisibleLines.Count);
    }

    [Fact]
    public void Title_only_block_does_not_invent_a_body_wait()
    {
        var session = new DialogueDisplaySession(new DialogueMessageBlock(61, 61, [DialogueMessagePart.FromText("NPC:")]), 7, 0);
        Assert.Equal(DialogueDisplayPhase.Completed, session.Current.Phase);
        session.Update(24, false, false);
        Assert.Empty(session.Current.VisibleLines);
    }

    [Fact]
    public void Typewriter_uses_reference_default_interval_then_waits_for_advance_and_cleans_up()
    {
        var block = new DialogueMessageBlock(61, 61, [DialogueMessagePart.FromText("AB")]);
        var session = new DialogueDisplaySession(block, inheritedColor: 7, commandColor: 0);

        Assert.Equal(DialogueDisplayPhase.Typing, session.Current.Phase);
        Assert.Equal(7, session.Current.Color);
        Assert.Equal(string.Empty, session.Current.VisibleText);
        session.Update(23, advancePressed: false, acceleratePressed: false);
        Assert.Equal("A", session.Current.VisibleText);
        session.Update(1, advancePressed: false, acceleratePressed: false);
        Assert.Equal("AB", session.Current.VisibleText);
        Assert.Equal(7, Assert.Single(session.Current.VisibleRuns).Color);
        session.Update(24, advancePressed: false, acceleratePressed: false);
        Assert.Equal("AB", session.Current.VisibleText);
        Assert.Equal(DialogueDisplayPhase.WaitingForAdvance, session.Current.Phase);
        session.Update(100, advancePressed: true, acceleratePressed: false);
        Assert.Equal(DialogueDisplayPhase.Completed, session.Current.Phase);
        Assert.Equal(string.Empty, session.Current.VisibleText);
    }

    [Fact]
    public void Acceleration_finishes_current_text_but_does_not_consume_advance_until_waiting()
    {
        var session = new DialogueDisplaySession(
            new DialogueMessageBlock(61, 61, [DialogueMessagePart.FromText("AB")]), 4, 9);

        session.Update(24, advancePressed: false, acceleratePressed: true);

        Assert.Equal("AB", session.Current.VisibleText);
        Assert.Equal(DialogueDisplayPhase.WaitingForAdvance, session.Current.Phase);
        Assert.Equal(9, session.Current.Color);
        session.Update(100, advancePressed: true, acceleratePressed: false);
        Assert.Equal(DialogueDisplayPhase.Completed, session.Current.Phase);
    }

    [Fact]
    public void Fifth_dialogue_line_waits_then_clears_the_page_before_continuing()
    {
        var session = new DialogueDisplaySession(new DialogueMessageBlock(61, 61, [
            DialogueMessagePart.FromText("A"), DialogueMessagePart.FromText("B"),
            DialogueMessagePart.FromText("C"), DialogueMessagePart.FromText("D"),
            DialogueMessagePart.FromText("E")]), 4, 0);

        session.Update(24, false, false);
        session.Update(24, false, false);
        session.Update(24, false, false);
        session.Update(24, false, false);
        Assert.Equal(4, session.Current.LineIndex);
        Assert.Equal("D", session.Current.VisibleText);
        session.Update(0, false, false);
        Assert.Equal(DialogueDisplayPhase.WaitingForAdvance, session.Current.Phase);
        session.Update(100, true, false);
        Assert.Equal(DialogueDisplayPhase.Typing, session.Current.Phase);
        Assert.Equal(string.Empty, session.Current.VisibleText);
        Assert.True(session.Current.ClearDisplay);
        Assert.Equal(0, session.Current.LineIndex);
        session.Update(24, false, false);
        Assert.Equal("E", session.Current.VisibleText);
    }

    [Fact]
    public void Leading_clear_restores_before_body_without_introducing_an_input_wait()
    {
        var session = new DialogueDisplaySession(new DialogueMessageBlock(61, 61, [
            DialogueMessagePart.Clear, DialogueMessagePart.FromText("A")]), 4, 0);

        Assert.Equal(DialogueDisplayPhase.Typing, session.Current.Phase);
        Assert.True(session.Current.ClearDisplay);
        Assert.Empty(session.Current.VisibleText);
        Assert.Empty(session.Current.VisibleLines);
        session.Update(23, false, false);
        Assert.Equal(DialogueDisplayPhase.Typing, session.Current.Phase);
        Assert.Equal("A", session.Current.VisibleText);
        session.Update(1, false, false);
        Assert.Equal("A", session.Current.VisibleText);
        Assert.False(session.Current.ClearDisplay);
        Assert.Equal(DialogueDisplayPhase.WaitingForAdvance, session.Current.Phase);
    }

    [Fact]
    public void Clear_only_block_completes_without_waiting_when_no_dialogue_line_has_been_shown()
    {
        var session = new DialogueDisplaySession(
            new DialogueMessageBlock(61, 61, [DialogueMessagePart.Clear]), 4, 0);

        session.Update(0, advancePressed: false, acceleratePressed: false);

        Assert.Equal(DialogueDisplayPhase.Completed, session.Current.Phase);
        Assert.True(session.Current.ClearDisplay);
    }

    [Fact]
    public void Waiting_palette_shift_rotates_on_each_reference_100_millisecond_boundary_and_restores_on_input()
    {
        var session = new DialogueDisplaySession(
            new DialogueMessageBlock(61, 61, [DialogueMessagePart.FromText("A")]), 4, 0);
        session.Update(24, false, false);
        session.Update(99, false, false);
        Assert.Equal(0, session.Current.PaletteRotation);
        session.Update(1, false, false);
        Assert.Equal(1, session.Current.PaletteRotation);
        session.Update(500, false, false);
        Assert.Equal(0, session.Current.PaletteRotation);
        session.Update(100, true, false);
        Assert.Equal(0, session.Current.PaletteRotation);
        Assert.Equal(DialogueDisplayPhase.Completed, session.Current.Phase);
    }

    [Fact]
    public void Reference_controls_preserve_per_run_colors_delay_and_prompt_selection()
    {
        var tokens = DialogueTextTokenizer.Tokenize("A-B' C@D\\-$07E", DialogueTextTokenizer.DefaultColor);
        Assert.Equal(new[] { "A", "B", " ", "C", "D", "-", "E" }, tokens.Select(token => token.Text));
        Assert.Equal(new[] { 0x4F, 0x8D, 0x1A, 0x1A, 0x17, 0x17, 0x17 }, tokens.Select(token => token.Color));
        Assert.Equal(80, tokens[6].DelayMilliseconds);
        Assert.Equal(136, DialogueTextTokenizer.TokenizeLine("$12A", DialogueTextTokenizer.DefaultColor, 24).Tokens[0].DelayMilliseconds);
        Assert.Equal(1, DialogueTextTokenizer.GetPromptFrame("A)"));
        Assert.Equal(2, DialogueTextTokenizer.GetPromptFrame("A("));
        Assert.Throws<InvalidDataException>(() => DialogueTextTokenizer.Tokenize("~01", DialogueTextTokenizer.DefaultColor));
        Assert.Throws<InvalidDataException>(() => DialogueTextTokenizer.Tokenize("$7", DialogueTextTokenizer.DefaultColor));
    }

    [Fact]
    public void Inherited_palette_index_zero_is_not_replaced_with_default_color()
    {
        var session = new DialogueDisplaySession(
            new DialogueMessageBlock(61, 61, [DialogueMessagePart.FromText("A")]), inheritedColor: 0, commandColor: 0);

        Assert.Equal(0, session.Current.Color);
        session.Update(24, advancePressed: false, acceleratePressed: false);
        Assert.Equal(0, Assert.Single(session.Current.VisibleRuns).Color);
        Assert.Equal(0, DialogueTextTokenizer.TokenizeLine("A", initialColor: 0, initialDelayMilliseconds: 24).Tokens[0].Color);
    }

    [Fact]
    public void Quote_control_toggles_yellow_in_dialogue_and_backslash_escapes_it()
    {
        var tokens = DialogueTextTokenizer.Tokenize("A\"B\"C\\\"D", DialogueTextTokenizer.DefaultColor);
        Assert.Equal(new[] { "A", "B", "C", "\"", "D" }, tokens.Select(token => token.Text));
        Assert.Equal(new[] { 0x4F, 0x2D, 0x4F, 0x4F, 0x4F }, tokens.Select(token => token.Color));
    }

    [Fact]
    public void Color_and_delay_controls_persist_between_message_lines()
    {
        var session = new DialogueDisplaySession(new DialogueMessageBlock(61, 61, [
            DialogueMessagePart.FromText("A'$12"), DialogueMessagePart.FromText("B")]), 0x4F, 0);
        session.Update(24, false, false);
        Assert.Equal(DialogueDisplayPhase.Typing, session.Current.Phase);
        Assert.Equal(string.Empty, session.Current.VisibleText);
        Assert.Equal(1, session.Current.LineIndex);
        session.Update(135, false, false);
        Assert.Equal("B", session.Current.VisibleText);
        Assert.Equal(DialogueDisplayPhase.Typing, session.Current.Phase);
        session.Update(1, false, false);
        Assert.Equal("B", session.Current.VisibleText);
        Assert.Equal(0x1A, Assert.Single(session.Current.VisibleRuns).Color);
        Assert.Equal(DialogueDisplayPhase.WaitingForAdvance, session.Current.Phase);
    }

    [Fact]
    public void Color_segments_are_exposed_without_control_syntax_in_visible_text()
    {
        var session = new DialogueDisplaySession(new DialogueMessageBlock(61, 61,
            [DialogueMessagePart.FromText("A-B")]), 0x4F, 0);
        session.Update(48, false, false);
        Assert.Equal("AB", session.Current.VisibleText);
        Assert.Equal(new[] { new DialogueVisibleRun("A", 0x4F), new DialogueVisibleRun("B", 0x8D) }, session.Current.VisibleRuns);
    }

    [Fact]
    public void Clear_entry_waits_for_input_before_recovery_and_continues_with_next_text()
    {
        var session = new DialogueDisplaySession(new DialogueMessageBlock(61, 61, [
            DialogueMessagePart.FromText("A"), DialogueMessagePart.Clear, DialogueMessagePart.FromText("B")]), 3, 0);

        session.Update(24, false, false);
        Assert.Equal(DialogueDisplayPhase.WaitingForAdvance, session.Current.Phase);
        Assert.Equal("A", session.Current.VisibleText);
        session.Update(100, true, false);
        Assert.Equal(DialogueDisplayPhase.Typing, session.Current.Phase);
        Assert.True(session.Current.ClearDisplay);
        Assert.Equal(string.Empty, session.Current.VisibleText);
        session.Update(24, false, false);
        Assert.Equal("B", session.Current.VisibleText);
        Assert.Equal(DialogueDisplayPhase.WaitingForAdvance, session.Current.Phase);
        session.Update(100, true, false);
        Assert.Equal(DialogueDisplayPhase.Completed, session.Current.Phase);
    }
}
