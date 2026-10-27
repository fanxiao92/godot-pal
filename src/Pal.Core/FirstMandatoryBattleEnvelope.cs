using System.Security.Cryptography;
using System.Text;

namespace Pal.Core;

/// <summary>The complete typed, JSON-independent interchange value used by offline and native adapters.</summary>
public sealed class FirstMandatoryBattleEnvelope
{
    public string ContextSha256 { get; }
    public BattleContextIdentity Identity { get; }
    public BattleContextProvenance Provenance { get; }
    public BattleContextLayout Layout { get; }
    public IReadOnlyList<int> PartyRoleIds { get; }
    public IReadOnlyList<BattleRoleSnapshot> Roles { get; }
    public BattleWorldSnapshot World { get; }
    public BattleClosure Closure { get; }
    public IReadOnlyList<BattleResourceReference> Resources { get; }

    public FirstMandatoryBattleEnvelope(string contextSha256, BattleContextIdentity identity, BattleContextProvenance provenance,
        BattleContextLayout layout, IReadOnlyList<int> partyRoleIds, IReadOnlyList<BattleRoleSnapshot> roles,
        BattleWorldSnapshot world, BattleClosure closure, IReadOnlyList<BattleResourceReference> resources)
    {
        ContextSha256 = contextSha256 ?? throw new ArgumentNullException(nameof(contextSha256));
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        ArgumentNullException.ThrowIfNull(provenance);
        Provenance = new(provenance.OptionalGrinding, provenance.PlayerEquipmentChanges, Copy(provenance.FieldSources, nameof(provenance.FieldSources)));
        Layout = layout ?? throw new ArgumentNullException(nameof(layout));
        PartyRoleIds = Copy(partyRoleIds, nameof(partyRoleIds));
        Roles = Copy((roles ?? throw new ArgumentNullException(nameof(roles))).Select(CloneRole).ToArray(), nameof(roles));
        ArgumentNullException.ThrowIfNull(world);
        World = world with { SceneEntries = Copy(world.SceneEntries, nameof(world.SceneEntries)), EventObjects = Copy(world.EventObjects, nameof(world.EventObjects)) };
        ArgumentNullException.ThrowIfNull(closure);
        Closure = closure with { Entries = Copy(closure.Entries.Select(x => x with { OwnerTickets = Copy(x.OwnerTickets, nameof(x.OwnerTickets)), ResourceIds = Copy(x.ResourceIds, nameof(x.ResourceIds)) }).ToArray(), nameof(closure.Entries)) };
        Resources = Copy((resources ?? throw new ArgumentNullException(nameof(resources))).Select(x => x with { OwnerTickets = Copy(x.OwnerTickets, nameof(x.OwnerTickets)) }).ToArray(), nameof(resources));
    }

    private static BattleRoleSnapshot CloneRole(BattleRoleSnapshot role)
    {
        ArgumentNullException.ThrowIfNull(role);
        var hidden = new System.Collections.ObjectModel.ReadOnlyDictionary<string,BattleExperienceSnapshot>(
            (role.HiddenExperience ?? throw new ArgumentNullException(nameof(role.HiddenExperience))).ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal));
        return role with { HiddenExperience = hidden, Equipment = Copy(role.Equipment,nameof(role.Equipment)), Magics = Copy(role.Magics,nameof(role.Magics)), Statuses = Copy(role.Statuses,nameof(role.Statuses)) };
    }

    private static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> values, string name) =>
        Array.AsReadOnly((values ?? throw new ArgumentNullException(name)).ToArray());
}

/// <summary>Canonical native envelope encoding: ASCII magic, then little-endian Int32 and length-prefixed UTF-8 strings/arrays, in property order below.</summary>
public static class FirstMandatoryBattleEnvelopeCodec
{
    public static string Sha256(FirstMandatoryBattleEnvelope envelope) => Convert.ToHexStringLower(SHA256.HashData(Encode(envelope)));

