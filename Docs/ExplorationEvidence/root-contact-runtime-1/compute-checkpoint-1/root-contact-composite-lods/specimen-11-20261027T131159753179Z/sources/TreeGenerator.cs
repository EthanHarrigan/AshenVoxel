using System;
using System.Collections.Generic;

namespace AshenVoxel.Core.Trees
{
    /// <summary>Shared seeded growth grammar: leader, ordered scaffolds, recursive forks,
    /// tapered curved segments, connected woody spines, and supported local foliage clusters.
    /// This is a bounded constructive model, not a physical growth/space-colonization solver.</summary>
    public static class TreeGenerator
    {
        public static TreeResult Generate(VoxelWorld world,TreeParameters parameters,float x,float y,float z,int seed,TreeGenerationBudget budget=null)
        {
            if(world==null)throw new ArgumentNullException(nameof(world));
            if(parameters==null)throw new ArgumentNullException(nameof(parameters));
            if(!Finite(x)||!Finite(y)||!Finite(z))throw new ArgumentOutOfRangeException("Tree base must be finite meters.");
            var builder=new Builder(world,parameters,new TreePoint(x,y,z),seed,budget??new TreeGenerationBudget());
            return builder.Build();
        }
        static bool Finite(float n)=>!float.IsNaN(n)&&!float.IsInfinity(n);
        static double Clamp(double n,double low,double high)=>Math.Max(low,Math.Min(high,n));
        static uint Hash(int x,int y,int z,int seed)
        {
            unchecked{uint h=(uint)x*374761393u+(uint)y*668265263u+(uint)z*2246822519u+(uint)seed*3266489917u;h=(h^(h>>13))*1274126177u;return h^(h>>16);}
        }
        static double Unit(uint n)=>n/(double)uint.MaxValue;
        static double Noise(double x,double y,double z,int seed)
        {
            int ix=(int)Math.Floor(x),iy=(int)Math.Floor(y),iz=(int)Math.Floor(z);
            double tx=x-ix,ty=y-iy,tz=z-iz;tx=tx*tx*(3-2*tx);ty=ty*ty*(3-2*ty);tz=tz*tz*(3-2*tz);
            double value=0;
            for(int dz=0;dz<2;dz++)for(int dy=0;dy<2;dy++)for(int dx=0;dx<2;dx++)
                value+=(Unit(Hash(ix+dx,iy+dy,iz+dz,seed))*2-1)*(dx==0?1-tx:tx)*(dy==0?1-ty:ty)*(dz==0?1-tz:tz);
            return value;
        }
        static uint Shade(uint packed,double brightness,double warmth=0)
        {
            int r=(int)Math.Round(((packed>>16)&255)*brightness+warmth),g=(int)Math.Round(((packed>>8)&255)*brightness),b=(int)Math.Round((packed&255)*brightness-warmth);
            return VoxelColor.Pack((byte)Clamp(r,0,255),(byte)Clamp(g,0,255),(byte)Clamp(b,0,255));
        }
        sealed class Cluster
        {
            public TreePoint Center,Axis;
            public double Radius,Stretch,Thickness;
            public int Seed;
        }
        sealed class Builder
        {
            readonly VoxelWorld world;
            readonly TreeParameters p;
            readonly TreePoint origin;
            readonly int seed;
            readonly TreeGenerationBudget budget;
            readonly List<TreeBranch> branches=new List<TreeBranch>();
            readonly List<Cluster> clusters=new List<Cluster>();
            readonly TreeResult result;
            readonly double s,height,radius,crown,phase,treeTone;
            readonly List<int> trunkIndices=new List<int>();
            bool SculptedStem=>p.BarkStyle==TreeBarkStyle.Redwood;
            bool hasBounds;
            static readonly Int3[] Neighbors={new Int3(1,0,0),new Int3(-1,0,0),new Int3(0,1,0),new Int3(0,-1,0),new Int3(0,0,1),new Int3(0,0,-1)};
            public Builder(VoxelWorld world,TreeParameters parameters,TreePoint origin,int seed,TreeGenerationBudget budget)
            {
                this.world=world;p=parameters;this.origin=origin;this.seed=seed;this.budget=budget;s=world.VoxelSize;
                double individual=(Unit(Hash(11,3,17,seed))*2-1)*p.Variation;
                height=p.Height*p.Age*(1+individual*.08);
                radius=p.TrunkRadius*Math.Pow(p.Age,1.15)*(1+individual*.12);
                crown=p.CrownRadius*Math.Pow(p.Age,.85)*(1+individual*.1);
                phase=Unit(Hash(17,31,5,seed))*Math.PI*2;
                treeTone=(Unit(Hash(37,11,19,seed))*2-1)*.05;
                result=new TreeResult{Species=p.Species,Seed=seed};
            }
            public TreeResult Build()
            {
                GrowLeader();
                GrowRoots();
                GrowScaffolds();
                for(int i=0;i<branches.Count;i++)
                {if(Spent||(result.Truncation&TreeTruncation.VoxelBudget)!=0){result.Truncation|=TreeTruncation.VoxelBudget;break;}RasterWood(branches[i],i);}
                for(int i=0;i<clusters.Count;i++)
                {if(Spent||(result.Truncation&TreeTruncation.VoxelBudget)!=0){result.Truncation|=TreeTruncation.VoxelBudget;break;}RasterFoliage(clusters[i]);}
                result.Branches=TreeResult.Freeze(branches);
                if(!hasBounds){result.MinCell=default;result.MaxCell=default;result.BoundsMin=origin;result.BoundsMax=origin;}
                return result;
            }
            bool Spent=>result.VoxelCandidates>=budget.MaxVoxelCandidates;
            bool Candidate()
            {
                if(Spent){result.Truncation|=TreeTruncation.VoxelBudget;return false;}
                result.VoxelCandidates++;return true;
            }
            int Segment(int parent,int level,TreePoint start,TreePoint end,double startRadius,double endRadius,TreeBranchKind kind)
            {
                if(branches.Count>=budget.MaxBranches){result.Truncation|=TreeTruncation.BranchBudget;return -1;}
                double distance=parent>=0?branches[parent].PathDistance+(start-branches[parent].Start).Length:0;
                branches.Add(new TreeBranch(parent,level,start,end,Math.Max(.000001,startRadius),Math.Max(.000001,endRadius),distance,kind));
                return branches.Count-1;
            }
            void GrowLeader()
            {
                int count=Math.Max(7,Math.Min(SculptedStem?192:36,(int)Math.Ceiling(height/.65)));
                TreePoint previous=origin+new TreePoint(0,-s*.5,0);int parent=-1;
                for(int i=1;i<=count;i++)
                {
                    double t=i/(double)count;
                    // Weak dominance allows a gnarled meandering trunk; strong dominance preserves a straight leader.
                    double bend=p.Gnarl*(1-p.ApicalDominance*.65)*height*.065;
                    TreePoint point=origin+new TreePoint((Math.Sin(phase+t*4.1)-Math.Sin(phase))*bend*t,height*p.LeaderHeightFraction*t,
                        (Math.Cos(phase+t*3.2)-Math.Cos(phase))*bend*.7*t);
                    double a=(i-1)/(double)count;
                    double startRadius=LeaderRadius(a),endRadius=LeaderRadius(t);
                    int index=Segment(parent,0,previous,point,startRadius,endRadius,TreeBranchKind.Trunk);
                    if(index<0)break;
                    trunkIndices.Add(index);parent=index;previous=point;
                }
                if(trunkIndices.Count>0)
                {
                    var last=branches[trunkIndices[trunkIndices.Count-1]];
                    AddCluster(last.End,last.End-last.Start,Math.Max(s*1.35,p.LeafClusterRadius*Math.Pow(p.Age,.65))*.8,seed+131);
                    if(p.FoliageStyle!=TreeFoliageStyle.Broadleaf)
                    {
                        // Fine leader sprays fill the young upper crown rather than leaving a bare pole.
                        for(int i=0;i<trunkIndices.Count;i++)
                        {
                            TreeBranch stem=branches[trunkIndices[i]];
                            double fraction=(stem.End.y-origin.y)/height;
                            if(fraction<.77)continue;
                            int samples=Math.Max(1,(int)Math.Ceiling((stem.End-stem.Start).Length/.32));
                            for(int j=0;j<samples;j++)
                            {
                                TreePoint point=TreePoint.Lerp(stem.Start,stem.End,(j+.5)/samples);
                                double rr=Math.Max(s*1.3,p.LeafClusterRadius*Math.Pow(p.Age,.65)*(1.5-(point.y-origin.y)/height));
                                double angle=phase+i*2.4+j*1.7;
                                AddCluster(point,new TreePoint(Math.Cos(angle),.15,Math.Sin(angle)),rr,seed+i*173+j*317);
                            }
                        }
                        // Keep the youngest leader continuously needled: stochastic skipped clumps
                        // must not leave a small top tuft balanced on a long bare pole.
                        int topSamples=Math.Max(3,(int)Math.Ceiling(height*.095/.14));
                        for(int i=0;i<=topSamples;i++)
                        {
                            double u=i/(double)topSamples,t=.905+.095*u;
                            double f=t*trunkIndices.Count;int index=Math.Min(trunkIndices.Count-1,(int)f);
                            TreeBranch stem=branches[trunkIndices[index]];
                            TreePoint point=TreePoint.Lerp(stem.Start,stem.End,f-index);
                            double angle=phase+i*2.399963229728653;
                            double rr=Math.Max(s*1.6,p.LeafClusterRadius*Math.Pow(p.Age,.65)*(.34+.3*(1-u)));
                            AddCluster(point,new TreePoint(Math.Cos(angle),0,Math.Sin(angle)),rr,seed+1709+i*613,true);
                        }
                    }
                }
            }
            double LeaderRadius(double t)
            {
                double stem=radius*(.045+.955*Math.Pow(1-t,1.0+p.ApicalDominance*.4));
                // Relief-bearing boles localize the swollen root collar in physical meters.
                // A height-scaled flare would inflate a 75m tree's breast-height diameter.
                if(SculptedStem)
                    return stem+radius*p.RootFlare*.65*Math.Exp(-height*t/Math.Max(s*2,radius*.28));
                return stem*(1+p.RootFlare*Math.Pow(1-t,9)*.65);
            }
            void GrowRoots()
            {
                if(trunkIndices.Count==0)return;
                int roots=Math.Max(3,4+(int)(p.RootFlare*4));
                for(int i=0;i<roots;i++)
                {
                    double uneven=Unit(Hash(i,43,29,seed));
                    double a=phase+(i+(uneven-.5)*.3)*Math.PI*2/roots;
                    double reach=radius*(1.7+p.RootFlare*1.1)*(SculptedStem?.78+uneven*.35:1);
                    TreePoint start=origin+new TreePoint(0,SculptedStem?radius*(.9+uneven*.45):Math.Min(radius*.42,.55),0);
                    TreePoint tip=origin+new TreePoint(Math.Cos(a)*reach,-s*.25,Math.Sin(a)*reach);
                    // Follow the nearest local surface for buttress ends; bounded vertical search, never a world scan.
                    int tx=(int)Math.Floor(tip.x/s),tz=(int)Math.Floor(tip.z/s),cy=(int)Math.Floor(origin.y/s);
                    for(int dy=0;dy<=Math.Min(12,(int)Math.Ceiling(.8/s));dy++)
                    {
                        if(world.Get(tx,cy-dy,tz)!=MaterialId.Air){tip=new TreePoint(tip.x,(cy-dy+.8)*s,tip.z);break;}
                    }
                    if(SculptedStem)
                    {
                        // A rising buttress blends into the bole before its exposed root descends.
                        TreePoint knee=TreePoint.Lerp(start,tip,.48)+new TreePoint(0,-radius*.19,0);
                        int attach=Math.Min(trunkIndices.Count-1,Math.Max(0,(int)((start.y-origin.y)/height*trunkIndices.Count)));
                        int root=Segment(trunkIndices[attach],0,start,knee,radius*(.36+p.RootFlare*.12),radius*.29,TreeBranchKind.Root);
                        if(root>=0)Segment(root,0,knee,tip,radius*.29,Math.Max(s*.3,radius*.065),TreeBranchKind.Root);
                    }
                    else Segment(trunkIndices[0],0,start,tip,radius*(.43+p.RootFlare*.16),Math.Max(s*.3,radius*.08),TreeBranchKind.Root);
                }
            }
            void GrowScaffolds()
            {
                if(trunkIndices.Count==0)return;
                int count=Math.Max(p.WhorlSize,(int)Math.Round(p.BranchCount*(.65+.35*Math.Min(p.Age,1))));
                int groups=(count+p.WhorlSize-1)/p.WhorlSize;
                for(int i=0;i<count;i++)
                {
                    if(branches.Count>=budget.MaxBranches){result.Truncation|=TreeTruncation.BranchBudget;break;}
                    int group=i/p.WhorlSize;double u=(group+.35)/(double)groups;
                    double t=p.CrownStart+(.94-p.CrownStart)*u;
                    t+=individualHeight(i)*p.Variation*(p.WhorlSize>1?.03:.018);
                    t=Clamp(t,p.CrownStart,.965);
                    double attach=t*trunkIndices.Count;int ti=Math.Min(trunkIndices.Count-1,(int)attach);
                    TreeBranch stem=branches[trunkIndices[ti]];
                    TreePoint start=TreePoint.Lerp(stem.Start,stem.End,attach-ti);
                    uint key=Hash(i,5,3,seed);
                    double individual=Unit(key)*2-1;
                    double angle=phase+group*2.399963229728653+(i%p.WhorlSize)*Math.PI*2/p.WhorlSize+individual*.19*p.Variation;
                    double elevation=(90-p.BranchAngle)*Math.PI/180+individual*(p.FoliageStyle==TreeFoliageStyle.ScaleSpray?.55:.12)*p.Variation;
                    var direction=new TreePoint(Math.Cos(angle)*Math.Cos(elevation),Math.Sin(elevation),Math.Sin(angle)*Math.Cos(elevation));
                    double normalizedHeight=(start.y-origin.y)/height;
                    double reach=crown*Envelope(normalizedHeight)*(1+individual*.15*p.Variation)*.68;
                    double length=reach/Math.Max(.4,Math.Cos(elevation));
                    double branchRadius=radius*(.56-.23*t)*(1-p.ApicalDominance*.5);
                    double attachmentRadius=stem.StartRadius+(stem.EndRadius-stem.StartRadius)*(attach-ti);
                    branchRadius=Math.Min(branchRadius,attachmentRadius*.72);
                    GrowBranch(start,direction,length,branchRadius,1,trunkIndices[ti],unchecked((int)key));
                }
                if(p.FoliageStyle!=TreeFoliageStyle.Broadleaf)
                {
                    // The youngest leader bears progressively shorter laterals almost to
                    // its apex, not a long green-covered pole above the last mature tier.
                    for(int i=0;i<7;i++)
                    {
                        double t=.945+i*.0072,attach=t*trunkIndices.Count;
                        int ti=Math.Min(trunkIndices.Count-1,(int)attach);
                        TreeBranch stem=branches[trunkIndices[ti]];
                        TreePoint start=TreePoint.Lerp(stem.Start,stem.End,attach-ti);
                        double angle=phase+i*2.399963229728653;
                        TreePoint direction=new TreePoint(Math.Cos(angle),.08,Math.Sin(angle)).Normalized;
                        double reach=crown*Envelope(t)*(.78+Unit(Hash(i,13,79,seed))*.2);
                        double r=stem.StartRadius+(stem.EndRadius-stem.StartRadius)*(attach-ti);
                        GrowBranch(start,direction,Math.Max(s*2,reach),r*.42,1,trunkIndices[ti],unchecked(seed+9013+i*313));
                    }
                }
            }
            double individualHeight(int i)=>Unit(Hash(i,37,73,seed))*2-1;
            double Envelope(double h)
            {
                if(p.ConeTaper>0)
                {
                    double baseHeight=p.CrownStart*p.LeaderHeightFraction;
                    return Math.Max(.05,Math.Pow(Clamp((1-h)/(1-baseHeight),.01,1.25),p.ConeTaper));
                }
                double u=Clamp((h-p.CrownStart*p.LeaderHeightFraction)/(1-p.CrownStart*p.LeaderHeightFraction),0,1);
                return .12+.88*Math.Pow(Math.Sin(Math.PI*u),.52);
            }
            TreePoint CrownLimit(TreePoint point)
            {
                double y=Math.Max(point.y,origin.y+height*.12);
                bool conifer=p.FoliageStyle!=TreeFoliageStyle.Broadleaf;
                double shoulder=origin.y+height*(conifer?.98:.88),cap=height*(conifer?.04:.16);
                if(y>shoulder)y=shoulder+cap*(1-Math.Exp(-(y-shoulder)/cap));
                double dx=point.x-origin.x,dz=point.z-origin.z,length=Math.Sqrt(dx*dx+dz*dz);
                double maximum=crown*Envelope((y-origin.y)/height)*1.18;
                if(length>maximum){dx*=maximum/length;dz*=maximum/length;}
                return new TreePoint(origin.x+dx,y,origin.z+dz);
            }
            void GrowBranch(TreePoint start,TreePoint direction,double length,double startRadius,int level,int parent,int localSeed)
            {
                if(length<s*.9||branches.Count>=budget.MaxBranches)
                {if(branches.Count>=budget.MaxBranches)result.Truncation|=TreeTruncation.BranchBudget;return;}
                int segments=3;TreePoint previous=start,lastDirection=direction.Normalized;int last=parent;
                double droop=p.BranchDroop*Math.Pow(level/(double)p.BranchLevels,1.5);
                double turn=(Unit(Hash(level,parent,13,localSeed))*2-1)*p.Gnarl*.5;
                for(int part=1;part<=segments;part++)
                {
                    double u=part/(double)segments;
                    TreePoint horizontal=new TreePoint(-lastDirection.z,0,lastDirection.x);
                    TreePoint force=new TreePoint(0,p.Tropism*.34-droop*u*.6,0)+horizontal*(turn*(.6+u));
                    lastDirection=(lastDirection+force*.55).Normalized;
                    TreePoint next=CrownLimit(previous+lastDirection*(length/segments));
                    // Keep a material segment even for a crown-clamped tip; zero-length segments are omitted.
                    if((next-previous).LengthSquared<1e-12)break;
                    double r0=startRadius*(1-(part-1)/(double)segments*.58),r1=startRadius*(1-u*.58);
                    int index=Segment(last,level,previous,next,r0,r1,level==1?TreeBranchKind.Primary:level==p.BranchLevels?TreeBranchKind.Twig:TreeBranchKind.Secondary);
                    if(index<0)return;
                    previous=next;last=index;
                }
                if(last==parent)return;
                if(level>=p.BranchLevels-1)
                    AddBearingFoliage(last,localSeed,level==p.BranchLevels?1:.78);
                if(level>=p.BranchLevels)return;
                // Leaf-bearing side shoots populate outer scaffold sections in 3D rather than
                // decorating only the furthest recursive fork endpoints with disconnected pads.
                if(level<=p.BranchLevels-2)
                {
                    int shootCount=p.FoliageStyle==TreeFoliageStyle.ScaleSpray?3:2;
                    for(int shoot=0;shoot<shootCount;shoot++)
                    {
                        uint key=Hash(shoot,level,71,localSeed);
                        if(level>1&&Unit(key)>.52)continue;
                        int attachment=last-(shoot==0?1:shoot==2?2:0);
                        TreeBranch bearing=branches[attachment];
                        TreePoint bud=TreePoint.Lerp(bearing.Start,bearing.End,.45+Unit(key)*.3);
                        TreePoint outward=new TreePoint(-lastDirection.z,0,lastDirection.x)*(shoot==0?-1:1);
                        TreePoint twig=(lastDirection*.38+outward*.7+new TreePoint(0,.48-p.BranchDroop*.83+(Unit(key)-.5)*.25,0)).Normalized;
                        double twigLength=Math.Max(s*2,p.LeafClusterRadius*Math.Pow(p.Age,.65)*1.9+length*.12);
                        double along=Clamp(TreePoint.Dot(bud-bearing.Start,bearing.End-bearing.Start)/Math.Max((bearing.End-bearing.Start).LengthSquared,1e-20),0,1);
                        double bearingRadius=bearing.StartRadius+(bearing.EndRadius-bearing.StartRadius)*along;
                        GrowBranch(bud,twig,twigLength,Math.Min(startRadius*.25,bearingRadius*.65),p.BranchLevels,attachment,unchecked((int)key));
                    }
                }
                int children=2;
                for(int child=0;child<children;child++)
                {
                    uint hash=Hash(child,level,91,localSeed);
                    double varied=(Unit(hash)*2-1)*p.Variation;
                    double fork=(p.ForkAngle*(child==0?-1:1)*(1+varied*.16))*Math.PI/180;
                    double ca=Math.Cos(fork),sa=Math.Sin(fork);
                    double verticalFork=(child==0?.25:-.2)*(1-p.ApicalDominance*.4)+varied*.16;
                    TreePoint bent=new TreePoint(lastDirection.x*ca-lastDirection.z*sa,lastDirection.y*(.63+p.ApicalDominance*.22)+p.Tropism*.18-droop*.28+verticalFork,lastDirection.x*sa+lastDirection.z*ca).Normalized;
                    // Dominant leaders favor finer subordinate limbs; broadleaf forks retain more girth.
                    double ratio=.48+(1-p.ApicalDominance)*.08;
                    GrowBranch(previous,bent,length*(ratio+varied*.05),branches[last].EndRadius*.67,level+1,last,unchecked((int)hash));
                }
            }
            void AddBearingFoliage(int lastIndex,int localSeed,double scale)
            {
                TreeBranch last=branches[lastIndex];
                int firstIndex=lastIndex;
                while(branches[firstIndex].ParentIndex>=0&&branches[branches[firstIndex].ParentIndex].Level==last.Level&&lastIndex-firstIndex<2)
                    firstIndex=branches[firstIndex].ParentIndex;
                TreePoint start=branches[firstIndex].Start,axis=(last.End-start).Normalized;
                double r=Math.Max(s*1.35,p.LeafClusterRadius*Math.Pow(p.Age,.65)*scale);
                int samples=scale>.9?3:2;
                for(int i=0;i<samples;i++)
                {
                    double t=.18+.82*i/Math.Max(1.0,samples-1);
                    TreePoint point=TreePoint.Lerp(start,last.End,t);
                    // Vertical jitter is smaller than the cluster radius, preserving a woody attachment.
                    double jitter=(Unit(Hash(i,19,7,localSeed))*2-1)*r*.23;
                    if(p.FoliageStyle==TreeFoliageStyle.Broadleaf)point+=new TreePoint(0,jitter,0);
                    if(p.FoliageStyle==TreeFoliageStyle.Broadleaf)
                        AddCluster(point,axis,r*(.86+Unit(Hash(i,73,29,localSeed))*.24),unchecked(localSeed+i*7919));
                    else
                    {
                        // Several small unequal tufts replace a single geometrically smooth needle pad.
                        TreePoint lateral=TreePoint.Cross(axis,new TreePoint(0,1,0)).Normalized;
                        double tuft=r*.66;
                        AddCluster(point,axis,tuft,unchecked(localSeed+i*7919));
                        int side=(i&1)==0?-1:1;
                        {
                            double shift=side*(.25+Unit(Hash(i,side,53,localSeed))*.2)*r;
                            TreePoint center=point+lateral*shift+new TreePoint(0,side*r*.22,0);
                            AddCluster(center,(axis+lateral*(side*.35)).Normalized,tuft*(.84+Unit(Hash(i,side,61,localSeed))*.23),unchecked(localSeed+i*7919+side*199));
                        }
                    }
                }
            }
            void AddCluster(TreePoint center,TreePoint axis,double r,int localSeed,bool required=false)
            {
                if(p.FoliageDensity<=0)return;
                if(clusters.Count>=budget.MaxFoliageClusters){result.Truncation|=TreeTruncation.FoliageBudget;return;}
                if(!required&&Unit(Hash(29,7,19,localSeed))>.35+.65*p.FoliageDensity)return;
                bool broad=p.FoliageStyle==TreeFoliageStyle.Broadleaf;
                clusters.Add(new Cluster{Center=center,Axis=axis.Normalized,Radius=Math.Max(s*1.2,r),
                    Stretch=broad?1.08:p.FoliageStyle==TreeFoliageStyle.NeedleFan?1.75:1.55,
                    Thickness=broad?1.25+Unit(Hash(7,3,19,localSeed))*.2:p.FoliageStyle==TreeFoliageStyle.NeedleFan?.78:1.13+Unit(Hash(7,3,19,localSeed))*.17,Seed=localSeed});
            }
            void RasterWood(TreeBranch branch,int branchIndex)
            {
                TreePoint delta=branch.End-branch.Start;double length=delta.LengthSquared;
                bool trunk=branch.Kind==TreeBranchKind.Trunk;
                bool buttress=branch.Kind==TreeBranchKind.Root&&SculptedStem;
                bool relief=trunk&&(p.BarkRelief>0||SculptedStem);
                double maxRadius=Math.Max(branch.StartRadius,branch.EndRadius)+(relief?p.BarkRelief+radius*p.RootFlare*.19:0);
                int x0=(int)Math.Floor((Math.Min(branch.Start.x,branch.End.x)-maxRadius)/s),x1=(int)Math.Floor((Math.Max(branch.Start.x,branch.End.x)+maxRadius)/s);
                int y0=(int)Math.Floor((Math.Min(branch.Start.y,branch.End.y)-maxRadius)/s),y1=(int)Math.Floor((Math.Max(branch.Start.y,branch.End.y)+maxRadius)/s);
                int z0=(int)Math.Floor((Math.Min(branch.Start.z,branch.End.z)-maxRadius)/s),z1=(int)Math.Floor((Math.Max(branch.Start.z,branch.End.z)+maxRadius)/s);
                // A continuous bole is a stack of adjoining horizontal cross sections. Do not
                // repeatedly test a radius-deep spherical endcap around every short segment:
                // tall dense trunks would spend almost all their budget revisiting old wood.
                if(trunk)
                {y0=(int)Math.Floor(Math.Min(branch.Start.y,branch.End.y)/s);y1=(int)Math.Floor(Math.Max(branch.Start.y,branch.End.y)/s);}
                if(buttress)y0=Math.Min(y0,(int)Math.Floor((origin.y-s)/s));
                ClipBounds(ref x0,ref x1,ref y0,ref y1,ref z0,ref z1);
                // A budget must never interrupt a branch between disconnected shell samples and
                // its connectivity spine. Preflight the whole local transaction before writing.
                long needed=(long)Math.Max(0,x1-x0+1)*Math.Max(0,y1-y0+1)*Math.Max(0,z1-z0+1)+SpineCost(branch.Start,branch.End);
                if(branch.ParentIndex>=0&&(branch.Start-branches[branch.ParentIndex].End).LengthSquared>s*s*.01)
                    needed+=SpineCost(branches[branch.ParentIndex].Start,branch.Start);
                if(needed>budget.MaxVoxelCandidates-result.VoxelCandidates)
                {result.Truncation|=TreeTruncation.VoxelBudget;return;}
                TreePoint tangent=delta.Normalized;
                TreePoint side=TreePoint.Cross(tangent,Math.Abs(tangent.y)>.95?new TreePoint(1,0,0):new TreePoint(0,1,0)).Normalized;
                TreePoint other=TreePoint.Cross(tangent,side).Normalized;
                for(int z=z0;z<=z1;z++)for(int y=y0;y<=y1;y++)for(int x=x0;x<=x1;x++)
                {
                    if(!Candidate())return;
                    TreePoint sample=new TreePoint((x+.5)*s,(y+.5)*s,(z+.5)*s);
                    double t=length<1e-16?0:Clamp(TreePoint.Dot(sample-branch.Start,delta)/length,0,1);
                    if(trunk&&Math.Abs(delta.y)>1e-12)t=Clamp((sample.y-branch.Start.y)/delta.y,0,1);
                    if(buttress)t=Clamp(((sample.x-branch.Start.x)*delta.x+(sample.z-branch.Start.z)*delta.z)/Math.Max(delta.x*delta.x+delta.z*delta.z,1e-20),0,1);
                    TreePoint radial=sample-TreePoint.Lerp(branch.Start,branch.End,t);
                    if(trunk)radial=new TreePoint(radial.x,0,radial.z);
                    if(buttress)radial=new TreePoint(radial.x,Math.Max(0,radial.y),radial.z);
                    // World-up angular coordinates keep bark grooves continuous across leader segments.
                    double angle=branch.Kind==TreeBranchKind.Trunk?Math.Atan2(radial.z,radial.x):Math.Atan2(TreePoint.Dot(radial,other),TreePoint.Dot(radial,side));
                    double r=branch.StartRadius+(branch.EndRadius-branch.StartRadius)*t;
                    if(relief)r+=TrunkRelief(angle,sample.y-origin.y);
                    if(radial.LengthSquared<=r*r)WriteWood(new Int3(x,y,z),BarkColor(branch,branchIndex,angle,t));
                }
                // Attach an interior scaffold point to its parent spine even below raster radius.
                if(branch.ParentIndex>=0&&(branch.Start-branches[branch.ParentIndex].End).LengthSquared>s*s*.01)
                    RasterSpine(branches[branch.ParentIndex].Start,branch.Start,branch,branchIndex);
                RasterSpine(branch.Start,branch.End,branch,branchIndex);
            }
            double RidgeSignal(double angle,double y)
            {
                // Unequal winding ribbons, not a regular gear. The nearest angular ridge is
                // stable in world space and meanders slowly over several meters of the bole.
                double twist=Math.Sin(y*.19+phase)*.07+Math.Sin(y*.071+phase*1.7)*.045;
                double u=(angle+phase+twist)*p.BarkRidgeCount/(Math.PI*2);
                int closest=(int)Math.Floor(u+.5);double value=0;
                for(int i=closest-1;i<=closest+1;i++)
                {
                    int wrapped=((i%p.BarkRidgeCount)+p.BarkRidgeCount)%p.BarkRidgeCount;
                    uint key=Hash(wrapped,83,7,seed);
                    double center=i+(Unit(key)-.5)*.38;
                    double width=.15+Unit(Hash(wrapped,71,13,seed))*.13;
                    double d=(u-center)/width;
                    value=Math.Max(value,Math.Exp(-d*d)*(.65+Unit(Hash(wrapped,29,37,seed))*.35));
                }
                return value;
            }
            double TrunkRelief(double angle,double y)
            {
                double h=Math.Max(0,y),ridge=RidgeSignal(angle,h);
                double barkFade=.16+.84*Math.Exp(-h/Math.Max(1,height*.24));
                double basal=Math.Exp(-h/Math.Max(s*2,radius*1.7));
                double broad=.5+.5*Math.Cos(angle*5+phase+Math.Sin(angle*3+phase)*.36+h*.055);
                // The larger basal flutes are buttress wood. Ordinary bark relief remains
                // controlled independently in meters, so 10cm samples retain honest scale.
                return p.BarkRelief*barkFade*(ridge-.24)+radius*p.RootFlare*.18*basal*Math.Pow(broad,2.5);
            }
            long SpineCost(TreePoint a,TreePoint b)
            {
                Int3 from=Cell(a),to=Cell(b);
                return 1L+Math.Abs((long)from.x-to.x)+Math.Abs((long)from.y-to.y)+Math.Abs((long)from.z-to.z);
            }
            void RasterSpine(TreePoint a,TreePoint b,TreeBranch branch,int branchIndex)
            {
                TreePoint delta=b-a;
                int steps=Math.Max(1,(int)Math.Ceiling(Math.Max(Math.Abs(delta.x),Math.Max(Math.Abs(delta.y),Math.Abs(delta.z)))/s*3));
                Int3 previous=Cell(a);
                if(Candidate())WriteWood(previous,BarkColor(branch,branchIndex,0,0));
                for(int i=1;i<=steps;i++)
                {
                    Int3 next=Cell(TreePoint.Lerp(a,b,i/(double)steps));
                    while(previous.x!=next.x){previous=new Int3(previous.x+Math.Sign(next.x-previous.x),previous.y,previous.z);if(!Candidate())return;WriteWood(previous,BarkColor(branch,branchIndex,0,i/(double)steps));}
                    while(previous.y!=next.y){previous=new Int3(previous.x,previous.y+Math.Sign(next.y-previous.y),previous.z);if(!Candidate())return;WriteWood(previous,BarkColor(branch,branchIndex,0,i/(double)steps));}
                    while(previous.z!=next.z){previous=new Int3(previous.x,previous.y,previous.z+Math.Sign(next.z-previous.z));if(!Candidate())return;WriteWood(previous,BarkColor(branch,branchIndex,0,i/(double)steps));}
                }
            }
            uint BarkColor(TreeBranch branch,int index,double angle,double t)
            {
                double axial=branch.PathDistance+(branch.End-branch.Start).Length*t;
                int around=(int)Math.Floor((angle+Math.PI)*Math.Max(4,branch.StartRadius/.027));
                double grooves=Unit(Hash(around,0,0,seed+101))*2-1;
                double slow=Noise(Math.Cos(angle)*2,axial*.42,Math.Sin(angle)*2,seed+37);
                double brightness=1+treeTone+grooves*.16+slow*.06;
                if(p.BarkStyle==TreeBarkStyle.Birch)
                {
                    // Young fine twigs retain warm dark bark rather than extending the white
                    // mature trunk pattern across the whole crown like bleached antlers.
                    if(branch.Level>=2||branch.StartRadius<.055)
                        return Shade(0xff66513b,Clamp(.95+treeTone+slow*.09+grooves*.09,.69,1.12));
                    int band=(int)Math.Floor(axial*8);
                    uint lenticel=Hash(around/2,band,index/4,seed+311);
                    bool dark=Unit(lenticel)>.84||Unit(Hash(around/5,(int)Math.Floor(axial*1.7),index/6,seed+547))>.96;
                    if(dark)return Shade(0xff302f29,.8+Unit(lenticel)*.35);
                    brightness=1+treeTone+slow*.05+grooves*.035;
                }
                else if(p.BarkStyle==TreeBarkStyle.Redwood)
                {
                    double y=TreePoint.Lerp(branch.Start,branch.End,t).y-origin.y;
                    double ridge=branch.Kind==TreeBranchKind.Trunk?RidgeSignal(angle,y):.5;
                    // Weathered crests and darker reddish fissures follow the same ribbons
                    // as geometry. Narrow interrupted fibers add pigment below relief scale.
                    double fibers=Unit(Hash(around,(int)Math.Floor(axial*.7),0,seed+101))*2-1;
                    brightness=.85+treeTone+ridge*.24+fibers*.075+slow*.06;
                }
                else if(p.BarkStyle==TreeBarkStyle.Pine)
                {
                    double plates=Unit(Hash(around/3,(int)Math.Floor(axial*4.4),index/4,seed+719));
                    brightness+=plates<.17?-.2:(plates-.5)*.09;
                }
                // Quantization supports repeatable palettes while preserving long aligned fissures.
                brightness=Math.Round(brightness*32)/32;
                return Shade(p.BarkColor,Clamp(brightness,.54,1.21));
            }
            void WriteWood(Int3 cell,uint color)
            {
                if(cell.y==0)return; // preserve the protected floor; roots may enter other earth cells.
                if(!world.InBounds(cell)){result.Truncation|=TreeTruncation.WorldBoundary;return;}
                MaterialId old=world.Get(cell);
                // Branches do not excavate existing geology; root/ground intersections may occupy organic cover.
                if(old==MaterialId.Stone||old==MaterialId.Sand||old==MaterialId.Fruit)return;
                if(world.Set(cell,MaterialId.Wood,color))
                {if(old!=MaterialId.Wood)result.WoodCells++;Bounds(cell);}
            }
            void ClipBounds(ref int x0,ref int x1,ref int y0,ref int y1,ref int z0,ref int z1)
            {
                if(x0<0||z0<0||x1>=world.SizeX||z1>=world.SizeZ||y1>=world.SizeY)result.Truncation|=TreeTruncation.WorldBoundary;
                x0=Math.Max(0,x0);x1=Math.Min(world.SizeX-1,x1);y0=Math.Max(1,y0);y1=Math.Min(world.SizeY-1,y1);z0=Math.Max(0,z0);z1=Math.Min(world.SizeZ-1,z1);
            }
            Int3 Cell(TreePoint point)=>new Int3((int)Math.Floor(point.x/s),(int)Math.Floor(point.y/s),(int)Math.Floor(point.z/s));
            void Bounds(Int3 cell)
            {
                if(!hasBounds){result.MinCell=cell;result.MaxCell=cell;hasBounds=true;}
                else
                {
                    result.MinCell=new Int3(Math.Min(cell.x,result.MinCell.x),Math.Min(cell.y,result.MinCell.y),Math.Min(cell.z,result.MinCell.z));
                    result.MaxCell=new Int3(Math.Max(cell.x,result.MaxCell.x),Math.Max(cell.y,result.MaxCell.y),Math.Max(cell.z,result.MaxCell.z));
                }
                result.BoundsMin=new TreePoint(result.MinCell.x*s,result.MinCell.y*s,result.MinCell.z*s);
                result.BoundsMax=new TreePoint((result.MaxCell.x+1)*s,(result.MaxCell.y+1)*s,(result.MaxCell.z+1)*s);
            }
            bool Supports(Int3 cell)
            {
                foreach(Int3 d in Neighbors)
                {MaterialId id=world.Get(cell+d);if(id==MaterialId.Wood||id==MaterialId.Leaves)return true;}
                return false;
            }
            void RasterFoliage(Cluster cluster)
            {
                double r=cluster.Radius*Math.Pow(.4+.6*p.FoliageDensity,.333333);
                TreePoint axis=new TreePoint(cluster.Axis.x,0,cluster.Axis.z).Normalized;
                if(Math.Abs(axis.y)>.9)axis=new TreePoint(1,0,0);
                TreePoint side=TreePoint.Cross(new TreePoint(0,1,0),axis).Normalized;
                double thick=Math.Max(s*.75,r*cluster.Thickness);
                double ex=r*Math.Sqrt(axis.x*axis.x*cluster.Stretch*cluster.Stretch+side.x*side.x)*1.12;
                double ez=r*Math.Sqrt(axis.z*axis.z*cluster.Stretch*cluster.Stretch+side.z*side.z)*1.12;
                double ey=thick*1.12;
                int x0=(int)Math.Floor((cluster.Center.x-ex)/s),x1=(int)Math.Ceiling((cluster.Center.x+ex)/s);
                int y0=(int)Math.Floor((cluster.Center.y-ey)/s),y1=(int)Math.Ceiling((cluster.Center.y+ey)/s);
                int z0=(int)Math.Floor((cluster.Center.z-ez)/s),z1=(int)Math.Ceiling((cluster.Center.z+ez)/s);
                ClipBounds(ref x0,ref x1,ref y0,ref y1,ref z0,ref z1);
                var candidates=new HashSet<Int3>();var ordered=new List<Int3>();
                for(int z=z0;z<=z1;z++)for(int y=y0;y<=y1;y++)for(int x=x0;x<=x1;x++)
                {
                    if(!Candidate())return;
                    MaterialId old=world.Get(x,y,z);if(old!=MaterialId.Air)continue;
                    TreePoint delta=new TreePoint((x+.5)*s,(y+.5)*s,(z+.5)*s)-cluster.Center;
                    double a=TreePoint.Dot(delta,axis)/(r*cluster.Stretch),b=TreePoint.Dot(delta,side)/r,c=delta.y/thick;
                    double shape=a*a+b*b+c*c;
                    double px=(x+.5)*s,py=(y+.5)*s,pz=(z+.5)*s;
                    double noise=Noise(px*3.1,py*3.1,pz*3.1,cluster.Seed)*.34+Noise(px*7.1,py*7.1,pz*7.1,cluster.Seed+79)*.13;
                    // A spatial field carves correlated small air pockets; this is geometry,
                    // not random per-cell color confetti. Keep the inner bearing region intact.
                    double pore=Noise(px*4.3,py*4.3,pz*4.3,seed+1049)*.74+Noise(px*6.7,py*6.7,pz*6.7,seed+2081)*.26;
                    bool cavity=shape>.18&&pore>.12+p.FoliageDensity*.16;
                    if(!cavity&&shape<.74+p.FoliageDensity*.16+noise)
                    {var cell=new Int3(x,y,z);candidates.Add(cell);ordered.Add(cell);}
                }
                if(candidates.Count==0)return;
                // Keep only components attached to existing wood/foliage. All wood was rasterized first.
                // Seeds and neighbor order are explicit; HashSet enumeration never decides growth order.
                var connected=new HashSet<Int3>();var queue=new Queue<Int3>();var leaves=new List<Int3>();
                foreach(Int3 cell in ordered)if(Supports(cell)&&connected.Add(cell))queue.Enqueue(cell);
                while(queue.Count>0)
                {
                    Int3 cell=queue.Dequeue();leaves.Add(cell);
                    foreach(Int3 d in Neighbors){Int3 next=cell+d;if(candidates.Contains(next)&&connected.Add(next))queue.Enqueue(next);}
                }
                if(leaves.Count==0)return;
                foreach(Int3 cell in leaves)
                {
                    // Connectivity work is bounded by the already budgeted cluster candidate region.
                    if(world.Set(cell,MaterialId.Leaves,LeafColor(cell,cluster)))
                    {result.LeafCells++;Bounds(cell);}
                }
                result.FoliageClusters++;
                AddFruit(leaves,cluster);
            }
            uint LeafColor(Int3 cell,Cluster cluster)
            {
                double clump=(Math.Round(Unit(Hash(19,37,11,cluster.Seed))*4)-2)*.028;
                double spatial=Noise(cell.x*s*.6,cell.y*s*.6,cell.z*s*.6,seed+919)*.035;
                double fine=(Math.Round(Unit(Hash(cell.x,cell.y,cell.z,seed+197))*2)-1)*.008;
                double shade=Clamp(1+treeTone+clump+spatial+fine,.84,1.13);
                // Twelve restrained brightness steps, with only tiny within-cluster variation.
                shade=.84+Math.Round((shade-.84)/.026)*.026;
                double warmth=Math.Round(treeTone*28+(Math.Round(Unit(Hash(13,7,37,cluster.Seed))*2)-1)*2);
                return Shade(p.LeafColor,shade,warmth);
            }
            void AddFruit(List<Int3> leaves,Cluster cluster)
            {
                if(p.FruitProbability<=0||Unit(Hash(71,17,37,cluster.Seed))>p.FruitProbability)return;
                // One attached 10 cm fruit sample per selected bearing cluster. The oak preset uses
                // brown acorns; this is a visibility exaggeration, not a literal botanical acorn size.
                int offset=(int)(Hash(3,13,29,cluster.Seed)%(uint)leaves.Count);
                for(int i=0;i<leaves.Count;i++)
                {
                    Int3 leaf=leaves[(offset+i)%leaves.Count];
                    if((leaf.y+.5)*s>cluster.Center.y+s)continue;
                    Int3 fruit=leaf+new Int3(0,-1,0);
                    if(fruit.y<=0||!world.InBounds(fruit)||world.Get(fruit)!=MaterialId.Air)continue;
                    if(!Candidate())return;
                    uint tint=Shade(p.FruitColor,.88+Unit(Hash(fruit.x,fruit.y,fruit.z,seed))*.2);
                    if(world.Set(fruit,MaterialId.Fruit,tint)){result.FruitCells++;Bounds(fruit);}
                    return;
                }
            }
        }
    }
}
