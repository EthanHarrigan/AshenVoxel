using System;
using AshenVoxel.WorldScale;

namespace AshenVoxel.Exploration
{
    public interface IShrubMatterOwner
    {
        // Called at the same main-thread boundary as the instanced batch swap.
        void PublishShrubOwnership(bool active);
        bool CanPublishShrubOwnership(bool active);
        bool BlocksBody(double x0,double y0,double z0,double x1,double y1,double z1);
        double WeightedSoftVoxels(WorldPoint feet,double radius,double height);
        bool TryGetSupportHeight(double x,double z,double radius,double low,double high,out double height);
    }
    public sealed partial class VegetationStreaming
    {
        public sealed class ShrubLease
        {
            public PopulationKey Cell {get;internal set;}
            public PopulationInstance Instance {get;internal set;}
            public long Epoch {get;internal set;}
            public long Token {get;internal set;}
            public bool Active {get;internal set;}
            public bool Closed {get;internal set;}
            internal bool WantHidden;
            internal bool Restoring;
            internal object OwnerCell;
            internal object NaturalRoot;
            internal string Source;
            internal IShrubMatterOwner Owner;
        }
        public bool HasReservedShrubMatter=>shrubLease!=null&&!shrubLease.Closed;
        /// <summary>A lease that owns matter (claimed or being claimed). A speculatively prepared,
        /// still-invisible specimen does not, so it never holds terrain publication.</summary>
        public bool HasActiveShrubMatter=>shrubLease!=null&&!shrubLease.Closed&&(shrubLease.Active||shrubLease.WantHidden||shrubLease.Restoring);
        ShrubLease shrubLease;long shrubToken;
        bool shrubBuildHidden;long shrubBuildToken;
        public bool TryReserveShrub(WorldPoint point,int candidates,IShrubMatterOwner owner,out ShrubLease lease,out string reason)
        {
            lease=null;reason="No nearby loaded shrub102/103";
            if(!Ready||owner==null||shrubLease!=null||candidates<1||candidates>1024||rootField==null||shrubToken==long.MaxValue||Math.Abs(point.X)>1e12||Math.Abs(point.Z)>1e12){reason="Shrub slot busy or registry not ready";return false;}
            CachedCell selected=null;PopulationInstance best=default;double closest=36;int visited=0;
            long x0=(long)Math.Floor((point.X-8)/16),x1=(long)Math.Floor((point.X+8)/16),z0=(long)Math.Floor((point.Z-8)/16),z1=(long)Math.Floor((point.Z+8)/16);
            for(long z=z0;z<=z1;z++)for(long x=x0;x<=x1;x++)if(cells.TryGetValue(new PopulationKey(x,z,2),out var cell)&&cell.Published)
                foreach(var instance in cell.Data.Instances)
                {
                    if(++visited>candidates){reason="Selection count limit; request rejected intact";return false;}
                    if((instance.PrototypeId!=102&&instance.PrototypeId!=103)||instance.Scale!=1||Harvested(cell.Data,instance))continue;
                    double dx=point.X-instance.Position.X,dz=point.Z-instance.Position.Z,dy=point.Y-instance.Position.Y;
                    double distance=dx*dx+dz*dz+dy*dy;
                    if(distance>=closest)continue;closest=distance;selected=cell;best=instance;
                }
            if(selected==null)return false;
            lease=new ShrubLease{Cell=selected.Data.Key,Instance=best,Epoch=selected.Epoch,OwnerCell=selected,Owner=owner,Source=rootField.SourceIdentity,Token=++shrubToken};
            shrubLease=lease;reason="Complete payload reserved; original remains authoritative";return true;
        }
        bool ShrubCurrent(ShrubLease lease)=>lease!=null&&!lease.Closed&&ReferenceEquals(shrubLease,lease)&&
            cells.TryGetValue(lease.Cell,out var cell)&&ReferenceEquals(cell,lease.OwnerCell)&&cell.Epoch==lease.Epoch&&rootField!=null&&rootField.SourceIdentity==lease.Source&&NaturalLeaseRootCurrent(lease);
        public bool RequestShrubOwnership(ShrubLease lease)
        {
            if(!ShrubCurrent(lease))return false;lease.WantHidden=true;
            // Fast path: remove the original from the batches already on screen and hand ownership
            // over in this same frame. The full matrix rebuild still runs (in the background, with
            // the lease hidden) but no longer gates the first cut.
            if(!lease.Active&&lease.Owner.CanPublishShrubOwnership(true)&&HideInPublished(lease.Instance)>0)
            {
                // Do not abort a rebuild that is under way (repeated claims would starve streaming);
                // its result is re-patched at publication, and the next build skips the lease itself.
                FastOwnershipPublications++;lease.Active=true;fastHidden.Add(lease.Instance);if(!buildingMatrices)matricesDirty=true;
                lease.Owner.PublishShrubOwnership(true);return true;
            }
            RestartShrubMatrices();return true;
        }
        public int FastOwnershipPublications {get;private set;}
        readonly System.Collections.Generic.List<PopulationInstance> fastHidden=new System.Collections.Generic.List<PopulationInstance>();
        /// <summary>A build that began before a fast claim may still contain the claimed original.</summary>
        void ReapplyFastHidden(){foreach(var instance in fastHidden)HideInPublished(instance);}
        /// <summary>Swap-remove every published matrix of this instance (all LOD/root parts).
        /// Returns how many were removed; 0 means the instance was not found at the current origin.</summary>
        int HideInPublished(PopulationInstance instance)
        {
            float x=(float)(instance.Position.X-origin.X),y=(float)(instance.Position.Y-origin.Y),z=(float)(instance.Position.Z-origin.Z);int removed=0;
            foreach(var batch in batches)
            {
                if(batch.ModelId!=instance.PrototypeId)continue;
                for(int i=0;i<batch.Count;i++)
                {
                    var m=batch.Matrices[i];
                    if(Math.Abs(m.m03-x)>.001f||Math.Abs(m.m13-y)>.001f||Math.Abs(m.m23-z)>.001f)continue;
                    batch.Matrices[i]=batch.Matrices[batch.Count-1];batch.Count--;i--;removed++;
                }
            }
            return removed;
        }
        // ---- Harvested specimens: broken and left behind; the original never returns this session ----
        readonly struct HarvestKey:IEquatable<HarvestKey>
        {
            readonly PopulationKey cell;readonly ulong stable;readonly int prototype;
            public HarvestKey(PopulationKey cell,PopulationInstance instance){this.cell=cell;stable=instance.StableKey;prototype=instance.PrototypeId;}
            public bool Equals(HarvestKey o)=>cell.Equals(o.cell)&&stable==o.stable&&prototype==o.prototype;
            public override bool Equals(object o)=>o is HarvestKey k&&Equals(k);
            public override int GetHashCode(){unchecked{return cell.GetHashCode()*397^stable.GetHashCode()^prototype*7919;}}
        }
        readonly System.Collections.Generic.HashSet<HarvestKey> harvested=new System.Collections.Generic.HashSet<HarvestKey>();
        public int HarvestedCount=>harvested.Count;
        bool Harvested(PopulationCell cell,PopulationInstance instance)=>harvested.Count>0&&harvested.Contains(new HarvestKey(cell.Key,instance));
        /// <summary>Keep the broken specimen: the original stays hidden (draw, collision, aim) for the
        /// rest of the session and the lease closes without restoring it, so the next specimen can be
        /// prepared at once. The owner keeps drawing whatever matter remains.</summary>
        public bool CommitShrub(ShrubLease lease)
        {
            if(!ReferenceEquals(shrubLease,lease)||lease.Closed||!lease.Active||lease.Restoring)return false;
            harvested.Add(new HarvestKey(lease.Cell,lease.Instance));
            // Published batches already omit it (hidden while leased); later rebuilds skip it as harvested.
            CloseShrub();return true;
        }
        public void RestoreShrub(ShrubLease lease)
        {
            if(!ReferenceEquals(shrubLease,lease)||lease.Closed)return;
            if(!lease.Active&&!lease.WantHidden){CloseShrub();return;}
            lease.Restoring=true;
            if(!lease.Active||lease.Owner.CanPublishShrubOwnership(false)){lease.WantHidden=false;RestartShrubMatrices();}
        }
        void RestartShrubMatrices()
        {
            buildingMatrices=false;coverageReserve=0;finalizing=false;ReleaseNextBatches();ReleaseBuildingPages();matrixCells.Clear();finalizePages.Clear();matricesDirty=true;
        }
        bool ShrubRetains(PopulationKey key)=>shrubLease!=null&&!shrubLease.Closed&&shrubLease.Cell.Equals(key);
        static bool SameShrub(ShrubLease lease,PopulationCell cell,PopulationInstance instance)=>lease!=null&&lease.Cell.Equals(cell.Key)&&
            lease.Instance.StableKey==instance.StableKey&&lease.Instance.PrototypeId==instance.PrototypeId&&lease.Instance.Position.X==instance.Position.X&&
            lease.Instance.Position.Y==instance.Position.Y&&lease.Instance.Position.Z==instance.Position.Z&&lease.Instance.YawDegrees==instance.YawDegrees&&instance.Scale==1;
        bool SkipShrubNext(PopulationCell cell,PopulationInstance instance)=>Harvested(cell,instance)||shrubBuildHidden&&shrubLease!=null&&shrubBuildToken==shrubLease.Token&&SameShrub(shrubLease,cell,instance);
        bool SkipShrubCurrent(PopulationCell cell,PopulationInstance instance)=>Harvested(cell,instance)||shrubLease!=null&&shrubLease.Active&&SameShrub(shrubLease,cell,instance);
        void CaptureShrubSelection(){shrubBuildHidden=ShrubCurrent(shrubLease)&&shrubLease.WantHidden;shrubBuildToken=shrubLease?.Token??0;}
        bool ValidateShrubSelection()
        {
            var lease=shrubLease;if(lease==null||lease.Token!=shrubBuildToken)return !shrubBuildHidden;
            if(shrubBuildHidden&&!lease.Active&&(!ShrubCurrent(lease)||!lease.Owner.CanPublishShrubOwnership(true)))
            {lease.WantHidden=false;lease.Restoring=true;return false;}
            if(!shrubBuildHidden&&lease.Active&&!lease.Owner.CanPublishShrubOwnership(false))
            {lease.WantHidden=true;lease.Restoring=true;return false;}
            return true;
        }
        void PublishShrubSelection()
        {
            var lease=shrubLease;if(lease==null||lease.Token!=shrubBuildToken)return;
            if(shrubBuildHidden)
            {
                if(!lease.Active){lease.Active=true;lease.Owner.PublishShrubOwnership(true);}
            }
            else if(lease.Active||lease.Restoring)CloseShrub();
        }
        void CloseShrub()
        {
            var lease=shrubLease;if(lease==null)return;shrubLease=null;lease.Active=false;lease.Closed=true;lease.WantHidden=false;
            lease.Owner.PublishShrubOwnership(false);lease.Owner=null;lease.OwnerCell=null;
        }
        void TickShrubLease()
        {
            var lease=shrubLease;if(lease==null)return;
            if(lease.Restoring)
            {if(lease.WantHidden&&(!lease.Active||lease.Owner.CanPublishShrubOwnership(false))){lease.WantHidden=false;RestartShrubMatrices();}return;}
            if(!ShrubCurrent(lease)){RestoreShrub(lease);return;}
            double dx=observer.X-lease.Instance.Position.X,dz=observer.Z-lease.Instance.Position.Z;
            if(dx*dx+dz*dz>24*24&&lease.WantHidden)RestoreShrub(lease);
        }
        public bool BlocksBody(double x0,double y0,double z0,double x1,double y1,double z1)
        {
            if(BlocksBodyExcludingShrub(x0,y0,z0,x1,y1,z1))return true;
            return shrubLease!=null&&shrubLease.Active&&shrubLease.Owner.BlocksBody(x0,y0,z0,x1,y1,z1);
        }
        double ShrubSoft(WorldPoint feet,double radius,double height)=>shrubLease!=null&&shrubLease.Active?shrubLease.Owner.WeightedSoftVoxels(feet,radius,height):0;
        void ShrubSupport(double x,double z,double radius,double low,double high,ref double height)
        {if(shrubLease!=null&&shrubLease.Active&&shrubLease.Owner.TryGetSupportHeight(x,z,radius,low,high,out double own))height=Math.Max(height,own);}
    }
}
