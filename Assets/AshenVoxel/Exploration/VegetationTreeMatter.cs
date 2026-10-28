using System;
using System.Threading.Tasks;
using AshenVoxel.Core;
using RootContactBuildPipeline;
using AshenVoxel.Exploration.Experimental;
using AshenVoxel.WorldScale;
namespace AshenVoxel.Exploration
{
    public sealed partial class VegetationStreaming
    {
        // Legacy explicit activation still exists for editor review actions.
        public bool TryReserveNaturalTree(WorldPoint point,int candidates,IShrubMatterOwner owner,out ShrubLease lease,out string reason)
        {
            lease=null;reason="Approach an existing complete tree or rock";
            if(!Ready||owner==null||HasReservedShrubMatter||rootField==null||candidates<1||candidates>100000||shrubToken==long.MaxValue)return false;
            CachedCell selected=null;PopulationInstance best=default;double closest=36;int visited=0;
            foreach(var cell in cells.Values)
            {
                if(!cell.Published)continue;
                foreach(var instance in cell.Data.Instances)
                {
                    double dx=point.X-instance.Position.X,dz=point.Z-instance.Position.Z,dy=point.Y-instance.Position.Y;
                    if(Math.Abs(dx)>8||Math.Abs(dz)>8)continue;
                    if(++visited>candidates){reason="Selection budget reached; original remains intact";return false;}
                    if(!NaturalTreeTemplate.Supports(instance.PrototypeId)||instance.Scale!=1)continue;
                    double distance=dx*dx+dy*dy+dz*dz;if(distance>=closest)continue;
                    closest=distance;selected=cell;best=instance;
                }
            }
            if(selected==null)return false;
            lease=new ShrubLease{Cell=selected.Data.Key,Instance=best,Epoch=selected.Epoch,OwnerCell=selected,Owner=owner,Source=rootField.SourceIdentity,Token=++shrubToken,NaturalRoot=SelectedRoot(selected.Data,best,false)};
            shrubLease=lease;reason="Complete specimen reserved; original remains authoritative";return true;
        }
        public bool TryReserveNaturalMatterRay(WorldPoint origin,WorldPoint direction,double reach,int candidates,IShrubMatterOwner owner,out ShrubLease lease,out string reason)
            =>TryReserveNaturalMatter(origin,direction,reach,false,candidates,owner,out lease,out reason);
        public bool TryReserveNaturalMatterPunch(WorldPoint origin,WorldPoint direction,int candidates,IShrubMatterOwner owner,out ShrubLease lease,out string reason)
            =>TryReserveNaturalMatter(origin,direction,NaturalMatterPunch.Reach,true,candidates,owner,out lease,out reason);
        // Cheap retention check for the single approached specimen. It reads the
        // same committed matter/root recipe as admission, never a visual mesh.
        public bool NaturalMatterLeaseAimed(ShrubLease lease,WorldPoint origin,WorldPoint direction,double reach)
        {
            double length=Math.Sqrt(direction.X*direction.X+direction.Y*direction.Y+direction.Z*direction.Z);
            if(!ShrubCurrent(lease)||!Finite(origin.X)||!Finite(origin.Y)||!Finite(origin.Z)||!Finite(length)||length<1e-9||!Finite(reach)||reach<=0||reach>12||
                !models.TryGetValue(lease.Instance.PrototypeId,out var model))return false;
            double angle=lease.Instance.YawDegrees*Math.PI/180,co=Math.Cos(angle),si=Math.Sin(angle);
            double x=origin.X-lease.Instance.Position.X,y=origin.Y-lease.Instance.Position.Y,z=origin.Z-lease.Instance.Position.Z;
            double dx=direction.X/length,dy=direction.Y/length,dz=direction.Z/length;
            return !double.IsPositiveInfinity(MatterRay(model.Data,lease.NaturalRoot as RootBinding,true,x*co-z*si,y,x*si+z*co,
                dx*co-dz*si,dy,dx*si+dz*co,reach));
        }
        bool TryReserveNaturalMatter(WorldPoint origin,WorldPoint direction,double reach,bool assisted,int candidates,IShrubMatterOwner owner,out ShrubLease lease,out string reason)
        {
            lease=null;reason=assisted?"Aim at wood or rock within 2.5 m":"Aim at published tree or rock hard matter";
            double length=Math.Sqrt(direction.X*direction.X+direction.Y*direction.Y+direction.Z*direction.Z);
            if(!Ready||owner==null||HasReservedShrubMatter||candidates<1||candidates>100000||rootField==null||shrubToken==long.MaxValue||!Finite(origin.X)||!Finite(origin.Y)||!Finite(origin.Z)||!Finite(length)||length<1e-9||!Finite(reach)||reach<=0||reach>12)return false;
            double dx=direction.X/length,dy=direction.Y/length,dz=direction.Z/length,closest=reach,assistedClosest=NaturalMatterPunch.RayReach(1);int visited=0;
            var aim=new System.Numerics.Vector3((float)dx,(float)dy,(float)dz);
            CachedCell selected=null,assistedSelected=null;PopulationInstance best=default,assistedBest=default;
            foreach(var cell in cells.Values)
            {
                if(!cell.Published)continue;
                double size=cell.Data.Key.SizeMeters;
                double cx=Math.Max(cell.Data.Key.X*size-origin.X,Math.Max(0,origin.X-(cell.Data.Key.X+1)*size));
                double cz=Math.Max(cell.Data.Key.Z*size-origin.Z,Math.Max(0,origin.Z-(cell.Data.Key.Z+1)*size));
                double padding=reach+maximumRadius+(assisted?NaturalMatterPunch.Radius:0);
                if(cx*cx+cz*cz>padding*padding)continue;
                foreach(var instance in cell.Data.Instances)
                {
                    // Bound registry scanning, including non-targets: never choose from a partial scan.
                    if(++visited>candidates){reason="Aim selection budget reached; original remains intact";return false;}
                    if(!NaturalTreeTemplate.Supports(instance.PrototypeId)||instance.Scale!=1||!models.TryGetValue(instance.PrototypeId,out var model))continue;
                    var p=model.Data;double ox=origin.X-instance.Position.X,oy=origin.Y-instance.Position.Y,oz=origin.Z-instance.Position.Z;
                    var selectedRoot=SelectedRoot(cell.Data,instance,false);
                    double boundsReach=reach+(assisted?NaturalMatterPunch.Radius:0);
                    if(Math.Abs(ox)>p.Radius*1.415+boundsReach||Math.Abs(oz)>p.Radius*1.415+boundsReach||oy<(selectedRoot?.Cpu.Collision.MinY??p.MinY)-boundsReach||oy>p.MaxY+boundsReach)continue;
                    var root=SelectedRoot(cell.Data,instance,false);
                    double angle=instance.YawDegrees*Math.PI/180,co=Math.Cos(angle),si=Math.Sin(angle);
                    for(int ray=0;ray<(assisted?NaturalMatterPunch.Rays:1);ray++)
                    {
                        // One registry scan. Once a center hit exists, off-center candidates cannot win.
                        if(ray>0&&selected!=null)break;
                        var offset=NaturalMatterPunch.Offset(aim,ray);
                        double x=ox+offset.X,y=oy+offset.Y,z=oz+offset.Z;
                        double hit=MatterRay(p,root,assisted,x*co-z*si,y,x*si+z*co,dx*co-dz*si,dy,dx*si+dz*co,ray==0?closest:assistedClosest);
                        if(double.IsPositiveInfinity(hit))continue;
                        if(ray==0)
                        {
                            if(selected!=null&&hit==closest&&instance.StableKey>=best.StableKey)continue;
                            closest=hit;selected=cell;best=instance;
                        }
                        else
                        {
                            if(assistedSelected!=null&&hit==assistedClosest&&instance.StableKey>=assistedBest.StableKey)continue;
                            assistedClosest=hit;assistedSelected=cell;assistedBest=instance;
                        }
                    }
                }
            }
            if(selected==null){selected=assistedSelected;best=assistedBest;}
            if(selected==null)return false;
            lease=new ShrubLease{Cell=selected.Data.Key,Instance=best,Epoch=selected.Epoch,OwnerCell=selected,Owner=owner,Source=rootField.SourceIdentity,Token=++shrubToken,NaturalRoot=SelectedRoot(selected.Data,best,false)};
            shrubLease=lease;reason="Aimed complete specimen reserved; source remains visible while preparing";return true;
        }
        public bool OriginalHardCell(ShrubLease lease,int x,int y,int z)
        {
            if(lease?.NaturalRoot is RootBinding root)return root.Cpu.Collision.BlocksLocal(x*.1+1e-6,y*.1+1e-6,z*.1+1e-6,(x+1)*.1-1e-6,(y+1)*.1-1e-6,(z+1)*.1-1e-6);
            return lease!=null&&models.TryGetValue(lease.Instance.PrototypeId,out var model)&&
                model.Data.BlocksLocal(x*.1+1e-6,y*.1+1e-6,z*.1+1e-6,(x+1)*.1-1e-6,(y+1)*.1-1e-6,(z+1)*.1-1e-6);
        }
        public sealed class LeasedMatter
        {
            public readonly VoxelWorld World;public readonly Int3 Anchor;public readonly string MatterIdentity;
            internal LeasedMatter(VoxelWorld world,Int3 anchor,string identity){World=world;Anchor=anchor;MatterIdentity=identity;}
        }
        public bool TryPrepareLeasedMatterAsync(ShrubLease lease,out Task<LeasedMatter> task,out string reason)
        {
            task=null;reason="Lease is no longer current";if(!ShrubCurrent(lease))return false;
            if(!NaturalTreeTemplate.TryRequest(lease.Instance.PrototypeId,out var template)){reason="Matter worker busy; original remains intact";return false;}
            var artifact=(lease.NaturalRoot as RootBinding)?.Cpu.Artifact;
            task=Task.Run(async()=>CopyLeasedMatter(await template.ConfigureAwait(false),artifact));reason="Preparing exact leased matter";return true;
        }
        bool NaturalLeaseRootCurrent(ShrubLease lease)
        {
            if(!(lease.OwnerCell is CachedCell cell))return false;
            var root=SelectedRoot(cell.Data,lease.Instance,false);
            return ReferenceEquals(root,lease.NaturalRoot)&&(root==null||!root.Retiring&&RootSourceCurrent(root.Cpu.Request));
        }
        static LeasedMatter CopyLeasedMatter(NaturalTreeTemplate.Prepared template,CompleteArtifact root)
        {
            if(root==null){var canonical=template.CreateWorld(out var canonicalAnchor);return new LeasedMatter(canonical,canonicalAnchor,template.MatterIdentity);}
            if(template.PrototypeId!=10&&template.PrototypeId!=11)throw new InvalidOperationException("Unsupported grounded-root model");
            int minY=0;for(int i=0;i<root.CellCount;i++){var c=root.CellAt(i);if(c.Position.y<30)minY=Math.Min(minY,c.Position.y+template.Anchor.y);}
            // Copy immutable upper matter directly into its final domain. A large
            // rooted tree no longer allocates and fills an intermediate full tree.
            var result=template.CreateWorld(out var movedAnchor,30,-minY);int owned=0;
            for(int i=0;i<root.CellCount;i++)
            {
                var c=root.CellAt(i);if(c.Position.y==30)continue;if(c.Position.y>30||c.Material!=MaterialId.Wood)throw new InvalidOperationException("Invalid grounded-root owned cell");
                var p=c.Position+movedAnchor;if(!result.InBounds(p.x,p.y,p.z))throw new InvalidOperationException("Grounded-root cell outside replay domain");
                result.Set(p,MaterialId.Wood,c.Color);owned++;
            }
            if(owned!=root.RenderCellCount)throw new InvalidOperationException("Grounded-root context partition mismatch");
            int count=0;foreach(var chunk in result.Chunks.Values)count=checked(count+chunk.NonAirCount);
            if(count>NaturalTreeTemplate.MaximumCells)throw new InvalidOperationException("Grounded-root complete matter capacity");
            return new LeasedMatter(result,movedAnchor,template.MatterIdentity+"/root="+root.Identity.Hash);
        }
        static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value)&&Math.Abs(value)<=1e12;
        // Exact voxel traversal over hard spans and four-weight woody sticks.
        // Unit-weight leaves are transparent to the same solid-matter tool ray.
        // Independent of triangle/LOD caches.
        static double HardRay(ExplorationPrototype p,double ox,double oy,double oz,double dx,double dy,double dz,double reach)
            =>MatterRay(p,null,false,ox,oy,oz,dx,dy,dz,reach);
        static double MatterRay(ExplorationPrototype p,RootBinding root,bool soft,double ox,double oy,double oz,double dx,double dy,double dz,double reach)
        {
            int x=(int)Math.Floor(ox*10),y=(int)Math.Floor(oy*10),z=(int)Math.Floor(oz*10);
            int sx=Math.Sign(dx),sy=Math.Sign(dy),sz=Math.Sign(dz);
            double tx=sx==0?double.PositiveInfinity:((x+(sx>0?1:0))*.1-ox)/dx;
            double ty=sy==0?double.PositiveInfinity:((y+(sy>0?1:0))*.1-oy)/dy;
            double tz=sz==0?double.PositiveInfinity:((z+(sz>0?1:0))*.1-oz)/dz;
            double ax=sx==0?double.PositiveInfinity:.1/Math.Abs(dx),ay=sy==0?double.PositiveInfinity:.1/Math.Abs(dy),az=sz==0?double.PositiveInfinity:.1/Math.Abs(dz),t=0;
            for(int steps=0;steps<400&&t<=reach+NaturalMatterPunch.BoundaryTolerance;steps++)
            {
                if((root!=null?root.Cpu.Collision.BlocksLocal(x*.1+1e-7,y*.1+1e-7,z*.1+1e-7,(x+1)*.1-1e-7,(y+1)*.1-1e-7,(z+1)*.1-1e-7):p.BlocksLocal(x*.1+1e-7,y*.1+1e-7,z*.1+1e-7,(x+1)*.1-1e-7,(y+1)*.1-1e-7,(z+1)*.1-1e-7))||(root!=null?root.Cpu.Collision.ResistanceLocal(x*.1+1e-7,y*.1+1e-7,z*.1+1e-7,(x+1)*.1-1e-7,(y+1)*.1-1e-7,(z+1)*.1-1e-7):p.HasResistance?p.ResistanceLocal(x*.1+1e-7,y*.1+1e-7,z*.1+1e-7,(x+1)*.1-1e-7,(y+1)*.1-1e-7,(z+1)*.1-1e-7):0)>(soft?0:2))return Math.Min(t,reach);
                double next=Math.Min(tx,Math.Min(ty,tz));if(next>reach+NaturalMatterPunch.BoundaryTolerance)break;
                if(tx<=next+1e-12){x+=sx;tx+=ax;}if(ty<=next+1e-12){y+=sy;ty+=ay;}if(tz<=next+1e-12){z+=sz;tz+=az;}t=next;
            }
            return double.PositiveInfinity;
        }
    }
}
