using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Pal.Core;
using Pal.Game.Resources;

namespace Pal.Game.Profiles;

internal static class NativeBattleEnvelopeAdapter
{
    internal static FirstMandatoryBattleEnvelope Read(PalFirstMandatoryBattleProfile n, string uri)
    {
        try
        {
            T Need<T>(T? value, string field) where T : class => value ?? throw new InvalidDataException(field + " is null");
            T[] Arr<T>(T[]? value, string field) where T : class
            {
                if (value is null || value.Any(x => x is null)) throw new InvalidDataException(field + " contains null");
                return value;
            }
            string Str(string? value, string field) => value ?? throw new InvalidDataException(field + " is null");
            int[] Ints(int[]? value, string field) => value ?? throw new InvalidDataException(field + " is null");
            string[] Strs(string[]? value, string field)
            {
                if (value is null || value.Any(x => x is null)) throw new InvalidDataException(field + " contains null");
                return value;
            }
            BattleExperienceSnapshot Exp(PalBattleExperience x) => new(x.Value,x.Level,x.Count);
            PalBattleIdentity i=Need(n.Identity,"Identity"); PalBattleProvenance p=Need(n.Provenance,"Provenance"); PalBattleLayout l=Need(n.Layout,"Layout");
            var identity=new BattleContextIdentity(Str(i.SnapshotKind,"Identity.SnapshotKind"),i.SceneId,i.MapId,i.OwnerId,i.TriggerAddress);
            var provenance=new BattleContextProvenance(p.OptionalGrinding,p.PlayerEquipmentChanges,Array.AsReadOnly(Arr(p.FieldSources,"Provenance.FieldSources").Select(x=>new BattleFieldSource(Str(x.Path,"FieldSource.Path"),Str(x.SourceKind,"FieldSource.SourceKind"),x.HasSourceAddress?x.SourceAddress:null,Str(x.AppliesTo,"FieldSource.AppliesTo"))).ToArray()));
            var layout=new BattleContextLayout(l.EquipmentRoleCount,l.EquipmentSlotCount,l.LevelUpMagicRoleCount);
            var roles=Array.AsReadOnly(Arr(n.Roles,"Roles").Select(x=>{
                PalBattleHiddenExperience h=Need(x.HiddenExperience,"Role.HiddenExperience");
                var hidden=new Dictionary<string,BattleExperienceSnapshot>(StringComparer.Ordinal){{"health",Exp(Need(h.Health,"Hidden.Health"))},{"magic",Exp(Need(h.Magic,"Hidden.Magic"))},{"attack",Exp(Need(h.Attack,"Hidden.Attack"))},{"magicPower",Exp(Need(h.MagicPower,"Hidden.MagicPower"))},{"defense",Exp(Need(h.Defense,"Hidden.Defense"))},{"dexterity",Exp(Need(h.Dexterity,"Hidden.Dexterity"))},{"flee",Exp(Need(h.Flee,"Hidden.Flee"))}};
                return new BattleRoleSnapshot(x.RoleId,x.Level,x.Hp,x.MaxHp,x.Mp,x.MaxMp,Exp(Need(x.PrimaryExperience,"Role.PrimaryExperience")),hidden,Array.AsReadOnly(Ints(x.Equipment,"Role.Equipment").ToArray()),Array.AsReadOnly(Ints(x.Magics,"Role.Magics").ToArray()),Array.AsReadOnly(Ints(x.Statuses,"Role.Statuses").ToArray()),x.CoveredByRoleId);
            }).ToArray());
            PalBattleWorld q=Need(n.World,"World"); PalBattleLeader leader=Need(q.Leader,"World.Leader"); PalBattleSearchProof proof=Need(q.SearchProof,"World.SearchProof");
            var world=new BattleWorldSnapshot(q.SceneId,q.MapId,q.PaletteId,q.NightPalette,q.MusicId,q.BattleMusicId,new(leader.X,leader.Y,Str(leader.Direction,"Leader.Direction"),leader.Layer,leader.Phase),new(proof.OwnerId,proof.SampleIndex,proof.CollisionPass),Array.AsReadOnly(Arr(q.SceneEntries,"World.SceneEntries").Select(x=>new BattleSceneEntrySnapshot(x.SceneId,x.EnterEntry,x.TeleportEntry)).ToArray()),Array.AsReadOnly(Arr(q.EventObjects,"World.EventObjects").Select(x=>new BattleEventObjectSnapshot(x.OwnerId,x.X,x.Y,x.Direction,x.State,x.VanishTime,x.TriggerMode,x.TriggerEntry,x.AutoEntry,x.AutoIdleFrameCount,x.SpriteId,x.SpriteFrames,x.CurrentFrame,x.ScriptIdleFrameCount,x.SpritePtrOffset,x.AutoSpriteFrames,x.Layer)).ToArray()));
            PalBattleClosure c=Need(n.Closure,"Closure");
            var closure=new BattleClosure(Str(c.Entry,"Closure.Entry"),Array.AsReadOnly(Arr(c.Entries,"Closure.Entries").Select(x=>new BattleClosureEntry(Str(x.Id,"ClosureEntry.Id"),Str(x.Category,"ClosureEntry.Category"),ClosureIdentity(Need(x.Identity,"ClosureEntry.Identity"),Str,Ints,Strs),Array.AsReadOnly(Ints(x.OwnerTickets,"ClosureEntry.OwnerTickets").ToArray()),Str(x.Status,"ClosureEntry.Status"),Array.AsReadOnly(Strs(x.ResourceIds,"ClosureEntry.ResourceIds").ToArray()))).ToArray()));
            var resources=Array.AsReadOnly(Arr(n.Resources,"Resources").Select(x=>new BattleResourceReference(Str(x.Id,"Resource.Id"),Str(x.Kind,"Resource.Kind"),Str(x.Reference,"Resource.Reference"),Str(x.Sha256,"Resource.Sha256"),Array.AsReadOnly(Ints(x.OwnerTickets,"Resource.OwnerTickets").ToArray()))).ToArray());
            return new(Str(n.ContextSha256,"ContextSha256"),identity,provenance,layout,Array.AsReadOnly(Ints(n.PartyRoleIds,"PartyRoleIds").ToArray()),roles,world,closure,resources);
        }
        catch (InvalidDataException e) { throw new InvalidDataException(uri + ": malformed typed native envelope: " + e.Message,e); }
        catch (Exception e) when (e is NullReferenceException or ArgumentNullException or InvalidCastException or ArgumentException or OverflowException or KeyNotFoundException)
        { throw new InvalidDataException(uri + ": malformed typed native envelope",e); }
    }

