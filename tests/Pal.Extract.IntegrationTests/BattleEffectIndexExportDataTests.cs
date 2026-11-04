using System.Buffers.Binary;
using System.Text.Json;
using Xunit;

namespace Pal.Extract.Tests;

// CLI-level proof of the three closing #91 tables (battle-effect-index DATA 11,
// enemy-pos DATA 13, level-up-exp DATA 14) through one hand-authored fixture: the
// thirteen-table manifest of the frozen #78 list is now complete, so the index
// asserts all 13 entries and all 14 output files for the first time. The chunk 11
// and 13 literals are the hand-verified real chunks byte for byte (the receipt's
// real-data check), the chunk 14 literal pairs the real first five thresholds and
// 32000 plateau with identity indexes and byte-swap sentinels, and the error path
// proves the chunk 11 length gate.
public sealed class BattleEffectIndexExportDataTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"pal-export-data-battle-effect-index-{Guid.NewGuid():N}");
    private readonly string input;

    public BattleEffectIndexExportDataTests()
    {
        input = Path.Combine(root, "input");
        Directory.CreateDirectory(input);
    }

    // Hand-authored little-endian EVENTOBJECT/SCENE records (same literals as the
    // #79/#80 tests): chunks 0 and 1 must stay valid now that export-data registers
    // all thirteen tables.
    private static byte[] EventObjectRecord1 => Words(-32768, 0x1234, 0, -1, 0x8000, 65535, unchecked((short)0x8000), 8, 0xABCD, 0, 3, 32767, 1, 65535, 2, 32768);
    private static byte[] EventObjectRecord2 => Words(-15, 1840, 1200, 2, 10000, 0, 32767, 5, 0x1234, 65535, 0, 0, 0, 0, 4660, 0);
    private static byte[] SceneRecord1 => Words(65535, 0x1234, 0, 65535);
    private static byte[] SceneRecord2 => Words(1, 32768, 0xABCD, 5077);

    // The real chunk 11 bytes, hand-verified from the local read-only Data.mkf: the
    // ten sprite rows (sprite 5 diverges to attack 3, sprite 7 pairs preMagic 4 with
    // attack 0, sprite 8 is the 6/4 pair).
    private static byte[] BattleEffectIndexRecord => Words(0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 3, 2, 2, 4, 0, 6, 4, 3, 3);

    // The real chunk 13 bytes, hand-verified the same way: the 5x5 PALPOS grid whose
    // row i comes live from column i (row 0: (100,110) then (70,130)/(70,140)s, row
    // 3: (50,100)/(30,110), row 4: (90,80)), unused lower cells all {0,0}.
    private static byte[] EnemyPosRecord => Words(
        100, 110, 70, 130, 70, 140, 70, 140, 70, 140,
        0, 0, 140, 106, 100, 110, 100, 110, 100, 110,
        0, 0, 0, 0, 160, 100, 160, 100, 160, 100,
        0, 0, 0, 0, 0, 0, 50, 100, 30, 110,
        0, 0, 0, 0, 0, 0, 0, 0, 90, 80);

    // The same 100-word literal as the unit tests: the real first five thresholds
    // (0, 15, 40, 90, 165), identity word indexes in between, 0x1234/0xABCD/0x8000
    // at indexes 47..49, and the real 32000 plateau ending at the 65535 boundary.
    private static byte[] LevelUpExpRecord => Words(Values);

    private static int[] Values
    {
        get
        {
            var values = Enumerable.Range(0, 100).ToArray();
            values[1] = 15;
            values[2] = 40;
            values[3] = 90;
            values[4] = 165;
            values[47] = 4660;
            values[48] = 43981;
            values[49] = 32768;
            for (int index = 95; index < 99; index++) values[index] = 32000;
            values[99] = 65535;
            return values;
        }
    }

    private static byte[] Words(params int[] values)
    {
        var bytes = new byte[values.Length * 2];
        for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), unchecked((ushort)values[i]));
        return bytes;
    }

    [Fact]
    public async Task Export_data_extracts_the_three_closing_tables_and_the_complete_index()
    {
        // SSS chunk 2: five all-zero WIN95 records (70 = 5 x 14 bytes, not a multiple
        // of 12, so the variant stays decidable); the all-zero DATA evidence makes
        // record 0 the player view via rgwName[0] = 0 and records 1..4 undetermined.
        MkfFixture.Write(input, "SSS.MKF",
            [.. EventObjectRecord1, .. EventObjectRecord2], [.. SceneRecord1, .. SceneRecord2], new byte[70]);
        // The full 15-chunk DATA fixture: chunks 0..6 as in #90, chunks 7-10 and 12
        // empty (no table is registered there), chunk 11 the battle-effect-index
        // record, chunk 13 the enemy-pos record, chunk 14 the level-up-exp record.
        MkfFixture.Write(input, "DATA.MKF",
            new byte[18], new byte[70], new byte[10], new byte[900], new byte[32], new byte[12], new byte[20],
            Array.Empty<byte>(), Array.Empty<byte>(), Array.Empty<byte>(), Array.Empty<byte>(),
            BattleEffectIndexRecord, Array.Empty<byte>(), EnemyPosRecord, LevelUpExpRecord);
        string output = Path.Combine(root, "data");
        var result = await CliProcess.Run(root, "export-data", "--input", input, "--output", output);
        Assert.True(result.ExitCode == 0, result.Stderr);
        // All thirteen frozen tables plus index.json - the complete #78 manifest.
        Assert.Equal(new[] { Path.Combine(output, "battle-effect-index.json"), Path.Combine(output, "battle-field.json"), Path.Combine(output, "enemy-pos.json"), Path.Combine(output, "enemy-team.json"), Path.Combine(output, "enemy.json"), Path.Combine(output, "event-object.json"), Path.Combine(output, "index.json"), Path.Combine(output, "level-up-exp.json"), Path.Combine(output, "level-up-magic.json"), Path.Combine(output, "magic.json"), Path.Combine(output, "object.json"), Path.Combine(output, "player-roles.json"), Path.Combine(output, "scene.json"), Path.Combine(output, "store.json") },
            Directory.GetFiles(output).Order());

        using var battleEffectIndex = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "battle-effect-index.json")));
        Assert.Equal(1, battleEffectIndex.RootElement.GetProperty("formatVersion").GetInt32());
        Assert.Equal("faithful", battleEffectIndex.RootElement.GetProperty("profile").GetString());
        Assert.Equal("battle-effect-index", battleEffectIndex.RootElement.GetProperty("fileId").GetString());
        var sprites = battleEffectIndex.RootElement.GetProperty("records")[0].GetProperty("sprites");
        Assert.Equal(10, sprites.GetArrayLength()); // exactly ten battle sprite rows
        Assert.Equal(0, sprites[0].GetProperty("preMagicEffectIndex").GetInt32());
        Assert.Equal(0, sprites[0].GetProperty("attackEffectIndex").GetInt32());
        Assert.Equal(5, sprites[5].GetProperty("preMagicEffectIndex").GetInt32()); // diverging attack row
        Assert.Equal(3, sprites[5].GetProperty("attackEffectIndex").GetInt32());
        Assert.Equal(4, sprites[7].GetProperty("preMagicEffectIndex").GetInt32()); // 4 with attack 0
        Assert.Equal(0, sprites[7].GetProperty("attackEffectIndex").GetInt32());
        Assert.Equal(6, sprites[8].GetProperty("preMagicEffectIndex").GetInt32()); // the 6/4 pair
        Assert.Equal(4, sprites[8].GetProperty("attackEffectIndex").GetInt32());

        using var enemyPos = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "enemy-pos.json")));
        Assert.Equal(1, enemyPos.RootElement.GetProperty("formatVersion").GetInt32());
        Assert.Equal("faithful", enemyPos.RootElement.GetProperty("profile").GetString());
        Assert.Equal("enemy-pos", enemyPos.RootElement.GetProperty("fileId").GetString());
        var positions = enemyPos.RootElement.GetProperty("records")[0].GetProperty("positions");
        Assert.Equal(5, positions.GetArrayLength()); // exactly MAX_ENEMIES_IN_TEAM rows
        var row0 = positions[0];
        Assert.Equal(100, row0[0].GetProperty("x").GetInt32()); // one enemy: (100, 110)
        Assert.Equal(110, row0[0].GetProperty("y").GetInt32());
        Assert.Equal(70, row0[1].GetProperty("x").GetInt32());  // two+: (70, 130)
        Assert.Equal(130, row0[1].GetProperty("y").GetInt32());
        Assert.Equal(0, positions[1][0].GetProperty("x").GetInt32()); // unused cell stays {0,0}
        Assert.Equal(140, positions[1][1].GetProperty("x").GetInt32());
        Assert.Equal(50, positions[3][3].GetProperty("x").GetInt32()); // row 3's live cells
        Assert.Equal(30, positions[3][4].GetProperty("x").GetInt32());
        Assert.Equal(110, positions[3][4].GetProperty("y").GetInt32());
        Assert.Equal(90, positions[4][4].GetProperty("x").GetInt32()); // five enemies: (90, 80)
        Assert.Equal(80, positions[4][4].GetProperty("y").GetInt32());

        using var levelUpExp = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "level-up-exp.json")));
        Assert.Equal(1, levelUpExp.RootElement.GetProperty("formatVersion").GetInt32());
        Assert.Equal("faithful", levelUpExp.RootElement.GetProperty("profile").GetString());
        Assert.Equal("level-up-exp", levelUpExp.RootElement.GetProperty("fileId").GetString());
        var thresholds = levelUpExp.RootElement.GetProperty("records")[0].GetProperty("levelUpExp");
        Assert.Equal(100, thresholds.GetArrayLength()); // exactly MAX_LEVELS + 1 thresholds
        Assert.Equal(0, thresholds[0].GetInt32());      // index 0 unused
        Assert.Equal(15, thresholds[1].GetInt32());     // real: exp to leave level 1
        Assert.Equal(165, thresholds[4].GetInt32());
        Assert.Equal(4660, thresholds[47].GetInt32());  // 0x1234
        Assert.Equal(43981, thresholds[48].GetInt32()); // 0xABCD
        Assert.Equal(32768, thresholds[49].GetInt32()); // 0x8000 stays a WORD
        Assert.Equal(32000, thresholds[95].GetInt32()); // real plateau
        Assert.Equal(65535, thresholds[99].GetInt32()); // the WORD boundary

        using var index = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "index.json")));
        var tables = index.RootElement.GetProperty("tables").EnumerateArray().ToArray();
        Assert.Equal(13, tables.Length); // the complete frozen list, #79-#91
        Assert.Equal("battle-effect-index.json", tables[0].GetProperty("file").GetString()); // lexicographic file order
        var battleEffectIndexEntry = tables[0];
        Assert.Equal("DATA.MKF", battleEffectIndexEntry.GetProperty("archive").GetString());
        Assert.Equal(11, battleEffectIndexEntry.GetProperty("chunk").GetInt32());
        Assert.Equal(1, battleEffectIndexEntry.GetProperty("recordCount").GetInt32());
        Assert.Equal(40, battleEffectIndexEntry.GetProperty("byteLength").GetInt32());
        // Independent expectation: the fixture's chunk 11 bytes are the hand-verified
        // real chunk, so this is its hand-computed SHA-256 (Python hashlib and
        // PowerShell Get-FileHash both agree).
        Assert.Equal("b815c493870790c3649e950cfb8ecb62249972a84ced7371641d4a7e4b4be68d",
            battleEffectIndexEntry.GetProperty("sha256").GetString());
        Assert.Equal("battle-field.json", tables[1].GetProperty("file").GetString());
        Assert.Equal("enemy-pos.json", tables[2].GetProperty("file").GetString());
        var enemyPosEntry = tables[2];
        Assert.Equal("DATA.MKF", enemyPosEntry.GetProperty("archive").GetString());
        Assert.Equal(13, enemyPosEntry.GetProperty("chunk").GetInt32());
        Assert.Equal(1, enemyPosEntry.GetProperty("recordCount").GetInt32());
        Assert.Equal(100, enemyPosEntry.GetProperty("byteLength").GetInt32());
        // Independent expectation: hand-computed SHA-256 of the real chunk 13 bytes.
        Assert.Equal("97a101a8dd6cad71b95692bdb07ee3a34a4c2875ff01db6a7e2f175a47a42741",
            enemyPosEntry.GetProperty("sha256").GetString());
        Assert.Equal("enemy-team.json", tables[3].GetProperty("file").GetString());
        Assert.Equal("enemy.json", tables[4].GetProperty("file").GetString());
        Assert.Equal("event-object.json", tables[5].GetProperty("file").GetString());
        Assert.Equal("level-up-exp.json", tables[6].GetProperty("file").GetString()); // between event-object and level-up-magic
        var levelUpExpEntry = tables[6];
        Assert.Equal("DATA.MKF", levelUpExpEntry.GetProperty("archive").GetString());
        Assert.Equal(14, levelUpExpEntry.GetProperty("chunk").GetInt32());
        Assert.Equal(1, levelUpExpEntry.GetProperty("recordCount").GetInt32());
        Assert.Equal(200, levelUpExpEntry.GetProperty("byteLength").GetInt32());
        // Independent expectation: hand-computed SHA-256 of the 200 literal bytes above.
        Assert.Equal("bf0824871e37d08ca1953f838e0ac701dc691b82544bb40373c01202b1708b76",
            levelUpExpEntry.GetProperty("sha256").GetString());
        Assert.Equal("level-up-magic.json", tables[7].GetProperty("file").GetString());
        Assert.Equal("magic.json", tables[8].GetProperty("file").GetString());
        Assert.Equal("object.json", tables[9].GetProperty("file").GetString());
        Assert.Equal("player-roles.json", tables[10].GetProperty("file").GetString());
        Assert.Equal("scene.json", tables[11].GetProperty("file").GetString());
        Assert.Equal("store.json", tables[12].GetProperty("file").GetString());
    }

    [Fact]
    public async Task Export_data_fails_when_data_chunk_11_is_not_a_multiple_of_40_bytes()
    {
        MkfFixture.Write(input, "SSS.MKF",
            [.. EventObjectRecord1, .. EventObjectRecord2], [.. SceneRecord1, .. SceneRecord2], new byte[70]);
        MkfFixture.Write(input, "DATA.MKF",
            new byte[18], new byte[70], new byte[10], new byte[900], new byte[32], new byte[12], new byte[20],
            Array.Empty<byte>(), Array.Empty<byte>(), Array.Empty<byte>(), Array.Empty<byte>(),
            new byte[41], Array.Empty<byte>(), new byte[100], new byte[200]); // chunk 11 defect only
        string output = Path.Combine(root, "data");
        var result = await CliProcess.Run(root, "export-data", "--input", input, "--output", output);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("DATA.MKF", result.Stderr, StringComparison.Ordinal);
        Assert.Contains("chunk 11", result.Stderr, StringComparison.Ordinal);
        Assert.Contains("41", result.Stderr, StringComparison.Ordinal);
        Assert.Contains("40", result.Stderr, StringComparison.Ordinal);
        Assert.False(Directory.Exists(output)); // no partial output is written
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
