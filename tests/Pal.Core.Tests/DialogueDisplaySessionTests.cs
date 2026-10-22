using Pal.Core;
using Xunit;

namespace Pal.Core.Tests;

public class DialogueDisplaySessionTests
{
    [Fact]
    public void Typewriter_uses_reference_default_interval_then_waits_for_advance_and_cleans_up()
    {
        var block = new DialogueMessageBlock(61, 61, [DialogueMessagePart.FromText("AB")]);
        var session = new DialogueDisplaySession(block, inheritedColor: 7, commandColor: 0);

        Assert.Equal(DialogueDisplayPhase.Typing, session.Current.Phase);
        Assert.Equal(7, session.Current.Color);
        Assert.Equal(string.Empty, session.Current.VisibleText);
        session.Update(23, advancePressed: false, accelerateHeld: false);
        Assert.Equal(string.Empty, session.Current.VisibleText);
        session.Update(1, advancePressed: false, accelerateHeld: false);
        Assert.Equal("A", session.Current.VisibleText);
        session.Update(24, advancePressed: false, accelerateHeld: false);
        Assert.Equal("AB", session.Current.VisibleText);
        Assert.Equal(DialogueDisplayPhase.WaitingForAdvance, session.Current.Phase);
        session.Update(0, advancePressed: true, accelerateHeld: false);
        Assert.Equal(DialogueDisplayPhase.Completed, session.Current.Phase);
        Assert.Equal(string.Empty, session.Current.VisibleText);
    }

    [Fact]
    public void Acceleration_finishes_current_text_but_does_not_consume_advance_until_waiting()
    {
        var session = new DialogueDisplaySession(
            new DialogueMessageBlock(61, 61, [DialogueMessagePart.FromText("AB")]), 4, 9);

        session.Update(0, advancePressed: false, accelerateHeld: true);

        Assert.Equal("AB", session.Current.VisibleText);
        Assert.Equal(DialogueDisplayPhase.WaitingForAdvance, session.Current.Phase);
        Assert.Equal(9, session.Current.Color);
        session.Update(0, advancePressed: true, accelerateHeld: false);
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
        session.Update(0, true, false);
        Assert.Equal(DialogueDisplayPhase.Typing, session.Current.Phase);
        Assert.Equal(string.Empty, session.Current.VisibleText);
        Assert.True(session.Current.ClearDisplay);
        Assert.Equal(0, session.Current.LineIndex);
        session.Update(24, false, false);
        Assert.Equal("E", session.Current.VisibleText);
    }

    [Fact]
    public void Clear_only_block_completes_without_waiting_when_no_dialogue_line_has_been_shown()
    {
        var session = new DialogueDisplaySession(
            new DialogueMessageBlock(61, 61, [DialogueMessagePart.Clear]), 4, 0);

        session.Update(0, advancePressed: false, accelerateHeld: false);

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
        session.Update(0, true, false);
        Assert.Equal(0, session.Current.PaletteRotation);
        Assert.Equal(DialogueDisplayPhase.Completed, session.Current.Phase);
    }

    [Fact]
    public void Clear_entry_waits_for_input_before_recovery_and_continues_with_next_text()
    {
        var session = new DialogueDisplaySession(new DialogueMessageBlock(61, 61, [
            DialogueMessagePart.FromText("A"), DialogueMessagePart.Clear, DialogueMessagePart.FromText("B")]), 3, 0);

        session.Update(24, false, false);
        Assert.Equal(DialogueDisplayPhase.WaitingForAdvance, session.Current.Phase);
        Assert.Equal("A", session.Current.VisibleText);
        session.Update(0, true, false);
        Assert.Equal(DialogueDisplayPhase.Typing, session.Current.Phase);
        Assert.True(session.Current.ClearDisplay);
        Assert.Equal(string.Empty, session.Current.VisibleText);
        session.Update(24, false, false);
        Assert.Equal("B", session.Current.VisibleText);
        Assert.Equal(DialogueDisplayPhase.WaitingForAdvance, session.Current.Phase);
        session.Update(0, true, false);
        Assert.Equal(DialogueDisplayPhase.Completed, session.Current.Phase);
    }
}
