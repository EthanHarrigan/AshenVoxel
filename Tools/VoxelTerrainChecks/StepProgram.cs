using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using System.Text.Json;
using System.Collections.Generic;
using AshenVoxel.Core;
using AshenVoxel.WorldScale;

/// <summary>Engine-free invariants for merged voxel-step terrain. Exact unit-face oracles at
/// the level geometry (t=0) and at the fully morphed parent geometry (t=1).</summary>
static class StepProgram
{
    sealed class Field:IWorldField
    {public Func<double,double,double> Height;public string SourceIdentity=>"explicit-voxel-step-fixture";public SurfaceSample Sample(double x,double z)=>new SurfaceSample(Height(x,z),6,6,true);}
    static int checks;
    static void Check(bool value,string name){if(!value)throw new Exception("FAIL "+name);checks++;Console.WriteLine("PASS "+name);}
    static LodTile Tile(long x=0,long z=0,int level=0)=>new LodTile(new TileKey(x,z,level),x*25.6*Math.Pow(2,level),z*25.6*Math.Pow(2,level),25.6*Math.Pow(2,level),-1000,1000);
    static SurfaceMaterialSampler Sampler(Func<double,double,double> h)=>new SurfaceMaterialSampler(new Field{Height=h},new SurfaceProfile(enableSnow:false));

    // Decoded height texels for oracle use.
    static long Level(VoxelStepSurface s,int x,int z)=>s.HeightTexels[(x+1)+(z+1)*(s.Quads+2)]-VoxelStepSurface.HeightBias;
    static long Parent(VoxelStepSurface s,int px,int pz)=>s.HeightTexels[(px+1)+(s.Quads+2+pz+1)*(s.Quads+2)]-VoxelStepSurface.HeightBias;
    static int PI(int v,int n)=>v<0?-1:v>=n?n/2:v>>1;
    static long ParentOfColumn(VoxelStepSurface s,int x,int z)=>Parent(s,PI(x,s.Quads),PI(z,s.Quads));

    /// <summary>Exact oracle. Unit faces keyed (axis,positive,plane,along,yCell) in level cells.</summary>
    static string Oracle(VoxelStepSurface s,bool morphed)
    {
        int n=s.Quads,step=s.Step;double sp=s.CellMeters;
        var have=new Dictionary<(int,bool,int,int,long),int>();var tops=new Dictionary<(int,int),long>();
        long Cell(double localY)=>(long)Math.Round(localY/.1);
        foreach(var q in s.Faces)
        {
            if(q.Axis==1)
            {
                int x0=(int)Math.Round(q.X/sp),z0=(int)Math.Round(q.Z/sp),w=(int)Math.Round(q.V/sp),d=(int)Math.Round(q.U/sp);
                long y=Cell(morphed?q.MorphLow:q.Y);
                if(q.MorphLow!=q.MorphHigh)return "top quad with uneven morph";
                for(int z=z0;z<z0+d;z++)for(int x=x0;x<x0+w;x++){if(tops.ContainsKey((x,z)))return "duplicate top "+x+","+z;tops[(x,z)]=y;}
                continue;
            }
            bool ax=q.Axis==0;int plane=(int)Math.Round((ax?q.X:q.Z)/sp),a0=(int)Math.Round((ax?q.Z:q.X)/sp),len=(int)Math.Round((ax?q.V:q.U)/sp);
            long lo=Cell(morphed?q.MorphLow:q.Y),hi=Cell(morphed?q.MorphHigh:q.Y+q.Height);
            if(hi<=lo)continue; // degenerate or back-facing: culled
            for(int a=a0;a<a0+len;a++)for(long y=lo;y<hi;y++)
            {var k=(q.Axis,q.Positive,plane,a,y);have.TryGetValue(k,out int c);have[k]=c+1;}
        }
        // Expected heights.
        long H(int x,int z)=>morphed?ParentOfColumn(s,x,z):Level(s,x,z);
        for(int z=0;z<n;z++)for(int x=0;x<n;x++)
        {
            if(!tops.TryGetValue((x,z),out long y))return "missing top "+x+","+z;
            if(y!=H(x,z))return "top height "+x+","+z+" "+y+" vs "+H(x,z);
        }
        // Every interior exposed unit face exactly once; boundary faces covered at least to the neighbour.
        for(int side=0;side<4;side++)
        {
            bool alongX=side<2,positive=(side&1)!=0;int dx=alongX?(positive?1:-1):0,dz=alongX?0:(positive?1:-1);
            for(int line=0;line<n;line++)for(int along=0;along<n;along++)
            {
                int x=alongX?line:along,z=alongX?along:line,nx=x+dx,nz=z+dz;
                bool boundary=nx<0||nz<0||nx>=n||nz>=n;
                int plane=alongX?line+(positive?1:0):line+(positive?1:0);
                long ha=H(x,z),hb=H(nx,nz);
                for(long y=Math.Min(ha,hb)-4*step;y<Math.Max(ha,hb)+4*step;y++)
                {
                    have.TryGetValue((alongX?0:2,positive,plane,along,y),out int c);
                    bool expected=y>=hb&&y<ha;
                    if(boundary){if(expected&&c<1)return "boundary gap at "+x+","+z+" side "+side+" y"+y;if(c>1)return "boundary duplicate";continue;}
                    if(expected&&c!=1)return "interior wall coverage "+c+" at "+x+","+z+" side "+side+" y"+y+" ("+ha+">"+hb+")";
                    if(!expected&&c!=0)return "unexpected interior wall at "+x+","+z+" side "+side+" y"+y;
                }
            }
        }
        return null;
    }


