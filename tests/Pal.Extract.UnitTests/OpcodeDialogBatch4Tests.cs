using Pal.Extract;
using Xunit;
using static Pal.Extract.Tests.OpcodeCounterexampleHarness;

namespace Pal.Extract.Tests;

// M5 opcode batch 4 (#99): dialog presentation - redraw, the four setDialog variants,
// restoreScreen, backupScreen and showDialog. Two opcodes are new (0x008E restoreScreen and
// 0x00A6 backupScreen, both in script.c with no mapped operands), six are re-verified entries
// from earlier batches. The setDialog quartet and showDialog keep their frozen field mappings;
// 0x0005 redraw is corrected here: script.c:3295 reads the delay from operand 1 and the party
// gesture update from operand 2 (script.c:3287), while operand 0 is unused by the reference
// case, so the previous delayUnits-on-operand-0 binding was wrong. rngWord stays a full WORD:
// the reference tests the whole operand for zero (script.c:3397 `? TRUE : FALSE`), non-zero
// enabling the animated RGM.MKF face whose start-up backs up the screen for a later restore.
// Counter-examples follow the #78/#96 skeleton: edit a semantic field -> re-encoded bytes
// differ from the source bytes and equal the hand-computed value; unknown fields and
// out-of-range values are rejected.
public sealed class OpcodeDialogBatch4Tests
{
    // --- 0x0005 redraw (trigger switch, script.c:3269): clears the dialog, then either restores
    //     the screen backed up by an RNG dialog (PAL_DialogIsPlayingRNG, 3275-3277), redraws the
    //     battle scene (3279-3283), or rebuilds the scene - updating party gestures first when
    //     operand 2 is non-zero (3287-3289) - and delays operand1 x 60 ms with 0 read as 60 ms
    //     (3295, runtime normalization only, the codec keeps 0). ---
    [Fact]
    public void Redraw_edits_delay_units_and_gesture_flag_on_the_reference_operands()
    {
        byte[] original = Words(0x0005, 77, 2, 1);
        var command = Decode(0, 0x0005, 77, 2, 1);
        Assert.Equal("redraw", (string)command["op"]!);
        Assert.Equal(2, (int)command["delayUnits"]!);
        Assert.Equal(1, (int)command["updateGestures"]!);

        Assert.Equal(Words(0x0005, 77, 5, 1), EditAndReencode(command, "delayUnits", 5)); // operand 1, operand 0 preserved
        Assert.Equal(Words(0x0005, 77, 2, 0), EditAndReencode(Decode(0, 0x0005, 77, 2, 1), "updateGestures", 0));
        Assert.Equal(Words(0x0005, 77, 65_535, 1), EditAndReencode(Decode(0, 0x0005, 77, 2, 1), "delayUnits", 65_535)); // full WORD is legal

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0005, 77, 2, 1), "delayUnits", 1));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0005, 77, 2, 1), "frames", 3)); // unknown field
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0005, 77, 2, 1), "delayUnits", 65_536));
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0005, 77, 2, 1), "updateGestures", -1));
    }

    // --- 0x003B setDialog center (trigger switch, script.c:3391): PAL_StartDialog(kDialogCenter,
    //     (BYTE)operand0, 0, operand2 ? TRUE : FALSE) - color is operand 0's low byte, the portrait
    //     parameter is hard 0, and rngWord gates the animated face video. ---
    [Fact]
    public void SetDialog_center_edits_color_and_rng_word_and_preserves_the_high_byte()
    {
        byte[] original = Words(0x003B, 0x3112, 0, 0);
        var command = Decode(0, 0x003B, 0x3112, 0, 0);
        Assert.Equal("setDialog", (string)command["op"]!);
        Assert.Equal("center", (string)command["box"]!);
        Assert.Equal(0x12, (int)command["color"]!);
        Assert.Equal(0, (int)command["rngWord"]!);

        Assert.Equal(Words(0x003B, 0x31C8, 0, 0), EditAndReencode(command, "color", 200)); // low byte re-encoded, high byte 0x31 preserved
        Assert.Equal(Words(0x003B, 0x3112, 0, 1), EditAndReencode(Decode(0, 0x003B, 0x3112, 0, 0), "rngWord", 1));

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x003B, 0x3112, 0, 0), "rngWord", 65_535));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x003B, 0x3112, 0, 0), "color", 256));
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x003B, 0x3112, 0, 0), "color", -1));
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x003B, 0x3112, 0, 0), "box", "middle"));
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x003B, 0x3112, 0, 0), "portraitId", 5)); // center has no portrait operand
    }

    // --- 0x003C setDialog upper (trigger switch, script.c:3401): PAL_StartDialog(kDialogUpper,
    //     (BYTE)operand1, operand0, operand2 ? TRUE : FALSE) - portrait word, color low byte,
    //     rng word. The low-byte high-preservation counter-example for this opcode lives in
    //     OpcodeCounterexampleTests; this case pins the portrait and rng words. ---
    [Fact]
    public void SetDialog_upper_edits_portrait_and_rng_word()
    {
        byte[] original = Words(0x003C, 59, 0x4F79, 1);
        var command = Decode(0, 0x003C, 59, 0x4F79, 1);
        Assert.Equal("upper", (string)command["box"]!);
        Assert.Equal(59, (int)command["portraitId"]!);
        Assert.Equal(0x79, (int)command["color"]!);
        Assert.Equal(1, (int)command["rngWord"]!);

        Assert.Equal(Words(0x003C, 61, 0x4F79, 1), EditAndReencode(command, "portraitId", 61)); // RGM.MKF face chunk
        Assert.Equal(Words(0x003C, 59, 0x4F79, 0), EditAndReencode(Decode(0, 0x003C, 59, 0x4F79, 1), "rngWord", 0));

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x003C, 59, 0x4F79, 1), "portraitId", 60));

        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x003C, 59, 0x4F79, 1), "portraitId", 65_536));
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x003C, 59, 0x4F79, 1), "rngWord", 65_536));
    }

    // --- 0x003D setDialog lower (trigger switch, script.c:3411): same operand layout as 0x003C
    //     with kDialogLower. ---
    [Fact]
    public void SetDialog_lower_edits_portrait_color_and_rng_word()
    {
        byte[] original = Words(0x003D, 59, 79, 1);
        var command = Decode(0, 0x003D, 59, 79, 1);
        Assert.Equal("lower", (string)command["box"]!);
        Assert.Equal(59, (int)command["portraitId"]!);
        Assert.Equal(79, (int)command["color"]!);
        Assert.Equal(1, (int)command["rngWord"]!);

        Assert.Equal(Words(0x003D, 58, 79, 1), EditAndReencode(command, "portraitId", 58));
        Assert.Equal(Words(0x003D, 59, 200, 1), EditAndReencode(Decode(0, 0x003D, 59, 79, 1), "color", 200));
        Assert.Equal(Words(0x003D, 59, 79, 0), EditAndReencode(Decode(0, 0x003D, 59, 79, 1), "rngWord", 0));

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x003D, 59, 79, 1), "color", 78));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x003D, 59, 79, 1), "box", "upper")); // wrong constant
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x003D, 59, 79, 1), "color", 256));
    }

    // --- 0x003E setDialog centerWindow (trigger switch, script.c:3421): PAL_StartDialog(
    //     kDialogCenterWindow, (BYTE)operand0, 0, FALSE) - color only, no portrait and no RNG;
    //     operands 1 and 2 keep their source WORDs. ---
    [Fact]
    public void SetDialog_centerWindow_edits_color_only_and_preserves_other_operands()
    {
        byte[] original = Words(0x003E, 0x2107, 9, 8);
        var command = Decode(0, 0x003E, 0x2107, 9, 8);
        Assert.Equal("centerWindow", (string)command["box"]!);
        Assert.Equal(7, (int)command["color"]!);

        Assert.Equal(Words(0x003E, 0x21C8, 9, 8), EditAndReencode(command, "color", 200)); // operands 1 and 2 preserved

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x003E, 0x2107, 9, 8), "color", 6));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x003E, 0x2107, 9, 8), "rngWord", 1)); // not mapped here
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x003E, 0x2107, 9, 8), "portraitId", 1));
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x003E, 0x2107, 9, 8), "color", -1));
    }

    // --- 0x008E restoreScreen (trigger switch only, script.c:3430): PAL_ClearDialog(TRUE) +
    //     VIDEO_RestoreScreen(gpScreen) + VIDEO_UpdateScreen(NULL) then wScriptEntry++. No
    //     operands are mapped; auto would reach TerminateOnError. Inside a message block the
    //     span scanner (MESSAGE_GetSpan, script.c:3125) also consumes 0x008E entries. ---
    [Fact]
    public void RestoreScreen_round_trips_preserved_operands_and_rejects_unknown_fields()
    {
        byte[] original = Words(0x008E, 5, 6, 7);
        var command = Decode(0, 0x008E, 5, 6, 7);
        Assert.Equal("restoreScreen", (string)command["op"]!);

        Assert.Equal(original, Reencode(command)); // no mapped fields: byte-exact

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x008E, 5, 6, 7), "to", "L0001"));
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x008E, 5, 6, 7), "color", 1));
    }

    // --- 0x00A6 backupScreen (shared PAL_InterpretInstruction, script.c:3071):
    //     VIDEO_BackupScreen(gpScreen) then the common return+1 (3085); no operands are mapped,
    //     both executors reach it through the default delegation. ---
    [Fact]
    public void BackupScreen_round_trips_preserved_operands_and_rejects_unknown_fields()
    {
        byte[] original = Words(0x00A6, 5, 6, 7);
        var command = Decode(0, 0x00A6, 5, 6, 7);
        Assert.Equal("backupScreen", (string)command["op"]!);

        Assert.Equal(original, Reencode(command)); // no mapped fields: byte-exact

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x00A6, 5, 6, 7), "to", "L0001"));
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x00A6, 5, 6, 7), "frames", 1));
    }

    // --- 0xFFFF showDialog (trigger script.c:3440 / auto script.c:3609, behavior differs):
    //     messageId is operand 0; descriptionLineWord is operand 1, unused by the dialog display
    //     but read by the WIN95 item/spell menu callers to advance the description line
    //     (itemmenu.c:275 / magicmenu.c:197: operand1 != 1 advances one line). With a message
    //     file the trigger successor advances by MESSAGE_GetSpan (3446), skipping the whole
    //     contiguous message block; message id 0 inside the loop restores the screen (3454-3457). ---
    [Fact]
    public void ShowDialog_edits_message_id_and_description_line_and_preserves_operand2()
    {
        byte[] original = Words(0xFFFF, 101, 17, 7);
        var command = Decode(0, 0xFFFF, 101, 17, 7);
        Assert.Equal("showDialog", (string)command["op"]!);
        Assert.Equal(101, (int)command["messageId"]!);
        Assert.Equal(17, (int)command["descriptionLineWord"]!);

        Assert.Equal(Words(0xFFFF, 102, 17, 7), EditAndReencode(command, "messageId", 102)); // raw message reference, operand 2 preserved
        Assert.Equal(Words(0xFFFF, 101, 1, 7), EditAndReencode(Decode(0, 0xFFFF, 101, 17, 7), "descriptionLineWord", 1));
        Assert.Equal(Words(0xFFFF, 0, 17, 7), EditAndReencode(Decode(0, 0xFFFF, 101, 17, 7), "messageId", 0)); // 0 stays 0 (screen restore message)

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0xFFFF, 101, 17, 7), "descriptionLineWord", 2));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0xFFFF, 101, 17, 7), "text", "hello")); // text lives in the language pack
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0xFFFF, 101, 17, 7), "box", "upper"));
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0xFFFF, 101, 17, 7), "messageId", 65_536));
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0xFFFF, 101, 17, 7), "descriptionLineWord", 65_536));
    }
}
