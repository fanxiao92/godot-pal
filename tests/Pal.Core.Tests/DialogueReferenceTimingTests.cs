using Pal.Core;
using Xunit;

namespace Pal.Core.Tests;

public class DialogueReferenceTimingTests
{
    [Theory]
    [InlineData("AB", 24)]
    [InlineData("$12AB", 136)]
    public void Glyph_is_drawn_before_its_delay_including_the_last_glyph(string text, uint delay)
    {
        var display = Create(text);
        display.Update(0, false, false);
        Assert.Equal("A", display.Current.VisibleText);
        display.Update(delay - 1, false, false);
        Assert.Equal("A", display.Current.VisibleText);
        display.Update(1, false, false);
        Assert.Equal("AB", display.Current.VisibleText);
        Assert.Equal(DialogueDisplayPhase.Typing, display.Current.Phase);
        display.Update(delay - 1, false, false);
        Assert.Equal(DialogueDisplayPhase.Typing, display.Current.Phase);
        display.Update(1, false, false);
        Assert.Equal(DialogueDisplayPhase.WaitingForAdvance, display.Current.Phase);
    }

    [Fact]
    public void Zero_delay_draws_all_glyphs_and_enters_wait_without_extra_time()
    {
        var display = new DialogueDisplaySession(new DialogueMessageBlock(1, 1, [DialogueMessagePart.FromText("AB")]), 7, 0, 0);
        display.Update(0, false, false);
        Assert.Equal("AB", display.Current.VisibleText);
        Assert.Equal(DialogueDisplayPhase.WaitingForAdvance, display.Current.Phase);
    }

    [Fact]
    public void Wait_latches_short_press_until_100ms_without_resetting_accumulated_time()
    {
        var display = Create("A"); display.Update(24, false, false);
        display.Update(0, true, false);
        Assert.Equal(DialogueDisplayPhase.WaitingForAdvance, display.Current.Phase);
        display.Update(99, false, false);
        Assert.Equal(DialogueDisplayPhase.WaitingForAdvance, display.Current.Phase);
        display.Update(1, false, false);
        Assert.Equal(DialogueDisplayPhase.Completed, display.Current.Phase);
        Assert.Equal(0, display.Current.PaletteRotation);
        Assert.True(display.Current.ClearDisplay);
    }

    [Fact]
    public void Late_wait_preserves_next_period_and_accepts_press_only_at_its_boundary()
    {
        var display = Create("A"); display.Update(24, false, false);
        display.Update(249, false, false);
        Assert.Equal(2, display.Current.PaletteRotation);
        display.Update(0, true, false);
        Assert.Equal(DialogueDisplayPhase.WaitingForAdvance, display.Current.Phase);
        display.Update(50, false, false);
        Assert.Equal(DialogueDisplayPhase.WaitingForAdvance, display.Current.Phase);
        display.Update(1, false, false);
        Assert.Equal(DialogueDisplayPhase.Completed, display.Current.Phase);
    }

    [Fact]
    public void Fresh_skip_is_sticky_over_lines_but_wait_input_does_not_cross_the_clear_boundary()
    {
        var display = new DialogueDisplaySession(new DialogueMessageBlock(1, 1, [
            DialogueMessagePart.FromText("AB"), DialogueMessagePart.FromText("CD"),
            DialogueMessagePart.Clear, DialogueMessagePart.FromText("EF")]), 7, 0);
        display.Update(0, false, true);
        Assert.Equal("A", display.Current.VisibleText);
        display.Update(24, false, false);
        Assert.Equal(DialogueDisplayPhase.Typing, display.Current.Phase);
        display.Update(0, false, false);
        Assert.Equal("CD", display.Current.VisibleText);
        Assert.Equal(DialogueDisplayPhase.WaitingForAdvance, display.Current.Phase);
        display.Update(100, true, true);
        Assert.Equal(DialogueDisplayPhase.Typing, display.Current.Phase);
        Assert.True(display.Current.ClearDisplay);
        display.Update(0, false, false);
        Assert.Equal("E", display.Current.VisibleText);
        Assert.Equal(DialogueDisplayPhase.Typing, display.Current.Phase);
    }

    [Fact]
    public void Non_search_wait_key_does_not_accelerate_typing_or_leak_into_wait()
    {
        var display = Create("AB");
        display.Update(0, true, false);
        Assert.Equal("A", display.Current.VisibleText);
        Assert.Equal(DialogueDisplayPhase.Typing, display.Current.Phase);
        display.Update(48, false, false);
        display.Update(100, false, false);
        Assert.Equal(DialogueDisplayPhase.WaitingForAdvance, display.Current.Phase);
    }

    [Theory]
    [InlineData(24u)]
    [InlineData(136u)]
    public void Fresh_skip_waits_for_current_glyph_post_delay_and_survives_release(uint delay)
    {
        var display = new DialogueDisplaySession(new DialogueMessageBlock(1, 1, [DialogueMessagePart.FromText("ABC")]), 7, 0, (int)delay);
        display.Update(0, false, false);
        display.Update(0, false, true);
        Assert.Equal("A", display.Current.VisibleText);
        display.Update(delay - 1, false, false);
        Assert.Equal("A", display.Current.VisibleText);
        Assert.Equal(DialogueDisplayPhase.Typing, display.Current.Phase);
        display.Update(1, false, false);
        Assert.Equal("ABC", display.Current.VisibleText);
        Assert.Equal(DialogueDisplayPhase.WaitingForAdvance, display.Current.Phase);
        display.Update(100, false, false);
        Assert.Equal(DialogueDisplayPhase.WaitingForAdvance, display.Current.Phase);
    }

    [Fact]
    public void Fresh_skip_on_last_glyph_preserves_its_delay_and_does_not_confirm_the_wait()
    {
        var display = Create("AB"); display.Update(24, false, false);
        display.Update(0, false, true);
        Assert.Equal("AB", display.Current.VisibleText);
        Assert.Equal(DialogueDisplayPhase.Typing, display.Current.Phase);
        display.Update(23, false, false);
        Assert.Equal(DialogueDisplayPhase.Typing, display.Current.Phase);
        display.Update(1, false, false);
        Assert.Equal(DialogueDisplayPhase.WaitingForAdvance, display.Current.Phase);
        display.Update(100, false, false);
        Assert.Equal(DialogueDisplayPhase.WaitingForAdvance, display.Current.Phase);
    }

    [Fact]
    public void Late_update_applies_latched_skip_without_reusing_elapsed_for_the_new_wait()
    {
        var display = Create("ABC"); display.Update(0, false, false);
        display.Update(0, false, true);
        Assert.Equal("A", display.Current.VisibleText);
        display.Update(1000, false, false);
        Assert.Equal("ABC", display.Current.VisibleText);
        Assert.Equal(DialogueDisplayPhase.WaitingForAdvance, display.Current.Phase);
        Assert.Equal(0, display.Current.PaletteRotation);
        display.Update(99, true, false);
        Assert.Equal(DialogueDisplayPhase.WaitingForAdvance, display.Current.Phase);
        display.Update(1, false, false);
        Assert.Equal(DialogueDisplayPhase.Completed, display.Current.Phase);
    }

    private static DialogueDisplaySession Create(string text) => new(new DialogueMessageBlock(1, 1, [DialogueMessagePart.FromText(text)]), 7, 0);
}