    private static BattleClosureIdentity ClosureIdentity(PalBattleClosureIdentity x,Func<string?,string,string> str,Func<int[]?,string,int[]> ints,Func<string[]?,string,string[]> strs)=>x switch
    {
        PalBattleEventRangeIdentity v=>new BattleEventRangeIdentity(v.StartAddress,v.BattleAddress,v.WonStartAddress,v.EndAddress),
        PalBattleCommandIdentity v=>new BattleCommandIdentity(v.CommandAddress,ints(v.Operands,"Command.Operands"),v.EnemyTeamId,v.EnemyObjectId,v.EnemyId,v.EnemyCount,v.BattlefieldId,str(v.SpecialValue,"Command.SpecialValue"),str(v.ReverseEncoding,"Command.ReverseEncoding")),
        PalBattleRoleHooksIdentity v=>new BattleRoleHooksIdentity(v.DyingRoleId,v.DyingStartAddress,v.DyingEndAddress,v.FriendDeathRoleId,v.FriendDeathStartAddress,v.FriendDeathEndAddress,str(v.BranchCoverage,"Hooks.BranchCoverage")),
        PalBattleEquipmentLayoutIdentity v=>new BattleEquipmentLayoutIdentity(v.RoleCount,v.SlotCount,v.DualAttackObjectId,v.PersistentEquipmentStatus),
        PalBattleGrowthLayoutIdentity v=>new BattleGrowthLayoutIdentity(v.RoleCount,v.IncludesPrimaryExperience,v.IncludesHiddenExperience,v.IncludesRecoveryAndCleanup),
        PalBattleDynamicAutoIdentity v=>new BattleDynamicAutoIdentity(v.StartAddress,v.EndAddress,strs(v.Effects,"Dynamic.Effects"),str(v.Advance,"Dynamic.Advance")),
        PalBattleFailureBoundaryIdentity v=>new BattleFailureBoundaryIdentity(v.LostEntryAddress,v.ReloadCommand,str(v.Behavior,"Failure.Behavior")),
        PalBattleImageSetIdentity v=>new BattleImageSetIdentity(v.EnemyId,str(v.EnemyArchiveEntry,"Images.EnemyArchiveEntry"),strs(v.RoleSpriteIds,"Images.RoleSpriteIds"),v.BattlefieldId,str(v.BattlefieldArchiveEntry,"Images.BattlefieldArchiveEntry"),v.IncludesLayoutAndEffectFrames),
        PalBattleLocalizedDialogueIdentity v=>new BattleLocalizedDialogueIdentity(strs(v.Languages,"Dialogue.Languages"),strs(v.Contents,"Dialogue.Contents")),
        _=>throw new InvalidDataException("unknown closure identity resource type")
    };

