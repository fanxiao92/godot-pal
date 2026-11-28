using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Pal.Core;
using Pal.Game.Resources;
using Pal.Core.Legacy;

namespace Pal.Game.Profiles;

public static class NativeBattleEnvelopeAdapter
{
    public static FirstMandatoryBattleEnvelope Read(PalFirstMandatoryBattleProfile n, string uri)
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
            PalBattleIdentity i=Need(n.Identity,"Identity"); PalBattleProvenance p=Need(n.Provenance,"Provenance"); PalBattleLayout l=Need(n.Layout,"Layout");
            var identity=new BattleContextIdentity(Str(i.SnapshotKind,"Identity.SnapshotKind"),i.SceneId,i.MapId,i.OwnerId,i.TriggerAddress);
            var provenance=new BattleContextProvenance(p.OptionalGrinding,p.PlayerEquipmentChanges,Array.AsReadOnly(Arr(p.FieldSources,"Provenance.FieldSources").Select(x=>new BattleFieldSource(Str(x.Path,"FieldSource.Path"),Str(x.SourceKind,"FieldSource.SourceKind"),x.HasSourceAddress?x.SourceAddress:null,Str(x.AppliesTo,"FieldSource.AppliesTo"))).ToArray()));
            var layout=new BattleContextLayout(l.EquipmentRoleCount,l.EquipmentSlotCount,l.LevelUpMagicRoleCount);
            var roles=Array.AsReadOnly(Arr(n.Roles,"Roles").Select(NativeBattleRoleAdapter.Read).ToArray());
            PalBattleWorld q=Need(n.World,"World"); PalBattleLeader leader=Need(q.Leader,"World.Leader");
            var world=new BattleWorldSnapshot(q.SceneId,q.MapId,q.PaletteId,q.NightPalette,q.MusicId,q.BattleMusicId,new(leader.X,leader.Y,Str(leader.Direction,"Leader.Direction"),leader.Layer,leader.Phase),Array.AsReadOnly(Arr(q.SceneEntries,"World.SceneEntries").Select(x=>new BattleSceneEntrySnapshot(x.SceneId,x.EnterEntry,x.TeleportEntry)).ToArray()),Array.AsReadOnly(Arr(q.EventObjects,"World.EventObjects").Select(x=>new BattleEventObjectSnapshot(x.OwnerId,x.X,x.Y,x.Direction,x.State,x.VanishTime,x.TriggerMode,x.TriggerEntry,x.AutoEntry,x.AutoIdleFrameCount,x.SpriteId,x.SpriteFrames,x.CurrentFrame,x.ScriptIdleFrameCount,x.SpritePtrOffset,x.AutoSpriteFrames,x.Layer)).ToArray()));
            PalBattleClosure c=Need(n.Closure,"Closure");
            var closure=new BattleClosure(Str(c.Entry,"Closure.Entry"),Array.AsReadOnly(Arr(c.Entries,"Closure.Entries").Select(x=>new BattleClosureEntry(Str(x.Id,"ClosureEntry.Id"),Str(x.Category,"ClosureEntry.Category"),Array.AsReadOnly(Ints(x.OwnerTickets,"ClosureEntry.OwnerTickets").ToArray()),Str(x.Status,"ClosureEntry.Status"),Array.AsReadOnly(Strs(x.ResourceIds,"ClosureEntry.ResourceIds").ToArray()))).ToArray()));
            var resources=Array.AsReadOnly(Arr(n.Resources,"Resources").Select(x=>new BattleResourceReference(Str(x.Id,"Resource.Id"),Str(x.Kind,"Resource.Kind"),Str(x.Reference,"Resource.Reference"),Str(x.Sha256,"Resource.Sha256"),Array.AsReadOnly(Ints(x.OwnerTickets,"Resource.OwnerTickets").ToArray()))).ToArray());
            return new(Str(n.ContextSha256,"ContextSha256"),identity,provenance,layout,Array.AsReadOnly(Ints(n.PartyRoleIds,"PartyRoleIds").ToArray()),roles,world,closure,resources);
        }
        catch (InvalidDataException e) { throw new InvalidDataException(uri + ": malformed typed native envelope: " + e.Message,e); }
        catch (Exception e) when (e is NullReferenceException or ArgumentNullException or InvalidCastException or ArgumentException or OverflowException or KeyNotFoundException)
        { throw new InvalidDataException(uri + ": malformed typed native envelope",e); }
    }

    public static void Write(PalFirstMandatoryBattleProfile n,FirstMandatoryBattleEnvelope e)
    {
        n.ContextSha256=e.ContextSha256; n.NativeEnvelopeSha256=FirstMandatoryBattleEnvelopeCodec.Sha256(e);
        n.Identity=new(){SnapshotKind=e.Identity.SnapshotKind,SceneId=e.Identity.SceneId,MapId=e.Identity.MapId,OwnerId=e.Identity.OwnerId,TriggerAddress=e.Identity.TriggerAddress};
        n.Provenance=new(){OptionalGrinding=e.Provenance.OptionalGrinding,PlayerEquipmentChanges=e.Provenance.PlayerEquipmentChanges,FieldSources=e.Provenance.FieldSources.Select(x=>new PalBattleFieldSource{Path=x.Path,SourceKind=x.SourceKind,HasSourceAddress=x.SourceAddress.HasValue,SourceAddress=x.SourceAddress??0,AppliesTo=x.AppliesTo}).ToArray()};
        n.Layout=new(){EquipmentRoleCount=e.Layout.EquipmentRoleCount,EquipmentSlotCount=e.Layout.EquipmentSlotCount,LevelUpMagicRoleCount=e.Layout.LevelUpMagicRoleCount}; n.PartyRoleIds=e.PartyRoleIds.ToArray();
        n.Roles=e.Roles.Select(NativeBattleRoleAdapter.Write).ToArray();
        n.World=new(){SceneId=e.World.SceneId,MapId=e.World.MapId,PaletteId=e.World.PaletteId,NightPalette=e.World.NightPalette,MusicId=e.World.MusicId,BattleMusicId=e.World.BattleMusicId,Leader=new(){X=e.World.Leader.X,Y=e.World.Leader.Y,Direction=e.World.Leader.Direction,Layer=e.World.Leader.Layer,Phase=e.World.Leader.Phase},SceneEntries=e.World.SceneEntries.Select(x=>new PalBattleSceneEntry{SceneId=x.SceneId,EnterEntry=x.EnterEntry,TeleportEntry=x.TeleportEntry}).ToArray(),EventObjects=e.World.EventObjects.Select(x=>new PalBattleEventObject{OwnerId=x.OwnerId,X=x.X,Y=x.Y,Direction=x.Direction,State=x.State,VanishTime=x.VanishTime,TriggerMode=x.TriggerMode,TriggerEntry=x.TriggerEntry,AutoEntry=x.AutoEntry,AutoIdleFrameCount=x.AutoIdleFrameCount,SpriteId=x.SpriteId,SpriteFrames=x.SpriteFrames,CurrentFrame=x.CurrentFrame,ScriptIdleFrameCount=x.ScriptIdleFrameCount,SpritePtrOffset=x.SpritePtrOffset,AutoSpriteFrames=x.AutoSpriteFrames,Layer=x.Layer}).ToArray()};
        n.Closure=new(){Entry=e.Closure.Entry,Entries=e.Closure.Entries.Select(x=>new PalBattleClosureEntry{Id=x.Id,Category=x.Category,OwnerTickets=x.OwnerTickets.ToArray(),Status=x.Status,ResourceIds=x.ResourceIds.ToArray()}).ToArray()};
        n.Resources=e.Resources.Select(x=>new PalBattleResourceReference{Id=x.Id,Kind=x.Kind,Reference=x.Reference,Sha256=x.Sha256,OwnerTickets=x.OwnerTickets.ToArray()}).ToArray();
    }
}