    public static byte[] Encode(FirstMandatoryBattleEnvelope e)
    {
        ArgumentNullException.ThrowIfNull(e);
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream, new UTF8Encoding(false, true), leaveOpen: true);
        w.Write(Encoding.ASCII.GetBytes("PAL-FMB-NATIVE\0\x01"));
        S(w, e.ContextSha256); Identity(w, e.Identity);
        B(w, e.Provenance.OptionalGrinding); B(w, e.Provenance.PlayerEquipmentChanges);
        A(w, e.Provenance.FieldSources, (x, v) => { S(x, v.Path); S(x, v.SourceKind); B(x, v.SourceAddress.HasValue); if (v.SourceAddress.HasValue) I(x, v.SourceAddress.Value); S(x, v.AppliesTo); });
        I(w, e.Layout.EquipmentRoleCount); I(w, e.Layout.EquipmentSlotCount); I(w, e.Layout.LevelUpMagicRoleCount);
        AI(w, e.PartyRoleIds);
        A(w, e.Roles, Role);
        World(w, e.World);
        S(w, e.Closure.Entry); A(w, e.Closure.Entries, Closure);
        A(w, e.Resources, (x, v) => { S(x, v.Id); S(x, v.Kind); S(x, v.Reference); S(x, v.Sha256); AI(x, v.OwnerTickets); });
        w.Flush(); return stream.ToArray();
    }

    private static void Identity(BinaryWriter w, BattleContextIdentity v) { S(w, v.SnapshotKind); I(w, v.SceneId); I(w, v.MapId); I(w, v.OwnerId); I(w, v.TriggerAddress); }
    private static void Experience(BinaryWriter w, BattleExperienceSnapshot v) { I(w, v.Value); I(w, v.Level); I(w, v.Count); }
    private static void Role(BinaryWriter w, BattleRoleSnapshot v)
    {
        I(w, v.RoleId); I(w, v.Level); I(w, v.Hp); I(w, v.MaxHp); I(w, v.Mp); I(w, v.MaxMp); Experience(w, v.PrimaryExperience);
        foreach (string name in new[] { "health", "magic", "attack", "magicPower", "defense", "dexterity", "flee" }) Experience(w, v.HiddenExperience[name]);
        AI(w, v.Equipment); AI(w, v.Magics); AI(w, v.Statuses); I(w, v.CoveredByRoleId);
    }
    private static void World(BinaryWriter w, BattleWorldSnapshot v)
    {
        I(w, v.SceneId); I(w, v.MapId); I(w, v.PaletteId); B(w, v.NightPalette); I(w, v.MusicId); I(w, v.BattleMusicId);
        I(w, v.Leader.X); I(w, v.Leader.Y); S(w, v.Leader.Direction); I(w, v.Leader.Layer); I(w, v.Leader.Phase);
        I(w, v.SearchProof.OwnerId); I(w, v.SearchProof.SampleIndex); B(w, v.SearchProof.CollisionPass);
        A(w, v.SceneEntries, (x, q) => { I(x, q.SceneId); I(x, q.EnterEntry); I(x, q.TeleportEntry); });
        A(w, v.EventObjects, (x, q) => { I(x,q.OwnerId); I(x,q.X); I(x,q.Y); I(x,q.Direction); I(x,q.State); I(x,q.VanishTime); I(x,q.TriggerMode); I(x,q.TriggerEntry); I(x,q.AutoEntry); I(x,q.AutoIdleFrameCount); I(x,q.SpriteId); I(x,q.SpriteFrames); I(x,q.CurrentFrame); I(x,q.ScriptIdleFrameCount); I(x,q.SpritePtrOffset); I(x,q.AutoSpriteFrames); I(x,q.Layer); });
    }
    private static void Closure(BinaryWriter w, BattleClosureEntry v)
    {
        S(w,v.Id); S(w,v.Category); ClosureIdentity(w,v.Identity); AI(w,v.OwnerTickets); S(w,v.Status); A(w,v.ResourceIds,S);
    }
    private static void ClosureIdentity(BinaryWriter w, BattleClosureIdentity v)
    {
        switch (v)
        {
            case BattleEventRangeIdentity x: S(w,"event-range"); I(w,x.StartAddress); I(w,x.BattleAddress); I(w,x.WonStartAddress); I(w,x.EndAddress); break;
            case BattleCommandIdentity x: S(w,"battle-command"); I(w,x.CommandAddress); AI(w,x.Operands); I(w,x.EnemyTeamId); I(w,x.EnemyObjectId); I(w,x.EnemyId); I(w,x.EnemyCount); I(w,x.BattlefieldId); S(w,x.SpecialValue); S(w,x.ReverseEncoding); break;
            case BattleRoleHooksIdentity x: S(w,"role-script-ranges"); I(w,x.DyingRoleId); I(w,x.DyingStartAddress); I(w,x.DyingEndAddress); I(w,x.FriendDeathRoleId); I(w,x.FriendDeathStartAddress); I(w,x.FriendDeathEndAddress); S(w,x.BranchCoverage); break;
            case BattleEquipmentLayoutIdentity x: S(w,"equipment-layout"); I(w,x.RoleCount); I(w,x.SlotCount); I(w,x.DualAttackObjectId); B(w,x.PersistentEquipmentStatus); break;
            case BattleGrowthLayoutIdentity x: S(w,"growth-layout"); I(w,x.RoleCount); B(w,x.IncludesPrimaryExperience); B(w,x.IncludesHiddenExperience); B(w,x.IncludesRecoveryAndCleanup); break;
            case BattleDynamicAutoIdentity x: S(w,"dynamic-auto-range"); I(w,x.StartAddress); I(w,x.EndAddress); A(w,x.Effects,S); S(w,x.Advance); break;
            case BattleFailureBoundaryIdentity x: S(w,"failure-boundary"); I(w,x.LostEntryAddress); I(w,x.ReloadCommand); S(w,x.Behavior); break;
            case BattleImageSetIdentity x: S(w,"battle-image-set"); I(w,x.EnemyId); S(w,x.EnemyArchiveEntry); A(w,x.RoleSpriteIds,S); I(w,x.BattlefieldId); S(w,x.BattlefieldArchiveEntry); B(w,x.IncludesLayoutAndEffectFrames); break;
            case BattleLocalizedDialogueIdentity x: S(w,"localized-dialogue-set"); A(w,x.Languages,S); A(w,x.Contents,S); break;
            default: throw new InvalidDataException("Unknown closure identity variant.");
        }
    }
    private static void I(BinaryWriter w,int v)=>w.Write(v);
    private static void B(BinaryWriter w,bool v)=>w.Write(v);
    private static void S(BinaryWriter w,string v) { if(v is null) throw new InvalidDataException("Canonical strings cannot be null."); byte[] b=Encoding.UTF8.GetBytes(v); w.Write(b.Length); w.Write(b); }
    private static void AI(BinaryWriter w,IReadOnlyList<int> v)=>A(w,v,I);
    private static void A<T>(BinaryWriter w,IReadOnlyList<T> v,Action<BinaryWriter,T> write) { if(v is null) throw new InvalidDataException("Canonical arrays cannot be null."); w.Write(v.Count); foreach(T item in v) { if(item is null) throw new InvalidDataException("Canonical array items cannot be null."); write(w,item); } }
}
