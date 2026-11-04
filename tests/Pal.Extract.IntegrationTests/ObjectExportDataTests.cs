using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace Pal.Extract.Tests;

// CLI-level proof of the object union table registration (#81 player/item views,
// #82 magic/enemy views): a miniature SSS.MKF whose chunk 2 is hand-authored
// OBJECT records plus the DATA.MKF view-evidence chunks
// (store/enemy/enemy-team/player-roles) must decode through `export-data` with one
// declared view per record and the frozen index manifest.
public sealed class ObjectExportDataTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"pal-export-data-object-{Guid.NewGuid():N}");
    private readonly string input;

    public ObjectExportDataTests()
    {
        input = Path.Combine(root, "input");
        Directory.CreateDirectory(input);
    }

    // Hand-authored little-endian EVENTOBJECT/SCENE records (same literals as the
    // #79/#80 tests): chunks 0 and 1 must stay valid now that export-data registers
    // more than the flat tables.
    private static byte[] EventObjectRecord1 => Words(-32768, 0x1234, 0, -1, 0x8000, 65535, unchecked((short)0x8000), 8, 0xABCD, 0, 3, 32767, 1, 65535, 2, 32768);
    private static byte[] EventObjectRecord2 => Words(-15, 1840, 1200, 2, 10000, 0, 32767, 5, 0x1234, 65535, 0, 0, 0, 0, 4660, 0);
    private static byte[] SceneRecord1 => Words(65535, 0x1234, 0, 65535);
    private static byte[] SceneRecord2 => Words(1, 32768, 0xABCD, 5077);

    // Hand-authored WIN95 OBJECT records (same literals as the unit tests): a player
    // record (evidence: rgwName[0] = 0), an item record with flags 0 (store sells item
    // 1), an item record with flags 65535 (store sells item 2), a magic record (real
    // object[295], evidence: rgwMagic/enemy wMagic), and enemy records (real
    // object[536]/object[539], evidence: the enemy team).
    private static byte[] PlayerRecord => Words(0, 0, 43445, 43374, 0x1234, 65535, 32768);
    private static byte[] ItemRecordFlagsZero => Words(197, 150, 43022, 39140, 0, 40661, 0);
    private static byte[] ItemRecordFlagsMax => Words(0xABCD, 2000, 39554, 0, 42890, 40524, 65535);
    private static byte[] MagicRecord => Words(47, 0, 43028, 0, 0, 43308, 18);
    private static byte[] EnemyRecord => Words(138, 0, 0, 42828, 0, 0, 0);
    private static byte[] EnemyRecordWithResistance => Words(141, 10, 42394, 0, 0, 0, 0);
    private static byte[] UndeterminedRecord => Words(1, 2, 3, 4, 5, 6, 7);

    private static byte[] Words(params int[] values)
    {
        var bytes = new byte[values.Length * 2];
        for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), unchecked((ushort)values[i]));
        return bytes;
    }

    // Writes the DATA.MKF view-evidence fixture: one store selling items 1 and 2, one
    // empty enemy, an empty enemy team, and PLAYERROLES whose rgwName[0] = 0 (a name
    // word of 0 is still player evidence; fight.c reads rgObject[rgwName[w]].player);
    // the trailing 32 zero bytes are DATA chunk 4 as the registered magic table (#88,
    // its zero type exporting the layerOffset view), the 12 zero bytes DATA chunk 5
    // as the registered battle-field table (#89, five signed zeros), and the 20 zero
    // bytes DATA chunk 6 as the registered level-up-magic table (#90, five level 0 /
    // learns-nothing role pairs); none is view evidence.
    private static void WriteDataFixture(string input) =>
        WriteDataFixture(input, Words(1, 2, 0, 0, 0, 0, 0, 0, 0));

    private static void WriteDataFixture(string input, byte[] store) =>
        WriteDataFixture(input, store, new byte[70], new byte[10], new byte[900]);

    private static void WriteDataFixture(string input, byte[] store, byte[] enemy, byte[] enemyTeam, byte[] roles) =>
        MkfFixture.Write(input, "DATA.MKF", store, enemy, enemyTeam, roles, new byte[32], new byte[12], new byte[20]);

    [Fact]
    public async Task Export_data_extracts_object_records_with_views_and_the_index_entry()
    {
        MkfFixture.Write(input, "SSS.MKF",
            [.. EventObjectRecord1, .. EventObjectRecord2], [.. SceneRecord1, .. SceneRecord2],
            [.. PlayerRecord, .. ItemRecordFlagsZero, .. ItemRecordFlagsMax], Array.Empty<byte>(), new byte[24]);
        WriteDataFixture(input);
        string output = Path.Combine(root, "data");
        var result = await CliProcess.Run(root, "export-data", "--input", input, "--output", output);
        Assert.True(result.ExitCode == 0, result.Stderr);
        Assert.Equal(new[] { Path.Combine(output, "battle-field.json"), Path.Combine(output, "enemy-team.json"), Path.Combine(output, "enemy.json"), Path.Combine(output, "event-object.json"), Path.Combine(output, "index.json"), Path.Combine(output, "level-up-magic.json"), Path.Combine(output, "magic.json"), Path.Combine(output, "object.json"), Path.Combine(output, "player-roles.json"), Path.Combine(output, "scene.json"), Path.Combine(output, "store.json") },
            Directory.GetFiles(output).Order());

        using var table = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "object.json")));
        Assert.Equal(1, table.RootElement.GetProperty("formatVersion").GetInt32());
        Assert.Equal("faithful", table.RootElement.GetProperty("profile").GetString());
        Assert.Equal("object", table.RootElement.GetProperty("fileId").GetString());
        var records = table.RootElement.GetProperty("records");
        Assert.Equal(3, records.GetArrayLength());

        var player = records[0];
        Assert.Equal("player", player.GetProperty("view").GetString());
        Assert.Equal(43445, player.GetProperty("scriptOnFriendDeath").GetInt32());
        Assert.Equal(43374, player.GetProperty("scriptOnDying").GetInt32());

        var zero = records[1];
        Assert.Equal("item", zero.GetProperty("view").GetString());
        Assert.Equal(197, zero.GetProperty("bitmap").GetInt32());
        Assert.Equal(150, zero.GetProperty("price").GetInt32());
        Assert.Equal(40661, zero.GetProperty("scriptDesc").GetInt32());
        Assert.Equal(0, zero.GetProperty("flags").GetInt32());
        foreach (string playerField in new[] { "reserved", "scriptOnDying", "padding" })
            Assert.False(zero.TryGetProperty(playerField, out _)); // one view per record

        var max = records[2];
        Assert.Equal("item", max.GetProperty("view").GetString());
        Assert.Equal(65535, max.GetProperty("flags").GetInt32());
        Assert.Equal(42890, max.GetProperty("scriptOnThrow").GetInt32());

        using var index = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "index.json")));
        var tables = index.RootElement.GetProperty("tables").EnumerateArray().ToArray();
        Assert.Equal(10, tables.Length);
        Assert.Equal("battle-field.json", tables[0].GetProperty("file").GetString()); // lexicographic file order
        Assert.Equal("enemy-team.json", tables[1].GetProperty("file").GetString());
        Assert.Equal("enemy.json", tables[2].GetProperty("file").GetString());
        Assert.Equal("event-object.json", tables[3].GetProperty("file").GetString());
        var entry = tables.Single(table => table.GetProperty("file").GetString() == "object.json");
        Assert.Equal("SSS.MKF", entry.GetProperty("archive").GetString());
        Assert.Equal(2, entry.GetProperty("chunk").GetInt32());
        Assert.Equal(3, entry.GetProperty("recordCount").GetInt32());
        Assert.Equal(42, entry.GetProperty("byteLength").GetInt32());
        // Independent expectation: hand-verified SHA-256 of the 42 literal bytes above.
        Assert.Equal("a709ee29c2bee27c6d72f651d275bb65816f6c4aa714cf368c27eb4a6b6442f7",
            entry.GetProperty("sha256").GetString());
    }

    [Fact]
    public async Task Export_data_extracts_magic_and_enemy_view_records_from_the_data_evidence()
    {
        // Records: 0 player, 1 item, 2 magic, 3/4 enemy (enemy-team evidence),
        // 5/6 undetermined (no reference at all). Seven records keep the chunk length
        // (98) a multiple of 14 but not of 12, so the WIN95 variant stays decidable.
        MkfFixture.Write(input, "SSS.MKF",
            [.. EventObjectRecord1, .. EventObjectRecord2], [.. SceneRecord1, .. SceneRecord2],
            [.. PlayerRecord, .. ItemRecordFlagsZero, .. MagicRecord, .. EnemyRecord, .. EnemyRecordWithResistance, .. UndeterminedRecord, .. UndeterminedRecord],
            Array.Empty<byte>(), new byte[24]);
        // Evidence: the store sells item 1; the first ENEMY casts object 2 and the
        // the second carries wMagic 0xFFFF (= no magic, not evidence - a surviving value
        // would index outside the 7-record table and fail); the enemy team fields
        // objects 3 and 4; PLAYERROLES names object 0 and has rgwMagic[0][0] = 2.
        var enemyWords = new int[35];
        enemyWords[15] = 2;
        var noMagicWords = new int[35];
        noMagicWords[15] = 0xFFFF;
        var roleWords = new int[450];
        roleWords[18] = 0;
        roleWords[192] = 2;
        WriteDataFixture(input, Words(1, 0, 0, 0, 0, 0, 0, 0, 0),
            Words([.. enemyWords, .. noMagicWords]), Words(3, 4, 0xFFFF, 0, 0), Words(roleWords));
        string output = Path.Combine(root, "data");
        var result = await CliProcess.Run(root, "export-data", "--input", input, "--output", output);
        Assert.True(result.ExitCode == 0, result.Stderr);

        using var table = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "object.json")));
        var records = table.RootElement.GetProperty("records");
        Assert.Equal(7, records.GetArrayLength());

        Assert.Equal("player", records[0].GetProperty("view").GetString());
        Assert.Equal("item", records[1].GetProperty("view").GetString());

        var magic = records[2];
        Assert.Equal("magic", magic.GetProperty("view").GetString());
        Assert.Equal(47, magic.GetProperty("magicId").GetInt32());
        Assert.Equal(0, magic.GetProperty("reserved1").GetInt32());
        Assert.Equal(43028, magic.GetProperty("scriptOnSuccess").GetInt32());
        Assert.Equal(0, magic.GetProperty("scriptOnUse").GetInt32());
        Assert.Equal(0, magic.GetProperty("scriptDesc").GetInt32());
        Assert.Equal(43308, magic.GetProperty("reserved2").GetInt32());
        Assert.Equal(18, magic.GetProperty("flags").GetInt32());
        Assert.False(magic.TryGetProperty("bitmap", out _)); // one view per record

        var enemy = records[3];
        Assert.Equal("enemy", enemy.GetProperty("view").GetString());
        Assert.Equal(138, enemy.GetProperty("enemyId").GetInt32());
        Assert.Equal(0, enemy.GetProperty("resistanceToSorcery").GetInt32());
        Assert.Equal(42828, enemy.GetProperty("scriptOnBattleEnd").GetInt32());
        Assert.Equal(0, enemy.GetProperty("scriptOnReady").GetInt32());
        Assert.Equal(2, enemy.GetProperty("padding").GetArrayLength()); // WIN95: WORD 5/6 preserved

        var resistant = records[4];
        Assert.Equal("enemy", resistant.GetProperty("view").GetString());
        Assert.Equal(141, resistant.GetProperty("enemyId").GetInt32());
        Assert.Equal(10, resistant.GetProperty("resistanceToSorcery").GetInt32());
        Assert.Equal(42394, resistant.GetProperty("scriptOnTurnStart").GetInt32());

        var undetermined = records[5];
        Assert.Equal("undetermined", undetermined.GetProperty("view").GetString());
        Assert.Equal(7, undetermined.GetProperty("data").GetArrayLength());
        Assert.Equal("undetermined", records[6].GetProperty("view").GetString());

        using var index = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "index.json")));
        var entry = index.RootElement.GetProperty("tables").EnumerateArray()
            .Single(table => table.GetProperty("file").GetString() == "object.json");
        Assert.Equal(7, entry.GetProperty("recordCount").GetInt32());
        Assert.Equal(98, entry.GetProperty("byteLength").GetInt32());
        // Independent expectation: hash of the 98 hand-authored bytes above.
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(
                [.. PlayerRecord, .. ItemRecordFlagsZero, .. MagicRecord, .. EnemyRecord, .. EnemyRecordWithResistance, .. UndeterminedRecord, .. UndeterminedRecord])),
            entry.GetProperty("sha256").GetString());
    }

    [Fact]
    public async Task Export_data_extracts_dos_object_records_without_the_script_desc_slot()
    {
        byte[] dosPlayer = Words(0, 0, 5, 6, 0x1234, 0xABCD);
        byte[] dosMagic = Words(47, 0, 43028, 0, 43308, 18);
        byte[] dosEnemy = Words(138, 0, 0, 42828, 0, 0x55AA);
        byte[] dosUndetermined = Words(9, 8, 7, 6, 5, 4);
        MkfFixture.Write(input, "SSS.MKF",
            [.. EventObjectRecord1, .. EventObjectRecord2], [.. SceneRecord1, .. SceneRecord2],
            [.. dosPlayer, .. dosMagic, .. dosEnemy, .. dosUndetermined], Array.Empty<byte>(), new byte[24]);
        // Evidence: PLAYERROLES names object 0 and learns magic 1 (rgwMagic[0][0] = 1);
        // the ENEMY carries wMagic 0xFFFF (no magic: not evidence); the enemy team
        // fields object 2; the empty store leaves nothing else.
        var enemyWords = new int[35];
        enemyWords[15] = 0xFFFF;
        var roleWords = new int[450];
        roleWords[18] = 0;
        roleWords[192] = 1;
        WriteDataFixture(input, new byte[18], Words(enemyWords), Words(2, 0xFFFF, 0, 0, 0), Words(roleWords));
        string output = Path.Combine(root, "data");
        var result = await CliProcess.Run(root, "export-data", "--input", input, "--output", output);
        Assert.True(result.ExitCode == 0, result.Stderr);

        using var table = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "object.json")));
        var records = table.RootElement.GetProperty("records");
        Assert.Equal(4, records.GetArrayLength());
        Assert.Equal("player", records[0].GetProperty("view").GetString());
        Assert.Equal(6, records[0].GetProperty("scriptOnDying").GetInt32());
        Assert.Equal(2, records[0].GetProperty("padding").GetArrayLength()); // DOS: 2 padding words

        var magic = records[1];
        Assert.Equal("magic", magic.GetProperty("view").GetString());
        Assert.Equal(47, magic.GetProperty("magicId").GetInt32());
        Assert.Equal(43028, magic.GetProperty("scriptOnSuccess").GetInt32());
        Assert.Equal(43308, magic.GetProperty("reserved2").GetInt32()); // DOS layout: WORD 4
        Assert.Equal(18, magic.GetProperty("flags").GetInt32());        // DOS layout: WORD 5
        Assert.False(magic.TryGetProperty("scriptDesc", out _));        // DOS has no description slot

        var enemy = records[2];
        Assert.Equal("enemy", enemy.GetProperty("view").GetString());
        Assert.Equal(138, enemy.GetProperty("enemyId").GetInt32());
        Assert.Equal(42828, enemy.GetProperty("scriptOnBattleEnd").GetInt32());
        Assert.Equal(1, enemy.GetProperty("padding").GetArrayLength()); // DOS: 1 padding word

        Assert.Equal("undetermined", records[3].GetProperty("view").GetString());
        Assert.Equal(6, records[3].GetProperty("data").GetArrayLength());

        using var index = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "index.json")));
        var entry = index.RootElement.GetProperty("tables").EnumerateArray()
            .Single(table => table.GetProperty("file").GetString() == "object.json");
        Assert.Equal(4, entry.GetProperty("recordCount").GetInt32());
        Assert.Equal(48, entry.GetProperty("byteLength").GetInt32());
    }

    [Fact]
    public async Task Export_data_fails_when_chunk_2_is_not_a_multiple_of_14_or_12_bytes()
    {
        MkfFixture.Write(input, "SSS.MKF",
            [.. EventObjectRecord1, .. EventObjectRecord2], [.. SceneRecord1, .. SceneRecord2],
            new byte[13], Array.Empty<byte>(), new byte[24]);
        WriteDataFixture(input);
        string output = Path.Combine(root, "data");
        var result = await CliProcess.Run(root, "export-data", "--input", input, "--output", output);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("SSS.MKF", result.Stderr, StringComparison.Ordinal);
        Assert.Contains("chunk 2", result.Stderr, StringComparison.Ordinal);
        Assert.Contains("13", result.Stderr, StringComparison.Ordinal);
        Assert.False(Directory.Exists(output)); // no partial output is written
    }

    [Fact]
    public async Task Export_data_fails_when_a_data_evidence_chunk_is_missing()
    {
        MkfFixture.Write(input, "SSS.MKF",
            [.. EventObjectRecord1, .. EventObjectRecord2], [.. SceneRecord1, .. SceneRecord2],
            new byte[28], Array.Empty<byte>(), new byte[24]);
        string output = Path.Combine(root, "data");

        MkfFixture.Write(input, "DATA.MKF", Words(1, 2, 0, 0, 0, 0, 0, 0, 0), new byte[70]); // no chunk 2: enemy team missing
        var noEnemyTeam = await CliProcess.Run(root, "export-data", "--input", input, "--output", output);
        Assert.Equal(1, noEnemyTeam.ExitCode);
        Assert.Contains("DATA.MKF", noEnemyTeam.Stderr, StringComparison.Ordinal);
        // The registered battle-field table (DATA chunk 5, #89) sorts first among the
        // DATA tables in file order and names the first missing DATA chunk.
        Assert.Contains("chunk 5", noEnemyTeam.Stderr, StringComparison.Ordinal);
        Assert.False(Directory.Exists(output));

        MkfFixture.Write(input, "DATA.MKF", Words(1, 2, 0, 0, 0, 0, 0, 0, 0), new byte[70], new byte[10]); // no chunk 3, so chunks 4, 5, and 6 are absent too
        var noRoles = await CliProcess.Run(root, "export-data", "--input", input, "--output", output);
        Assert.Equal(1, noRoles.ExitCode);
        Assert.Contains("DATA.MKF", noRoles.Stderr, StringComparison.Ordinal);
        // Same first-named missing chunk as above: the registered battle-field table
        // (chunk 5) is decoded before the roles evidence (chunk 3).
        Assert.Contains("chunk 5", noRoles.Stderr, StringComparison.Ordinal);
        Assert.False(Directory.Exists(output));
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