    /// <summary>C# mirror of GroundSurface.shader StepShade/StepDecode index maths. Returns the texel index read.</summary>
    static int ShaderTexel(VoxelStepSurface s,double px,double py,double pz,int axis,bool positive,double morph,out bool parent)
    {
        int n=s.Quads;double c=s.CellMeters;
        double nx=axis==0?(positive?1:-1):0,nz=axis==2?(positive?1:-1):0;
        int cx=Math.Max(0,Math.Min(n-1,(int)Math.Floor((px-nx*.5*c)/c))),cz=Math.Max(0,Math.Min(n-1,(int)Math.Floor((pz-nz*.5*c)/c)));
        int pcx=cx>>1,pcz=cz>>1;
        double vq=s.VerticalMeters,pvq=VoxelStepSurface.VerticalCells(2*s.Step)*.1;double top=Level(s,cx,cz)*.1*(1-morph)+Parent(s,pcx,pcz)*.1*morph,block=vq*(1-morph)+pvq*morph;
        int slice=(int)Math.Max(0,Math.Min(VoxelStepSurface.ColorSlices-1,Math.Floor((top-py)/block+.0005)));
        parent=morph>=.5;
        return parent?VoxelStepSurface.ParentTexel(n,slice,pcx,pcz):cx+(cz+slice*n)*n;
    }
    static string ShaderContract(VoxelStepSurface s,SurfaceMaterialSampler sampler,LodTile tile,bool morphed)
    {
        int n=s.Quads,step=s.Step;long cells=256L<<tile.Key.Level,ox=tile.Key.X*cells,oz=tile.Key.Z*cells;double c=s.CellMeters;
        int checkedPoints=0;
        foreach(var q in s.Faces)
        {
            int axis=q.Axis;
            // Sample the centre of every block cell on the face.
            double ux=axis==1?0:axis==0?0:q.U,uz=axis==1?q.U:0;double vx=axis==1?q.V:0,vz=axis==0?q.V:0;
            double lo=morphed?q.MorphLow:q.Y,hi=morphed?q.MorphHigh:q.Y+q.Height;
            if(axis!=1&&hi<=lo+1e-6)continue;
            double blk=morphed?2*c:c;double vblk=morphed?VoxelStepSurface.VerticalCells(2*step)*.1:s.VerticalMeters;
            int alongCount=(int)Math.Round((axis==1?q.V:axis==0?q.V:q.U)/blk),upCount=axis==1?(int)Math.Round(q.U/blk):(int)Math.Round((hi-lo)/vblk);
            if(morphed&&axis==1){alongCount=Math.Max(1,(int)Math.Round(q.V/c));upCount=Math.Max(1,(int)Math.Round(q.U/c));blk=c;}
            for(int a=0;a<Math.Max(1,alongCount);a++)for(int b=0;b<Math.Max(1,upCount);b++)
            {
                double px,py,pz;
                if(axis==1){px=q.X+(a+.5)*blk;pz=q.Z+(b+.5)*blk;py=morphed?q.MorphLow:q.Y;}
                else if(axis==0){px=q.X;pz=q.Z+(a+.5)*blk;py=lo+(b+.5)*vblk;}
                else{px=q.X+(a+.5)*blk;pz=q.Z;py=lo+(b+.5)*vblk;}
                int texel=ShaderTexel(s,px,py,pz,axis,q.Positive,morphed?1:0,out bool parent);
                // Expected: the authoritative block containing the point, owned by the column behind the face.
                int ownScale=parent?2*step:step;double own=ownScale*.1;int vScale=VoxelStepSurface.VerticalCells(ownScale);double vown=vScale*.1;
                double ix=px-(axis==0?(q.Positive?1:-1)*.5*own:0),iz=pz-(axis==2?(q.Positive?1:-1)*.5*own:0);
                int col=(int)Math.Floor(ix/own),row=(int)Math.Floor(iz/own);
                int limit=parent?n/2:n;if(col<0||row<0||col>=limit||row>=limit)continue;
                long plane=parent?Parent(s,col,row):Level(s,col,row);long planeAbs=plane+s.AnchorCell;
                long cx=ox+(long)col*ownScale+ownScale/2,cz=oz+(long)row*ownScale+ownScale/2;
                var column=sampler.EvaluateColumn((cx+.5)*.1,(cz+.5)*.1);long realTop=VoxelTerrainPage.TopCell(column.SurfaceHeightMeters);
                int k=(int)Math.Floor(((plane*.1)-py)/vown+.0005);k=Math.Max(0,k);
                uint expected;
                if(k<VoxelStepSurface.ExactSlices)
                {var m=sampler.SampleMatter(new WorldCell(cx,VoxelStepSurface.SliceCell(planeAbs,realTop,vScale,k),cz),column);if(m.Material==MaterialId.Air)m=sampler.SampleMatter(new WorldCell(cx,realTop,cz),column);expected=VoxelStepSurface.PackTexel(m.Tint,m.Material,column.Wetness);}
                else expected=s.Colors[parent?VoxelStepSurface.ParentTexel(n,VoxelStepSurface.ExactSlices,col,row):col+(row+VoxelStepSurface.ExactSlices*n)*n];
                if(s.Colors[texel]!=expected)return "shader texel mismatch axis "+axis+" at "+px.ToString("F3")+","+py.ToString("F3")+","+pz.ToString("F3")+" k"+k+(parent?" parent":"");
                checkedPoints++;
            }
        }
        return checkedPoints>0?null:"no points checked";
    }
    static ushort PackColumn(float metres,double cell)=>(ushort)Math.Max(0,Math.Min(65535,Math.Round(metres/cell)));
    static ushort PackCells(float metres)=>(ushort)Math.Max(0,Math.Min(65535,Math.Round(metres*10.0)+VoxelStepSurface.HeightBias));
    static void Main(string[] args)
    {
        var timer=Stopwatch.StartNew();
        // Flat.
        var flat=VoxelStepSurface.Build(Sampler((x,z)=>10),Tile(),256,9);
        Check(flat.Step==1&&flat.Heights.Length==65536&&Math.Abs(flat.AnchorY-10)<1e-6,"flat tile anchors on the exact10cm occupancy boundary");
        Check(flat.Faces.Count(f=>f.Axis==1)<=4,"flat tops merge regardless of per-cell pigment ("+flat.Faces.Count(f=>f.Axis==1)+" rectangles)");
        Check(Oracle(flat,false)==null&&Oracle(flat,true)==null,"flat tile is exactly covered at level and parent geometry");
        // Pigment still varies per cell in the colour texels.
        var distinct=new HashSet<uint>();for(int i=0;i<65536;i++)distinct.Add(flat.Colors[i]&0xFFFFFFu);
        Check(distinct.Count>8,"per-cell pigment is preserved in colour texels ("+distinct.Count+" distinct top colours)");

        // Analytic slopes and ridges at several levels and negative coordinates.
        var shapes=new (string,Func<double,double,double>)[]{
            ("gentle slope",(x,z)=>10+.17*x+.08*z),
            ("steep slope",(x,z)=>40+.9*x-.6*z),
            ("ridges",(x,z)=>30+3*Math.Sin(x*.31)+2*Math.Cos(z*.23+x*.05)+.4*Math.Sin(x*1.7+z*2.3)),
            ("cliff",(x,z)=>x%13<6?50:31.37),
            ("mountain",(x,z)=>2500+60*Math.Sin(x*.02)+45*Math.Cos(z*.017)+7*Math.Sin(x*.4+z*.3))};
        foreach(var (name,h) in shapes)
        {
            var sampler=Sampler(h);
            foreach(var (tx,tz,level,n) in new[]{(0L,0L,0,256),(-1L,-1L,0,256),(3L,-2L,0,64),(-2L,1L,1,128),(1L,1L,2,64),(-1L,0L,3,32),(0L,0L,0,8)})
            {
                var s=VoxelStepSurface.Build(sampler,Tile(tx,tz,level),n,h(tx*25.6*(1<<level),tz*25.6*(1<<level))-400);
                string a=Oracle(s,false),b=Oracle(s,true);
                if(a!=null||b!=null)throw new Exception("FAIL "+name+" tile "+tx+","+tz+" L"+level+" n"+n+": "+(a??b));
            }
            Check(true,name+": exact level and parent coverage at7 tile/level/size combinations incl. negative coordinates");
        }

        // Heights agree with authority and with the coarser level's own quantisation.
        {
            var sampler=Sampler((x,z)=>10+.17*x+.08*z+.5*Math.Sin(x*.7));
            var fine=VoxelStepSurface.Build(sampler,Tile(-1,-1),256,-50);bool ok=true;
            for(int z=0;z<256;z+=9)for(int x=0;x<256;x+=9)
            {
                double px=(-256+x+.5)*.1,pz=(-256+z+.5)*.1;
                ok&=Math.Abs(fine.AnchorY+fine.Heights[x+z*256]-VoxelTerrainPage.SurfaceTop(sampler.EvaluateColumn(px,pz).SurfaceHeightMeters))<.000002;
            }
            Check(ok,"10cm level heights equal the collision/matter surface authority");
            var coarse=VoxelStepSurface.Build(sampler,Tile(-1,-1),128,-50);ok=true;
            for(int z=0;z<128;z++)for(int x=0;x<128;x++)ok&=Math.Abs(coarse.AnchorY+coarse.Heights[x+z*128]-(fine.AnchorY+(ParentOfColumn(fine,2*x,2*z)*.1)))<.00001;
            Check(ok,"fully morphed parent heights equal the next coarser tile exactly (continuous LOD)");
            var bigger=VoxelStepSurface.Build(sampler,Tile(-1,-1,1),128,-50);ok=true;
            // The level-1 tile covering (-1,-1)..(0,0) at 20cm: the quadrant matching tile(-1,-1,0) is its upper-right.
            for(int z=0;z<128;z++)for(int x=0;x<64;x++)ok&=true;
            Check(coarse.Step==2&&bigger.Step==4,"step doubles per level and per halved column count");
            var huge=VoxelStepSurface.Build(sampler,Tile(-1,-1,5),8,-50);Check(huge.Step==1024&&huge.VerticalStep==16&&huge.Heights.All(h=>Math.Abs(h*10-Math.Round(h*10))<1e-3),"coarse startup tiles cap vertical blocks at1.6m instead of quantising heights by100m");
        }

        // Colour texels.
        {
            var sampler=Sampler((x,z)=>20+.3*x);var s=VoxelStepSurface.Build(sampler,Tile(2,3),64,0);bool ok=true;
            for(int z=0;z<64;z+=5)for(int x=0;x<64;x+=5)
            {
                long cx=2*256+x*4+2,cz=3*256+z*4+2;var col=sampler.EvaluateColumn((cx+.5)*.1,(cz+.5)*.1);
                long realTop=VoxelTerrainPage.TopCell(col.SurfaceHeightMeters),plane=VoxelStepSurface.QuantizedTop(realTop,4);
                for(int k=0;k<VoxelStepSurface.ExactSlices;k++)
                {
                    var m=sampler.SampleMatter(new WorldCell(cx,VoxelStepSurface.SliceCell(plane,realTop,4,k),cz),col);
                    ok&=s.Colors[x+(z+k*64)*64]==VoxelStepSurface.PackTexel(m.Tint,m.Material,col.Wetness);
                }
            }
            Check(ok,"exact colour slices are the authoritative pigment of each block's own cells");
            ok=true;
            for(int z=0;z<32;z+=3)for(int x=0;x<32;x+=3)
            {
                long cx=2*256+x*8+4,cz=3*256+z*8+4;var col=sampler.EvaluateColumn((cx+.5)*.1,(cz+.5)*.1);
                long realTop=VoxelTerrainPage.TopCell(col.SurfaceHeightMeters),plane=VoxelStepSurface.QuantizedTop(realTop,8);
                for(int k=0;k<VoxelStepSurface.ExactSlices;k++)
                {
                    var m=sampler.SampleMatter(new WorldCell(cx,VoxelStepSurface.SliceCell(plane,realTop,8,k),cz),col);
                    ok&=s.Colors[VoxelStepSurface.ParentTexel(64,k,x,z)]==VoxelStepSurface.PackTexel(m.Tint,m.Material,col.Wetness);
                }
            }
            Check(ok,"parent colour slices are the authoritative pigment of the parent blocks");
            var rgba=s.ColorBytesRGBA();uint c0=s.Colors[0];
            Check(rgba.Length==s.Colors.Length*4&&rgba[0]==(byte)(c0>>16)&&rgba[2]==(byte)c0&&rgba[3]==(byte)(c0>>24),"RGBA32 byte order matches texture memory layout");
            var used=Enumerable.Range(0,64*64*VoxelStepSurface.ColorSlices).Concat(from k in Enumerable.Range(0,VoxelStepSurface.ColorSlices) from z in Enumerable.Range(0,32) from x in Enumerable.Range(0,32) select VoxelStepSurface.ParentTexel(64,k,x,z)).ToArray();
            Check(used.Distinct().Count()==used.Length&&used.All(i=>i<s.Colors.Length)&&used.Select(i=>s.Colors[i]).All(c=>MaterialCatalog.IsValid(VoxelStepSurface.TexelMaterial(c))&&VoxelStepSurface.TexelMaterial(c)!=MaterialId.Air&&(c&0xFFFFFFu)!=0),"every used colour texel is distinct, in range and carries a solid material and pigment");
            Check(s.ColorHeight==64*VoxelStepSurface.ColorSlices+32*((VoxelStepSurface.ColorSlices+1)/2)&&s.HeightHeight==66+34&&s.HeightTexels.Length==s.HeightWidth*s.HeightHeight,"texture layouts match the documented contract");
        }

        // Determinism.
        {
            var sampler=Sampler((x,z)=>30+3*Math.Sin(x*.31)+2*Math.Cos(z*.23));
            var a=VoxelStepSurface.Build(sampler,Tile(5,-4),256,0,default,false);var b=VoxelStepSurface.Build(sampler,Tile(5,-4),256,0,default,true);
            Check(a.Faces.SequenceEqual(b.Faces)&&a.Colors.SequenceEqual(b.Colors)&&a.HeightTexels.SequenceEqual(b.HeightTexels),"parallel and serial builds are identical");
            using(var cancel=new CancellationTokenSource()){cancel.Cancel();bool threw=false;try{VoxelStepSurface.Build(sampler,Tile(),256,0,cancel.Token,true);}catch(OperationCanceledException){threw=true;}Check(threw,"cancellation stops the build");}
        }


        // Shader contract: every block on every rendered face reads its own authoritative texel.
        foreach(var (name,h) in new (string,Func<double,double,double>)[]{("ridges",(x,z)=>30+3*Math.Sin(x*.31)+2*Math.Cos(z*.23+x*.05)+.4*Math.Sin(x*1.7+z*2.3)),("cliff",(x,z)=>x%13<6?50:31.37)})
        {
            var sampler=Sampler(h);
            foreach(var (tx,tz,level,n) in new[]{(0L,0L,0,256),(-1L,2L,0,64),(1L,-1L,1,128)})
            {
                var tile=Tile(tx,tz,level);var s=VoxelStepSurface.Build(sampler,tile,n,-500);
                string a=ShaderContract(s,sampler,tile,false),b=ShaderContract(s,sampler,tile,true);
                if(a!=null||b!=null)throw new Exception("FAIL shader contract "+name+" "+tx+","+tz+" L"+level+": "+(a??b));
            }
        }
        Check(true,"shader lookup maths reads each block's exact pigment on tops and walls, at level and parent geometry");
        // Packed vertex encoding is exact for every corner, including negative and morph heights.
        {
            var sampler=Sampler((x,z)=>3000+80*Math.Sin(x*.05)+.9*x);bool ok=true;
            foreach(var (level,n) in new[]{(0,256),(2,64),(4,32)})
            {
                var s=VoxelStepSurface.Build(sampler,Tile(-3,5,level),n,2500);double cell=s.CellMeters;
                foreach(var q in s.Faces)foreach(float v in new[]{q.X,q.Z,q.X+(q.Axis==2?q.U:q.Axis==1?q.V:0),q.Z+(q.Axis==1?q.U:q.Axis==0?q.V:0)})ok&=Math.Abs(PackColumn(v,cell)*cell-v)<1e-3;
                foreach(var q in s.Faces)foreach(float v in new[]{q.Y,q.Y+q.Height,q.MorphLow,q.MorphHigh})ok&=Math.Abs((PackCells(v)-VoxelStepSurface.HeightBias)*.1-v)<1e-3;
            }
            Check(ok,"12-byte packed vertices reproduce every corner and morph height exactly");
        }
        // Real bundled terrain: compare with the existing per-pigment voxel LOD at the saved spawn.
        string root=args.Length>0?args[0]:".";
        var report=new Dictionary<string,object>();
        string samples=Path.Combine(root,"Assets/AshenVoxel/Exploration/Samples");
        if(File.Exists(Path.Combine(samples,"ExplorationFine.bytes")))
        {
            var real=LoadReal(samples);long oldFaces=0,newFaces=0,newBytes=0,oldBytes=0;double oldMs=0,newMs=0;int tiles=0;
            foreach(int dz in new[]{-1,0,1})foreach(int dx in new[]{-1,0,1})
            {
                var key=Tile(-745+dx,-399+dz);
                var c1=Stopwatch.StartNew();var old=VoxelLodSurface.Build(real,key,256,0,default,true);oldMs+=c1.Elapsed.TotalMilliseconds;
                var c2=Stopwatch.StartNew();var neu=VoxelStepSurface.Build(real,key,256,0,default,true);newMs+=c2.Elapsed.TotalMilliseconds;
                string a=Oracle(neu,false),b=Oracle(neu,true);if(a!=null||b!=null)throw new Exception("FAIL real tile oracle "+(a??b));
                oldFaces+=old.Faces.Count;newFaces+=neu.Faces.Count;oldBytes+=(long)old.Faces.Count*120;newBytes+=neu.PayloadBytes;tiles++;
            }
            Check(newFaces<oldFaces,"real spawn tiles: merged steps "+newFaces+" faces vs "+oldFaces+" per-pigment voxel faces ("+(oldFaces/(double)newFaces).ToString("F2")+"x)");
            report["spawn_tiles"]=tiles;report["old_faces"]=oldFaces;report["step_faces"]=newFaces;report["old_mib"]=oldBytes/1048576.0;report["step_mib_incl_textures"]=newBytes/1048576.0;
            report["old_build_ms"]=oldMs/tiles;report["step_build_ms"]=newMs/tiles;
            Console.WriteLine("real spawn 3x3: old "+(oldBytes/1048576.0).ToString("F1")+" MiB, step "+(newBytes/1048576.0).ToString("F1")+" MiB incl textures; build "+(oldMs/tiles).ToString("F0")+" vs "+(newMs/tiles).ToString("F0")+" ms/tile");
            // Coarser levels of the same area.
            foreach(var (level,n) in new[]{(1,128),(2,64),(3,64),(4,32)})
            {
                var s=VoxelStepSurface.Build(real,Tile((long)Math.Floor(-745.0/(1<<level)),(long)Math.Floor(-399.0/(1<<level)),level),n,0,default,true);
                string a=Oracle(s,false),b=Oracle(s,true);if(a!=null||b!=null)throw new Exception("FAIL real L"+level+" "+(a??b));
                report["L"+level+"_n"+n+"_faces"]=s.Faces.Count;
            }
            Check(true,"real terrain coarse levels1-4 exactly covered at level and parent geometry");
        }
        report["checks"]=checks;report["seconds"]=timer.Elapsed.TotalSeconds;
        if(args.Length>1)File.WriteAllText(args[1],JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("All "+checks+" voxel step checks passed.");
    }
    static SurfaceMaterialSampler LoadReal(string p)
    {
        HeightAtlas macro,fine;ClimateAtlas mc,fc;ExplorationBiomeAtlas biomes;
        using(var f=File.OpenRead(Path.Combine(p,"ExplorationMacro.bytes")))macro=AtlasIO.Read(f);
        using(var f=File.OpenRead(Path.Combine(p,"ExplorationFine.bytes")))fine=AtlasIO.Read(f);
        using(var f=File.OpenRead(Path.Combine(p,"ExplorationMacro.climate.bytes")))mc=ClimateArchiveIO.Read(f);
        using(var f=File.OpenRead(Path.Combine(p,"ExplorationFine.climate.bytes")))fc=ClimateArchiveIO.Read(f);
        using(var f=File.OpenRead(Path.Combine(p,"ExplorationBiomes.bytes")))biomes=ExplorationBiomeAtlasIO.Read(f);
        var field=new ExplorationAtlasField(macro,mc,fine,fc,512,macro.ExtentX,macro.ExtentZ,1,biomes,macro.ExtentX,macro.ExtentZ);
        return new SurfaceMaterialSampler(new TerrainDetailField(field,714103),new SurfaceProfile(seed:714103,maxSnowDepthMeters:2.4f,winterFraction:.2f));
    }
}
