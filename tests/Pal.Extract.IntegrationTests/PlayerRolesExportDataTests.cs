using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace Pal.Extract.Tests;

// CLI-level proof of the player-roles table registration (#87, completing the
// #86/#87 split): a miniature SSS.MKF whose three chunks are valid for the
// registered tables plus a hand-authored DATA.MKF chunk 3 must decode through
// `export-data` with the frozen attribute-group fields, the structured #87
// equipment/magic/sound/cover fields, and the index entry. DATA chunk 3 is both
// the exported player-roles table and the object view evidence source, so the
// record literals here double as valid evidence: the six name words 36..41 (the
// real-data shape) stay inside the 43-record object chunk and are disjoint from
// the item evidence (equipment 10/11) and the magic evidence (magic slot 0 and
// cooperativeMagic 12/13), so the exported name/equipment/magic fields and the
// object.json views stay consistent. The record bytes are the same literals the
// #86 tests used, so the placeholder-free structured names carry identical
// WORDs - the lossless replacement pinned at the CLI level.
public sealed class PlayerRolesExportDataTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"pal-export-data-player-roles-{Guid.NewGuid():N}");
    private readonly string input;

    public PlayerRolesExportDataTests()
    {
        input = Path.Combine(root, "input");
        Directory.CreateDirectory(input);
    }

    // Hand-authored little-endian EVENTOBJECT/SCENE records (same literals as the
    // #79/#80 tests): chunks 0 and 1 must stay valid now that export-data registers
    // more than the SSS tables.
    private static byte[] EventObjectRecord1 => Words(-32768, 0x1234, 0, -1, 0x8000, 65535, unchecked((short)0x8000), 8, 0xABCD, 0, 3, 32767, 1, 65535, 2, 32768);
    private static byte[] EventObjectRecord2 => Words(-15, 1840, 1200, 2, 10000, 0, 32767, 5, 0x1234, 65535, 0, 0, 0, 0, 4660, 0);
    private static byte[] SceneRecord1 => Words(65535, 0x1234, 0, 65535);
    private static byte[] SceneRecord2 => Words(1, 32768, 0xABCD, 5077);

    // The hand-authored 900-byte PLAYERROLES record, one frozen-schema field per
    // row (same literals as the unit tests; serialized little-endian).
    private static int[] Avatar => [1, 2, 3, 4, 5, 6];
    private static int[] BattleSprite => [65535, 0, 0x8000, 32767, 0x1234, 0xABCD];
    private static int[] SpriteNum => [0, 65535, 1, 0x8000, 32767, 0x1234];
    private static int[] Name => [36, 37, 38, 40, 39, 41]; // player-view evidence indices
    private static int[] AttackAll => [0, 1, 0, 1, 0, 0];
    private static int[] Unknown1 => [0, 0, 0, 0, 0, 65535];
    private static int[] Level => [1, 10, 99, 5, 28, 34];
    private static int[] MaxHp => [65535, 100, 0, 0x8000, 12345, 999];
    private static int[] MaxMp => [0, 65535, 50, 32768, 100, 777];
    private static int[] Hp => [65535, 1, 0, 32767, 12345, 0];
    private static int[] Mp => [0, 65535, 49, 1, 0, 777];
    private static int[][] EquipmentRows =>
    [
        [10, 0, 0, 0, 0, 0],
        [0, 11, 0, 0, 0, 0],
        [10, 11, 0, 0, 0, 0],
        [0, 0, 10, 0, 0, 0],
        [0, 0, 0, 11, 0, 0],
        [0, 0, 0, 0, 10, 11],
    ];
    private static int[] AttackStrength => [100, 200, 300, 65535, 0, 42];
    private static int[] MagicStrength => [50, 0, 65535, 10, 20, 30];
    private static int[] Defense => [25, 35, 45, 55, 65, 75];
    private static int[] Dexterity => [90, 80, 70, 60, 65535, 0];
    private static int[] FleeRate => [10, 20, 30, 40, 50, 60];
    private static int[] PoisonResistance => [0, 5, 10, 65535, 1, 2];
    private static int[][] ElementalRows =>
    [
        [0, 1, 2, 3, 4, 5],
        [10, 20, 30, 40, 50, 60],
        [65535, 0, 65535, 0, 1, 2],
        [100, 200, 300, 400, 500, 600],
        [7, 14, 21, 28, 35, 42],
    ];
    private static int[] Unknown2 => [1, 2, 3, 4, 5, 6];
    private static int[] Unknown3 => [6, 5, 4, 3, 2, 1];
    private static int[] Unknown4 => [0, 65535, 0x8000, 32767, 0x1234, 0xABCD];
    private static int[] CoveredBy => [0, 1, 2, 3, 4, 5];
    private static IEnumerable<int[]> MagicRows()
    {
        yield return [12, 13, 0, 0, 0, 0];
        for (int slot = 1; slot < 31; slot++) yield return [0, 0, 0, 0, 0, 0];
        yield return [12, 0, 13, 0, 12, 0];
    }
    private static int[] WalkFrames => [4, 4, 4, 4, 4, 4];
    private static int[] CooperativeMagic => [13, 0, 0, 0, 0, 0];
    private static int[] Unknown5 => [9, 8, 7, 6, 5, 4];
    private static int[] Unknown6 => [11, 22, 33, 44, 55, 66];
    private static int[] DeathSound => [0, 1, 2, 3, 4, 5];
    private static int[] AttackSound => [10, 20, 30, 40, 50, 60];
    private static int[] WeaponSound => [65535, 0, 1, 2, 3, 4];
    private static int[] CriticalSound => [100, 200, 300, 400, 500, 600];
    private static int[] MagicSound => [7, 14, 21, 28, 35, 42];
    private static int[] CoverSound => [0, 65535, 0x8000, 32767, 0x1234, 0xABCD];
    private static int[] DyingSound => [5, 4, 3, 2, 1, 0];

    private static byte[] Record { get; } = Words(
    [
        .. Avatar, .. BattleSprite, .. SpriteNum, .. Name, .. AttackAll, .. Unknown1,
        .. Level, .. MaxHp, .. MaxMp, .. Hp, .. Mp,
        .. EquipmentRows.SelectMany(row => row),
        .. AttackStrength, .. MagicStrength, .. Defense, .. Dexterity, .. FleeRate, .. PoisonResistance,
        .. ElementalRows.SelectMany(row => row),
        .. Unknown2, .. Unknown3, .. Unknown4, .. CoveredBy,
        .. MagicRows().SelectMany(row => row),
        .. WalkFrames, .. CooperativeMagic, .. Unknown5, .. Unknown6,
        .. DeathSound, .. AttackSound, .. WeaponSound, .. CriticalSound, .. MagicSound, .. CoverSound, .. DyingSound,
    ]);

    private static byte[] Words(params int[] values)
    {
        var bytes = new byte[values.Length * 2];
        for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), unchecked((ushort)values[i]));
        return bytes;
    }

    [Fact]
    public async Task Export_data_extracts_player_roles_fields_and_the_index_entry()
    {
        // SSS chunk 2: 43 all-zero WIN95 records (602 = 43 x 14 bytes, not a
        // multiple of 12, so the variant stays decidable). The role record below
        // decides views at 36..41 (player, via the six name words), 10/11 (item,
        // via equipment), and 12/13 (magic, via magic slot 0 and cooperativeMagic);
        // the other 33 records stay undetermined.
        MkfFixture.Write(input, "SSS.MKF",
            [.. EventObjectRecord1, .. EventObjectRecord2], [.. SceneRecord1, .. SceneRecord2], new byte[602]);
        MkfFixture.Write(input, "DATA.MKF", new byte[18], new byte[70], new byte[10], Record);
        string output = Path.Combine(root, "data");
        var result = await CliProcess.Run(root, "export-data", "--input", input, "--output", output);
        Assert.True(result.ExitCode == 0, result.Stderr);
        Assert.Equal(new[] { Path.Combine(output, "enemy-team.json"), Path.Combine(output, "enemy.json"), Path.Combine(output, "event-object.json"), Path.Combine(output, "index.json"), Path.Combine(output, "object.json"), Path.Combine(output, "player-roles.json"), Path.Combine(output, "scene.json"), Path.Combine(output, "store.json") },
            Directory.GetFiles(output).Order());

        using var table = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "player-roles.json")));
        Assert.Equal(1, table.RootElement.GetProperty("formatVersion").GetInt32());
        Assert.Equal("faithful", table.RootElement.GetProperty("profile").GetString());
        Assert.Equal("player-roles", table.RootElement.GetProperty("fileId").GetString());
        var records = table.RootElement.GetProperty("records");
        Assert.Equal(1, records.GetArrayLength());
        var record = records[0];
        AssertRow(record.GetProperty("name"), Name);           // the #86 structured attribute group
        AssertRow(record.GetProperty("level"), Level);
        AssertRow(record.GetProperty("maxHp"), MaxHp);
        AssertRow(record.GetProperty("hp"), Hp);
        AssertRow(record.GetProperty("poisonResistance"), PoisonResistance);
        AssertRows(record, "elementalResistance", ElementalRows);
        AssertRows(record, "equipment", EquipmentRows); // the #87 structured equipment/magic/sound/cover group
        var magic = record.GetProperty("magic");
        Assert.Equal(32, magic.GetArrayLength());
        AssertRow(magic[0], MagicRows().First());
        AssertRow(magic[31], MagicRows().Last());
        AssertRow(record.GetProperty("coveredBy"), CoveredBy);
        AssertRow(record.GetProperty("cooperativeMagic"), CooperativeMagic);
        AssertRow(record.GetProperty("dyingSound"), DyingSound);

        // The same DATA chunk 3 feeds the object view evidence: the exported name
        // words decide exactly the six player views, the equipment members 10/11
        // the two item views, and magic/cooperativeMagic 12/13 the two magic
        // views; no other record gains a view.
        using var objects = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "object.json")));
        var objectRecords = objects.RootElement.GetProperty("records");
        Assert.Equal(43, objectRecords.GetArrayLength());
        int players = 0, items = 0, magics = 0, undetermined = 0;
        for (int objectIndex = 0; objectIndex < objectRecords.GetArrayLength(); objectIndex++)
            switch (objectRecords[objectIndex].GetProperty("view").GetString())
            {
                case "player": Assert.Contains(objectIndex, Name); players++; break;
                case "item": Assert.Contains(objectIndex, EquipmentRows.SelectMany(row => row).Where(value => value != 0)); items++; break;
                case "magic": Assert.Contains(objectIndex, MagicRows().SelectMany(row => row).Where(value => value != 0).Append(CooperativeMagic[0])); magics++; break;
                default: undetermined++; break;
            }
        Assert.Equal(6, players);   // rgwName = [36,37,38,40,39,41], the exported name row
        Assert.Equal(2, items);     // equipment items 10/11
        Assert.Equal(2, magics);    // magic/cooperative magic objects 12/13
        Assert.Equal(33, undetermined);

        using var index = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "index.json")));
        var tables = index.RootElement.GetProperty("tables").EnumerateArray().ToArray();
        Assert.Equal(7, tables.Length);
        Assert.Equal("enemy-team.json", tables[0].GetProperty("file").GetString()); // lexicographic file order
        Assert.Equal("object.json", tables[3].GetProperty("file").GetString());
        Assert.Equal("player-roles.json", tables[4].GetProperty("file").GetString());
        var entry = tables.Single(table => table.GetProperty("file").GetString() == "player-roles.json");
        Assert.Equal("DATA.MKF", entry.GetProperty("archive").GetString());
        Assert.Equal(3, entry.GetProperty("chunk").GetInt32());
        Assert.Equal(1, entry.GetProperty("recordCount").GetInt32());
        Assert.Equal(900, entry.GetProperty("byteLength").GetInt32());
        // Independent expectation: hand-verified SHA-256 of the 900 literal bytes
        // above (Python hashlib and certutil both agree on this constant), plus an
        // in-test hash of the same record.
        Assert.Equal("46115d08c5d2c87a240bfdcc1a74c7cbe480957554b41b82c54b13753935cdef",
            entry.GetProperty("sha256").GetString());
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Record)), entry.GetProperty("sha256").GetString());
    }

    [Fact]
    public async Task Export_data_fails_when_data_chunk_3_is_not_a_multiple_of_900_bytes()
    {
        MkfFixture.Write(input, "SSS.MKF",
            [.. EventObjectRecord1, .. EventObjectRecord2], [.. SceneRecord1, .. SceneRecord2], new byte[602]);
        MkfFixture.Write(input, "DATA.MKF", new byte[18], new byte[70], new byte[10], new byte[899]); // chunk 3 defect only
        string output = Path.Combine(root, "data");
        var result = await CliProcess.Run(root, "export-data", "--input", input, "--output", output);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("DATA.MKF", result.Stderr, StringComparison.Ordinal);
        Assert.Contains("chunk 3", result.Stderr, StringComparison.Ordinal);
        Assert.Contains("899", result.Stderr, StringComparison.Ordinal);
        Assert.False(Directory.Exists(output)); // no partial output is written
    }

    private static void AssertRow(JsonElement row, int[] expected)
    {
        Assert.Equal(expected.Length, row.GetArrayLength());
        for (int index = 0; index < expected.Length; index++)
            Assert.Equal(expected[index], row[index].GetInt32());
    }

    private static void AssertRows(JsonElement record, string field, int[][] expected)
    {
        var rows = record.GetProperty(field);
        Assert.Equal(expected.Length, rows.GetArrayLength());
        for (int row = 0; row < expected.Length; row++)
            AssertRow(rows[row], expected[row]);
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
