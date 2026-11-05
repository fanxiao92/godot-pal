using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pal.Extract;
using Xunit;
using static Pal.Extract.Tests.OpcodeCounterexampleHarness;

namespace Pal.Extract.Tests;

// Object union table (SSS.MKF chunk 2, OBJECT, 14 bytes WIN95 / 12 bytes DOS) - #81
// (player/item views) and #82 (magic/enemy/poison views). Expected values are
// hand-computed from independently authored little-endian byte literals; the
// per-view schema is the frozen table in docs/design/data-schema.md, which mirrors
// reference/sdlpal/global.h (tagOBJECT_PLAYER / tagOBJECT_ITEM / tagOBJECT_MAGIC /
// tagOBJECT_ENEMY / tagOBJECT_POISON). Boundary words: 0, 65535, 0x8000 (32768
// unsigned), and real-data samples (43445 = object[36].wScriptOnFriendDeath,
// 40661 = object[61].wScriptDesc, 43028 = object[295].wScriptOnSuccess,
// 42828 = object[536].wScriptOnBattleEnd, 40862 = object[551].wPlayerScript).
// A record never carries two views: the declared view fixes the exact field set.
public sealed class ObjectTableTests
{
    // Hand-authored WIN95 records (7 WORDs each, little-endian). The magic/enemy/
    // poison literals are the real object[295]/object[536]/object[551] WORD groups.
    private static byte[] PlayerRecord => Words(0, 0, 43445, 43374, 0x1234, 65535, 32768);
    private static byte[] ItemRecordFlagsZero => Words(197, 150, 43022, 39140, 0, 40661, 0);
    private static byte[] ItemRecordFlagsMax => Words(0xABCD, 2000, 39554, 0, 42890, 40524, 65535);
    private static byte[] MagicRecordFlags18 => Words(47, 0, 43028, 0, 0, 43308, 18);
    private static byte[] EnemyRecord536 => Words(138, 0, 0, 42828, 0, 0, 0);
    private static byte[] PoisonRecord551 => Words(0, 16, 40862, 0, 40864, 0, 0);
    private static byte[] UndeterminedRecord => Words(1, 2, 3, 4, 5, 6, 7);

    private static ObjectViewEvidence EvidenceFor(int[] players, int[] items, int[]? magic = null, int[]? enemies = null, int[]? poison = null) =>
        new(new HashSet<int>(players), new HashSet<int>(items), new HashSet<int>(magic ?? []), new HashSet<int>(enemies ?? []), new HashSet<int>(poison ?? []));

    [Fact]
    public void DetectVariant_decides_by_divisibility_and_rejects_undecidable_lengths()
    {
        Assert.Equal(ObjectUnionVariant.Win95, ObjectUnionTable.DetectVariant(14));
        Assert.Equal(ObjectUnionVariant.Win95, ObjectUnionTable.DetectVariant(7910)); // real data: 7910 % 12 = 2
        Assert.Equal(ObjectUnionVariant.Dos, ObjectUnionTable.DetectVariant(12));
        Assert.Equal(ObjectUnionVariant.Dos, ObjectUnionTable.DetectVariant(84 + 12)); // 96: 8x12, not 14-divisible

        var ambiguous = Assert.Throws<InvalidDataException>(() => ObjectUnionTable.DetectVariant(84)); // lcm(12,14)
        Assert.Contains("SSS.MKF", ambiguous.Message);
        Assert.Contains("chunk 2", ambiguous.Message);
        Assert.Contains("both 14 (WIN95) and 12 (DOS)", ambiguous.Message);

        var neither = Assert.Throws<InvalidDataException>(() => ObjectUnionTable.DetectVariant(13));
        Assert.Contains("SSS.MKF", neither.Message);
        Assert.Contains("chunk 2", neither.Message);
        Assert.Contains("13", neither.Message);
        Assert.Throws<InvalidDataException>(() => ObjectUnionTable.DetectVariant(0)); // empty chunk is not a table
    }

