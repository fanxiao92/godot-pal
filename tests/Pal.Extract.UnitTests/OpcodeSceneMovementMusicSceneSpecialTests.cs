using Pal.Extract;
using Xunit;
using static Pal.Extract.Tests.OpcodeCounterexampleHarness;

namespace Pal.Extract.Tests;

// M5 opcode scene-movement music/scene/special batch (#112, the closing batch
// of the 75-opcode scene-movement domain and of all 165 opcodes, 12 opcodes) -
// setMusic 0x0043 (re-verified), setBattleMusic 0x0045, playSound 0x0047,
// stopMusic 0x0077, playCdMusic 0x00A3, changeScene 0x0059,
// setPlayerSprite 0x0065, setSceneScripts 0x006D (re-verified), setParty
// 0x0075 (re-verified), changeSceneMap 0x0099, noop 0x0078 (re-verified),
// advanceEntry 0x00A7.
// Topology per script.c: eleven opcode bits live in the shared
// PAL_InterpretInstruction switch (trigger switch 3206-3440 and auto switch
// 3522-3641 have no case for any of them, each case appears exactly once) and
// every successor is sequential (plain break into the script.c:3085 tail
// return wScriptEntry + 1; AUDIO_PlayMusic/AUDIO_PlaySound/AUDIO_PlayCDTrack/
// PAL_LoadResources complete synchronously inside the case - no stall, no
// jump). The exception is 0x00A7: the only case of PAL_RunAutoScript's own
// switch beyond the shared set (script.c:3641-3643, a bare wScriptEntry++),
// with no trigger case and no PAL_InterpretInstruction case, so a trigger
// execution would terminate in the shared default TerminateOnError
// (script.c:3077) - the executor is auto-only (the first AutoSwitchOnly
// descriptor) while the successor stays sequential (the standard
// one-instruction-per-frame advance).
// Semantics per the reference cases:
//   - 0x0043 setMusic (script.c:1642-1648): wNumMusic = operand0 and
//     AUDIO_PlayMusic(musicId, mode != 1, (mode == 3 && musicId != 9) ? 3.0f
//     : 0.0f): mode 1 plays once, anything else loops, mode 3 additionally
//     fades in over 3 s except for music 9. Real data: 155 hits.
//   - 0x0045 setBattleMusic (script.c:1658-1663): wNumBattleMusic = operand0,
//     the RIX segment the next battle plays looped (battle.c:728). Real data:
//     68 hits (battleMusicId 14..86, the 37 form x16).
//   - 0x0047 playSound (script.c:1704-1709): AUDIO_PlaySound(operand0) plays
//     the voc.mkf / sounds.mkf segment (audio.c:518, absolute value at
//     runtime). Real data: 313 hits (soundId 2..275, the 78 form x104).
//   - 0x0077 stopMusic (script.c:2215-2222): AUDIO_PlayMusic(0, FALSE,
//     operand0 == 0 ? 2.0f : operand0 * 3) fades out (2 s / n * 3 s) and
//     wNumMusic = 0. Real data: 44 hits (fadeUnits 0 x20 / 1 x24).
//   - 0x00A3 playCdMusic (script.c:3025-3038): wNumMusic = operand1 and
//     AUDIO_PlayCDTrack((SHORT)operand0) - the -1 form maps to -2 "do NOTHING"
//     (audio.c:576); failures and no-CD fall back to the looped RIX segment
//     operand1. Real data: 4 hits (tracks 2/4/8/9, musicId 14/22/27/67).
//   - 0x0059 changeScene (script.c:1870-1885): wNumScene = operand0 (the
//     1-based scene.json row) inside the 1..300 gate, arming the next-frame
//     scene load. Real data: 883 hits (sceneId 2..294).
//   - 0x0065 setPlayerSprite (script.c:1995-2005): rgwSpriteNum[operand0] =
//     operand1 - a 0-based role index picking the MGO.MKF normal-scene sprite;
//     truthy operand2 reloads the sprites outside battle. Real data: 124 hits.
//   - 0x006D setSceneScripts (script.c:2065-2089): rgScene[sceneId - 1]
//     takes non-zero onEnter/onTeleport entry WORDs, both zero clears both.
//     Real data: 62 hits (onEnter reaches 36203 past the signed range).
//   - 0x0075 setParty (script.c:2164-2197): non-zero roleIds (1-based, 0
//     skips the slot) fill the party slots; all-zero leaves the hack party.
//     Real data: 119 hits.
//   - 0x0099 changeSceneMap (script.c:2742-2756): rgScene[sceneId - 1]
//     takes mapNum; the 0xFFFF sentinel retargets the current scene and
//     reloads it. Real data: 2 hits (both the sentinel form).
//   - 0x0078 noop (script.c:2224-2228): the upstream "FIXME: ???" case - an
//     empty body, no operand read. Real data: 35 hits (all-zero operands).
//   - 0x00A7 advanceEntry (script.c:3641-3643): the auto-only bare advance.
//     Real data: 295 hits (all-zero operands).
// Signedness: playCdMusic.track is the batch's only SignedWord field (the
// (SHORT) cast at script.c:3032 with the -1 -> -2 sentinel); every other
// field is an unsigned WORD.
// Counter-examples follow the #78/#96 skeleton: edit a semantic field ->
// re-encoded bytes differ from the source bytes and equal the hand-computed
// value; unknown fields and out-of-range values are rejected.
public sealed class OpcodeSceneMovementMusicSceneSpecialTests
{
    // --- 0x0043 setMusic (#78 named, #112 re-verified, shared, script.c:1642-1648):
    //     "Set background music" - wNumMusic = operand0 and AUDIO_PlayMusic(operand0,
    //     operand1 != 1, (operand1 == 3 && operand0 != 9) ? 3.0f : 0.0f): the musicId
    //     selects the RIX music.mkf segment, the mode is the play selector (1 = play
    //     once, anything else loops, 3 = fade in over 3 s except music 9). The
    //     musicId/mode mapping holds. Real data: 155 hits - mode 0 x148 / 1 x2 / 3 x5,
    //     musicId 0..87. ---
    [Fact]
    public void SetMusic_reverified_keeps_music_segment_and_mode_selector()
    {
        byte[] original = Words(0x0043, 1, 0, 0); // the loop default form (mode 0)
        var command = Decode(0, 0x0043, 1, 0, 0);
        Assert.Equal("setMusic", (string)command["op"]!);
        Assert.Equal(1, (int)command["musicId"]!);
        Assert.Equal(0, (int)command["mode"]!);

        Assert.Equal(original, Reencode(command)); // both mapped WORDs re-encode byte-exact
        Assert.Equal(Words(0x0043, 1, 1, 0), EditAndReencode(Decode(0, 0x0043, 1, 0, 0), "mode", 1)); // the real play-once form
        Assert.Equal(Words(0x0043, 1, 3, 0), EditAndReencode(Decode(0, 0x0043, 1, 0, 0), "mode", 3)); // the real fade-in form (musicId 1 != 9)
        Assert.Equal(Words(0x0043, 9, 0, 0), EditAndReencode(Decode(0, 0x0043, 1, 0, 0), "musicId", 9)); // music 9: the mode-3 fade exception target (mode untouched)
        Assert.Equal(Words(0x0043, 87, 0, 0), EditAndReencode(Decode(0, 0x0043, 1, 0, 0), "musicId", 87)); // the real top segment
        Assert.Equal(Words(0x0043, 0, 0, 0), EditAndReencode(Decode(0, 0x0043, 1, 0, 0), "musicId", 0)); // segment 0 (the stopMusic callee's target)

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0043, 1, 0, 0), "mode", 3));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0043, 1, 0, 0), "loop", true)); // unknown field: looping is the mode word's != 1 form, not a boolean
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0043, 1, 0, 0), "battleMusicId", 1)); // unknown field: 0x0045's own operand
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0043, 1, 0, 0), "fadeTime", 3)); // unknown field (the callee parameter's own name)
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0043, 1, 0, 0), "musicId", 65_536));
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0043, 1, 0, 0), "mode", 65_536));
    }

    // --- 0x0045 setBattleMusic (new, shared, script.c:1658-1663): "Set battle
    //     music" - gpGlobals->wNumBattleMusic = operand0 (global.h:535): the RIX
    //     music.mkf segment the next battle plays looped (battle.c:728
    //     AUDIO_PlayMusic(wNumBattleMusic, TRUE, 0)); the case reads no other
    //     operand. Real data: 68 hits - battleMusicId 14..86 (37 x16 / 38 x10 /
    //     39 x13 the majority band). ---
    [Fact]
    public void SetBattleMusic_edits_the_battle_music_segment()
    {
        byte[] original = Words(0x0045, 37, 0, 0); // the real-data majority form (@0DDA band)
        var command = Decode(0, 0x0045, 37, 0, 0);
        Assert.Equal("setBattleMusic", (string)command["op"]!);
        Assert.NotEqual("setMusic", (string)command["op"]!); // the battle selector is its own verb
        Assert.Equal(37, (int)command["battleMusicId"]!);

        Assert.Equal(original, Reencode(command)); // the mapped WORD and the preserved pair re-encode byte-exact
        Assert.Equal(Words(0x0045, 14, 0, 0), EditAndReencode(Decode(0, 0x0045, 37, 0, 0), "battleMusicId", 14)); // the real low form
        Assert.Equal(Words(0x0045, 86, 0, 0), EditAndReencode(Decode(0, 0x0045, 37, 0, 0), "battleMusicId", 86)); // the real top form
        Assert.Equal(Words(0x0045, 65535, 0, 0), EditAndReencode(Decode(0, 0x0045, 37, 0, 0), "battleMusicId", 65_535)); // full unsigned WORD (no retail bound at the encoding layer)

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0045, 37, 0, 0), "battleMusicId", 38));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0045, 37, 0, 0), "musicId", 37)); // unknown field: setMusic's operand, not this verb's
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0045, 37, 0, 0), "wNumBattleMusic", 37)); // unknown field (the global's own name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0045, 37, 0, 0), "loop", true)); // unknown field: looping is fixed TRUE at the battle consumption
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0045, 37, 0, 0), "battleMusicId", 65_536));
    }

    // --- 0x0047 playSound (new, shared, script.c:1704-1709): "Play sound
    //     effect" - AUDIO_PlaySound(operand0) (audio.c:518): plays the voc.mkf /
    //     sounds.mkf segment operand0 (the parameter doc "number of the sound;
    //     the absolute value is used" - no (SHORT) cast, stays an unsigned
    //     WORD); the case reads no other operand. Real data: 313 hits - soundId
    //     2..275 with the 78 form x104. ---
    [Fact]
    public void PlaySound_edits_the_sound_segment()
    {
        byte[] original = Words(0x0047, 78, 0, 0); // the real-data majority form (x104)
        var command = Decode(0, 0x0047, 78, 0, 0);
        Assert.Equal("playSound", (string)command["op"]!);
        Assert.NotEqual("playMusic", (string)command["op"]!); // sounds and music are separate MKF families
        Assert.Equal(78, (int)command["soundId"]!);

        Assert.Equal(original, Reencode(command)); // the mapped WORD and the preserved pair re-encode byte-exact
        Assert.Equal(Words(0x0047, 2, 0, 0), EditAndReencode(Decode(0, 0x0047, 78, 0, 0), "soundId", 2)); // the real low form
        Assert.Equal(Words(0x0047, 275, 0, 0), EditAndReencode(Decode(0, 0x0047, 78, 0, 0), "soundId", 275)); // the real top form
        Assert.Equal(Words(0x0047, 65535, 0, 0), EditAndReencode(Decode(0, 0x0047, 78, 0, 0), "soundId", 65_535)); // full unsigned WORD: the callee's abs() stays runtime-only

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0047, 78, 0, 0), "soundId", 104));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0047, 78, 0, 0), "musicId", 78)); // unknown field: the RIX segment family
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0047, 78, 0, 0), "vocId", 78)); // unknown field name
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0047, 78, 0, 0), "iSoundNum", 78)); // unknown field (the callee parameter's own name)
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0047, 78, 0, 0), "soundId", 65_536));
    }

    // --- 0x0077 stopMusic (new, shared, script.c:2215-2222): "Stop current
    //     playing music" - AUDIO_PlayMusic(0, FALSE, (operand0 == 0) ? 2.0f :
    //     operand0 * 3) fades the current music out (2 s for WORD 0, otherwise
    //     operand0 * 3 s) and plays segment 0, then wNumMusic = 0; operands 1/2
    //     are unused by the case and preserved. Real data: 44 hits - fadeUnits
    //     0 x20 / 1 x24 (one hit also carries a preserved operand1 = 1). ---
    [Fact]
    public void StopMusic_edits_the_fade_units_and_keeps_preserved_operands()
    {
        byte[] original = Words(0x0077, 1, 1, 0); // the real-data form with both a unit fade and a preserved operand1
        var command = Decode(0, 0x0077, 1, 1, 0);
        Assert.Equal("stopMusic", (string)command["op"]!);
        Assert.NotEqual("setMusic", (string)command["op"]!); // the stop verb plays no chosen segment
        Assert.Equal(1, (int)command["fadeUnits"]!);

        Assert.Equal(original, Reencode(command)); // the mapped WORD and the preserved operand1 re-encode byte-exact
        Assert.Equal(Words(0x0077, 0, 1, 0), EditAndReencode(Decode(0, 0x0077, 1, 1, 0), "fadeUnits", 0)); // the real WORD-0 form (the 2 s default)
        Assert.Equal(Words(0x0077, 1, 1, 0), EditAndReencode(Decode(0, 0x0077, 0, 1, 0), "fadeUnits", 1)); // back to the 3 s unit form
        Assert.Equal(Words(0x0077, 65535, 1, 0), EditAndReencode(Decode(0, 0x0077, 1, 1, 0), "fadeUnits", 65_535)); // full unsigned WORD (no retail bound at the encoding layer)

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0077, 1, 1, 0), "fadeUnits", 0));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0077, 1, 1, 0), "musicId", 0)); // unknown field: segment 0 is hardcoded in the case body
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0077, 1, 1, 0), "fade", 1)); // unknown field: showFbp's pacing word
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0077, 1, 1, 0), "delay", 1)); // unknown field: PAL_FadeOut's delay unit
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0077, 1, 1, 0), "mode", 1)); // unknown field: setMusic's selector, not a stop word
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0077, 1, 1, 0), "fadeUnits", 65_536));
    }

    // --- 0x00A3 playCdMusic (new, shared, script.c:3025-3038): "Play CD music.
    //     Use the RIX music for fallback." - wNumMusic = operand1 and, with a CD
    //     available, AUDIO_PlayCDTrack reads operand0 through the (SHORT) cast:
    //     -1 (0xFFFF) maps to -2, audio.c:576's "do NOTHING" (leave the CD
    //     untouched), any other value is the CD track number; a failed CD play
    //     falls back to AUDIO_PlayMusic(operand1, TRUE, 0) - the looped RIX
    //     segment, also the no-CD path. The batch's only SignedWord field (the
    //     simulateMagic/enemySummon (SHORT) precedent). Real data: 4 hits -
    //     tracks 2/4/8/9 with musicId 14/22/27/67. ---
    [Fact]
    public void PlayCdMusic_edits_the_signed_track_and_rix_fallback()
    {
        byte[] original = Words(0x00A3, 2, 14, 0); // the real-data @234F form
        var command = Decode(0, 0x00A3, 2, 14, 0);
        Assert.Equal("playCdMusic", (string)command["op"]!);
        Assert.Equal(2, (int)command["track"]!); // a positive track passes through the (SHORT) cast unchanged
        Assert.Equal(14, (int)command["musicId"]!);

        Assert.Equal(original, Reencode(command)); // the mapped pair and the preserved operand2 re-encode byte-exact
        Assert.Equal(Words(0x00A3, 4, 14, 0), EditAndReencode(Decode(0, 0x00A3, 2, 14, 0), "track", 4)); // the real @5DA4 track (fallback segment untouched)
        Assert.Equal(Words(0x00A3, 8, 14, 0), EditAndReencode(Decode(0, 0x00A3, 2, 14, 0), "track", 8)); // the real @6D1F track
        Assert.Equal(Words(0x00A3, 9, 14, 0), EditAndReencode(Decode(0, 0x00A3, 2, 14, 0), "track", 9)); // the real @7D26 track
        Assert.Equal(Words(0x00A3, 2, 67, 0), EditAndReencode(Decode(0, 0x00A3, 2, 14, 0), "musicId", 67)); // the fallback segment alone
        Assert.Equal(Words(0x00A3, 9, 67, 0), Reencode(Decode(0, 0x00A3, 9, 67, 0))); // the full real @7D26 form round-trips
        Assert.Equal(Words(0x00A3, unchecked((int)0xFFFF), 14, 0), Reencode(Decode(0, 0x00A3, unchecked((int)0xFFFF), 14, 0))); // WORD 0xFFFF through the SHORT cast round-trips byte-exact
        Assert.Equal(-1, (int)Decode(0, 0x00A3, unchecked((int)0xFFFF), 14, 0)["track"]!); // ...decodes as the -1 do-nothing sentinel
        Assert.Equal(Words(0x00A3, unchecked((int)0xFFFF), 14, 0), EditAndReencode(Decode(0, 0x00A3, 2, 14, 0), "track", -1)); // and re-encodes from the signed field
        Assert.Equal(Words(0x00A3, unchecked((int)0x8000), 14, 0), EditAndReencode(Decode(0, 0x00A3, 2, 14, 0), "track", -32_768)); // signed WORD bottom

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x00A3, 2, 14, 0), "track", 4));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x00A3, 2, 14, 0), "cdTrack", 4)); // unknown field name
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x00A3, 2, 14, 0), "numTrack", 4)); // unknown field (the case's local name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x00A3, 2, 14, 0), "soundId", 4)); // unknown field: the voc.mkf family
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x00A3, 2, 14, 0), "mode", 4)); // unknown field: setMusic's selector
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x00A3, 2, 14, 0), "track", -32_769)); // beyond signed WORD
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x00A3, 2, 14, 0), "track", 32_768)); // beyond signed WORD
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x00A3, 2, 14, 0), "musicId", 65_536));
    }

    // --- 0x0059 changeScene (new, shared, script.c:1870-1885): "Change to the
    //     specified scene" - inside the 1..MAX_SCENES (300) and not-already-there
    //     gate, wNumScene = operand0 and the case arms the next-frame scene load
    //     (PAL_SetLoadFlags(kLoadScene), fEnteringScene = TRUE, wLayer = 0);
    //     operand0 is the 1-based scene.json row (rgScene[sceneId - 1]); an
    //     out-of-range or same-scene WORD is a runtime no-op and encodes
    //     unchanged. Real data: 883 hits - sceneId 2..294. ---
    [Fact]
    public void ChangeScene_edits_the_scene_row()
    {
        byte[] original = Words(0x0059, 2, 0, 0); // the real-data @0012 low form
        var command = Decode(0, 0x0059, 2, 0, 0);
        Assert.Equal("changeScene", (string)command["op"]!);
        Assert.NotEqual("setSceneScripts", (string)command["op"]!); // switching rows vs writing their scripts are different verbs
        Assert.Equal(2, (int)command["sceneId"]!);

        Assert.Equal(original, Reencode(command)); // the mapped WORD and the preserved pair re-encode byte-exact
        Assert.Equal(Words(0x0059, 294, 0, 0), EditAndReencode(Decode(0, 0x0059, 2, 0, 0), "sceneId", 294)); // the real top form
        Assert.Equal(Words(0x0059, 300, 0, 0), EditAndReencode(Decode(0, 0x0059, 2, 0, 0), "sceneId", 300)); // MAX_SCENES bound (palcommon.h:69)
        Assert.Equal(Words(0x0059, 0, 0, 0), EditAndReencode(Decode(0, 0x0059, 2, 0, 0), "sceneId", 0)); // the runtime no-op gate form still encodes 0
        Assert.Equal(Words(0x0059, 65535, 0, 0), EditAndReencode(Decode(0, 0x0059, 2, 0, 0), "sceneId", 65_535)); // full unsigned WORD (no retail bound at the encoding layer)

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0059, 2, 0, 0), "sceneId", 3));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0059, 2, 0, 0), "mapNum", 2)); // unknown field: 0x0099's own operand
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0059, 2, 0, 0), "wNumScene", 2)); // unknown field (the global's own name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0059, 2, 0, 0), "x", 2)); // unknown field: no party position operand here
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0059, 2, 0, 0), "onEnter", 2)); // unknown field: 0x006D's own operand
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0059, 2, 0, 0), "sceneId", 65_536));
    }

    // --- 0x0065 setPlayerSprite (new, shared, script.c:1995-2005): "Set the
    //     player's sprite" - g.PlayerRoles.rgwSpriteNum[operand0] = operand1:
    //     operand0 is a direct 0-based PLAYERROLES role index (the
    //     removeEquipment playerRole shape) and operand1 the MGO.MKF sprite
    //     number shown in normal scenes (global.h:303, an MGO chunk at
    //     res.c:325 - the setFollower spriteNum precedent); a truthy operand2
    //     reloads the player sprites immediately when not in battle. Real data:
    //     124 hits - playerRole 0 x119 / 1 / 2 x3 / 3, spriteNum 2 x48 .. 632,
    //     reload 0 x41 / 0xFFFF x83. ---
    [Fact]
    public void SetPlayerSprite_edits_role_sprite_and_reload()
    {
        byte[] original = Words(0x0065, 0, 2, 0); // the real-data majority form (role 0, sprite 2, no reload)
        var command = Decode(0, 0x0065, 0, 2, 0);
        Assert.Equal("setPlayerSprite", (string)command["op"]!);
        Assert.Equal(0, (int)command["playerRole"]!); // the 0-based direct index (WORD 0 is a real role here, not a sentinel)
        Assert.Equal(2, (int)command["spriteNum"]!);
        Assert.Equal(0, (int)command["reload"]!);

        Assert.Equal(original, Reencode(command)); // all three mapped WORDs re-encode byte-exact
        Assert.Equal(Words(0x0065, 0, 2, unchecked((int)0xFFFF)), EditAndReencode(Decode(0, 0x0065, 0, 2, 0), "reload", unchecked((int)0xFFFF))); // the real truthy majority form
        Assert.Equal(Words(0x0065, 0, 193, 0), EditAndReencode(Decode(0, 0x0065, 0, 2, 0), "spriteNum", 193)); // the real x17 sprite form
        Assert.Equal(Words(0x0065, 0, 232, 0), EditAndReencode(Decode(0, 0x0065, 0, 2, 0), "spriteNum", 232)); // the real x25 sprite form
        Assert.Equal(Words(0x0065, 0, 632, 0), EditAndReencode(Decode(0, 0x0065, 0, 2, 0), "spriteNum", 632)); // the real top sprite
        Assert.Equal(Words(0x0065, 1, 2, 0), EditAndReencode(Decode(0, 0x0065, 0, 2, 0), "playerRole", 1)); // the real second role
        Assert.Equal(Words(0x0065, 5, 2, 0), EditAndReencode(Decode(0, 0x0065, 0, 2, 0), "playerRole", 5)); // the 0-based top role (MAX_PLAYER_ROLES - 1)
        Assert.Equal(Words(0x0065, 0, 65535, 0), EditAndReencode(Decode(0, 0x0065, 0, 2, 0), "spriteNum", 65_535)); // full unsigned WORD (no retail bound at the encoding layer)

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0065, 0, 2, 0), "reload", 1));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0065, 0, 2, 0), "roleId", 0)); // unknown field: the setParty family is 1-based, this index is not
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0065, 0, 2, 0), "battleSprite", 2)); // unknown field: 0x0031's F.MKF battle override
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0065, 0, 2, 0), "rgwSpriteNum", 2)); // unknown field (the global's own name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0065, 0, 2, 0), "inBattle", true)); // unknown field: the battle gate is read from the globals, not an operand
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0065, 0, 2, 0), "playerRole", 65_536));
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0065, 0, 2, 0), "reload", 65_536));
    }

    // --- 0x006D setSceneScripts (#78 named, #112 re-verified, shared,
    //     script.c:2065-2089): "Set the enter script and teleport script for a
    //     scene" - rgScene[operand0 - 1].wScriptOnEnter/wScriptOnTeleport take
    //     operand1/2 when non-zero, a zero operand keeps that script, and both
    //     zero clear both; operand0 gates the whole case (WORD 0 skips). The
    //     sceneId/onEnter/onTeleport mapping holds; the entries are
    //     cross-structure chunk-4 WORDs like setAutoEntry.entry (real onEnter
    //     reaches 36203 past the signed range). Real data: 62 hits. ---
    [Fact]
    public void SetSceneScripts_reverified_keeps_scene_row_and_entry_words()
    {
        byte[] original = Words(0x006D, 2, 19792, 0); // the real-data x9 form (sceneId 2, onEnter 0x4D50)
        var command = Decode(0, 0x006D, 2, 19792, 0);
        Assert.Equal("setSceneScripts", (string)command["op"]!);
        Assert.Equal(2, (int)command["sceneId"]!);
        Assert.Equal(19792, (int)command["onEnter"]!);
        Assert.Equal(0, (int)command["onTeleport"]!); // WORD 0 keeps the existing teleport script

        Assert.Equal(original, Reencode(command)); // all three mapped WORDs re-encode byte-exact
        Assert.Equal(Words(0x006D, 2, 20697, 0), EditAndReencode(Decode(0, 0x006D, 2, 19792, 0), "onEnter", 20697)); // the real x10 onEnter form (0x50D9)
        Assert.Equal(Words(0x006D, 2, 19792, 11870), EditAndReencode(Decode(0, 0x006D, 2, 19792, 0), "onTeleport", 11870)); // the real sole onTeleport form (0x2E5E)
        Assert.Equal(Words(0x006D, 2, 0, 0), EditAndReencode(Decode(0, 0x006D, 2, 19792, 0), "onEnter", 0)); // both zero: the case's clear-both rule
        Assert.Equal(Words(0x006D, 2, 36203, 0), EditAndReencode(Decode(0, 0x006D, 2, 19792, 0), "onEnter", 36203)); // the real top onEnter (0x8D6B) past the signed range
        Assert.Equal(Words(0x006D, 294, 19792, 0), EditAndReencode(Decode(0, 0x006D, 2, 19792, 0), "sceneId", 294)); // the real top scene row
        Assert.Equal(Words(0x006D, 0, 19792, 0), EditAndReencode(Decode(0, 0x006D, 2, 19792, 0), "sceneId", 0)); // WORD 0: the whole-case skip form still encodes 0

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x006D, 2, 19792, 0), "onEnter", 0));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x006D, 2, 19792, 0), "to", "L0001")); // unknown field: the entries are plain WORDs, not in-file labels
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x006D, 2, 19792, 0), "entry", 19792)); // unknown field: the setAutoEntry/setTriggerEntry shape is per object
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x006D, 2, 19792, 0), "wScriptOnEnter", 19792)); // unknown field (the struct's own name)
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x006D, 2, 19792, 0), "onEnter", 65_536));
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x006D, 2, 19792, 0), "onTeleport", 65_536));
    }

    // --- 0x0075 setParty (#78 named, #112 re-verified, shared, script.c:2164-2197):
    //     "Set the player party" - each non-zero operand i fills the next rgParty
    //     slot with wPlayerRole = operand - 1 (1-based role id, WORD 0 skips the
    //     slot); all-zero operands leave the Dream 2.11 hack party of role 0; the
    //     case then reloads the player sprites, clears rgPoisonStatus and updates
    //     equipment. The roleId0/1/2 mapping holds. Real data: 119 hits -
    //     roleId0 1..5 (1 x80 majority), roleId1 0..6, roleId2 0/3/5. ---
    [Fact]
    public void SetParty_reverified_keeps_the_three_role_ids()
    {
        byte[] original = Words(0x0075, 1, 2, 0); // a real-data multi-member form
        var command = Decode(0, 0x0075, 1, 2, 0);
        Assert.Equal("setParty", (string)command["op"]!);
        Assert.Equal(1, (int)command["roleId0"]!);
        Assert.Equal(2, (int)command["roleId1"]!);
        Assert.Equal(0, (int)command["roleId2"]!); // WORD 0 skips the third slot

        Assert.Equal(original, Reencode(command)); // all three mapped WORDs re-encode byte-exact
        Assert.Equal(Words(0x0075, 1, 0, 0), EditAndReencode(Decode(0, 0x0075, 1, 2, 0), "roleId1", 0)); // the real solo majority form
        Assert.Equal(Words(0x0075, 3, 2, 0), EditAndReencode(Decode(0, 0x0075, 1, 2, 0), "roleId0", 3)); // the real x17 first-slot form (roleId1 untouched)
        Assert.Equal(Words(0x0075, 1, 2, 5), EditAndReencode(Decode(0, 0x0075, 1, 2, 0), "roleId2", 5)); // the real third-slot form
        Assert.Equal(Words(0x0075, 1, 6, 0), EditAndReencode(Decode(0, 0x0075, 1, 2, 0), "roleId1", 6)); // the 1-based top role
        Assert.Equal(Words(0x0075, 0, 0, 0), Reencode(Decode(0, 0x0075, 0, 0, 0))); // the all-zero hack-party source form (runtime Dream 2.11 rule)

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0075, 1, 2, 0), "roleId2", 5));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0075, 1, 2, 0), "playerRole", 0)); // unknown field: the 0-based shape is 0x0065's, not this verb's
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0075, 1, 2, 0), "spriteNum0", 1)); // unknown field: 0x0098's follower sprites are MGO numbers
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0075, 1, 2, 0), "roleId", 1)); // unknown field: the slots are individually named
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0075, 1, 2, 0), "roleId0", 65_536));
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0075, 1, 2, 0), "roleId1", 65_536));
    }

    // --- 0x0099 changeSceneMap (new, shared, script.c:2742-2756): "Change the
    //     map for the specified scene" - the scene.json row named by operand0
    //     (1-based, rgScene[sceneId - 1]) takes operand1 as wMapNum (the map
    //     number PAL_LoadMap consumes at res.c:252); the 0xFFFF sentinel form
    //     retargets the current scene and reloads it immediately. Real data: 2
    //     hits (@7C71/@7C8A) - both the sentinel form with mapNum 164/165. ---
    [Fact]
    public void ChangeSceneMap_edits_scene_row_map_and_the_sentinel()
    {
        byte[] original = Words(0x0099, unchecked((int)0xFFFF), 164, 0); // the real-data @7C71 form
        var command = Decode(0, 0x0099, unchecked((int)0xFFFF), 164, 0);
        Assert.Equal("changeSceneMap", (string)command["op"]!);
        Assert.Equal(65_535, (int)command["sceneId"]!); // the sentinel stays the raw unsigned WORD
        Assert.Equal(164, (int)command["mapNum"]!);

        Assert.Equal(original, Reencode(command)); // both mapped WORDs and the preserved operand2 re-encode byte-exact
        Assert.Equal(Words(0x0099, unchecked((int)0xFFFF), 165, 0), EditAndReencode(Decode(0, 0x0099, unchecked((int)0xFFFF), 164, 0), "mapNum", 165)); // the real @7C8A form
        Assert.Equal(Words(0x0099, 2, 164, 0), EditAndReencode(Decode(0, 0x0099, unchecked((int)0xFFFF), 164, 0), "sceneId", 2)); // the explicit 1-based row form
        Assert.Equal(Words(0x0099, 300, 164, 0), EditAndReencode(Decode(0, 0x0099, unchecked((int)0xFFFF), 164, 0), "sceneId", 300)); // the MAX_SCENES bound row
        Assert.Equal(Words(0x0099, 0, 164, 0), EditAndReencode(Decode(0, 0x0099, unchecked((int)0xFFFF), 164, 0), "sceneId", 0)); // WORD 0 indexes rgScene[-1] at runtime; the encoding keeps it

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0099, unchecked((int)0xFFFF), 164, 0), "mapNum", 165));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0099, unchecked((int)0xFFFF), 164, 0), "currentScene", true)); // unknown field: the sentinel is the WORD form, not a boolean
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0099, unchecked((int)0xFFFF), 164, 0), "wMapNum", 164)); // unknown field (the struct's own name)
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0099, unchecked((int)0xFFFF), 164, 0), "sceneId", -1)); // the sentinel is the unsigned WORD, not a signed -1
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0099, unchecked((int)0xFFFF), 164, 0), "reload", 1)); // unknown field: the reload is the sentinel form's side effect
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0099, unchecked((int)0xFFFF), 164, 0), "mapNum", 65_536));
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0099, unchecked((int)0xFFFF), 164, 0), "sceneId", 65_536));
    }

    // --- 0x0078 noop (#78 named, #112 re-verified, shared, script.c:2224-2228):
    //     the upstream "FIXME: ???" case - an empty body that breaks straight
    //     into the InterpretInstruction tail return: a genuine no-operation;
    //     the case reads no operand and the reference offers no stronger
    //     semantics to declare. Real data: 35 hits, all three operands zero. ---
    [Fact]
    public void Noop_reverified_reads_no_operands_and_preserves_source_words()
    {
        byte[] original = Words(0x0078, 0, 0, 0); // the real-data all-zero form (x35)
        var command = Decode(0, 0x0078, 0, 0, 0);
        Assert.Equal("noop", (string)command["op"]!);
        Assert.Null(command["frames"]); // no mapped field exists
        Assert.Null(command["musicId"]);

        Assert.Equal(original, Reencode(command)); // the preserved triple re-encodes byte-exact

        // Every operand field name is rejected: the case reads no operand.
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0078, 0, 0, 0), "frames", 1));
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0078, 0, 0, 0), "musicId", 1));
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0078, 0, 0, 0), "sceneId", 1));
    }

    // --- 0x00A7 advanceEntry (new, AUTO-ONLY - the batch's new executor shape,
    //     script.c:3641-3643): the one case of PAL_RunAutoScript's own switch
    //     beyond the shared set - a bare wScriptEntry++ with no upstream comment
    //     and no operand read: the standard one-instruction-per-frame advance,
    //     i.e. a do-nothing entry; a trigger execution would fall into
    //     PAL_InterpretInstruction's default TerminateOnError (script.c:3077).
    //     The verb reuses advanceEntry (0x0008's trigger-switch-only mirror).
    //     Real data: 295 hits, all three operands zero. ---
    [Fact]
    public void AdvanceEntry_auto_only_advances_one_entry_and_reads_no_operands()
    {
        byte[] original = Words(0x00A7, 0, 0, 0); // the real-data all-zero form (x295)
        var command = Decode(0, 0x00A7, 0, 0, 0);
        Assert.Equal("advanceEntry", (string)command["op"]!); // the shared verb of 0x0008's trigger mirror
        Assert.Null(command["to"]); // no mapped field exists
        var descriptor = OpcodeRegistry.ByOpcode[0x00A7];
        Assert.Equal(OpcodeExecutor.AutoSwitchOnly, descriptor.Executor); // the new auto-only executor shape
        Assert.Equal(OpcodeSuccessor.Sequential, descriptor.Successor); // the bare advance is the standard successor
        Assert.Equal(OpcodeExecutor.TriggerSwitchOnly, OpcodeRegistry.ByOpcode[0x0008].Executor); // the mirrored switch ownership

        Assert.Equal(original, Reencode(command)); // the preserved triple re-encodes byte-exact
        Assert.Equal(Words(0x0008, 0, 0, 0), Reencode(Decode(0, 0x0008, 0, 0, 0))); // the trigger mirror keeps its own opcode bit
        Assert.Equal(Words(0x00A7, 1, 2, 3), Reencode(Decode(0, 0x00A7, 1, 2, 3))); // carried source words stay preserved WORDs

        // Every operand field name is rejected: the case reads no operand.
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x00A7, 0, 0, 0), "frames", 1));
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x00A7, 0, 0, 0), "to", "L0001"));
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x00A7, 0, 0, 0), "noop", true)); // unknown field: the do-nothing nature is not an operand
    }
}
