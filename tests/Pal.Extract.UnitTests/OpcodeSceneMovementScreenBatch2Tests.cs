using System.Buffers.Binary;
using System.Text.Json.Nodes;
using Pal.Extract;
using Xunit;

namespace Pal.Extract.Tests;

// M5 opcode scene-movement screen batch 2 (#111, the closing half of the
// 19-opcode screen subdomain, 9 opcodes) - showFbp 0x0076,
// toggleDayNightPalette 0x0080, setPalette 0x008B, colorFade 0x008C,
// sceneFade 0x0093, showEndingAnimation 0x0096, fadeToScene 0x009B,
// scrollFbp 0x00A4, showFbpWithEffectSprite 0x00A5.
// Topology per script.c: all nine opcode bits live in the shared
// PAL_InterpretInstruction switch (trigger switch 3206-3440 and auto switch
// 3522-3641 have no case for any of them, each case appears exactly once) and
// every successor is sequential (plain break into the script.c:3085 tail
// return wScriptEntry + 1; the blocking callees PAL_ShowFBP/PAL_ScrollFBP/
// PAL_EndingAnimation/PAL_PaletteFade/PAL_ColorFade/PAL_SceneFade/
// VIDEO_FadeScreen complete synchronously inside the case like the #107
// partyWalkTo and #110 PAL_RNGPlay precedents - no stall, no jump).
// Semantics per the reference cases:
//   - 0x0076 showFbp (script.c:2199-2213): non-WIN95 path clears the ending
//     effect sprite (PAL_EndingSetEffectSprite(0)) then PAL_ShowFBP(fbpId,
//     fade) (ending.c:49-150) - fbpId is the fbp.mkf segment number and fade
//     the picture-show pacing (non-zero: 16 blend passes of (fade + 1) * 10 ms;
//     WORD 0: direct blit). Real data: 4 hits, all the fbpId 0xFFFF black
//     transition form.
//   - 0x0080 toggleDayNightPalette (script.c:2381-2389): flips fNightPalette
//     (the 0x0053/0x0054 pair's global) and PAL_PaletteFade(32 blend steps,
//     palette.c:381-446); the truthy WORD inverts fUpdateScene: WORD 0
//     re-renders the scene per step (FRAME_TIME 100 ms), non-zero fades the
//     palette only (FRAME_TIME/4). Real data: 4 hits (0x3, 1x1).
//   - 0x008B setPalette (script.c:2573-2582): wNumPalette = the pat.mkf
//     palette segment number (PAL_GetPalette, palette.c:40-60); applied
//     immediately via PAL_SetPalette (day half) unless a fade-in is pending.
//     Real data: 14 hits (0x7, 2, 5x5, 6).
//   - 0x008C colorFade (script.c:2584-2592): PAL_ColorFade(delay, (BYTE)color,
//     from) (palette.c:462-560) blends all 256 entries toward (from WORD 0) or
//     from (non-zero, real data 0xFFFF) the solid color over 64 steps of
//     delay * 10 ms (WORD 0 normalizes to 10 ms at runtime only); the color
//     reads the operand's low byte. Real data: 6 hits.
//   - 0x0093 sceneFade (script.c:2666-2674): PAL_SceneFade(wNumPalette,
//     fNightPalette, (SHORT)operand0) (palette.c:262-375) - a positive step
//     fades in, a negative step fades out (and arms fNeedToFadeIn); WORD 0
//     normalizes to 1 at runtime only. Real data: 16 hits including -4x3,
//     -2x4, -1x2 - the signedWord evidence.
//   - 0x0096 showEndingAnimation (script.c:2695-2700): non-WIN95
//     PAL_EndingAnimation() (ending.c:282-553) - the whole ending sequence,
//     no operands; no real chunk-4 hit.
//   - 0x009B fadeToScene (script.c:2768-2775): the 0x0073 fadeScene shape at
//     the fixed speed 2 (VIDEO_BackupScreen + PAL_MakeScene +
//     VIDEO_FadeScreen(2)); reads no operand - the two real hits' operand
//     words (2/0xFFFF/0) are preserved.
//   - 0x00A4 scrollFbp (script.c:3040-3054): non-WIN95 PAL_ScrollFBP(fbpId,
//     speed, TRUE) (ending.c:153-278) with the fbpId 68 -> 69 HACKHACK
//     precondition; speed WORD 0 normalizes to 1 at runtime only; operand 1
//     unused. No real chunk-4 hit.
//   - 0x00A5 showFbpWithEffectSprite (script.c:3057-3070): unless operand 1
//     is the 0xFFFF keep-sentinel, PAL_EndingSetEffectSprite selects the
//     MGO.MKF sprite animated over PAL_ShowFBP(fbpId, fade). No real chunk-4
//     hit.
// Signedness: sceneFade.step is the batch's only SignedWord field (the
// (SHORT) cast at script.c:2669-2672 with real negative hits); colorFade.color
// is a LowByte binding (the (BYTE) cast); every other field is an unsigned
// WORD.
// Counter-examples follow the #78/#96 skeleton: edit a semantic field ->
// re-encoded bytes differ from the source bytes and equal the hand-computed
// value; unknown fields and out-of-range values are rejected.
public sealed class OpcodeSceneMovementScreenBatch2Tests
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

    // --- 0x0076 showFbp (new, shared, script.c:2199-2213): "Show FBP picture" -
    //     the WIN95 build only clears the screen; otherwise the case clears the
    //     pending ending effect sprite (PAL_EndingSetEffectSprite(0)) and calls
    //     PAL_ShowFBP(fbpId, fade) (ending.c:49-150): fbpId is the fbp.mkf
    //     segment number ("number of chunk in fbp.mkf file", the #95 fbp
    //     segment contract; the musicId/rngId MKF-segment naming precedent) and
    //     fade the show pacing - a non-zero fade runs the 16-iteration 6-plane
    //     palette blend at (fade + 1) * 10 ms per pass, WORD 0 blits directly.
    //     Real data: 4 hits (@0B55/@0BEB/@1279/@6DBF), all fbpId 0xFFFF - the
    //     decompress fails and the buffer clears to black (the fade-to-black
    //     transition form); operands 1/2 all zero. ---
    [Fact]
    public void ShowFbp_edits_fbp_segment_and_fade()
    {
        byte[] original = Words(0x0076, unchecked((int)0xFFFF), 0, 0); // the real-data @0B55 black-transition form
        var command = Decode(0, 0x0076, unchecked((int)0xFFFF), 0, 0);
        Assert.Equal("showFbp", (string)command["op"]!);
        Assert.Equal(65_535, (int)command["fbpId"]!); // the 0xFFFF form stays the raw unsigned WORD
        Assert.Equal(0, (int)command["fade"]!);

        Assert.Equal(original, Reencode(command)); // both mapped WORDs re-encode byte-exact
        Assert.Equal(Words(0x0076, 68, 0, 0), EditAndReencode(Decode(0, 0x0076, unchecked((int)0xFFFF), 0, 0), "fbpId", 68)); // ending.c's own PAL_ShowFBP(68, 7) chunk
        Assert.Equal(Words(0x0076, 75, 0, 0), EditAndReencode(Decode(0, 0x0076, unchecked((int)0xFFFF), 0, 0), "fbpId", 75)); // ending.c's PAL_ShowFBP(75, 0) chunk
        Assert.Equal(Words(0x0076, unchecked((int)0xFFFF), 7, 0), EditAndReencode(Decode(0, 0x0076, unchecked((int)0xFFFF), 0, 0), "fade", 7)); // ending.c's own fade-7 form
        Assert.Equal(Words(0x0076, 76, 10, 0), Reencode(Decode(0, 0x0076, 76, 10, 0))); // ending.c's PAL_ShowFBP(77, 10) fade pacing
        Assert.Equal(Words(0x0076, 0, 0, 0), Reencode(Decode(0, 0x0076, 0, 0, 0))); // segment 0 with the direct-blit WORD-0 fade
        Assert.Equal(Words(0x0076, 65535, 65535, 0), Reencode(Decode(0, 0x0076, 65_535, 65_535, 0))); // full unsigned WORD tops

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0076, unchecked((int)0xFFFF), 0, 0), "fbpId", 68));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0076, unchecked((int)0xFFFF), 0, 0), "chunkNum", 68)); // unknown field (the callee parameter's own name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0076, unchecked((int)0xFFFF), 0, 0), "pictureId", 68)); // unknown field name
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0076, unchecked((int)0xFFFF), 0, 0), "imageId", 68)); // unknown field name
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0076, unchecked((int)0xFFFF), 0, 0), "effectSprite", 68)); // unknown field: the 0x00A5 sprite operand, this verb clears it
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0076, unchecked((int)0xFFFF), 0, 0), "fbpId", 65_536));
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0076, unchecked((int)0xFFFF), 0, 0), "fade", 65_536));
    }

    // --- 0x0080 toggleDayNightPalette (new, shared, script.c:2381-2389):
    //     "Toggle day/night palette" - flips gpGlobals->fNightPalette (the
    //     0x0053/0x0054 pair's global) and PAL_PaletteFade(wNumPalette,
    //     fNightPalette, !(operand0)) (palette.c:381-446): 32 blend steps from
    //     the current video palette to the target; WORD 0 re-renders the scene
    //     every step (FRAME_TIME = 100 ms pacing), a non-zero form fades the
    //     palette only (FRAME_TIME/4 = 25 ms pacing). Real data: 4 hits -
    //     @1277/@229F/@7031 the WORD-0 form, @3613 the 1 form. ---
    [Fact]
    public void ToggleDayNightPalette_edits_the_palette_only_flag()
    {
        byte[] original = Words(0x0080, 1, 0, 0); // the real-data @3613 palette-only form
        var command = Decode(0, 0x0080, 1, 0, 0);
        Assert.Equal("toggleDayNightPalette", (string)command["op"]!);
        Assert.NotEqual("useNightPalette", (string)command["op"]!); // the toggle is its own verb, not the pair's boolean
        Assert.Equal(1, (int)command["paletteOnly"]!);

        Assert.Equal(original, Reencode(command)); // the mapped WORD and the preserved pair re-encode byte-exact
        Assert.Equal(Words(0x0080, 0, 0, 0), EditAndReencode(Decode(0, 0x0080, 1, 0, 0), "paletteOnly", 0)); // the real-data @1277 scene-updating majority form
        Assert.Equal(Words(0x0080, 1, 0, 0), EditAndReencode(Decode(0, 0x0080, 0, 0, 0), "paletteOnly", 1)); // back to the real @3613 form
        Assert.Equal(Words(0x0080, 0xFFFF, 0, 0), EditAndReencode(Decode(0, 0x0080, 1, 0, 0), "paletteOnly", unchecked((int)0xFFFF))); // any non-zero WORD is the truthy form (waitUpdate's 0xFFFF shape)

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0080, 1, 0, 0), "paletteOnly", 0));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0080, 1, 0, 0), "night", true)); // unknown field: the flag is the 0x0053/0x0054 verb pair's global, not an operand
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0080, 1, 0, 0), "fNightPalette", 1)); // unknown field (the global's own name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0080, 1, 0, 0), "fUpdateScene", 1)); // unknown field (the callee parameter's own name, inverted by the case)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0080, 1, 0, 0), "paletteId", 1)); // unknown field: the palette stays the current wNumPalette
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0080, 1, 0, 0), "paletteOnly", 65_536));
    }

    // --- 0x008B setPalette (new, shared, script.c:2573-2582): "change the
    //     current palette" - gpGlobals->wNumPalette = operand0: the pat.mkf
    //     palette segment number every fade callee consumes via PAL_GetPalette
    //     (palette.c:40-60, the day/night halves at 0/256*3); the screen
    //     palette is applied immediately (PAL_SetPalette day half,
    //     palette.c:93) only while no fade-in is pending - fNeedToFadeIn
    //     defers the apply. Real data: 14 hits - paletteId 0x7/2/5x5/6;
    //     operands 1/2 all zero. ---
    [Fact]
    public void SetPalette_edits_the_pat_segment()
    {
        byte[] original = Words(0x008B, 5, 0, 0); // the real-data @56CF form
        var command = Decode(0, 0x008B, 5, 0, 0);
        Assert.Equal("setPalette", (string)command["op"]!);
        Assert.Equal(5, (int)command["paletteId"]!);

        Assert.Equal(original, Reencode(command)); // the mapped WORD and the preserved pair re-encode byte-exact
        Assert.Equal(Words(0x008B, 0, 0, 0), EditAndReencode(Decode(0, 0x008B, 5, 0, 0), "paletteId", 0)); // the real-data @55DE majority form (palette 0)
        Assert.Equal(Words(0x008B, 2, 0, 0), EditAndReencode(Decode(0, 0x008B, 5, 0, 0), "paletteId", 2)); // the real-data @565D form
        Assert.Equal(Words(0x008B, 6, 0, 0), EditAndReencode(Decode(0, 0x008B, 5, 0, 0), "paletteId", 6)); // the real-data top form
        Assert.Equal(Words(0x008B, 8, 0, 0), EditAndReencode(Decode(0, 0x008B, 5, 0, 0), "paletteId", 8)); // ending.c's own gpGlobals->wNumPalette = 8 form
        Assert.Equal(Words(0x008B, 65535, 0, 0), EditAndReencode(Decode(0, 0x008B, 5, 0, 0), "paletteId", 65_535)); // full unsigned WORD (no retail bound at the encoding layer)

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x008B, 5, 0, 0), "paletteId", 0));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x008B, 5, 0, 0), "iPaletteNum", 5)); // unknown field (the callee parameter's own name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x008B, 5, 0, 0), "wNumPalette", 5)); // unknown field (the global's own name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x008B, 5, 0, 0), "night", false)); // unknown field: the day half is fixed FALSE here, not an operand
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x008B, 5, 0, 0), "colorId", 5)); // unknown field name
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x008B, 5, 0, 0), "paletteId", 65_536));
    }

    // --- 0x008C colorFade (new, shared, script.c:2584-2592): "Fade from/to
    //     color" - PAL_ColorFade(operand1, (BYTE)(operand0), operand2)
    //     (palette.c:462-560) blends all 256 palette entries toward (from WORD
    //     0) or from (any non-zero form - real data uses 0xFFFF) the solid
    //     palette color over 64 steps of delay * 10 ms (WORD 0 normalizes to
    //     10 ms at runtime only); the color reads the operand's low byte
    //     through the (BYTE) cast; the case also clears fNeedToFadeIn.
    //     Real data: 6 hits - color 26x2/79x2/127x2, delay 0x1/2x3/4x2,
    //     fromColor 0x3/0xFFFFx3. ---
    [Fact]
    public void ColorFade_edits_color_delay_and_direction()
    {
        byte[] original = Words(0x008C, 26, 4, unchecked((int)0xFFFF)); // the real-data @4252 fade-from form
        var command = Decode(0, 0x008C, 26, 4, unchecked((int)0xFFFF));
        Assert.Equal("colorFade", (string)command["op"]!);
        Assert.Equal(26, (int)command["color"]!);
        Assert.Equal(4, (int)command["delay"]!);
        Assert.Equal(65_535, (int)command["fromColor"]!); // the truthy form stays the raw unsigned WORD

        Assert.Equal(original, Reencode(command)); // all three mapped WORDs re-encode byte-exact
        Assert.Equal(Words(0x008C, 79, 2, 0), Reencode(Decode(0, 0x008C, 79, 2, 0))); // the real-data @82EA fade-to form as a whole
        Assert.Equal(Words(0x008C, 127, 0, unchecked((int)0xFFFF)), Reencode(Decode(0, 0x008C, 127, 0, unchecked((int)0xFFFF)))); // the real-data @83CF delay-0 form as a whole
        Assert.Equal(Words(0x008C, 127, 4, unchecked((int)0xFFFF)), EditAndReencode(Decode(0, 0x008C, 26, 4, unchecked((int)0xFFFF)), "color", 127)); // the real top color keeps the delay-4 fade-from form
        Assert.Equal(Words(0x008C, 26, 2, unchecked((int)0xFFFF)), EditAndReencode(Decode(0, 0x008C, 26, 4, unchecked((int)0xFFFF)), "delay", 2)); // the real @4182 delay
        Assert.Equal(Words(0x008C, 26, 0, unchecked((int)0xFFFF)), EditAndReencode(Decode(0, 0x008C, 26, 4, unchecked((int)0xFFFF)), "delay", 0)); // delay WORD 0 still encodes 0 (runtime 10 ms)
        Assert.Equal(Words(0x008C, 26, 4, 0), EditAndReencode(Decode(0, 0x008C, 26, 4, unchecked((int)0xFFFF)), "fromColor", 0)); // the fade-to direction
        Assert.Equal(Words(0x008C, 26, 4, 1), EditAndReencode(Decode(0, 0x008C, 26, 4, 0), "fromColor", 1)); // the minimal truthy form
        // The (BYTE) cast keeps only the operand's low byte: a hypothetical high-byte
        // source WORD survives as preserved bits while the field edits the low byte.
        var highBits = Decode(0, 0x008C, unchecked((int)0x011A), 4, 0);
        Assert.Equal(26, (int)highBits["color"]!); // 0x011A & 0xFF
        Assert.Equal(Words(0x008C, unchecked((int)0x011A), 4, 0), Reencode(highBits)); // the high bits round-trip untouched
        Assert.Equal(Words(0x008C, unchecked((int)0x014F), 4, 0), EditAndReencode(Decode(0, 0x008C, unchecked((int)0x011A), 4, 0), "color", 79)); // the edit rewrites only the low byte (0x01 high | 0x4F low)
        Assert.Equal(Words(0x008C, 26, 65535, 0), EditAndReencode(Decode(0, 0x008C, 26, 4, 0), "delay", 65_535)); // full unsigned WORD top

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x008C, 26, 4, unchecked((int)0xFFFF)), "fromColor", 0));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x008C, 26, 4, 0), "bColor", 26)); // unknown field (the callee parameter's own name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x008C, 26, 4, 0), "iDelay", 4)); // unknown field (the callee parameter's own name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x008C, 26, 4, 0), "from", 1)); // unknown field name (the BOOL parameter's own name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x008C, 26, 4, 0), "toColor", 1)); // unknown field: the direction is one truthy word, not a pair
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x008C, 26, 4, 0), "color", 256)); // beyond the (BYTE) low-byte range
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x008C, 26, 4, 0), "color", -1)); // below the (BYTE) low-byte range
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x008C, 26, 4, 0), "delay", 65_536));
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x008C, 26, 4, 0), "fromColor", 65_536));
    }

    // --- 0x0093 sceneFade (new, shared, script.c:2666-2674): "Fade the screen.
    //     Update scene in the process." - PAL_SceneFade(wNumPalette,
    //     fNightPalette, (SHORT)operand0) (palette.c:262-375): a positive step
    //     fades in (the palette scale ramps 0 -> 63; a larger magnitude = fewer,
    //     faster 100 ms iterations, each re-rendering the scene) and a negative
    //     step fades out (63 -> 0); WORD 0 normalizes to 1 at runtime only; a
    //     negative step also arms fNeedToFadeIn (script.c:2672). The (SHORT)
    //     cast is the batch's only signed field - real data: 16 hits with step
    //     -4 (0xFFFC)x3, -2 (0xFFFE)x4, -1 (0xFFFF)x2, 1x3, 2x2, 4, 8. ---
    [Fact]
    public void SceneFade_edits_the_signed_step()
    {
        byte[] original = Words(0x0093, unchecked((int)0xFFFC), 0, 0); // the real-data @29E9 fade-out form
        var command = Decode(0, 0x0093, unchecked((int)0xFFFC), 0, 0);
        Assert.Equal("sceneFade", (string)command["op"]!);
        Assert.Equal(-4, (int)command["step"]!); // the (SHORT) cast decodes 0xFFFC as -4

        Assert.Equal(original, Reencode(command)); // the mapped WORD re-encodes byte-exact through the two's complement
        Assert.Equal(-4, (int)Decode(0, 0x0093, unchecked((int)0xFFFC), 0, 0)["step"]!); // and decodes back through the SHORT cast
        Assert.Equal(Words(0x0093, unchecked((int)0xFFFE), 0, 0), EditAndReencode(Decode(0, 0x0093, unchecked((int)0xFFFC), 0, 0), "step", -2)); // the real-data @0BEA majority fade-out step
        Assert.Equal(Words(0x0093, unchecked((int)0xFFFF), 0, 0), EditAndReencode(Decode(0, 0x0093, unchecked((int)0xFFFC), 0, 0), "step", -1)); // the real-data slowest fade-out step (arms fNeedToFadeIn)
        Assert.Equal(Words(0x0093, 1, 0, 0), EditAndReencode(Decode(0, 0x0093, unchecked((int)0xFFFC), 0, 0), "step", 1)); // the real-data @120D slowest fade-in step
        Assert.Equal(Words(0x0093, 2, 0, 0), EditAndReencode(Decode(0, 0x0093, unchecked((int)0xFFFC), 0, 0), "step", 2)); // the real-data @120D-adjacent fade-in step
        Assert.Equal(Words(0x0093, 8, 0, 0), EditAndReencode(Decode(0, 0x0093, unchecked((int)0xFFFC), 0, 0), "step", 8)); // the real-data @6143 fastest step
        Assert.Equal(Words(0x0093, 0, 0, 0), EditAndReencode(Decode(0, 0x0093, unchecked((int)0xFFFC), 0, 0), "step", 0)); // WORD 0 still encodes 0 (runtime 1)
        Assert.Equal(Words(0x0093, unchecked((int)0x8000), 0, 0), EditAndReencode(Decode(0, 0x0093, 0, 0, 0), "step", -32_768)); // signed WORD bottom
        Assert.Equal(Words(0x0093, 32_767, 0, 0), EditAndReencode(Decode(0, 0x0093, 0, 0, 0), "step", 32_767)); // signed WORD top

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0093, unchecked((int)0xFFFC), 0, 0), "step", 4));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0093, unchecked((int)0xFFFC), 0, 0), "iStep", 4)); // unknown field (the callee parameter's own name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0093, unchecked((int)0xFFFC), 0, 0), "speed", 4)); // unknown field: VIDEO_FadeScreen's fadeScene word - this callee paces its own ramp
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0093, unchecked((int)0xFFFC), 0, 0), "delay", 4)); // unknown field: PAL_FadeOut's delay unit
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0093, unchecked((int)0xFFFC), 0, 0), "direction", -4)); // unknown field: the sign of the step is the direction
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0093, unchecked((int)0xFFFC), 0, 0), "step", -32_769)); // beyond signed WORD
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0093, unchecked((int)0xFFFC), 0, 0), "step", 32_768)); // beyond signed WORD
    }

    // --- 0x0096 showEndingAnimation (new, shared, script.c:2695-2700): "Show
    //     the ending animation" - non-WIN95 PAL_EndingAnimation()
    //     (ending.c:282-553) plays the whole ending sequence (RNG movies, FBP
    //     stills, credits, AVI/CD fallbacks) and returns; the case reads no
    //     operand. Real data: no chunk-4 hit (the ending's own FBP/RNG sequence
    //     is hardcoded in ending.c, not scripted). ---
    [Fact]
    public void ShowEndingAnimation_reads_no_operands_and_preserves_source_words()
    {
        byte[] original = Words(0x0096, 0, 0, 0); // the operand-less case's all-zero form
        var command = Decode(0, 0x0096, 0, 0, 0);
        Assert.Equal("showEndingAnimation", (string)command["op"]!);
        Assert.Null(command["delay"]); // the ending playback is parameterless - no mapped field exists

        Assert.Equal(original, Reencode(command)); // the preserved triple re-encodes byte-exact

        // Every operand field name is rejected: the case reads no operand.
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0096, 0, 0, 0), "rngId", 1)); // the ending reuses the setPlayingRng selection, not an operand here
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0096, 0, 0, 0), "fbpId", 61)); // ending.c's own backdrop chunk is hardcoded, not an operand
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0096, 0, 0, 0), "delay", 1));
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0096, 0, 0, 0), "frames", 1));
    }

    // --- 0x009B fadeToScene (new, shared, script.c:2768-2775): "Fade to the
    //     current scene" (FIXME'd upstream) - the 0x0073 fadeScene shape at
    //     the fixed speed 2: VIDEO_BackupScreen(gpScreen) + PAL_MakeScene()
    //     rebuild the scene, then VIDEO_FadeScreen(2) cross-fades from the
    //     backup buffer; the case reads no operand. Real data: 2 hits
    //     (@7C72/@7C8B), both carrying the ignored operand words 2/0xFFFF/0 -
    //     preserved as source WORDs, exactly like animateObject's carried
    //     zeros. ---
    [Fact]
    public void FadeToScene_reads_no_operands_and_preserves_the_ignored_words()
    {
        byte[] original = Words(0x009B, 2, unchecked((int)0xFFFF), 0); // the real-data @7C72 form
        var command = Decode(0, 0x009B, 2, unchecked((int)0xFFFF), 0);
        Assert.Equal("fadeToScene", (string)command["op"]!);
        Assert.NotEqual("fadeScene", (string)command["op"]!); // the fixed-speed variant keeps its own verb
        Assert.Null(command["speed"]); // the speed 2 is the case's constant, not a field

        Assert.Equal(original, Reencode(command)); // the ignored 2/0xFFFF/0 triple re-encodes byte-exact as preserved source WORDs

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x009B, 2, unchecked((int)0xFFFF), 0), "speed", 2)); // unknown field: VIDEO_FadeScreen's constant 2, not an operand
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x009B, 2, unchecked((int)0xFFFF), 0), "mode", 2)); // unknown field name
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x009B, 2, unchecked((int)0xFFFF), 0), "delay", 2)); // unknown field: PAL_FadeOut's delay unit
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x009B, 2, unchecked((int)0xFFFF), 0), "sceneId", 2)); // unknown field: the scene is rebuilt from the current globals, not an operand
    }

    // --- 0x00A4 scrollFbp (new, shared, script.c:3040-3054): "Scroll FBP to
    //     the screen" - non-WIN95 only: fbpId 68 first shows chunk 69 directly
    //     (the upstream HACKHACK for the ending picture), then
    //     PAL_ScrollFBP(fbpId, speed, TRUE) (ending.c:153-278) scrolls the
    //     fbp.mkf picture onto the screen line by line downward; speed WORD 0
    //     normalizes to 1 at runtime only (ending.c:211-214); operand 1 is
    //     unused and preserved. Real data: no chunk-4 hit (ending.c drives its
    //     own scrolls at speed 0xF). ---
    [Fact]
    public void ScrollFbp_edits_fbp_segment_and_speed_and_keeps_preserved_operand()
    {
        byte[] original = Words(0x00A4, 68, 5, unchecked((int)0x0F)); // the ending HACKHACK chunk with ending.c's own 0xF speed and a preserved operand1
        var command = Decode(0, 0x00A4, 68, 5, unchecked((int)0x0F));
        Assert.Equal("scrollFbp", (string)command["op"]!);
        Assert.Equal(68, (int)command["fbpId"]!);
        Assert.Equal(15, (int)command["speed"]!);

        Assert.Equal(original, Reencode(command)); // both mapped WORDs and the preserved operand1 re-encode byte-exact
        Assert.Equal(Words(0x00A4, 74, 5, unchecked((int)0x0F)), EditAndReencode(Decode(0, 0x00A4, 68, 5, unchecked((int)0x0F)), "fbpId", 74)); // ending.c's own PAL_ScrollFBP(74, 0xF, TRUE) chunk
        Assert.Equal(Words(0x00A4, 60, 5, 1), Reencode(Decode(0, 0x00A4, 60, 5, 1))); // ending.c's credits chunk at speed 1
        Assert.Equal(Words(0x00A4, 68, 5, 0), EditAndReencode(Decode(0, 0x00A4, 68, 5, unchecked((int)0x0F)), "speed", 0)); // speed WORD 0 still encodes 0 (runtime 1)
        Assert.Equal(Words(0x00A4, 69, 5, unchecked((int)0x0F)), EditAndReencode(Decode(0, 0x00A4, 68, 5, unchecked((int)0x0F)), "fbpId", 69)); // the HACKHACK's directly-shown chunk as the scrolled id
        Assert.Equal(Words(0x00A4, 65535, 5, 0), EditAndReencode(Decode(0, 0x00A4, 68, 5, 0), "fbpId", 65_535)); // full unsigned WORD, the preserved operand1 kept
        Assert.Equal(Words(0x00A4, 68, 5, 65535), EditAndReencode(Decode(0, 0x00A4, 68, 5, 0), "speed", 65_535)); // full unsigned WORD top, the preserved operand1 kept

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x00A4, 68, 5, unchecked((int)0x0F)), "speed", 1));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x00A4, 68, 5, 0), "wScrollSpeed", 1)); // unknown field (the callee parameter's own name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x00A4, 68, 5, 0), "fade", 1)); // unknown field: the scroll has no palette-blend path, unlike 0x0076
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x00A4, 68, 5, 0), "direction", 1)); // unknown field: fScrollDown is the case's fixed TRUE
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x00A4, 68, 5, 0), "scrollDown", true)); // unknown field: the fixed TRUE is not an operand
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x00A4, 68, 5, 0), "fbpId", 65_536));
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x00A4, 68, 5, 0), "speed", 65_536));
    }

    // --- 0x00A5 showFbpWithEffectSprite (new, shared, script.c:3057-3070):
    //     "Show FBP picture with sprite effects" - non-WIN95 only: unless
    //     operand 1 is the 0xFFFF keep-sentinel, PAL_EndingSetEffectSprite
    //     selects the MGO.MKF sprite number that PAL_ShowFBP animates over the
    //     picture (decompressed from fpMGO and frame-advanced at ticks / 150,
    //     ending.c:79-129), then PAL_ShowFBP(fbpId, fade) as in 0x0076.
    //     Real data: no chunk-4 hit (ending.c hardcodes its own effect sprite
    //     0x27B over chunk 76). ---
    [Fact]
    public void ShowFbpWithEffectSprite_edits_segment_sprite_and_fade()
    {
        byte[] original = Words(0x00A5, 76, unchecked((int)0x027B), 7); // ending.c's own PAL_ShowFBP(76, 7) with effect sprite 0x27B
        var command = Decode(0, 0x00A5, 76, unchecked((int)0x027B), 7);
        Assert.Equal("showFbpWithEffectSprite", (string)command["op"]!);
        Assert.NotEqual("showFbp", (string)command["op"]!); // the sprite-effect variant keeps its own verb
        Assert.Equal(76, (int)command["fbpId"]!);
        Assert.Equal(635, (int)command["effectSprite"]!); // 0x27B, the MGO.MKF sprite number
        Assert.Equal(7, (int)command["fade"]!);

        Assert.Equal(original, Reencode(command)); // all three mapped WORDs re-encode byte-exact
        Assert.Equal(Words(0x00A5, 77, 0, 10), Reencode(Decode(0, 0x00A5, 77, 0, 10))); // ending.c's PAL_ShowFBP(77, 10) with the cleared sprite
        Assert.Equal(Words(0x00A5, 76, 0, 7), EditAndReencode(Decode(0, 0x00A5, 76, unchecked((int)0x027B), 7), "effectSprite", 0)); // the cleared-sprite form (no effect overlay)
        Assert.Equal(Words(0x00A5, 76, unchecked((int)0xFFFF), 7), EditAndReencode(Decode(0, 0x00A5, 76, unchecked((int)0x027B), 7), "effectSprite", 65_535)); // the keep-current sentinel form
        Assert.Equal(Words(0x00A5, 68, unchecked((int)0x027B), 7), EditAndReencode(Decode(0, 0x00A5, 76, unchecked((int)0x027B), 7), "fbpId", 68)); // ending.c's PAL_ShowFBP(68, 7) chunk keeps the sprite and fade
        Assert.Equal(Words(0x00A5, 76, unchecked((int)0x027B), 0), EditAndReencode(Decode(0, 0x00A5, 76, unchecked((int)0x027B), 7), "fade", 0)); // the direct-blit WORD-0 fade
        Assert.Equal(Words(0x00A5, 65535, 65535, 65535), Reencode(Decode(0, 0x00A5, 65_535, 65_535, 65_535))); // full unsigned WORD tops (0xFFFF fbpId keeps the sentinel semantics distinct)

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x00A5, 76, unchecked((int)0x027B), 7), "fade", 0));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x00A5, 76, 0, 7), "wSpriteNum", 1)); // unknown field (the callee parameter's own name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x00A5, 76, 0, 7), "spriteNum", 1)); // unknown field: setFollower's MGO sprite name - this is the ending effect overlay
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x00A5, 76, 0, 7), "effectSpriteId", 1)); // unknown field name
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x00A5, 76, 0, 7), "speed", 7)); // unknown field: the pacing is PAL_ShowFBP's fade, not a scroll speed
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x00A5, 76, 0, 7), "fbpId", 65_536));
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x00A5, 76, 0, 7), "effectSprite", 65_536));
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x00A5, 76, 0, 7), "fade", 65_536));
    }
}