    [Fact]
    public void Decode_reads_player_view_records_from_little_endian_bytes()
    {
        byte[] chunk = PlayerRecord;
        var document = ObjectUnionTable.DecodeTable(chunk, EvidenceFor([0], []));
        Assert.Equal(1, (int)document["formatVersion"]!);
        Assert.Equal("faithful", (string)document["profile"]!);
        Assert.Equal("object", (string)document["fileId"]!);
        var record = document["records"]![0]!.AsObject();
        Assert.Equal("player", (string)record["view"]!);

        // Little-endian proof straight from the byte literals: scriptOnFriendDeath = 43445
        // (0xA9B5) is stored b5 a9; 0x1234 in the padding is stored 34 12.
        Assert.Equal(0xB5, chunk[4]);
        Assert.Equal(0xA9, chunk[5]);
        Assert.Equal(0x34, chunk[8]);
        Assert.Equal(0x12, chunk[9]);

        Assert.Equal(new[] { 0, 0 }, record["reserved"]!.AsArray().Select(node => (int)node!));
        Assert.Equal(43445, (int)record["scriptOnFriendDeath"]!);   // real object[36].wScriptOnFriendDeath
        Assert.Equal(43374, (int)record["scriptOnDying"]!);         // real object[37].wScriptOnDying
        Assert.Equal(new[] { 0x1234, 65535, 32768 }, record["padding"]!.AsArray().Select(node => (int)node!));
        // Only the player view's fields: no item/magic/enemy/poison member is present.
        Assert.False(record.ContainsKey("bitmap"));
        Assert.False(record.ContainsKey("price"));
        Assert.False(record.ContainsKey("flags"));
    }

    [Fact]
    public void Decode_reads_item_view_records_with_flag_boundaries()
    {
        byte[] chunk = [.. ItemRecordFlagsZero, .. ItemRecordFlagsMax];
        var document = ObjectUnionTable.DecodeTable(chunk, EvidenceFor([], [0, 1]));
        var records = document["records"]!.AsArray();
        Assert.Equal(2, records.Count);

        var zero = records[0]!.AsObject();
        Assert.Equal("item", (string)zero["view"]!);
        Assert.Equal(197, (int)zero["bitmap"]!);                    // real object[61].wBitmap
        Assert.Equal(150, (int)zero["price"]!);                     // real object[61].wPrice
        Assert.Equal(43022, (int)zero["scriptOnUse"]!);             // real object[61].wScriptOnUse
        Assert.Equal(39140, (int)zero["scriptOnEquip"]!);
        Assert.Equal(0, (int)zero["scriptOnThrow"]!);
        Assert.Equal(40661, (int)zero["scriptDesc"]!);              // real object[61].wScriptDesc
        Assert.Equal(0, (int)zero["flags"]!);                       // boundary: no ITEMFLAG bit set

        var max = records[1]!.AsObject();
        Assert.Equal("item", (string)max["view"]!);
        Assert.Equal(0xABCD, (int)max["bitmap"]!);                  // bytes cd ab
        Assert.Equal(2000, (int)max["price"]!);
        Assert.Equal(39554, (int)max["scriptOnUse"]!);
        Assert.Equal(0, (int)max["scriptOnEquip"]!);
        Assert.Equal(42890, (int)max["scriptOnThrow"]!);
        Assert.Equal(40524, (int)max["scriptDesc"]!);
        Assert.Equal(65535, (int)max["flags"]!);                    // boundary: every bit set
        // Only the item view's fields: no player member is present.
        Assert.False(max.ContainsKey("scriptOnFriendDeath"));
        Assert.False(max.ContainsKey("reserved"));
    }

