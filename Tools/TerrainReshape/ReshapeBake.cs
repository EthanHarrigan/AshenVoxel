using System;using System.Collections.Generic;using System.Diagnostics;using System.Globalization;using System.IO;using System.Threading.Tasks;using AshenVoxel.WorldScale;
/// <summary>Whole-world landform reshape bake (offline, CPU). Samples the game's own composed height
/// (bundled atlas + packed overview windows), then:
///  1. remaps the broad mountain mass above a soft knee (detail finer than ~250 m is kept at full amplitude),
///  2. thermal weathering on a 2x coarser grid: cliffs above the angle of repose shed talus aprons,
///  3. particle hydraulic erosion: gullies, couloirs and alluvial fans,
/// and writes the result as an additive delta (TerrainReshapeAtlas) plus flow/talus masks.
/// Usage: ReshapeBake samplesDir patches.bin out.bytes [key=value ...]</summary>
static class ReshapeBake
{
    static readonly Dictionary<string,double> P=new Dictionary<string,double>{
        ["spacing"]=40,["blur"]=6,["knee"]=150,["kneeWidth"]=100,["scale"]=.5,
        ["thermalFactor"]=2,["thermalIterations"]=60,["repose"]=38,["cliffKeep"]=.2,
        ["drops"]=.8,["life"]=90,["capacity"]=4,["erode"]=.35,["deposit"]=.25,["evaporate"]=.015,["inertia"]=.08,
        ["x0"]=double.NaN,["z0"]=double.NaN,["x1"]=double.NaN,["z1"]=double.NaN};
    static void Main(string[] a)
    {
        string samples=a[0],patchFile=a[1],output=a[2];
        for(int i=3;i<a.Length;i++){var kv=a[i].Split('=');P[kv[0]]=double.Parse(kv[1],CultureInfo.InvariantCulture);}
        var clock=Stopwatch.StartNew();void Log(string s)=>Console.WriteLine($"{clock.Elapsed.TotalSeconds,7:F1}s {s}");
        var atlas=LoadAtlas(samples);
        double sp=P["spacing"];
        double wx0=double.IsNaN(P["x0"])?-atlas.WorldWidthMeters/2:P["x0"],wz0=double.IsNaN(P["z0"])?-atlas.WorldDepthMeters/2:P["z0"];
        double wx1=double.IsNaN(P["x1"])?atlas.WorldWidthMeters/2:P["x1"],wz1=double.IsNaN(P["z1"])?atlas.WorldDepthMeters/2:P["z1"];
        int cols=(int)Math.Round((wx1-wx0)/sp)+1,rows=(int)Math.Round((wz1-wz0)/sp)+1;
        Log($"grid {cols}x{rows} at {sp} m, origin ({wx0},{wz0})");
        var patches=LoadPatches(patchFile);Log($"{patches.Count} overview cells");
        // 1. Base heights (exactly the game's overview composition without decoded 5 m patches).
        var B=new float[cols*rows];
        Parallel.For(0,rows,z=>{for(int x=0;x<cols;x++){double px=wx0+x*sp,pz=wz0+z*sp;B[x+z*cols]=(float)Compose(atlas,patches,px,pz);}});
        Log("base sampled");
        var land=new bool[cols*rows];for(int i=0;i<B.Length;i++)land[i]=B[i]>0;
        // 2. Remap of the smoothed mass.
        var S=(float[])B.Clone();int R=(int)P["blur"];if(R>0)for(int k=0;k<3;k++)BoxBlur(S,cols,rows,R);
        double knee=P["knee"],kw=P["kneeWidth"],scale=P["scale"];
        var H=new float[cols*rows];
        Parallel.For(0,rows,z=>{for(int x=0;x<cols;x++){int i=x+z*cols;double s=S[i];
            double u=(s-knee)/kw,soft=u>30?kw*u:kw*Math.Log(1+Math.Exp(u));double fade=Math.Clamp(s/60,0,1);fade=fade*fade*(3-2*fade);
            H[i]=(float)(B[i]-(1-scale)*soft*fade);}});
        S=null;Log("remapped");
        var talus=new float[cols*rows];
        Thermal(H,talus,land,cols,rows,sp,(int)P["thermalFactor"],(int)P["thermalIterations"],P["repose"],P["cliffKeep"]);Log("thermal");
        var flow=new float[cols*rows];
        Hydraulic(H,flow,talus,land,cols,rows,sp);Log("hydraulic");
        // 3. Delta, masks.
        var delta=new short[cols*rows];double lo=0,hi=0,sum=0;int n=0;
        for(int i=0;i<delta.Length;i++)
        {
            double d=land[i]?Math.Max(H[i],.5)-B[i]:0;
            d=Math.Clamp(d,-8191,8191);delta[i]=(short)Math.Round(d/TerrainReshapeAtlas.DeltaUnitMeters);lo=Math.Min(lo,d);hi=Math.Max(hi,d);if(land[i]){sum+=d;n++;}
        }
        int aux=2,ac=(cols-1)/aux+1,ar=(rows-1)/aux+1;var fb=new byte[ac*ar];var tb=new byte[ac*ar];
        for(int z=0;z<ar;z++)for(int x=0;x<ac;x++)
        {
            double f=0,t=0;int c=0;
            for(int dz=0;dz<aux;dz++)for(int dx=0;dx<aux;dx++){int xx=Math.Min(cols-1,x*aux+dx),zz=Math.Min(rows-1,z*aux+dz),i=xx+zz*cols;if(!land[i])continue;f=Math.Max(f,flow[i]);t+=talus[i];c++;}
            if(c==0)continue;t/=c;
            fb[x+z*ac]=(byte)Math.Clamp(Math.Round(Math.Log(1+f)/9*255),0,255);
            tb[x+z*ac]=(byte)Math.Clamp(Math.Round(Math.Sqrt(Math.Max(0,t)/40)*255),0,255);
        }
        var inv=CultureInfo.InvariantCulture;var parts=new List<string>();foreach(var kv in P)if(!double.IsNaN(kv.Value))parts.Add(kv.Key+"="+kv.Value.ToString("R",inv));parts.Sort(StringComparer.Ordinal);
        string parameters="reshape-bake-v1;"+string.Join(";",parts)+";source="+atlas.SourceIdentity.Substring(0,Math.Min(96,atlas.SourceIdentity.Length))+";patches="+patches.Count;
        using(var f=File.Create(output))TerrainReshapeIO.Write(f,cols,rows,wx0,wz0,sp,aux,delta,fb,tb,parameters);
        Log($"wrote {output} ({new FileInfo(output).Length/1048576.0:F1} MiB); delta {lo:F0}..{hi:F0} m, land mean {sum/Math.Max(1,n):F0} m");
        if(Environment.GetEnvironmentVariable("RAW") is string raw){var bb=new byte[B.Length*4];Buffer.BlockCopy(B,0,bb,0,bb.Length);File.WriteAllBytes(raw+".base",bb);Buffer.BlockCopy(H,0,bb,0,bb.Length);File.WriteAllBytes(raw+".shaped",bb);}
    }
    sealed class Patch{public double X0,Z0;public int Spacing,Columns;public float[] H;public long CX,CZ;}
    static Dictionary<(long,long),Patch> LoadPatches(string path)
    {
        var d=new Dictionary<(long,long),Patch>();if(!File.Exists(path))return d;
        using var r=new BinaryReader(File.OpenRead(path));int n=r.ReadInt32();
        for(int i=0;i<n;i++){var p=new Patch{CX=r.ReadInt64(),CZ=r.ReadInt64(),Spacing=r.ReadInt32(),Columns=r.ReadInt32()};p.H=new float[p.Columns*p.Columns];
            var b=r.ReadBytes(p.H.Length*4);Buffer.BlockCopy(b,0,p.H,0,b.Length);p.X0=p.CX*2560.0-320;p.Z0=p.CZ*2560.0-320;d[(p.CX,p.CZ)]=p;}
        return d;
    }
    static double Weight(Patch p,double x,double z)
    {
        double minX=p.CX*2560.0,minZ=p.CZ*2560.0,maxX=minX+2560,maxZ=minZ+2560;
        double dx=Math.Max(0,Math.Max(minX-x,x-maxX)),dz=Math.Max(0,Math.Max(minZ-z,z-maxZ));double t=Math.Max(0,Math.Min(1,1-Math.Max(dx,dz)/320));return t*t*(3-2*t);
    }
    static double Height(Patch p,double x,double z)
    {
        int C=p.Columns;double px=Math.Clamp((x-p.X0)/p.Spacing,0,C-1),pz=Math.Clamp((z-p.Z0)/p.Spacing,0,C-1);
        int ix=Math.Min(C-2,(int)Math.Floor(px)),iz=Math.Min(C-2,(int)Math.Floor(pz));double fx=px-ix,fz=pz-iz;int a=ix+iz*C;
        return (p.H[a]*(1-fx)+p.H[a+1]*fx)*(1-fz)+(p.H[a+C]*(1-fx)+p.H[a+C+1]*fx)*fz;
    }
    /// <summary>Mirrors ExplorationOverviewField.State.Sample for a field with no decoded fine patches.</summary>
    static double Compose(ExplorationAtlasField atlas,Dictionary<(long,long),Patch> patches,double x,double z)
    {
        double b=atlas.Sample(x,z).HeightMeters;long cx=(long)Math.Floor(x/2560),cz=(long)Math.Floor(z/2560);double sum=0,total=0;
        for(long j=cz-1;j<=cz+1;j++)for(long i=cx-1;i<=cx+1;i++){if(!patches.TryGetValue((i,j),out var p))continue;double w=Weight(p,x,z);if(w<=0)continue;sum+=Height(p,x,z)*w;total+=w;}
        if(total<=0)return b;double fine=atlas.FineWeightAt(x,z);double weight=Math.Min(1,total)*(1-fine);return b+(sum/total-b)*weight;
    }
    static void BoxBlur(float[] a,int cols,int rows,int r)
    {
        var t=new float[a.Length];
        Parallel.For(0,rows,z=>{double acc=0;int c=0;int row=z*cols;for(int x=-r;x<cols;x++){if(x+r<cols){acc+=a[row+x+r];c++;}if(x-r-1>=0){acc-=a[row+x-r-1];c--;}if(x>=0)t[row+x]=(float)(acc/c);}});
        Parallel.For(0,cols,x=>{double acc=0;int c=0;for(int z=-r;z<rows;z++){if(z+r<rows){acc+=t[x+(z+r)*cols];c++;}if(z-r-1>=0){acc-=t[x+(z-r-1)*cols];c--;}if(z>=0)a[x+z*cols]=(float)(acc/c);}});
    }
    /// <summary>Thermal weathering on a coarse grid; only the coarse change is added back (removal scaled by cliffKeep so faces stay sheer).</summary>
    static void Thermal(float[] h,float[] talus,bool[] land,int cols,int rows,double sp,int k,int iterations,double reposeDeg,double keep)
    {
        int mc=(cols+k-1)/k,mr=(rows+k-1)/k;var c=new float[mc*mr];var cnt=new int[mc*mr];var sea=new bool[mc*mr];
        for(int z=0;z<rows;z++)for(int x=0;x<cols;x++){int j=x/k+z/k*mc;c[j]+=h[x+z*cols];cnt[j]++;}
        for(int j=0;j<c.Length;j++){c[j]/=cnt[j];sea[j]=c[j]<=0;}
        var c0=(float[])c.Clone();double limit=Math.Tan(reposeDeg*Math.PI/180)*sp*k;var inflow=new float[c.Length];var outflow=new float[c.Length];
        int[] dx={1,-1,0,0,1,1,-1,-1},dz={0,0,1,-1,1,-1,1,-1};double[] dd={1,1,1,1,1.41421356,1.41421356,1.41421356,1.41421356};
        for(int it=0;it<iterations;it++)
        {
            // Gather formulation: each cell computes what it loses and what it receives (deterministic, parallel).
            Parallel.For(1,mr-1,z=>{for(int x=1;x<mc-1;x++){int j=x+z*mc;if(sea[j]){outflow[j]=0;continue;}double total=0,maxd=0;
                for(int d=0;d<8;d++){double diff=c[j]-c[x+dx[d]+(z+dz[d])*mc]-limit*dd[d];if(diff>0){total+=diff;if(diff>maxd)maxd=diff;}}
                outflow[j]=total>0?(float)(maxd*.25):0;}});
            Parallel.For(1,mr-1,z=>{for(int x=1;x<mc-1;x++){int j=x+z*mc;double gain=0;
                for(int d=0;d<8;d++){int nx=x+dx[d],nz=z+dz[d];if(nx<1||nz<1||nx>=mc-1||nz>=mr-1)continue;int n=nx+nz*mc;if(outflow[n]<=0)continue;
                    double total=0,mine=0;for(int e=0;e<8;e++){double diff=c[n]-c[nx+dx[e]+(nz+dz[e])*mc]-limit*dd[e];if(diff>0){total+=diff;if(nx+dx[e]==x&&nz+dz[e]==z)mine=diff;}}
                    if(total>0)gain+=outflow[n]*mine/total;}
                inflow[j]=(float)gain;}});
            for(int j=0;j<c.Length;j++)c[j]+=inflow[j]-outflow[j];
        }
        Parallel.For(0,rows,z=>{for(int x=0;x<cols;x++)
        {
            double fx=(x+.5)/k-.5,fz=(z+.5)/k-.5;int x0=Math.Clamp((int)Math.Floor(fx),0,mc-1),z0=Math.Clamp((int)Math.Floor(fz),0,mr-1),x1=Math.Min(mc-1,x0+1),z1=Math.Min(mr-1,z0+1);double tx=Math.Clamp(fx-x0,0,1),tz=Math.Clamp(fz-z0,0,1);
            double D(int xx,int zz)=>c[xx+zz*mc]-c0[xx+zz*mc];double v=(D(x0,z0)*(1-tx)+D(x1,z0)*tx)*(1-tz)+(D(x0,z1)*(1-tx)+D(x1,z1)*tx)*tz;
            int i=x+z*cols;if(!land[i])continue;if(v<0)v*=keep;h[i]=(float)Math.Max(.5,h[i]+v);if(v>0)talus[i]+=(float)v;
        }});
    }
    /// <summary>Particle hydraulic erosion. Deterministic: tiles in four checkerboard phases, one RNG per tile;
    /// droplets that leave their tile's margin stop, so concurrent tiles never touch the same cells.</summary>
    static void Hydraulic(float[] h,float[] flow,float[] deposit,bool[] land,int cols,int rows,double sp)
    {
        const int T=256,M=120;// tile size; droplets stay within M cells of their tile (same-phase tiles are T apart)
        int life=(int)P["life"];double inertia=P["inertia"],capK=P["capacity"],erodeK=P["erode"],depK=P["deposit"],evap=P["evaporate"];
        int tc=(cols+T-1)/T,tr=(rows+T-1)/T;double perCell=P["drops"];
        for(int phase=0;phase<4;phase++)
        {
            var tiles=new List<(int,int)>();for(int ty=0;ty<tr;ty++)for(int tx=0;tx<tc;tx++)if((tx&1)==(phase&1)&&(ty&1)==(phase>>1))tiles.Add((tx,ty));
            Parallel.ForEach(tiles,tile=>
            {
                var (tx,ty)=tile;var rng=new Random(1000003*tx+7919*ty+17);
                int x0=tx*T,z0=ty*T,x1=Math.Min(cols,x0+T),z1=Math.Min(rows,z0+T);int area=(x1-x0)*(z1-z0);int drops=(int)(area*perCell);
                int lx0=Math.Max(1,x0-M),lz0=Math.Max(1,z0-M),lx1=Math.Min(cols-1,x1+M),lz1=Math.Min(rows-1,z1+M);
                double G(int x,int z)=>h[x+z*cols];
                for(int d=0;d<drops;d++)
                {
                    double px=x0+rng.NextDouble()*(x1-x0),pz=z0+rng.NextDouble()*(z1-z0),dx=0,dz=0,speed=1,water=1,sed=0;
                    for(int step=0;step<life;step++)
                    {
                        int ix=(int)px,iz=(int)pz;if(ix<lx0||iz<lz0||ix>=lx1-1||iz>=lz1-1)break;double fx=px-ix,fz=pz-iz;int a=ix+iz*cols;
                        if(!land[a])break;
                        double h00=G(ix,iz),h10=G(ix+1,iz),h01=G(ix,iz+1),h11=G(ix+1,iz+1);
                        double gx=(h10-h00)*(1-fz)+(h11-h01)*fz,gz=(h01-h00)*(1-fx)+(h11-h10)*fx;double hOld=h00*(1-fx)*(1-fz)+h10*fx*(1-fz)+h01*(1-fx)*fz+h11*fx*fz;
                        dx=dx*inertia-gx*(1-inertia);dz=dz*inertia-gz*(1-inertia);double len=Math.Sqrt(dx*dx+dz*dz);if(len<1e-9)break;dx/=len;dz/=len;
                        double nxp=px+dx,nzp=pz+dz;int jx=(int)nxp,jz=(int)nzp;if(jx<lx0||jz<lz0||jx>=lx1-1||jz>=lz1-1)break;double ux=nxp-jx,uz=nzp-jz;
                        double hNew=G(jx,jz)*(1-ux)*(1-uz)+G(jx+1,jz)*ux*(1-uz)+G(jx,jz+1)*(1-ux)*uz+G(jx+1,jz+1)*ux*uz;
                        double dh=hNew-hOld;flow[a]+=(float)water;
                        double cap=Math.Max(-dh/sp,.01)*speed*water*capK*sp*.05;
                        if(sed>cap||dh>0)
                        {
                            double amt=dh>0?Math.Min(dh,sed):(sed-cap)*depK;sed-=amt;
                            h[a]+=(float)(amt*(1-fx)*(1-fz));h[a+1]+=(float)(amt*fx*(1-fz));h[a+cols]+=(float)(amt*(1-fx)*fz);h[a+cols+1]+=(float)(amt*fx*fz);deposit[a]+=(float)amt;
                        }
                        else
                        {
                            double amt=Math.Min((cap-sed)*erodeK,-dh);sed+=amt;
                            h[a]-=(float)(amt*(1-fx)*(1-fz));h[a+1]-=(float)(amt*fx*(1-fz));h[a+cols]-=(float)(amt*(1-fx)*fz);h[a+cols+1]-=(float)(amt*fx*fz);
                        }
                        speed=Math.Sqrt(Math.Max(0,speed*speed-dh/sp*9.8*.1));water*=1-evap;px=nxp;pz=nzp;
                    }
                }
            });
        }
    }
    static ExplorationAtlasField LoadAtlas(string p)
    {
        HeightAtlas macro,fine;ClimateAtlas mc,fc;ExplorationBiomeAtlas biomes;
        using(var f=File.OpenRead(Path.Combine(p,"ExplorationMacro.bytes")))macro=AtlasIO.Read(f);using(var f=File.OpenRead(Path.Combine(p,"ExplorationFine.bytes")))fine=AtlasIO.Read(f);
        using(var f=File.OpenRead(Path.Combine(p,"ExplorationMacro.climate.bytes")))mc=ClimateArchiveIO.Read(f);using(var f=File.OpenRead(Path.Combine(p,"ExplorationFine.climate.bytes")))fc=ClimateArchiveIO.Read(f);
        using(var f=File.OpenRead(Path.Combine(p,"ExplorationBiomes.bytes")))biomes=ExplorationBiomeAtlasIO.Read(f);
        return new ExplorationAtlasField(macro,mc,fine,fc,512,macro.ExtentX,macro.ExtentZ,1,biomes,macro.ExtentX,macro.ExtentZ);
    }
}
