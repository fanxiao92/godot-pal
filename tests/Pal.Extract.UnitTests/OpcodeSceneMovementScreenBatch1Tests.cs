using System.Buffers.Binary;
using System.Text.Json.Nodes;
using Pal.Extract;
using Xunit;

namespace Pal.Extract.Tests;

// M5 opcode scene-movement screen batch 1 (#110, the first half of the 19-opcode
// screen subdomain, 10 opcodes) - shakeScreen 0x0035, setPlayingRng 0x0036,
// playRng 0x0037, fadeToRed 0x004F, fadeOut 0x0050, fadeIn 0x0051,
// useDayPalette 0x0053, useNightPalette 0x0054, waveScreen 0x0071,
// fadeScene 0x0073.
// Topology per script.c: all ten opcode bits live in the shared PAL_InterpretInstruction
// switch (trigger switch 3206-3440 and auto switch 3522-3641 have no case for any of
// them, each case appears exactly once) and every successor is sequential (plain break
// into the script.c:3085 tail return wScriptEntry + 1; the blocking callees
// PAL_RNGPlay/PAL_FadeOut/PAL_FadeIn/VIDEO_FadeScreen complete synchronously inside
// the case like the #107 partyWalkTo precedent - no stall, no jump).
// Semantics per the reference cases:
//   - 0x0035 shakeScreen: VIDEO_ShakeScreen(times, level) (script.c:1521-1534,
//     video.c:1029-1053) - the g_wShakeTime countdown decrements once per
//     VIDEO_UpdateScreen blit (video.c:571-615); level WORD 0 -> 4 and times WORD 0 ->
//     one immediate clean redraw are runtime-only normalizations (encoding stays 0).
//   - 0x0036 setPlayingRng: gpGlobals->iCurPlayingRNG = operand0 (script.c:1537-1541)
//     - the rng.mkf segment number playRng/ending later play.
//   - 0x0037 playRng: PAL_RNGPlay(current, startFrame, endFrame ?: -1, speed ?: 16)
//     (script.c:1544-1553) - endFrame WORD 0 = play until the end, speed WORD 0 = 16,
//     both runtime-only normalizations.
//   - 0x004F fadeToRed (#78-named, #110 re-verified, mapping holds): PAL_FadeToRed()
//     takes no parameters (palette.c:595-667).
//   - 0x0050 fadeOut (#78-named, #110 review correction frames -> delay): the operand
//     is PAL_FadeOut's iDelay (script.c:1775-1782, palette.c:123-190), the palette
//     fade-to-black pacing unit (duration = delay * 600 ms in 10 ms steps), not a
//     frame count; WORD 0 -> 1 at runtime only (real data's majority form).
//   - 0x0051 fadeIn: PAL_FadeIn(wNumPalette, fNightPalette, ((SHORT)operand0 > 0) ?
//     operand0 : 1) (script.c:1784-1792) - WORD 0 and the 0xFFFF form (SHORT -1) both
//     normalize to 1 at runtime while the encoding keeps the raw unsigned WORD
//     (real data: 0 and 0xFFFF only).
//   - 0x0053/0x0054 useDayPalette/useNightPalette: gpGlobals->fNightPalette = FALSE/
//     TRUE (script.c:1802-1813) - PAL_GetPalette reads the first/second half of each
//     pat.mkf palette chunk (palette.c:79-81); no operands.
//   - 0x0071 waveScreen: wScreenWave = operand0 (the level, amplitude level/256,
//     scene.c:415, stopping at 0 or >= 256, scene.c:389-397) and sWaveProgression =
//     (SHORT)operand1 (the signed per-update level increment, scene.c:389; real data
//     carries -4 = 0xFFFC, the signedWord evidence).
//   - 0x0073 fadeScene (#78-named, #110 review correction mode -> speed): the operand
//     is VIDEO_FadeScreen's wSpeed (script.c:2140-2146, video.c:1129-1290) - the
//     backup-screen-to-scene cross-fade pacing (larger = slower), not a mode selector.
// Signedness: waveScreen.progression is the batch's only SignedWord field (the
// (SHORT) cast at script.c:2137); every other field is an unsigned WORD.
// Counter-examples follow the #78/#96 skeleton: edit a semantic field ->
// re-encoded bytes differ from the source bytes and equal the hand-computed value;
// unknown fields and out-of-range values are rejected.
public sealed class OpcodeSceneMovementScreenBatch1Tests
{
    private const int ChunkCount = 8;

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> Labels =
        new Dictionary<string, IReadOnlyDictionary<string, int>>
        {
            ["shared"] = Enumerable.Range(0, ChunkCount).ToDictionary(OpcodeRegistry.LabelAt, address => address),
        };