    internal static void Write(PalFirstMandatoryBattleProfile n,FirstMandatoryBattleEnvelope e)
    {
        PalBattleExperience Exp(BattleExperienceSnapshot x)=>new(){Value=x.Value,Level=x.Level,Count=x.Count};
        n.ContextSha256=e.ContextSha256; n.NativeEnvelopeSha256=FirstMandatoryBattleEnvelopeCodec.Sha256(e);
        n.Identity=new(){SnapshotKind=e.Identity.SnapshotKind,SceneId=e.Identity.SceneId,MapId=e.Identity.MapId,OwnerId=e.Identity.OwnerId,TriggerAddress=e.Identity.TriggerAddress};
        n.Provenance=new(){OptionalGrinding=e.Provenance.OptionalGrinding,PlayerEquipmentChanges=e.Provenance.PlayerEquipmentChanges,FieldSources=e.Provenance.FieldSources.Select(x=>new PalBattleFieldSource{Path=x.Path,SourceKind=x.SourceKind,HasSourceAddress=x.SourceAddress.HasValue,SourceAddress=x.SourceAddress??0,AppliesTo=x.AppliesTo}).ToArray()};
        n.Layout=new(){EquipmentRoleCount=e.Layout.EquipmentRoleCount,EquipmentSlotCount=e.Layout.EquipmentSlotCount,LevelUpMagicRoleCount=e.Layout.LevelUpMagicRoleCount}; n.PartyRoleIds=e.PartyRoleIds.ToArray();
        n.Roles=e.Roles.Select(x=>new PalBattleRole{RoleId=x.RoleId,Level=x.Level,Hp=x.Hp,MaxHp=x.MaxHp,Mp=x.Mp,MaxMp=x.MaxMp,PrimaryExperience=Exp(x.PrimaryExperience),HiddenExperience=new(){Health=Exp(x.HiddenExperience["health"]),Magic=Exp(x.HiddenExperience["magic"]),Attack=Exp(x.HiddenExperience["attack"]),MagicPower=Exp(x.HiddenExperience["magicPower"]),Defense=Exp(x.HiddenExperience["defense"]),Dexterity=Exp(x.HiddenExperience["dexterity"]),Flee=Exp(x.HiddenExperience["flee"])},Equipment=x.Equipment.ToArray(),Magics=x.Magics.ToArray(),Statuses=x.Statuses.ToArray(),CoveredByRoleId=x.CoveredByRoleId}).ToArray();
        n.World=new(){SceneId=e.World.SceneId,MapId=e.World.MapId,PaletteId=e.World.PaletteId,NightPalette=e.World.NightPalette,MusicId=e.World.MusicId,BattleMusicId=e.World.BattleMusicId,Leader=new(){X=e.World.Leader.X,Y=e.World.Leader.Y,Direction=e.World.Leader.Direction,Layer=e.World.Leader.Layer,Phase=e.World.Leader.Phase},SearchProof=new(){OwnerId=e.World.SearchProof.OwnerId,SampleIndex=e.World.SearchProof.SampleIndex,CollisionPass=e.World.SearchProof.CollisionPass},SceneEntries=e.World.SceneEntries.Select(x=>new PalBattleSceneEntry{SceneId=x.SceneId,EnterEntry=x.EnterEntry,TeleportEntry=x.TeleportEntry}).ToArray(),EventObjects=e.World.EventObjects.Select(x=>new PalBattleEventObject{OwnerId=x.OwnerId,X=x.X,Y=x.Y,Direction=x.Direction,State=x.State,VanishTime=x.VanishTime,TriggerMode=x.TriggerMode,TriggerEntry=x.TriggerEntry,AutoEntry=x.AutoEntry,AutoIdleFrameCount=x.AutoIdleFrameCount,SpriteId=x.SpriteId,SpriteFrames=x.SpriteFrames,CurrentFrame=x.CurrentFrame,ScriptIdleFrameCount=x.ScriptIdleFrameCount,SpritePtrOffset=x.SpritePtrOffset,AutoSpriteFrames=x.AutoSpriteFrames,Layer=x.Layer}).ToArray()};
        n.Closure=new(){Entry=e.Closure.Entry,Entries=e.Closure.Entries.Select(x=>new PalBattleClosureEntry{Id=x.Id,Category=x.Category,Identity=WriteIdentity(x.Identity),OwnerTickets=x.OwnerTickets.ToArray(),Status=x.Status,ResourceIds=x.ResourceIds.ToArray()}).ToArray()};
        n.Resources=e.Resources.Select(x=>new PalBattleResourceReference{Id=x.Id,Kind=x.Kind,Reference=x.Reference,Sha256=x.Sha256,OwnerTickets=x.OwnerTickets.ToArray()}).ToArray();
    }

