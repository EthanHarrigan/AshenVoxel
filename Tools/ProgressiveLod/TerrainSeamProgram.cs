using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using AshenVoxel.WorldScale;
using AshenVoxel.WorldScaleRuntime;
using UnityEngine;
static class TerrainSeamProgram
{
    const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
    static int checks;
    sealed class Field:IWorldFieldBounds
    {
        public double Offset;public string SourceIdentity=>"seam-nonlinear-"+Offset;
        public SurfaceSample Sample(double x,double z)=>new SurfaceSample(40+Offset+Math.Sin(x*.16)*3+Math.Cos(z*.19)*2,6,6,true);
        public HeightBounds QueryBounds(double a,double b,double c,double d)=>new HeightBounds(35+Offset,45+Offset);
    }
    static object Get(object o,string n)=>o.GetType().GetField(n,Flags).GetValue(o);
    static void Set(object o,string n,object v)=>o.GetType().GetField(n,Flags).SetValue(o,v);
    static object Call(object o,string n,params object[] a)=>o.GetType().GetMethod(n,Flags).Invoke(o,a);
    static void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;Console.WriteLine("PASS "+why);}
    static LodTile Tile(long x,long z,int level){double d=25.6*(1<<level);return new LodTile(new TileKey(x,z,level),x*d,z*d,d,0,100);}
    static object Add(WorldExplorer w,LodTile tile,int n,Field field,bool contact=false)
    {
        var data=HeightProxySurface.Build(new SurfaceMaterialSampler(field,new SurfaceProfile(enableSnow:false)),tile,n,1);
        var s=Activator.CreateInstance(typeof(WorldExplorer).GetNestedType("Surface",BindingFlags.NonPublic),true);
        var go=new GameObject("seam fixture");Set(s,"Object",go);Set(s,"Mesh",new Mesh());Set(s,"Tile",tile);Set(s,"Quads",n);Set(s,"AnchorY",data.AnchorY);Set(s,"LocalHeights",data.Heights);Set(s,"HeightProxy",true);
        ((IDictionary)Get(w,contact?"contactSurfaces":"surfaces")).Add(tile.Key,s);
        if(!contact)((HashSet<TileKey>)Get(w,"active")).Add(tile.Key);
        return s;
    }
    static double Height(object s,double x,double z){var t=(LodTile)Get(s,"Tile");return HeightProxySurface.TriangleHeight((float[])Get(s,"LocalHeights"),(int)Get(s,"Quads"),(double)Get(s,"AnchorY"),(x-t.OriginXMeters)/t.SizeMeters*(int)Get(s,"Quads"),(z-t.OriginZMeters)/t.SizeMeters*(int)Get(s,"Quads"));}
    static void Profile(WorldExplorer w,object a,object b,bool horizontal,double fixedCoord,double start,double end)
    {
        var points=new SortedSet<double>{start,end};
        typeof(WorldExplorer).GetMethod("SeamBreakpoints",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{points,a,horizontal,fixedCoord,start,end});
        typeof(WorldExplorer).GetMethod("SeamBreakpoints",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{points,b,horizontal,fixedCoord,start,end});
        typeof(WorldExplorer).GetMethod("SeamCrossings",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{points,a,b,horizontal,fixedCoord});
        var list=new List<double>(points);double maxError=0,maxGap=0;bool crossingSplit=true;
        for(int i=0;i<list.Count-1;i++)
        {
            double p=list[i],q=list[i+1];
            double HA(object s,double t)=>Height(s,horizontal?t:fixedCoord,horizontal?fixedCoord:t);
            double a0=HA(a,p),a1=HA(a,q),b0=HA(b,p),b1=HA(b,q);
            crossingSplit&=(a0-b0)*(a1-b1)>-1e-12;
            for(int k=0;k<=7;k++){double f=k/7.0,t=p+(q-p)*f;maxError=Math.Max(maxError,Math.Abs(HA(a,t)-(a0+(a1-a0)*f)));maxError=Math.Max(maxError,Math.Abs(HA(b,t)-(b0+(b1-b0)*f)));maxGap=Math.Max(maxGap,Math.Abs(HA(a,t)-HA(b,t)));}
        }
        Check(crossingSplit,"profile crossings split ribbons before their sides reverse, avoiding bow-tie fins");
        Check(maxGap>.01,"fixture exposes an actual nonzero gap");
        Check(maxError<.000001,"union breakpoints match BOTH actual triangle profiles throughout every bridge segment");
        var indices=HeightProxySurface.SeamRibbonIndices(list.Count);
        Check(indices.Length==(list.Count-1)*12,"every profile interval has two forward and two reverse triangles");
        bool valid=true;for(int i=0;i<indices.Length;i++)valid&=indices[i]>=0&&indices[i]<list.Count*2;Check(valid,"all ribbon indices address their paired profile vertices");
    }
    static void Main(string[] args)
    {
        var root=new GameObject("exact seam numerical fixture");var w=root.AddComponent<WorldExplorer>();w.UseVoxelLodTerrain=true;
        w.ConfigureExternal(new Field(),new SurfaceProfile(enableSnow:false),new WorldPoint(12,50,12),Quaternion.Euler(0,0,0));Call(w,"ClearTerrain");
        var left=Add(w,Tile(0,0,1),8,new Field());var right=Add(w,Tile(2,0,0),64,new Field());var upper=Add(w,Tile(2,1,0),16,new Field{Offset=2});
        Profile(w,left,right,false,51.2,0,25.6);Profile(w,left,upper,false,51.2,25.6,51.2);
        var far=Add(w,Tile(1,0,2),8,new Field());var contact=Add(w,Tile(5,1,0),64,new Field{Offset=1});
        Profile(w,contact,far,false,128,25.6,51.2);Profile(w,contact,far,true,25.6,128,153.6);
        foreach(var s in new[]{left,right,upper,far,contact})Call(w,"TerrainSeamChanged",s);
        for(int i=0;i<100&&w.TerrainSeamPending>0;i++)Call(w,"TickTerrainSeams");
        Check(w.TerrainSeamPending==0&&w.TerrainSeamBytes>0&&w.TerrainSeamBytes<32L*1024*1024,"bounded runtime queue publishes actual seam meshes");
        var seams=(IDictionary)Get(w,"terrainSeams");Check(seams.Contains(contact),"interior contact-mask boundary earns its own bridge mesh");
        object previous=seams[contact];var mesh=(Mesh)Get(previous,"Mesh");
        Set(w,"availableCommitBytes",0L);Set(w,"nextCommitProbe",double.MaxValue);
        Call(w,"TerrainSeamChanged",contact);Call(w,"TickTerrainSeams");
        Check(w.TerrainSeamPending>0&&ReferenceEquals(seams[contact],previous),"commit pressure retains exact pending seam and its existing bridge");
        Set(w,"availableCommitBytes",8L*1024*1024*1024);
        for(int i=0;i<100&&w.TerrainSeamPending>0;i++)Call(w,"TickTerrainSeams");
        Check(w.TerrainSeamPending==0,"recovered commit headroom drains retained seam demand");
        previous=seams[contact];mesh=(Mesh)Get(previous,"Mesh");
        Call(w,"TerrainSeamRemoved",contact);((IDictionary)Get(w,"contactSurfaces")).Remove(((LodTile)Get(contact,"Tile")).Key);
        for(int i=0;i<100&&w.TerrainSeamPending>0;i++)Call(w,"TickTerrainSeams");
        Check(mesh.WasDestroyed&&!seams.Contains(contact),"retired contact cannot retain a floating stale seam");
        w.SetObserverPose(new WorldPoint(5012,50,12),Quaternion.Euler(0,0,0));
        foreach(DictionaryEntry entry in seams){var s=entry.Key;var seam=entry.Value;var go=(GameObject)Get(seam,"Object");if(go==null)continue;var tile=(LodTile)Get(s,"Tile");Check(Math.Abs(go.transform.position.x-(tile.OriginXMeters-w.RenderOrigin.X))<.001,"seam follows terrain origin rebase");}
        Call(w,"ClearTerrain");Check(w.TerrainSeamBytes==0&&w.TerrainSeamPending==0,"clear releases all seam meshes, dependencies and queued work");
        UnityEngine.Object.Destroy(root);File.WriteAllText(args[0],JsonSerializer.Serialize(new{status="PASS",checks,scope="numerical actual triangle edges and runtime shim; no native render/FPS"}));
        Console.WriteLine("All "+checks+" exact terrain seam checks passed");
    }
}