    private static byte[] Words(params int[] values)
    {
        var bytes = new byte[values.Length * 2];
        for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), unchecked((ushort)values[i]));
        return bytes;
    }

    private static JsonObject Decode(int index, params int[] instruction)
    {
        ushort opcode = unchecked((ushort)instruction[0]);
        ushort a = unchecked((ushort)instruction[1]), b = unchecked((ushort)instruction[2]), c = unchecked((ushort)instruction[3]);
        return OpcodeRegistry.DecodeCommand(index, ChunkCount, opcode, a, b, c);
    }

    private static byte[] Reencode(JsonObject command)
    {
        OpcodeRegistry.ValidateCommand(command, "shared", ChunkCount);
        var bytes = new byte[8];
        OpcodeRegistry.EncodeCommand(command, "shared", Labels, bytes);
        return bytes;
    }

    private static byte[] EditAndReencode(JsonObject command, string field, JsonNode? value)
    {
        command[field] = value;
        return Reencode(command);
    }

    // --- 0x0035 shakeScreen (new, shared, script.c:1521-1534): "Shake the screen" -
    //     VIDEO_ShakeScreen(times, level) (video.c:1029-1053) sets the g_wShakeTime
    //     countdown that decrements once per VIDEO_UpdateScreen blit, alternating the
    //     image up/down by the level's scanline offset until zero (video.c:571-615);
    //     level WORD 0 -> 4 (script.c:1523-1526) and times WORD 0 -> one immediate
    //     clean redraw instead of a countdown (script.c:1531-1533) are runtime-only.
    //     Real data: 23 hits; times 0x5/10/14/20/24/28/30/72/90x2/99x7/999x2; level
    //     0x13/1x4/2x2/3/4x2/5; operand2 all zero. ---
    [Fact]
    public void ShakeScreen_edits_times_and_level()
    {
        byte[] original = Words(0x0035, 99, 1, 0); // the real-data @60F2 form
        var command = Decode(0, 0x0035, 99, 1, 0);
        Assert.Equal("shakeScreen", (string)command["op"]!);
        Assert.Equal(99, (int)command["times"]!);
        Assert.Equal(1, (int)command["level"]!);

        Assert.Equal(original, Reencode(command)); // both mapped WORDs re-encode byte-exact
        Assert.Equal(Words(0x0035, 999, 1, 0), EditAndReencode(Decode(0, 0x0035, 99, 1, 0), "times", 999)); // the real-data @613F form's times
        Assert.Equal(Words(0x0035, 99, 4, 0), EditAndReencode(Decode(0, 0x0035, 99, 1, 0), "level", 4)); // the real-data @613F form's level
        Assert.Equal(Words(0x0035, 28, 4, 0), Reencode(Decode(0, 0x0035, 28, 4, 0))); // the real-data @7D9B form as a whole
        Assert.Equal(Words(0x0035, 99, 0, 0), EditAndReencode(Decode(0, 0x0035, 99, 1, 0), "level", 0)); // the real-data @61C4 level-0 form (runtime 4, encodes 0)
        Assert.Equal(Words(0x0035, 0, 0, 0), Reencode(Decode(0, 0x0035, 0, 0, 0))); // the real-data @2E5E WORD-0 stop/redraw form as a whole
        Assert.Equal(Words(0x0035, 0, 1, 0), EditAndReencode(Decode(0, 0x0035, 99, 1, 0), "times", 0)); // times WORD 0 on the level-1 form (runtime redraw, encodes 0)
        Assert.Equal(Words(0x0035, 90, 1, 0), EditAndReencode(Decode(0, 0x0035, 99, 1, 0), "times", 90)); // the real-data times-90 form keeps the level
        Assert.Equal(Words(0x0035, 65535, 5, 0), Reencode(Decode(0, 0x0035, unchecked((int)0xFFFF), 5, 0))); // full unsigned WORD top

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0035, 99, 1, 0), "times", 90));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0035, 99, 1, 0), "frames", 99)); // unknown field: the hide/waitUpdate countdown name, the shake ticks per screen update not game frame
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0035, 99, 1, 0), "shakeTime", 99)); // unknown field (the callee parameter's own name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0035, 99, 1, 0), "shakeLevel", 1)); // unknown field name
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0035, 99, 1, 0), "amplitude", 1)); // unknown field name
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0035, 99, 1, 0), "times", 65_536)); // times stays unsigned
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0035, 99, 1, 0), "level", 65_536)); // level stays unsigned
    }

    // --- 0x0036 setPlayingRng (new, shared, script.c:1537-1541): "Set the current
    //     playing RNG animation" - gpGlobals->iCurPlayingRNG = operand0 (global.h:509);
    //     the rng.mkf segment number that playRng 0x0037 and the ending then play
    //     (PAL_RNGPlay's iNumRNG reads rng.mkf chunk iNumRNG, rngplay.c:372-449).
    //     Real data: 9 hits, one per rngId 0/1/2/3/4/5/7/8/9; operands 1/2 all zero. ---
    [Fact]
    public void SetPlayingRng_edits_rng_segment_and_keeps_preserved_operands()
    {
        byte[] original = Words(0x0036, 1, 0, 0); // the real-data @08BC form
        var command = Decode(0, 0x0036, 1, 0, 0);
        Assert.Equal("setPlayingRng", (string)command["op"]!);
        Assert.NotEqual("playRng", (string)command["op"]!); // the selection keeps its own verb, no playback happens
        Assert.Equal(1, (int)command["rngId"]!);

        Assert.Equal(original, Reencode(command)); // the mapped WORD and the preserved pair re-encode byte-exact
        Assert.Equal(Words(0x0036, 0, 0, 0), EditAndReencode(Decode(0, 0x0036, 1, 0, 0), "rngId", 0)); // the real-data @2F8B form (rng.mkf segment 0)
        Assert.Equal(Words(0x0036, 9, 0, 0), EditAndReencode(Decode(0, 0x0036, 1, 0, 0), "rngId", 9)); // the real-data @8AF8 top form
        Assert.Equal(Words(0x0036, 3, 0, 0), EditAndReencode(Decode(0, 0x0036, 1, 0, 0), "rngId", 3)); // the real-data @565E form
        Assert.Equal(Words(0x0036, 65535, 0, 0), EditAndReencode(Decode(0, 0x0036, 1, 0, 0), "rngId", 65_535)); // full unsigned WORD (no retail bound at the encoding layer)

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0036, 1, 0, 0), "rngId", 9));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0036, 1, 0, 0), "rngNum", 1)); // unknown field (the callee parameter's own name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0036, 1, 0, 0), "movieId", 1)); // unknown field name
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0036, 1, 0, 0), "animationId", 1)); // unknown field name
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0036, 1, 0, 0), "startFrame", 0)); // unknown field: playRng's operand, the selection reads no frame
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0036, 1, 0, 0), "rngId", 65_536));
    }

    // --- 0x0037 playRng (new, shared, script.c:1544-1553): "Play RNG animation" -
    //     PAL_RNGPlay(current rng, startFrame, endFrame > 0 ? endFrame : -1, speed > 0
    //     ? speed : 16) - the movie comes from setPlayingRng's selection, not an
    //     operand; endFrame WORD 0 = play until the end (-1) and speed WORD 0 = 16 are
    //     runtime-only normalizations (encodings stay 0); PAL_RNGPlay blocks until the
    //     last frame (rngplay.c:409-434). Real data: 20 hits; startFrame 0x9/1/17/22/
    //     55/60/75/113/163/187/196/267; endFrame 0x8/1/16/21/54/59/74/109/112/162/186/
    //     195/266; speed 0/6/7/8x9/9/14/16x6. ---
    [Fact]
    public void PlayRng_edits_start_end_frames_and_speed()
    {
        byte[] original = Words(0x0037, 113, 162, 16); // the real-data @08C0 form
        var command = Decode(0, 0x0037, 113, 162, 16);
        Assert.Equal("playRng", (string)command["op"]!);
        Assert.Equal(113, (int)command["startFrame"]!);
        Assert.Equal(162, (int)command["endFrame"]!);
        Assert.Equal(16, (int)command["speed"]!);

        Assert.Equal(original, Reencode(command)); // all three mapped WORDs re-encode byte-exact
        Assert.Equal(Words(0x0037, 0, 112, 16), Reencode(Decode(0, 0x0037, 0, 112, 16))); // the real-data @08BE form as a whole
        Assert.Equal(Words(0x0037, 0, 162, 16), EditAndReencode(Decode(0, 0x0037, 113, 162, 16), "startFrame", 0)); // startFrame WORD 0 keeps the rest of the form
        Assert.Equal(Words(0x0037, 22, 0, 9), Reencode(Decode(0, 0x0037, 22, 0, 9))); // the real-data @61C8 form: endFrame 0 (play until the end) as a whole
        Assert.Equal(Words(0x0037, 113, 0, 16), EditAndReencode(Decode(0, 0x0037, 113, 162, 16), "endFrame", 0)); // endFrame WORD 0 still encodes 0 (runtime -1)
        Assert.Equal(Words(0x0037, 0, 0, 14), Reencode(Decode(0, 0x0037, 0, 0, 14))); // the real-data @2F8F form: both default forms together
        Assert.Equal(Words(0x0037, 113, 162, 0), EditAndReencode(Decode(0, 0x0037, 113, 162, 16), "speed", 0)); // speed WORD 0 still encodes 0 (runtime 16)
        Assert.Equal(Words(0x0037, 113, 162, 8), EditAndReencode(Decode(0, 0x0037, 113, 162, 16), "speed", 8)); // the real-data majority speed
        Assert.Equal(Words(0x0037, 267, 162, 16), EditAndReencode(Decode(0, 0x0037, 113, 162, 16), "startFrame", 267)); // the real-data start-frame top
        Assert.Equal(Words(0x0037, 113, 266, 16), EditAndReencode(Decode(0, 0x0037, 113, 162, 16), "endFrame", 266)); // the real-data end-frame top
        Assert.Equal(Words(0x0037, 113, 162, 65535), EditAndReencode(Decode(0, 0x0037, 113, 162, 16), "speed", 65_535)); // full unsigned WORD top

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0037, 113, 162, 16), "speed", 0));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0037, 113, 162, 16), "rngId", 1)); // unknown field: the movie comes from the setPlayingRng selection, not an operand
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0037, 113, 162, 16), "frames", 113)); // unknown field: the range is a frame pair, not one count
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0037, 113, 162, 16), "frame", 113)); // unknown field (setObjectGesture's name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0037, 113, 162, 16), "delay", 16)); // unknown field: the pacing is a speed (larger = slower here), not fadeOut's delay
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0037, 113, 162, 16), "startFrame", 65_536));
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0037, 113, 162, 16), "endFrame", 65_536));
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0037, 113, 162, 16), "speed", 65_536));
    }

    // --- 0x004F fadeToRed (#78-named, #110 re-verified, mapping holds, shared,
    //     script.c:1768-1773): "Fade the screen to red color (game over)" -
    //     PAL_FadeToRed() takes no parameters (palette.c:595-667: blends the current
    //     palette toward a red-dominant form over up to 32 steps, skipping color 0x4F
    //     so dialog text stays readable); the case reads no operand. Real data: 1 hit,
    //     all three operands zero. ---
    [Fact]
    public void FadeToRed_reads_no_operands_and_preserves_source_words()
    {
        byte[] original = Words(0x004F, 0, 0, 0); // the real-data @A074 form
        var command = Decode(0, 0x004F, 0, 0, 0);
        Assert.Equal("fadeToRed", (string)command["op"]!);
        Assert.Null(command["delay"]); // the fade is parameterless - no mapped field exists

        Assert.Equal(original, Reencode(command)); // the preserved triple re-encodes byte-exact

        // Every operand field name is rejected: the case reads no operand.
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x004F, 0, 0, 0), "delay", 1));
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x004F, 0, 0, 0), "frames", 1));
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x004F, 0, 0, 0), "speed", 1));
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x004F, 0, 0, 0), "steps", 32)); // the callee's fixed 32 steps are not an operand
    }

    // --- 0x0050 fadeOut (#78-named, #110 review correction frames -> delay, shared,
    //     script.c:1775-1782): "screen fade out" - PAL_FadeOut(iDelay) fades the
    //     palette to black (palette.c:123-190, total duration = delay * 600 ms in
    //     fixed 10 ms steps) and sets fNeedToFadeIn = TRUE; WORD 0 -> 1 at runtime
    //     only (encoding stays 0 - real data's majority form). The #78 name "frames"
    //     misread the pacing unit as a frame count. Real data: 908 hits; delay
    //     0x874/2x19/3x10/4x3/5/6; operands 1/2 all zero. ---
    [Fact]
    public void FadeOut_edits_delay_and_rejects_the_corrected_away_name()
    {
        byte[] original = Words(0x0050, 2, 0, 0); // the real-data @0856 form
        var command = Decode(0, 0x0050, 2, 0, 0);
        Assert.Equal("fadeOut", (string)command["op"]!);
        Assert.Equal(2, (int)command["delay"]!);
        Assert.Null(command["frames"]); // the #78 field name is corrected away

        Assert.Equal(original, Reencode(command)); // the mapped WORD and the preserved pair re-encode byte-exact
        Assert.Equal(Words(0x0050, 0, 0, 0), EditAndReencode(Decode(0, 0x0050, 2, 0, 0), "delay", 0)); // the real-data majority WORD-0 form (runtime 1, encodes 0)
        Assert.Equal(Words(0x0050, 3, 0, 0), EditAndReencode(Decode(0, 0x0050, 2, 0, 0), "delay", 3)); // the real-data @1205 form
        Assert.Equal(Words(0x0050, 5, 0, 0), EditAndReencode(Decode(0, 0x0050, 2, 0, 0), "delay", 5)); // the real-data @55F0 form
        Assert.Equal(Words(0x0050, 6, 0, 0), EditAndReencode(Decode(0, 0x0050, 2, 0, 0), "delay", 6)); // the real-data top form
        Assert.Equal(Words(0x0050, 65535, 0, 0), EditAndReencode(Decode(0, 0x0050, 2, 0, 0), "delay", 65_535)); // full unsigned WORD top

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0050, 2, 0, 0), "delay", 0));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0050, 2, 0, 0), "frames", 2)); // unknown field: the #78 name corrected away by #110
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0050, 2, 0, 0), "speed", 2)); // unknown field: VIDEO_FadeScreen's word - fadeOut paces PAL_FadeOut's delay
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0050, 2, 0, 0), "paletteId", 2)); // unknown field: the palette is the current globals', not an operand
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0050, 2, 0, 0), "delay", 65_536));
    }

    // --- 0x0051 fadeIn (new, shared, script.c:1784-1792): "screen fade in" -
    //     PAL_FadeIn(wNumPalette, fNightPalette, ((SHORT)operand0 > 0) ? operand0 : 1)
    //     fades back into the current globals' palette and clears fNeedToFadeIn; the
    //     gate makes WORD 0 and the 0xFFFF form (SHORT -1) both fall back to 1 at
    //     runtime while the encoding keeps the raw unsigned WORD. Real data: 4 hits;
    //     delay 0x2 (the default form) and 0xFFFFx2 (the SHORT -1 form). ---
    [Fact]
    public void FadeIn_edits_delay_and_roundtrips_the_default_forms()
    {
        byte[] original = Words(0x0051, unchecked((int)0xFFFF), 0, 0); // the real-data @0B56 form
        var command = Decode(0, 0x0051, unchecked((int)0xFFFF), 0, 0);
        Assert.Equal("fadeIn", (string)command["op"]!);
        Assert.NotEqual("fadeOut", (string)command["op"]!); // the opposite direction keeps its own verb
        Assert.Equal(65_535, (int)command["delay"]!); // the (SHORT)-1 form round-trips as the raw unsigned WORD

        Assert.Equal(original, Reencode(command)); // the mapped WORD and the preserved pair re-encode byte-exact
        Assert.Equal(Words(0x0051, 0, 0, 0), EditAndReencode(Decode(0, 0x0051, unchecked((int)0xFFFF), 0, 0), "delay", 0)); // the real-data @1288 WORD-0 form (also normalizes to 1)
        Assert.Equal(Words(0x0051, 1, 0, 0), EditAndReencode(Decode(0, 0x0051, unchecked((int)0xFFFF), 0, 0), "delay", 1)); // the fastest explicit step
        Assert.Equal(Words(0x0051, 32767, 0, 0), EditAndReencode(Decode(0, 0x0051, unchecked((int)0xFFFF), 0, 0), "delay", 32_767)); // the (SHORT)>0 gate's positive top
        Assert.Equal(Words(0x0051, 65535, 0, 0), EditAndReencode(Decode(0, 0x0051, 0, 0, 0), "delay", 65_535)); // full unsigned WORD re-encodes the SHORT -1 form

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0051, unchecked((int)0xFFFF), 0, 0), "delay", 0));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0051, unchecked((int)0xFFFF), 0, 0), "frames", 1)); // unknown field: fadeOut's corrected-away name, not this verb's either
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0051, unchecked((int)0xFFFF), 0, 0), "speed", 1)); // unknown field: VIDEO_FadeScreen's word
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0051, unchecked((int)0xFFFF), 0, 0), "paletteId", 1)); // unknown field: the palette comes from wNumPalette/fNightPalette globals
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0051, unchecked((int)0xFFFF), 0, 0), "night", true)); // unknown field: the flag is a separate verb pair (0x0053/0x0054)
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0051, unchecked((int)0xFFFF), 0, 0), "delay", -1)); // the 0xFFFF form encodes as the unsigned WORD, not a signed edit
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0051, unchecked((int)0xFFFF), 0, 0), "delay", 65_536));
    }

    // --- 0x0053 useDayPalette (new, shared, script.c:1802-1806): "use the day
    //     palette" - gpGlobals->fNightPalette = FALSE (global.h:533); PAL_GetPalette
    //     then reads the first half of each pat.mkf palette chunk (palette.c:79-81);
    //     the case reads no operand. Real data: 10 hits, all three operands zero. ---
    [Fact]
    public void UseDayPalette_reads_no_operands_and_preserves_source_words()
    {
        byte[] original = Words(0x0053, 0, 0, 0); // the real-data @08CD form (all 10 hits share it)
        var command = Decode(0, 0x0053, 0, 0, 0);
        Assert.Equal("useDayPalette", (string)command["op"]!);
        Assert.NotEqual("useNightPalette", (string)command["op"]!); // the day/night pair are two verbs, not a boolean field

        Assert.Equal(original, Reencode(command)); // the preserved triple re-encodes byte-exact

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0053, 0, 0, 0), "night", false)); // unknown field: the flag is the verb pair itself
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0053, 0, 0, 0), "paletteId", 5)); // unknown field: the palette number stays wNumPalette
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0053, 0, 0, 0), "nightWord", 0)); // unknown field name
    }

    // --- 0x0054 useNightPalette (new, shared, script.c:1809-1813): "use the night
    //     palette" - gpGlobals->fNightPalette = TRUE; PAL_GetPalette reads the second
    //     half of each pat.mkf palette chunk (the darker night colors, palette.c:79-81);
    //     the case reads no operand. Real data: 7 hits, all three operands zero. ---
    [Fact]
    public void UseNightPalette_reads_no_operands_and_preserves_source_words()
    {
        byte[] original = Words(0x0054, 0, 0, 0); // the real-data @06C7 form (all 7 hits share it)
        var command = Decode(0, 0x0054, 0, 0, 0);
        Assert.Equal("useNightPalette", (string)command["op"]!);

        Assert.Equal(original, Reencode(command)); // the preserved triple re-encodes byte-exact

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0054, 0, 0, 0), "night", true)); // unknown field: the flag is the verb pair itself
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0054, 0, 0, 0), "paletteId", 5)); // unknown field: the palette number stays wNumPalette
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0054, 0, 0, 0), "useDay", false)); // unknown field name
    }

    // --- 0x0071 waveScreen (new, shared, script.c:2132-2138): "Wave the screen" -
    //     wScreenWave = operand0 (global.h:379 "level of screen waving"; the wave
    //     amplitude scales as level/256 per column, scene.c:415, and the effect stops
    //     once the level reaches 0 or >= 256, scene.c:389-397) and sWaveProgression =
    //     (SHORT)operand1 (global.h:539; wScreenWave += sWaveProgression per update,
    //     scene.c:389). Real data: 11 hits; level 0x3/1/2x6/255; progression
    //     -4 (0xFFFC)x1/0x9/2x1; operand2 all zero. ---
    [Fact]
    public void WaveScreen_edits_level_and_signed_progression()
    {
        byte[] original = Words(0x0071, 255, unchecked((int)0xFFFC), 0); // the real-data @7ECA form: top level just below the 256 cutoff, negative progression
        var command = Decode(0, 0x0071, 255, unchecked((int)0xFFFC), 0);
        Assert.Equal("waveScreen", (string)command["op"]!);
        Assert.Equal(255, (int)command["level"]!);
        Assert.Equal(-4, (int)command["progression"]!); // the (SHORT) cast decodes 0xFFFC as -4

        Assert.Equal(original, Reencode(command)); // both mapped WORDs re-encode byte-exact
        Assert.Equal(Words(0x0071, 2, 0, 0), Reencode(Decode(0, 0x0071, 2, 0, 0))); // the real-data @2F98 majority form as a whole
        Assert.Equal(Words(0x0071, 2, unchecked((int)0xFFFC), 0), EditAndReencode(Decode(0, 0x0071, 255, unchecked((int)0xFFFC), 0), "level", 2)); // level 2 keeps the negative progression
        Assert.Equal(Words(0x0071, 1, 2, 0), Reencode(Decode(0, 0x0071, 1, 2, 0))); // the real-data @8870 form as a whole
        Assert.Equal(Words(0x0071, 0, unchecked((int)0xFFFC), 0), EditAndReencode(Decode(0, 0x0071, 255, unchecked((int)0xFFFC), 0), "level", 0)); // level 0 (stop) keeps the negative progression
        Assert.Equal(Words(0x0071, 0, 0, 0), Reencode(Decode(0, 0x0071, 0, 0, 0))); // the real-data @2E5F stop form as a whole
        Assert.Equal(Words(0x0071, 255, 0, 0), EditAndReencode(Decode(0, 0x0071, 255, unchecked((int)0xFFFC), 0), "progression", 0)); // the majority progression-0 form (constant level)
        Assert.Equal(Words(0x0071, 255, 2, 0), EditAndReencode(Decode(0, 0x0071, 255, unchecked((int)0xFFFC), 0), "progression", 2)); // the positive real form
        Assert.Equal(Words(0x0071, 255, unchecked((int)0xFFFC), 0), EditAndReencode(Decode(0, 0x0071, 255, 0, 0), "progression", -4)); // the negative form re-encodes two's-complement
        Assert.Equal(-4, (int)Decode(0, 0x0071, 255, unchecked((int)0xFFFC), 0)["progression"]!); // and decodes back through the SHORT cast
        Assert.Equal(Words(0x0071, 255, unchecked((int)0x8000), 0), EditAndReencode(Decode(0, 0x0071, 255, 0, 0), "progression", -32_768)); // signed WORD bottom
        Assert.Equal(Words(0x0071, 255, 32_767, 0), EditAndReencode(Decode(0, 0x0071, 255, 0, 0), "progression", 32_767)); // signed WORD top
        Assert.Equal(Words(0x0071, 65535, 2, 0), EditAndReencode(Decode(0, 0x0071, 255, 2, 0), "level", 65_535)); // full unsigned WORD (>= 256 is the runtime's stop, encoding keeps it)

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0071, 255, unchecked((int)0xFFFC), 0), "level", 2));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0071, 255, unchecked((int)0xFFFC), 0), "wave", 255)); // unknown field name
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0071, 255, unchecked((int)0xFFFC), 0), "waveLevel", 255)); // unknown field (the global's own name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0071, 255, unchecked((int)0xFFFC), 0), "amplitude", 255)); // unknown field: the consumption is the level/256 scale
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0071, 255, unchecked((int)0xFFFC), 0), "sWaveProgression", -4)); // unknown field (the struct member's name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0071, 255, 0, 0), "progression", -32_769)); // beyond signed WORD
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0071, 255, 0, 0), "progression", 32_768)); // beyond signed WORD
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0071, 255, 0, 0), "level", 65_536)); // level stays unsigned
    }

    // --- 0x0073 fadeScene (#78-named, #110 review correction mode -> speed, shared,
    //     script.c:2140-2146): "Fade the screen to scene" - VIDEO_BackupScreen +
    //     PAL_MakeScene rebuild the scene, then VIDEO_FadeScreen(wSpeed) cross-fades
    //     from the backup buffer to it (video.c:1129-1290, "the larger value, the
    //     slower", 12 passes of (speed + 1) * 10 ms) - a pacing unit, not a mode
    //     selector. Real data: 69 hits; speed 0x28/2x21/3x6/4x7/5x3/6x3/7x1;
    //     operand1 0x60/54/60x2/70/72x4/80 preserved; operand2 all zero. ---
    [Fact]
    public void FadeScene_edits_speed_and_rejects_the_corrected_away_name()
    {
        byte[] original = Words(0x0073, 7, 54, 0); // the real-data @55D9 form with the preserved operand1
        var command = Decode(0, 0x0073, 7, 54, 0);
        Assert.Equal("fadeScene", (string)command["op"]!);
        Assert.Equal(7, (int)command["speed"]!);
        Assert.Null(command["mode"]); // the #78 field name is corrected away

        Assert.Equal(original, Reencode(command)); // the mapped WORD and the preserved pair re-encode byte-exact
        Assert.Equal(Words(0x0073, 0, 0, 0), Reencode(Decode(0, 0x0073, 0, 0, 0))); // the real-data majority @0295 WORD-0 form as a whole
        Assert.Equal(Words(0x0073, 0, 54, 0), EditAndReencode(Decode(0, 0x0073, 7, 54, 0), "speed", 0)); // speed WORD 0 keeps the preserved operand1 (runtime 1 x 10 ms per step)
        Assert.Equal(Words(0x0073, 2, 54, 0), EditAndReencode(Decode(0, 0x0073, 7, 54, 0), "speed", 2)); // the real-data @08EA speed keeps the preserved operand1
        Assert.Equal(Words(0x0073, 2, 0, 0), Reencode(Decode(0, 0x0073, 2, 0, 0))); // the real-data @08EA form as a whole
        Assert.Equal(Words(0x0073, 3, 70, 0), Reencode(Decode(0, 0x0073, 3, 70, 0))); // the real-data @570A form as a whole
        Assert.Equal(Words(0x0073, 6, 54, 0), EditAndReencode(Decode(0, 0x0073, 7, 54, 0), "speed", 6)); // the real-data @6078 speed keeps the preserved operand1
        Assert.Equal(Words(0x0073, 6, 0, 0), Reencode(Decode(0, 0x0073, 6, 0, 0))); // the real-data @6078 form as a whole
        Assert.Equal(Words(0x0073, 65535, 54, 0), EditAndReencode(Decode(0, 0x0073, 7, 54, 0), "speed", 65_535)); // full unsigned WORD top

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0073, 7, 54, 0), "speed", 2));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0073, 7, 54, 0), "mode", 7)); // unknown field: the #78 name corrected away by #110
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0073, 7, 54, 0), "delay", 7)); // unknown field: fadeOut/fadeIn's PAL palette word - fadeScene paces VIDEO_FadeScreen's speed
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0073, 7, 54, 0), "frames", 7)); // unknown field: no frame count is read
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0073, 7, 54, 0), "sceneId", 7)); // unknown field: the scene is rebuilt from the current globals, not an operand
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0073, 7, 54, 0), "speed", 65_536));
    }
}