    [Fact]
    public void Decode_marks_records_without_evidence_as_undetermined_raw_words()
    {
        byte[] chunk = [.. PlayerRecord, .. UndeterminedRecord];
        var document = ObjectUnionTable.DecodeTable(chunk, EvidenceFor([0], []));
        var records = document["records"]!.AsArray();
        Assert.Equal(2, records.Count);
        Assert.Equal("player", (string)records[0]!["view"]!);
        var undetermined = records[1]!.AsObject();
        Assert.Equal("undetermined", (string)undetermined["view"]!);
        // The whole physical WORD group is preserved without any view interpretation.
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7 }, undetermined["data"]!.AsArray().Select(node => (int)node!));
        Assert.Equal(2, undetermined.Count); // view + data only
    }

    [Fact]
    public void Decode_reads_dos_records_without_the_script_desc_slot()
    {
        byte[] chunk = [.. Words(0, 0, 5, 6, 0x1234, 0xABCD), .. Words(10, 20, 30, 40, 50, 65535), .. Words(9, 8, 7, 6, 5, 4)];
        var document = ObjectUnionTable.DecodeTable(chunk, EvidenceFor([0], [1]));
        var records = document["records"]!.AsArray();
        Assert.Equal(3, records.Count);

        var player = records[0]!.AsObject();
        Assert.Equal("player", (string)player["view"]!);
        Assert.Equal(5, (int)player["scriptOnFriendDeath"]!);
        Assert.Equal(6, (int)player["scriptOnDying"]!);
        Assert.Equal(new[] { 0x1234, 0xABCD }, player["padding"]!.AsArray().Select(node => (int)node!)); // DOS: 2 padding words

        var item = records[1]!.AsObject();
        Assert.Equal("item", (string)item["view"]!);
        Assert.Equal(10, (int)item["bitmap"]!);
        Assert.Equal(20, (int)item["price"]!);
        Assert.Equal(30, (int)item["scriptOnUse"]!);
        Assert.Equal(40, (int)item["scriptOnEquip"]!);
        Assert.Equal(50, (int)item["scriptOnThrow"]!);
        Assert.Equal(65535, (int)item["flags"]!);                   // DOS layout: wFlags is WORD 5
        Assert.False(item.ContainsKey("scriptDesc"));               // DOS has no description slot

        var undetermined = records[2]!.AsObject();
        Assert.Equal(new[] { 9, 8, 7, 6, 5, 4 }, undetermined["data"]!.AsArray().Select(node => (int)node!));
    }

    [Fact]
    public void Decode_reads_magic_view_records_with_flag_boundaries()
    {
        // Real object[295] (referenced by rgwMagic) and object[303] (referenced by an
        // enemy wMagic), plus a synthetic every-bit-set flag boundary record.
        byte[] flagsMax = Words(0xABCD, 65535, 43210, 0, 40524, 32768, 65535);
        byte[] chunk = [.. MagicRecordFlags18, .. Words(66, 0, 43085, 0, 0, 43368, 10), .. flagsMax];
        var document = ObjectUnionTable.DecodeTable(chunk, EvidenceFor([], [], magic: [0, 1, 2]));
        var records = document["records"]!.AsArray();
        Assert.Equal(3, records.Count);

        var first = records[0]!.AsObject();
        Assert.Equal("magic", (string)first["view"]!);
        // Little-endian proof straight from the byte literals: wScriptOnSuccess = 43028
        // (0xA814) is stored 14 a8 at WORD 2 (byte offset 4).
        Assert.Equal(0x14, chunk[4]);
        Assert.Equal(0xA8, chunk[5]);
        Assert.Equal(47, (int)first["magicId"]!);                 // real object[295].wMagicNumber
        Assert.Equal(0, (int)first["reserved1"]!);
        Assert.Equal(43028, (int)first["scriptOnSuccess"]!);      // real object[295].wScriptOnSuccess
        Assert.Equal(0, (int)first["scriptOnUse"]!);
        Assert.Equal(0, (int)first["scriptDesc"]!);
        Assert.Equal(43308, (int)first["reserved2"]!);            // real object[295] WORD 5 (description-script slot)
        Assert.Equal(18, (int)first["flags"]!);                   // kMagicFlagUsableInBattle | kMagicFlagApplyToAll

        var second = records[1]!.AsObject();
        Assert.Equal(66, (int)second["magicId"]!);
        Assert.Equal(10, (int)second["flags"]!);                  // kMagicFlagUsableInBattle only

        var max = records[2]!.AsObject();
        Assert.Equal(65535, (int)max["reserved1"]!);             // boundary: every WORD bit set
        Assert.Equal(65535, (int)max["flags"]!);
        Assert.Equal(32768, (int)max["reserved2"]!);
        // Only the magic view's fields: no item/player/enemy/poison member is present.
        foreach (string other in new[] { "bitmap", "price", "reserved", "scriptOnDying", "enemyId", "poisonLevel", "data" })
            Assert.False(max.ContainsKey(other));
    }

    [Fact]
    public void Decode_reads_enemy_view_records_with_padding_slots()
    {
        // Real object[536] and object[539] (referenced by the enemy teams): 5 defined
        // WORDs, the undefined WIN95 WORD 5/6 slots are preserved as padding.
        byte[] chunk = [.. EnemyRecord536, .. Words(141, 10, 42394, 0, 0, 0, 0)];
        var document = ObjectUnionTable.DecodeTable(chunk, EvidenceFor([], [], enemies: [0, 1]));
        var records = document["records"]!.AsArray();

        var first = records[0]!.AsObject();
        Assert.Equal("enemy", (string)first["view"]!);
        Assert.Equal(138, (int)first["enemyId"]!);               // real object[536].wEnemyID -> enemy.json index
        Assert.Equal(0, (int)first["resistanceToSorcery"]!);
        Assert.Equal(0, (int)first["scriptOnTurnStart"]!);
        Assert.Equal(42828, (int)first["scriptOnBattleEnd"]!);   // real object[536].wScriptOnBattleEnd
        Assert.Equal(0, (int)first["scriptOnReady"]!);
        Assert.Equal(new[] { 0, 0 }, first["padding"]!.AsArray().Select(node => (int)node!));

        var second = records[1]!.AsObject();
        Assert.Equal(10, (int)second["resistanceToSorcery"]!);   // documented sorcery-resistance maximum
        Assert.Equal(42394, (int)second["scriptOnTurnStart"]!);
        foreach (string other in new[] { "bitmap", "magicId", "scriptOnSuccess", "poisonLevel", "data" })
            Assert.False(second.ContainsKey(other));
    }

    [Fact]
    public void Decode_reads_poison_view_records_when_evidence_declares_them()
    {
        // Real object[551]/object[552] WORD groups. No DATA.MKF source populates the
        // poison evidence (the reference indexes .poison only through run-time poison
        // state), so the evidence record is constructed directly here; on real data
        // these records stay undetermined until script-reference evidence exists.
        byte[] chunk = [.. PoisonRecord551, .. Words(1, 64, 40866, 65535, 40868, 0, 32768)];
        var document = ObjectUnionTable.DecodeTable(chunk, EvidenceFor([], [], poison: [0, 1]));
        var records = document["records"]!.AsArray();

        var first = records[0]!.AsObject();
        Assert.Equal("poison", (string)first["view"]!);
        Assert.Equal(0, (int)first["poisonLevel"]!);             // real object[551].wPoisonLevel
        Assert.Equal(16, (int)first["color"]!);                  // real object[551].wColor
        Assert.Equal(40862, (int)first["playerScript"]!);        // real object[551].wPlayerScript
        Assert.Equal(0, (int)first["reserved"]!);
        Assert.Equal(40864, (int)first["enemyScript"]!);         // real object[551].wEnemyScript
        Assert.Equal(new[] { 0, 0 }, first["padding"]!.AsArray().Select(node => (int)node!));

        var second = records[1]!.AsObject();
        Assert.Equal(1, (int)second["poisonLevel"]!);
        Assert.Equal(64, (int)second["color"]!);
        Assert.Equal(65535, (int)second["reserved"]!);           // boundary word inside a defined slot
        Assert.Equal(40868, (int)second["enemyScript"]!);
        Assert.Equal(new[] { 0, 32768 }, second["padding"]!.AsArray().Select(node => (int)node!)); // boundary in padding
        foreach (string other in new[] { "magicId", "enemyId", "bitmap", "scriptOnUse", "data" })
            Assert.False(second.ContainsKey(other));
    }

    [Fact]
    public void Decode_reads_dos_magic_and_enemy_records_with_the_dos_layout()
    {
        // DOS magic has no wScriptDesc slot (wReserved2 is WORD 4, wFlags WORD 5) and
        // the enemy view keeps one padding WORD.
        byte[] chunk = [.. Words(47, 0, 43028, 0, 43308, 18), .. Words(138, 0, 0, 42828, 0, 0x55AA)];
        var document = ObjectUnionTable.DecodeTable(chunk, EvidenceFor([], [], magic: [0], enemies: [1]));
        var records = document["records"]!.AsArray();

        var magic = records[0]!.AsObject();
        Assert.Equal("magic", (string)magic["view"]!);
        Assert.Equal(47, (int)magic["magicId"]!);
        Assert.Equal(43028, (int)magic["scriptOnSuccess"]!);
        Assert.Equal(43308, (int)magic["reserved2"]!);          // DOS layout: WORD 4
        Assert.Equal(18, (int)magic["flags"]!);                  // DOS layout: WORD 5
        Assert.False(magic.ContainsKey("scriptDesc"));           // DOS has no description slot

        var enemy = records[1]!.AsObject();
        Assert.Equal("enemy", (string)enemy["view"]!);
        Assert.Equal(138, (int)enemy["enemyId"]!);
        Assert.Equal(42828, (int)enemy["scriptOnBattleEnd"]!);
        Assert.Equal(new[] { 0x55AA }, enemy["padding"]!.AsArray().Select(node => (int)node!)); // DOS: 1 padding word
    }

    [Fact]
    public void ReadViewEvidence_collects_player_and_item_references_from_the_data_chunks()
    {
        // One store record (9 WORDs): slots hold items 10 and 11, zeros are empty slots.
        byte[] store = Words(10, 0, 11, 0, 0, 0, 0, 0, 0);
        // One enemy record (35 WORDs): wAttackEquivItem = WORD 17 = 13, wStealItem = WORD 19 = 14.
        var enemyWords = new int[35];
        enemyWords[17] = 13;
        enemyWords[19] = 14;
        byte[] enemy = Words(enemyWords);
        // PLAYERROLES (450 WORDs): rgwName = WORD 18..23 = 0..5 (a name of 0 is still read
        // as rgObject[0].player by fight.c), rgwEquipment = WORD 66.. = item 9.
        var roleWords = new int[450];
        for (int role = 0; role < 6; role++) roleWords[18 + role] = role;
        roleWords[66] = 9;
        byte[] roles = Words(roleWords);
        byte[] enemyTeam = new byte[10]; // one ENEMYTEAM record with only empty slots

        var evidence = ObjectUnionTable.ReadViewEvidence(store, enemy, enemyTeam, roles);
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, evidence.PlayerObjectIndices.Order());
        Assert.Equal(new[] { 9, 10, 11, 13, 14 }, evidence.ItemObjectIndices.Order());
        Assert.Empty(evidence.PlayerObjectIndices.Intersect(evidence.ItemObjectIndices));
        Assert.Empty(evidence.MagicObjectIndices); // the all-zero magic/coop slots learn nothing
        Assert.Empty(evidence.EnemyObjectIndices); // the empty enemy team fields nobody
        Assert.Empty(evidence.PoisonObjectIndices); // no extraction-time poison source exists
    }

    [Fact]
    public void ReadViewEvidence_collects_magic_and_enemy_references_from_the_data_chunks()
    {
        byte[] store = Words(1, 0, 0, 0, 0, 0, 0, 0, 0);
        // One ENEMY record (35 WORDs): wMagic = WORD 15 = 20 (0 and 0xFFFF mean no
        // magic and are not evidence), wAttackEquivItem = WORD 17 = 13, wStealItem = WORD 19 = 14.
        var enemyWords = new int[35];
        enemyWords[15] = 20;
        enemyWords[17] = 13;
        enemyWords[19] = 14;
        byte[] enemy = Words(enemyWords);
        // One ENEMYTEAM record (5 WORDs): 0 and 0xFFFF are empty slots, not enemies.
        byte[] enemyTeam = Words(21, 0xFFFF, 22, 0, 23);
        // PLAYERROLES: rgwMagic slots hold 24, 0 (not learned), 25, 26; the cooperative
        // slots hold 27 and 0 (no cooperative magic).
        var roleWords = new int[450];
        roleWords[192] = 24;
        roleWords[193] = 0;
        roleWords[194] = 25;
        roleWords[200] = 26;
        roleWords[390] = 27;
        roleWords[391] = 0;
        byte[] roles = Words(roleWords);

        var evidence = ObjectUnionTable.ReadViewEvidence(store, enemy, enemyTeam, roles);
        Assert.Equal(new[] { 0 }, evidence.PlayerObjectIndices.Order()); // the all-zero name words still name object 0
        Assert.Equal(new[] { 1, 13, 14 }, evidence.ItemObjectIndices.Order());
        Assert.Equal(new[] { 20, 24, 25, 26, 27 }, evidence.MagicObjectIndices.Order());
        Assert.Equal(new[] { 21, 22, 23 }, evidence.EnemyObjectIndices.Order());
    }

    [Fact]
    public void ReadViewEvidence_rejects_malformed_evidence_chunks()
    {
        byte[] store = Words(1, 0, 2, 0, 0, 0, 0, 0, 0);
        var enemyWords = new int[35];
        byte[] enemy = Words(enemyWords);
        var roleWords = new int[450];
        byte[] roles = Words(roleWords);
        byte[] enemyTeam = new byte[10];
        var valid = () => ObjectUnionTable.ReadViewEvidence(store, enemy, enemyTeam, roles);
        valid(); // the well-formed fixture passes

        var storeError = Assert.Throws<InvalidDataException>(() => ObjectUnionTable.ReadViewEvidence(new byte[19], enemy, enemyTeam, roles));
        Assert.Contains("DATA.MKF", storeError.Message);
        Assert.Contains("chunk 0", storeError.Message);
        Assert.Contains("18", storeError.Message);

        var enemyError = Assert.Throws<InvalidDataException>(() => ObjectUnionTable.ReadViewEvidence(store, new byte[69], enemyTeam, roles));
        Assert.Contains("DATA.MKF", enemyError.Message);
        Assert.Contains("chunk 1", enemyError.Message);
        Assert.Contains("70", enemyError.Message);

        var enemyTeamError = Assert.Throws<InvalidDataException>(() => ObjectUnionTable.ReadViewEvidence(store, enemy, new byte[11], roles));
        Assert.Contains("DATA.MKF", enemyTeamError.Message);
        Assert.Contains("chunk 2", enemyTeamError.Message);
        Assert.Contains("10", enemyTeamError.Message);

        var rolesError = Assert.Throws<InvalidDataException>(() => ObjectUnionTable.ReadViewEvidence(store, enemy, enemyTeam, new byte[899]));
        Assert.Contains("DATA.MKF", rolesError.Message);
        Assert.Contains("chunk 3", rolesError.Message);
        Assert.Contains("900", rolesError.Message);
    }

    [Fact]
    public void ReadViewEvidence_rejects_one_object_referenced_under_two_views()
    {
        // rgwName[0] = 5 makes object 5 player evidence while the store sells item 5.
        byte[] store = Words(5, 0, 0, 0, 0, 0, 0, 0, 0);
        byte[] enemy = Words(new int[35]);
        byte[] enemyTeam = new byte[10];
        var roleWords = new int[450];
        roleWords[18] = 5;
        byte[] roles = Words(roleWords);

        var error = Assert.Throws<InvalidDataException>(() => ObjectUnionTable.ReadViewEvidence(store, enemy, enemyTeam, roles));
        Assert.Contains("conflict", error.Message);
        Assert.Contains("5", error.Message);
        Assert.Contains("player", error.Message);
        Assert.Contains("item", error.Message);
    }

    [Fact]
    public void ReadViewEvidence_rejects_magic_and_enemy_view_conflicts()
    {
        byte[] store = Words(5, 0, 0, 0, 0, 0, 0, 0, 0);
        byte[] enemy = Words(new int[35]);

        // rgwMagic[0] = 5 while the store sells item 5: one object, two views.
        var roleWords = new int[450];
        roleWords[192] = 5;
        var itemConflict = Assert.Throws<InvalidDataException>(
            () => ObjectUnionTable.ReadViewEvidence(store, enemy, new byte[10], Words(roleWords)));
        Assert.Contains("conflict", itemConflict.Message);
        Assert.Contains("5", itemConflict.Message);
        Assert.Contains("magic", itemConflict.Message);
        Assert.Contains("item", itemConflict.Message);

        // The enemy team fields object 6 while rgwName[0] = 6 makes it a player.
        var playerRoleWords = new int[450];
        playerRoleWords[18] = 6;
        var enemyConflict = Assert.Throws<InvalidDataException>(
            () => ObjectUnionTable.ReadViewEvidence(new byte[18], enemy, Words(6, 0xFFFF, 0, 0, 0), Words(playerRoleWords)));
        Assert.Contains("conflict", enemyConflict.Message);
        Assert.Contains("6", enemyConflict.Message);
        Assert.Contains("enemy", enemyConflict.Message);
        Assert.Contains("player", enemyConflict.Message);
    }

    [Fact]
    public void Decode_rejects_evidence_indices_outside_the_record_count()
    {
        byte[] chunk = [.. PlayerRecord, .. ItemRecordFlagsZero, .. UndeterminedRecord]; // 3 records
        var playerError = Assert.Throws<InvalidDataException>(
            () => ObjectUnionTable.DecodeTable(chunk, EvidenceFor([3], [])));
        Assert.Contains("3", playerError.Message);
        Assert.Contains("player", playerError.Message);
        Assert.Contains("3-record", playerError.Message);

        var itemError = Assert.Throws<InvalidDataException>(
            () => ObjectUnionTable.DecodeTable(chunk, EvidenceFor([], [600])));
        Assert.Contains("600", itemError.Message);
        Assert.Contains("item", itemError.Message);

        var magicError = Assert.Throws<InvalidDataException>(
            () => ObjectUnionTable.DecodeTable(chunk, EvidenceFor([], [], magic: [3])));
        Assert.Contains("3", magicError.Message);
        Assert.Contains("magic", magicError.Message);

        var enemyError = Assert.Throws<InvalidDataException>(
            () => ObjectUnionTable.DecodeTable(chunk, EvidenceFor([], [], enemies: [600])));
        Assert.Contains("600", enemyError.Message);
        Assert.Contains("enemy", enemyError.Message);

        var poisonError = Assert.Throws<InvalidDataException>(
            () => ObjectUnionTable.DecodeTable(chunk, EvidenceFor([], [], poison: [3])));
        Assert.Contains("3", poisonError.Message);
        Assert.Contains("poison", poisonError.Message);
    }

    [Fact]
    public void ValidateDocument_accepts_decoded_records_and_rejects_multi_view_or_unknown_views()
    {
        byte[] chunk = [.. PlayerRecord, .. ItemRecordFlagsZero, .. UndeterminedRecord];
        var evidence = EvidenceFor([0], [1]);
        var document = ObjectUnionTable.DecodeTable(chunk, evidence);
        ObjectUnionTable.ValidateDocument(chunk.Length, document); // freshly decoded passes

        // A player-view record must not also carry item-view fields.
        var crossView = ObjectUnionTable.DecodeTable(chunk, evidence);
        crossView["records"]![0]!["bitmap"] = 1;
        Assert.Throws<InvalidDataException>(() => ObjectUnionTable.ValidateDocument(chunk.Length, crossView));

        // An item-view record must not also carry player-view fields.
        var reverseCrossView = ObjectUnionTable.DecodeTable(chunk, evidence);
        reverseCrossView["records"]![1]!["scriptOnDying"] = 1;
        Assert.Throws<InvalidDataException>(() => ObjectUnionTable.ValidateDocument(chunk.Length, reverseCrossView));

        // Views outside the frozen union vocabulary are rejected.
        var wrongView = ObjectUnionTable.DecodeTable(chunk, evidence);
        wrongView["records"]![2]!["view"] = "eventObject";
        Assert.Throws<InvalidDataException>(() => ObjectUnionTable.ValidateDocument(chunk.Length, wrongView));

        // The view declaration itself is mandatory.
        var missingView = ObjectUnionTable.DecodeTable(chunk, evidence);
        missingView["records"]![0]!.AsObject().Remove("view");
        Assert.Throws<InvalidDataException>(() => ObjectUnionTable.ValidateDocument(chunk.Length, missingView));
    }

    [Fact]
    public void ValidateDocument_accepts_magic_enemy_poison_views_and_rejects_cross_view_fields()
    {
        byte[] chunk = [.. MagicRecordFlags18, .. EnemyRecord536, .. PoisonRecord551];
        var evidence = EvidenceFor([], [], magic: [0], enemies: [1], poison: [2]);
        var document = ObjectUnionTable.DecodeTable(chunk, evidence);
        ObjectUnionTable.ValidateDocument(chunk.Length, document); // freshly decoded passes

        // A magic-view record must not also carry item-view fields.
        var magicWithBitmap = ObjectUnionTable.DecodeTable(chunk, evidence);
        magicWithBitmap["records"]![0]!["bitmap"] = 1;
        Assert.Throws<InvalidDataException>(() => ObjectUnionTable.ValidateDocument(chunk.Length, magicWithBitmap));

        // An enemy-view record must not also carry magic-view fields.
        var enemyWithMagic = ObjectUnionTable.DecodeTable(chunk, evidence);
        enemyWithMagic["records"]![1]!["magicId"] = 1;
        Assert.Throws<InvalidDataException>(() => ObjectUnionTable.ValidateDocument(chunk.Length, enemyWithMagic));

        // A poison-view record must not also carry enemy-view fields.
        var poisonWithEnemy = ObjectUnionTable.DecodeTable(chunk, evidence);
        poisonWithEnemy["records"]![2]!["enemyId"] = 1;
        Assert.Throws<InvalidDataException>(() => ObjectUnionTable.ValidateDocument(chunk.Length, poisonWithEnemy));

        // A poison record without its padding array is incomplete.
        var noPadding = ObjectUnionTable.DecodeTable(chunk, evidence);
        noPadding["records"]![2]!.AsObject().Remove("padding");
        Assert.Throws<InvalidDataException>(() => ObjectUnionTable.ValidateDocument(chunk.Length, noPadding));
    }

    [Fact]
    public void ValidateDocument_rejects_wrong_array_lengths_ranges_and_variant_mismatch()
    {
        byte[] chunk = [.. PlayerRecord, .. ItemRecordFlagsZero];
        var evidence = EvidenceFor([0], [1]);
        var shortData = ObjectUnionTable.DecodeTable(chunk, evidence);
        shortData["records"]![0]!["padding"] = new JsonArray(1, 2); // WIN player padding has 3 words
        Assert.Throws<InvalidDataException>(() => ObjectUnionTable.ValidateDocument(chunk.Length, shortData));

        var longData = ObjectUnionTable.DecodeTable(chunk, evidence);
        longData["records"]![0]!["reserved"] = new JsonArray(0);
        Assert.Throws<InvalidDataException>(() => ObjectUnionTable.ValidateDocument(chunk.Length, longData));

        foreach ((string field, int value) in new[] { ("bitmap", -1), ("flags", 65_536), ("scriptDesc", -1), ("price", 65_536) })
        {
            var document = ObjectUnionTable.DecodeTable(chunk, evidence);
            document["records"]![1]![field] = value;
            Assert.Throws<InvalidDataException>(() => ObjectUnionTable.ValidateDocument(chunk.Length, document));
        }

        var outOfRangeArray = ObjectUnionTable.DecodeTable(chunk, evidence);
        outOfRangeArray["records"]![0]!["padding"] = new JsonArray(0, 0, 65_536);
        Assert.Throws<InvalidDataException>(() => ObjectUnionTable.ValidateDocument(chunk.Length, outOfRangeArray));

        // Magic/enemy scalars and the enemy padding follow the same WORD gate.
        byte[] magicAndEnemy = [.. MagicRecordFlags18, .. EnemyRecord536];
        var twoViewEvidence = EvidenceFor([], [], magic: [0], enemies: [1]);
        foreach ((int record, string field, int value) in new[]
        {
            (0, "magicId", -1), (0, "flags", 65_536), (0, "reserved2", 65_536),
            (1, "enemyId", -1), (1, "resistanceToSorcery", 65_536), (1, "scriptOnReady", -1),
        })
        {
            var document = ObjectUnionTable.DecodeTable(magicAndEnemy, twoViewEvidence);
            document["records"]![record]![field] = value;
            Assert.Throws<InvalidDataException>(() => ObjectUnionTable.ValidateDocument(magicAndEnemy.Length, document));
        }
        var enemyPadding = ObjectUnionTable.DecodeTable(magicAndEnemy, twoViewEvidence);
        enemyPadding["records"]![1]!["padding"] = new JsonArray(0, 65_536); // right length, one word out of range
        Assert.Throws<InvalidDataException>(() => ObjectUnionTable.ValidateDocument(magicAndEnemy.Length, enemyPadding));

        // A DOS-length document (12-byte records) must not carry the WIN-only scriptDesc slot.
        var dosChunk = Words(10, 20, 30, 40, 50, 65535);
        var dosDocument = ObjectUnionTable.DecodeTable(dosChunk, EvidenceFor([], [0]));
        ObjectUnionTable.ValidateDocument(dosChunk.Length, dosDocument);
        dosDocument["records"]![0]!["scriptDesc"] = 1;
        Assert.Throws<InvalidDataException>(() => ObjectUnionTable.ValidateDocument(dosChunk.Length, dosDocument));

        // The same gate applies to the magic view's DOS layout (no description slot).
        byte[] dosMagic = Words(47, 0, 43028, 0, 43308, 18);
        var dosMagicDocument = ObjectUnionTable.DecodeTable(dosMagic, EvidenceFor([], [], magic: [0]));
        ObjectUnionTable.ValidateDocument(dosMagic.Length, dosMagicDocument);
        dosMagicDocument["records"]![0]!["scriptDesc"] = 1;
        Assert.Throws<InvalidDataException>(() => ObjectUnionTable.ValidateDocument(dosMagic.Length, dosMagicDocument));
    }

    [Fact]
    public void DescribeIndexEntry_reports_the_source_chunk_and_hand_verified_sha256()
    {
        byte[] winChunk = new byte[14]; // one all-zero WIN95 record
        var winEntry = ObjectUnionTable.DescribeIndexEntry(winChunk);
        Assert.Equal("object.json", (string)winEntry["file"]!);
        Assert.Equal("SSS.MKF", (string)winEntry["archive"]!);
        Assert.Equal(2, (int)winEntry["chunk"]!);
        Assert.Equal(1, (int)winEntry["recordCount"]!);
        Assert.Equal(14, (int)winEntry["byteLength"]!);
        // Independent expectation: hand-verified SHA-256 of 14 zero bytes (Python hashlib
        // and PowerShell SHA256.Create both agree on this constant).
        Assert.Equal("e7ecebbc590bc88b3761fa6cd03d749f87463dabb67021a5c6768c25ec68b3f2", (string)winEntry["sha256"]!);

        byte[] dosChunk = new byte[12];
        var dosEntry = ObjectUnionTable.DescribeIndexEntry(dosChunk);
        Assert.Equal(1, (int)dosEntry["recordCount"]!);
        Assert.Equal("15ec7bf0b50732b49f8228e07d24365338f9e3ab994b00af08e5a3bffe55fd8b", (string)dosEntry["sha256"]!);

        // Cross-check against an in-test hash of the three hand-authored records.
        byte[] literal = [.. PlayerRecord, .. ItemRecordFlagsZero, .. ItemRecordFlagsMax];
        var entry = ObjectUnionTable.DescribeIndexEntry(literal);
        Assert.Equal(3, (int)entry["recordCount"]!);
        Assert.Equal(42, (int)entry["byteLength"]!);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(literal)), (string)entry["sha256"]!);

        Assert.Throws<InvalidDataException>(() => ObjectUnionTable.DescribeIndexEntry(new byte[13]));
    }

    [Fact]
    public void RejectDuplicateKeys_rejects_duplicate_keys_in_an_object_document()
    {
        // Duplicate keys cannot survive JsonNode round-trips, so the strict gate works on the
        // parsed document model (shared counterexample path used by the events contract).
        using var duplicated = JsonDocument.Parse(
            """{ "formatVersion": 1, "records": [ { "view": "item", "view": "item" } ] }""");
        Assert.Throws<InvalidDataException>(() => StrictJson.RejectDuplicateKeys(duplicated.RootElement, "object.json"));

        using var clean = JsonDocument.Parse("""{ "formatVersion": 1, "records": [ { "view": "item" } ] }""");
        StrictJson.RejectDuplicateKeys(clean.RootElement, "object.json"); // must not throw
    }
}
