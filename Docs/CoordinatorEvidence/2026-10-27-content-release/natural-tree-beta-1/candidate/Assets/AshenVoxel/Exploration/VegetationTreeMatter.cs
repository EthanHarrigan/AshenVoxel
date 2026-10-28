using System;
using AshenVoxel.WorldScale;
namespace AshenVoxel.Exploration
{
    public sealed partial class VegetationStreaming
    {
        // Same exclusive registry lease, page publication, collision composition,
        // source hold and rollback as the installed shrub path. No forest edits.
        public bool TryReserveNaturalTree(WorldPoint point,int candidates,IShrubMatterOwner owner,out ShrubLease lease,out string reason)
        {
            lease=null;reason="Approach an existing young birch (models0/1); larger trees exceed the explicit4096-cell beta cap";
            if(!Ready||owner==null||HasReservedShrubMatter||candidates<1||candidates>1024||rootField==null||shrubToken==long.MaxValue||Math.Abs(point.X)>1e12||Math.Abs(point.Z)>1e12)return false;
            CachedCell selected=null;PopulationInstance best=default;double closest=36;int visited=0;
            long x0=(long)Math.Floor((point.X-8)/64),x1=(long)Math.Floor((point.X+8)/64),z0=(long)Math.Floor((point.Z-8)/64),z1=(long)Math.Floor((point.Z+8)/64);
            for(long z=z0;z<=z1;z++)for(long x=x0;x<=x1;x++)if(cells.TryGetValue(new PopulationKey(x,z,0),out var cell)&&cell.Published)
                foreach(var instance in cell.Data.Instances)
                {
                    if(++visited>candidates){reason="Tree selection budget reached; request rejected intact";return false;}
                    if((instance.PrototypeId!=0&&instance.PrototypeId!=1)||instance.Scale!=1)continue;
                    double dx=point.X-instance.Position.X,dz=point.Z-instance.Position.Z,dy=point.Y-instance.Position.Y,d=dx*dx+dz*dz+dy*dy;
                    if(d>=closest)continue;closest=d;selected=cell;best=instance;
                }
            if(selected==null)return false;
            lease=new ShrubLease{Cell=selected.Data.Key,Instance=best,Epoch=selected.Epoch,OwnerCell=selected,Owner=owner,Source=rootField.SourceIdentity,Token=++shrubToken};
            shrubLease=lease;reason="Complete natural tree reserved; original remains authoritative until publication";return true;
        }
    }
}