    private static PalBattleClosureIdentity WriteIdentity(BattleClosureIdentity x)=>x switch
    {
        BattleEventRangeIdentity v=>new PalBattleEventRangeIdentity{StartAddress=v.StartAddress,BattleAddress=v.BattleAddress,WonStartAddress=v.WonStartAddress,EndAddress=v.EndAddress},
        BattleCommandIdentity v=>new PalBattleCommandIdentity{CommandAddress=v.CommandAddress,Operands=v.Operands.ToArray(),EnemyTeamId=v.EnemyTeamId,EnemyObjectId=v.EnemyObjectId,EnemyId=v.EnemyId,EnemyCount=v.EnemyCount,BattlefieldId=v.BattlefieldId,SpecialValue=v.SpecialValue,ReverseEncoding=v.ReverseEncoding},
        BattleRoleHooksIdentity v=>new PalBattleRoleHooksIdentity{DyingRoleId=v.DyingRoleId,DyingStartAddress=v.DyingStartAddress,DyingEndAddress=v.DyingEndAddress,FriendDeathRoleId=v.FriendDeathRoleId,FriendDeathStartAddress=v.FriendDeathStartAddress,FriendDeathEndAddress=v.FriendDeathEndAddress,BranchCoverage=v.BranchCoverage},
        BattleEquipmentLayoutIdentity v=>new PalBattleEquipmentLayoutIdentity{RoleCount=v.RoleCount,SlotCount=v.SlotCount,DualAttackObjectId=v.DualAttackObjectId,PersistentEquipmentStatus=v.PersistentEquipmentStatus},
        BattleGrowthLayoutIdentity v=>new PalBattleGrowthLayoutIdentity{RoleCount=v.RoleCount,IncludesPrimaryExperience=v.IncludesPrimaryExperience,IncludesHiddenExperience=v.IncludesHiddenExperience,IncludesRecoveryAndCleanup=v.IncludesRecoveryAndCleanup},
        BattleDynamicAutoIdentity v=>new PalBattleDynamicAutoIdentity{StartAddress=v.StartAddress,EndAddress=v.EndAddress,Effects=v.Effects.ToArray(),Advance=v.Advance},
        BattleFailureBoundaryIdentity v=>new PalBattleFailureBoundaryIdentity{LostEntryAddress=v.LostEntryAddress,ReloadCommand=v.ReloadCommand,Behavior=v.Behavior},
        BattleImageSetIdentity v=>new PalBattleImageSetIdentity{EnemyId=v.EnemyId,EnemyArchiveEntry=v.EnemyArchiveEntry,RoleSpriteIds=v.RoleSpriteIds.ToArray(),BattlefieldId=v.BattlefieldId,BattlefieldArchiveEntry=v.BattlefieldArchiveEntry,IncludesLayoutAndEffectFrames=v.IncludesLayoutAndEffectFrames},
        BattleLocalizedDialogueIdentity v=>new PalBattleLocalizedDialogueIdentity{Languages=v.Languages.ToArray(),Contents=v.Contents.ToArray()},
        _=>throw new InvalidDataException("Unknown closure identity variant.")
    };
}
