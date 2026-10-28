using System;
using System.Collections.Generic;

namespace AshenVoxel.Core.Details
{
    /// <summary>Small deterministic constructive plants using shared bent axes, tapered leaf fans,
    /// radial/phyllotactic attachment, ellipsoid clusters and the authoritative Set path.
    /// Profiles are static morphology; they do not simulate growth, reproduction or seasons.</summary>
    public static class PlantGenerator
    {
        public const int Version=1;
        public static PlantResult Generate(VoxelWorld world,PlantProfile profile,Int3 baseCell,int seed,PlantGenerationBudget budget=null)
        {
            if(world==null)throw new ArgumentNullException(nameof(world));if(profile==null)throw new ArgumentNullException(nameof(profile));
            if(Math.Abs(world.VoxelSize-.1f)>1e-6)throw new ArgumentException("Plant matter requires the current0.1 m cell contract.",nameof(world));
            if(baseCell.x< -65536||baseCell.x>65536||baseCell.y< -65536||baseCell.y>65536||baseCell.z< -65536||baseCell.z>65536)
                throw new ArgumentOutOfRangeException(nameof(baseCell),"Finite plant anchors must remain inside the supported coordinate envelope.");
            return new Builder(world,profile,baseCell,seed,budget??new PlantGenerationBudget()).Build();
        }
        internal static uint Hash(int x,int y,int z,int seed)
        {unchecked{uint h=(uint)x*374761393u+(uint)y*668265263u+(uint)z*2246822519u+(uint)seed*3266489917u;h=(h^(h>>13))*1274126177u;return h^(h>>16);}}
        static double Clamp(double n,double a,double b)=>Math.Max(a,Math.Min(b,n));
        readonly struct P
        {
            public readonly double x,y,z;
            public P(double x,double y,double z){this.x=x;this.y=y;this.z=z;}
            public static P operator +(P a,P b)=>new P(a.x+b.x,a.y+b.y,a.z+b.z);
            public static P operator -(P a,P b)=>new P(a.x-b.x,a.y-b.y,a.z-b.z);
            public static P operator *(P a,double n)=>new P(a.x*n,a.y*n,a.z*n);
            public double Length=>Math.Sqrt(x*x+y*y+z*z);
        }
        sealed class Builder
        {
            readonly VoxelWorld world;readonly PlantProfile p;readonly Int3 origin;readonly int seed;readonly PlantGenerationBudget budget;
            readonly Dictionary<Int3,MaterialId> cells=new Dictionary<Int3,MaterialId>();
            readonly HashSet<Int3> blocked=new HashSet<Int3>();readonly PlantResult result;
            readonly double h,r,phase;readonly uint[] leaf,wood,accent,fungus;
            bool hasBounds;
            bool Spent=>(result.Truncation&(PlantTruncation.CandidateBudget|PlantTruncation.CellBudget))!=0;
            double Rand(int a,int b=0)=>Hash(a,b,37,seed)/(double)uint.MaxValue;
            public Builder(VoxelWorld world,PlantProfile profile,Int3 origin,int seed,PlantGenerationBudget budget)
            {
                this.world=world;p=profile;this.origin=origin;this.seed=seed;this.budget=budget;
                h=p.HeightMeters*(1+(Rand(11)*2-1)*p.Variation*.08);r=p.RadiusMeters*(1+(Rand(13)*2-1)*p.Variation*.08);phase=Rand(17)*Math.PI*2;
                leaf=Palette(p.FoliageColor,p.PaletteSteps);wood=Palette(p.WoodColor,p.PaletteSteps);accent=Palette(p.AccentColor,p.PaletteSteps);
                if(p.Kind==PlantKind.Reed)leaf[0]=p.AccentColor; // Dry seed heads share plant tissue while retaining the same small palette.
                fungus=Palette(p.AccentColor,p.PaletteSteps);fungus[0]=p.FoliageColor;
                int extent=(int)Math.Ceiling(p.RadiusMeters*1.4/.1)+2;
                result=new PlantResult{Kind=p.Kind,Seed=seed,RequestedMinCell=origin+new Int3(-extent,0,-extent),
                    RequestedMaxCell=origin+new Int3(extent,(int)Math.Ceiling(p.HeightMeters*1.2/.1)+3,extent)};
            }
            static uint[] Palette(uint color,int n)
            {
                var colors=new uint[n];for(int i=0;i<n;i++)
                {double b=.85+.3*i/Math.Max(1,n-1);colors[i]=VoxelColor.Pack((byte)Clamp(Math.Round(((color>>16)&255)*b),0,255),(byte)Clamp(Math.Round(((color>>8)&255)*b),0,255),(byte)Clamp(Math.Round((color&255)*b),0,255));}
                return colors;
            }
            uint Tint(MaterialId material,int band)
            {uint[] colors=material==MaterialId.Wood?wood:material==MaterialId.Fungus?fungus:material==MaterialId.Petal||material==MaterialId.Fruit?accent:leaf;return colors[Math.Max(0,Math.Min(colors.Length-1,band))];}
            public PlantResult Build()
            {
                if(p.HeightMeters<=.100001&&p.RadiusMeters<=.100001)
                    Put(0,0,0,p.Kind==PlantKind.Mushroom?MaterialId.Fungus:p.Kind==PlantKind.Flower?MaterialId.Petal:MaterialId.Leaves,(int)(Rand(6)*p.PaletteSteps));
                else switch(p.Kind)
                {
                    case PlantKind.Fern:Fern();break;case PlantKind.Shrub:Shrub();break;
                    case PlantKind.GrassTuft:Blades(false);break;case PlantKind.Reed:Blades(true);break;
                    case PlantKind.BroadleafHerb:Herb(false);break;case PlantKind.Flower:Herb(true);break;
                    case PlantKind.Mushroom:Mushroom();break;case PlantKind.Cactus:Cactus();break;
                    case PlantKind.Succulent:Succulent();break;case PlantKind.Cushion:Cushion();break;
                }
                var counts=new Dictionary<MaterialId,int>();foreach(var item in cells){if(counts.TryGetValue(item.Value,out int count))counts[item.Value]=count+1;else counts.Add(item.Value,1);}
                result.MaterialCounts=PlantResult.Freeze(counts);result.WrittenCells=cells.Count;
                return result;
            }
            bool Candidate()
            {
                if(Spent)return false;
                if(result.Candidates>=budget.MaxCandidates){result.Truncation|=PlantTruncation.CandidateBudget;return false;}
                result.Candidates++;return true;
            }
            static int Grid(double x)=>(int)Math.Round(x/.1,MidpointRounding.AwayFromZero);
            void Put(P a,MaterialId material,int band)=>Put(Grid(a.x),Math.Max(0,Grid(a.y)),Grid(a.z),material,band);
            void Put(int x,int y,int z,MaterialId material,int band)
            {
                if(!Candidate())return;Write(origin+new Int3(x,y,z),material,band);
            }
            void Write(Int3 at,MaterialId material,int band)
            {
                if(!world.InBounds(at)){result.Truncation|=PlantTruncation.WorldBoundary;return;}
                bool ours=cells.ContainsKey(at);
                if(!ours&&world.Get(at)!=MaterialId.Air){if(blocked.Add(at))result.BlockedCells++;result.Truncation|=PlantTruncation.Occupied;return;}
                if(!ours&&cells.Count>=budget.MaxCells){result.Truncation|=PlantTruncation.CellBudget;return;}
                // Thin branches retain priority when clusters overlap them; foreign solids are never overwritten.
                if(ours&&cells[at]==MaterialId.Wood&&material==MaterialId.Leaves)return;
                world.Set(at,material,Tint(material,band));cells[at]=material;
                if(!hasBounds){result.MinCell=at;result.MaxCell=at;hasBounds=true;}
                else{result.MinCell=new Int3(Math.Min(result.MinCell.x,at.x),Math.Min(result.MinCell.y,at.y),Math.Min(result.MinCell.z,at.z));
                    result.MaxCell=new Int3(Math.Max(result.MaxCell.x,at.x),Math.Max(result.MaxCell.y,at.y),Math.Max(result.MaxCell.z,at.z));}
            }
            void Line(P a,P b,MaterialId material,int band)
            {
                // Six-connected digital axis, including subvoxel stems; intermediate axis steps are explicit matter.
                var last=new Int3(Grid(a.x),Math.Max(0,Grid(a.y)),Grid(a.z));Put(last.x,last.y,last.z,material,band);
                int steps=Math.Max(1,(int)Math.Ceiling((b-a).Length/.035));
                for(int i=1;i<=steps&&!Spent;i++)
                {
                    P q=a+(b-a)*(i/(double)steps);var target=new Int3(Grid(q.x),Math.Max(0,Grid(q.y)),Grid(q.z));
                    while(last.x!=target.x&&!Spent){last=new Int3(last.x+Math.Sign(target.x-last.x),last.y,last.z);Put(last.x,last.y,last.z,material,band);}
                    while(last.y!=target.y&&!Spent){last=new Int3(last.x,last.y+Math.Sign(target.y-last.y),last.z);Put(last.x,last.y,last.z,material,band);}
                    while(last.z!=target.z&&!Spent){last=new Int3(last.x,last.y,last.z+Math.Sign(target.z-last.z));Put(last.x,last.y,last.z,material,band);}
                }
            }
            void Ellipsoid(P center,P axes,MaterialId material,int band,bool textured=false)
            {
                double ax=Math.Max(.055,axes.x),ay=Math.Max(.055,axes.y),az=Math.Max(.055,axes.z);
                int x0=(int)Math.Floor((center.x-ax)/.1),x1=(int)Math.Ceiling((center.x+ax)/.1),y0=Math.Max(0,(int)Math.Floor((center.y-ay)/.1)),y1=(int)Math.Ceiling((center.y+ay)/.1),z0=(int)Math.Floor((center.z-az)/.1),z1=(int)Math.Ceiling((center.z+az)/.1);
                for(int z=z0;z<=z1&&!Spent;z++)for(int y=y0;y<=y1&&!Spent;y++)for(int x=x0;x<=x1&&!Spent;x++)
                {
                    if(!Candidate())return;double dx=(x*.1-center.x)/ax,dy=(y*.1-center.y)/ay,dz=(z*.1-center.z)/az;
                    if(dx*dx+dy*dy+dz*dz<=1)
                    {
                        int tone=band;
                        if(textured)tone=(int)Clamp(band+(Hash(x/2,y/2,z/2,seed)%3==0?1:0),0,p.PaletteSteps-1);
                        Write(origin+new Int3(x,y,z),material,tone);
                    }
                }
                Put(center,material,band);
            }
            void Ribbon(P a,P b,double halfWidth,MaterialId material,int band,P side)
            {
                int count=Math.Max(2,(int)Math.Ceiling((b-a).Length/.07));P last=a;
                for(int i=0;i<=count&&!Spent;i++)
                {
                    double t=i/(double)count;P c=a+(b-a)*t+new P(0,Math.Sin(t*Math.PI)*halfWidth*.4,0);
                    double width=halfWidth*Math.Sin(Math.PI*t)*(.9+.1*Math.Cos(t*5));
                    Line(c-side*width,c+side*width,material,band);if(i>0)Line(last,c,material,band);last=c;
                }
            }
            P Radial(double angle,double distance,double y)=>new P(Math.Cos(angle)*distance,y,Math.Sin(angle)*distance);
            void Fern()
            {
                Line(new P(0,0,0),new P(0,h*.18,0),MaterialId.Leaves,0);
                for(int f=0;f<p.Stems&&!Spent;f++)
                {
                    double angle=phase+f*2.399963229728653+(Rand(f,3)-.5)*.24;
                    double length=r*(.78+.22*Rand(f,4)),rise=h*(.78+.22*Rand(f,5));
                    int count=Math.Max(6,(int)Math.Ceiling(length/.11));P last=new P(0,h*.12,0);P side=new P(-Math.Sin(angle),0,Math.Cos(angle));
                    for(int j=1;j<=count&&!Spent;j++)
                    {
                        double t=j/(double)count;P c=Radial(angle,length*t,h*.12+rise*Math.Sin(t*Math.PI*.85));
                        Line(last,c,MaterialId.Leaves,1);last=c;
                        // A two-cell longitudinal cadence leaves actual air between pinnae. Attaching
                        // a pair at every10 cm sample merges into a broad palm-like sheet after rasterization.
                        int cadence=Math.Max(2,(int)Math.Ceiling(.19/(length/count)));
                        if(j<cadence||j>=count||j%cadence!=0)continue;
                        if(p.Density<.4&&(j/cadence)%2==0)continue;
                        double pinna=r*.27*Math.Pow(Math.Sin(t*Math.PI),.8)*(.65+.35*p.Density);
                        for(int sign=-1;sign<=1;sign+=2)
                        {
                            P tip=c+side*(pinna*sign)+Radial(angle,length*.055,-h*.025);
                            // Separated paired pinnae remain narrow at their tip instead of solid frond plates.
                            Ribbon(c,tip,.022,MaterialId.Leaves,1+(f+j)%Math.Max(1,p.PaletteSteps-1),Radial(angle,1,0));
                        }
                    }
                }
            }
            void Shrub()
            {
                Line(new P(0,0,0),new P(0,h*.5,0),MaterialId.Wood,1);
                for(int i=0;i<p.Stems&&!Spent;i++)
                {
                    double angle=phase+i*2.399963229728653;P branch=new P(0,h*(.1+.26*Rand(i,1)),0);
                    P elbow=Radial(angle,r*.43,h*(.4+.12*Rand(i,2)));P end=Radial(angle,r*(.57+.17*Rand(i,3)),h*(.55+.22*Rand(i,4)));
                    Line(branch,elbow,MaterialId.Wood,i%p.PaletteSteps);Line(elbow,end,MaterialId.Wood,i%p.PaletteSteps);
                    double size=r*(.23+.09*Rand(i,5))*Math.Max(.4,p.Density);
                    Ellipsoid(end,new P(size,h*.2,size*.9),MaterialId.Leaves,1+i%Math.Max(1,p.PaletteSteps-1),true);
                    P tip=end+Radial(angle+.55,r*.15,h*.12);Line(end,tip,MaterialId.Wood,1);
                    Ellipsoid(tip,new P(size*.64,h*.13,size*.72),MaterialId.Leaves,1+i%Math.Max(1,p.PaletteSteps-1));
                    if(Rand(i,8)<p.FruitProbability)Put(end+new P(size*.7,-.06,0),MaterialId.Fruit,p.PaletteSteps-1);
                }
                Line(new P(0,h*.5,0),new P(0,h*.77,0),MaterialId.Wood,1);
                Ellipsoid(new P(0,h*.77,0),new P(r*.3,h*.22,r*.32),MaterialId.Leaves,2%p.PaletteSteps,true);
            }
            void Blades(bool reed)
            {
                for(int i=0;i<p.Stems&&!Spent;i++)
                {
                    double a=phase+i*2.399963229728653;double length=h*(.64+.36*Rand(i,2));
                    P basePoint=Radial(a,reed?r*(.45+.2*Rand(i,3)):r*.17*Rand(i,3),0);P last=basePoint;
                    int stemBand=reed?1+i%Math.Max(1,p.PaletteSteps-1):i%p.PaletteSteps;
                    if(reed)Line(new P(0,0,0),basePoint,MaterialId.Leaves,1);
                    int segments=Math.Max(4,(int)Math.Ceiling(length/.12));
                    for(int j=1;j<=segments&&!Spent;j++)
                    {
                        double t=j/(double)segments;P c=basePoint+Radial(a,r*t*t*(reed?.35:1),length*t-(reed?0:length*.24*t*t));
                        Line(last,c,MaterialId.Leaves,stemBand);last=c;
                    }
                    if(reed)
                    {
                        for(int side=-1;side<=1;side+=2)
                        {double t=side<0?.42:.64;P start=basePoint+Radial(a,r*t*t*.35,length*t);P tip=start+Radial(a+side*.5,r*.45,length*.14);Ribbon(start,tip,.023,MaterialId.Leaves,stemBand,new P(-Math.Sin(a),0,Math.Cos(a)));}
                        Ellipsoid(last,new P(.055,.12,.055),MaterialId.Leaves,0);
                    }
                }
            }
            void Herb(bool flower)
            {
                Line(new P(0,0,0),new P(0,h,0),MaterialId.Leaves,0);
                for(int i=0;i<p.Stems&&!Spent;i++)
                {
                    double a=phase+i*2.399963229728653,y=h*(.12+.55*i/Math.Max(1,p.Stems-1));
                    P start=new P(0,y,0),tip=Radial(a,r*(1-.5*i/Math.Max(1,p.Stems-1)),y+h*.12);
                    Ribbon(start,tip,Math.Max(.04,r*.18),MaterialId.Leaves,1+i%Math.Max(1,p.PaletteSteps-1),new P(-Math.Sin(a),0,Math.Cos(a)));
                }
                if(flower)
                {
                    P center=new P(0,h,0);int petals=5+(seed&1);
                    for(int j=0;j<petals&&!Spent;j++)
                    {P end=center+Radial(phase+j*Math.PI*2/petals,Math.Max(.1,r*.42),.025);Line(center,end,MaterialId.Petal,1+j%Math.Max(1,p.PaletteSteps-1));}
                    Put(center,MaterialId.Petal,p.PaletteSteps-1);
                }
            }
            void Mushroom()
            {
                double capRadius=r,capY=h*.67;double lean=(Rand(23)-.5)*r*.35;
                Line(new P(0,0,0),new P(lean,capY,0),MaterialId.Fungus,0);
                if(r>.18)Ellipsoid(new P(lean*.4,capY*.45,0),new P(Math.Max(.055,r*.18),capY*.48,Math.Max(.055,r*.18)),MaterialId.Fungus,0);
                int variant=(int)(Hash(31,7,11,seed)%3);double capThickness=h*(variant==0?.3:variant==1?.18:.38);
                int extent=(int)Math.Ceiling(capRadius/.1),bottom=Grid(capY),top=Grid(capY+capThickness);
                for(int z=-extent;z<=extent&&!Spent;z++)for(int x=-extent;x<=extent&&!Spent;x++)
                {
                    double dx=(x*.1-lean)/capRadius,dz=z*.1/capRadius,q=dx*dx+dz*dz;
                    if(q>1)continue;
                    double shape=variant==1?.3+.7*(1-q):Math.Sqrt(Math.Max(0,1-q));int ceiling=Grid(capY+capThickness*shape);
                    for(int y=bottom;y<=Math.Max(bottom,ceiling)&&!Spent;y++)
                    {int band=1+(int)Clamp((ceiling-y)/(double)Math.Max(1,top-bottom)*(p.PaletteSteps-2),0,p.PaletteSteps-2);
                        if(y==ceiling&&Hash(x/2,17,z/2,seed)%9==0)band=0;Put(x,y,z,MaterialId.Fungus,band);}
                }
            }
            void Cactus()
            {
                double trunk=Math.Max(.09,r*.24);Ellipsoid(new P(0,h*.45,0),new P(trunk,h*.46,trunk),MaterialId.Leaves,1,true);
                for(int i=0;i<Math.Min(5,p.Stems)&&!Spent;i++)
                {
                    double a=phase+i*2.399963229728653,y=h*(.3+.25*Rand(i,11));P start=new P(0,y,0),elbow=Radial(a,r*.63,y);
                    Line(start,elbow,MaterialId.Leaves,0);Ellipsoid(elbow+new P(0,h*.12,0),new P(trunk*.8,h*.18,trunk*.8),MaterialId.Leaves,(i+2)%p.PaletteSteps);
                    Line(start,elbow+new P(0,h*.12,0),MaterialId.Leaves,1);
                }
            }
            void Succulent()
            {
                Line(new P(0,0,0),new P(0,h*.25,0),MaterialId.Leaves,0);
                for(int i=0;i<p.Stems&&!Spent;i++)
                {
                    double a=phase+i*2.399963229728653;double length=r*(.6+.4*Rand(i,8));P start=new P(0,h*.1,0),tip=Radial(a,length,h*(.4+.55*Rand(i,9)));
                    Ribbon(start,tip,Math.Max(.055,r*.17),MaterialId.Leaves,i%p.PaletteSteps,new P(-Math.Sin(a),0,Math.Cos(a)));
                }
            }
            void Cushion()
            {
                Ellipsoid(new P(0,h*.33,0),new P(r*.68,h*.4,r*.68),MaterialId.Leaves,1,true);
                for(int i=0;i<p.Stems&&!Spent;i++)
                {double a=phase+i*2.399963229728653;P c=Radial(a,r*.66,h*.25);Line(new P(0,h*.2,0),c,MaterialId.Leaves,0);
                    Ellipsoid(c,new P(r*.26,h*.28,r*.26),MaterialId.Leaves,i%p.PaletteSteps,true);}
            }
        }
    }
}
