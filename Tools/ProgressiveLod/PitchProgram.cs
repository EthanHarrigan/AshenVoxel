using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using AshenVoxel.WorldScale;
using AshenVoxel.WorldScaleRuntime;
using UnityEngine;

static class Program
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static int checks;static bool passed=true;
    static void Check(bool value,string name){checks++;passed&=value;Console.WriteLine((value?"PASS ":"FAIL ")+name);}
    static void Shape(float aspect){Screen.height=900;Screen.width=(int)Math.Round(900*aspect);}
    static bool Legacy(Camera camera,WorldPoint eye,LodTile tile)
    {
        Vector3 f=camera.transform.forward;
        double horizontal=Math.Atan(Math.Tan(camera.fieldOfView*Math.PI/360)*Math.Max(1,camera.aspect));
        return VoxelLodQuality.InView(tile,eye,f.x,f.z,Math.Min(Math.PI,horizontal+20*Math.PI/180));
    }
    static LodTile Containing(double x,double y,double z,int level)
    {
        double size=25.6*(1<<level);long tx=(long)Math.Floor(x/size),tz=(long)Math.Floor(z/size);
        return new LodTile(new TileKey(tx,tz,level),tx*size,tz*size,size,y-.01,y+.01);
    }
    static void Main(string[] args)
    {
        var root=new GameObject("Pitch visibility fixture");var w=root.AddComponent<WorldExplorer>();w.UseVoxelLodTerrain=true;
        var cameraObject=new GameObject("Fixture camera");cameraObject.transform.SetParent(root.transform,false);
        var camera=cameraObject.AddComponent<Camera>();
        typeof(WorldExplorer).GetField("view",Private).SetValue(w,camera);
        var visible=(Func<LodTile,bool>)typeof(WorldExplorer).GetMethod("InVoxelView",Private).CreateDelegate(typeof(Func<LodTile,bool>),w);
        var quads=(Func<LodTile,int>)typeof(WorldExplorer).GetMethod("VoxelQuadsFor",Private).CreateDelegate(typeof(Func<LodTile,int>),w);
        var priority=(Func<LodTile,double>)typeof(WorldExplorer).GetMethod("WorkPriority",Private).CreateDelegate(typeof(Func<LodTile,double>),w);
        var distance=(Func<LodTile,double>)typeof(WorldExplorer).GetMethod("DistanceSquared",Private).CreateDelegate(typeof(Func<LodTile,double>),w);
        camera.fieldOfView=75;Shape(16f/9);
        w.SetObserverPose(new WorldPoint(0,1000,0),Quaternion.Euler(60,0,0));
        var known=new LodTile(new TileKey(1,-1,5),819.2,-819.2,819.2,0,0);
        Check(!Legacy(camera,w.GlobalCamera,known),"negative control reproduces the installed60degree pitched-view miss");
        Check(visible(known),"screen-visible lower-right mountain tile survives pitched visibility classification");
        Check(quads(known)==256&&Math.Abs(priority(known)-distance(known))<.01,"the corrected helper grants visible quality and view priority to the same tile");

        int configurations=0,rayTiles=0,misses=0,legacyMisses=0;string firstMiss=null;
        var eye=new WorldPoint(-19065,1100,-10200);
        foreach(float pitch in new float[]{-86,-75,-60,-45,-30,0,30,45,60,75,86})
        foreach(float fov in new float[]{45,75,110})
        foreach(float aspect in new float[]{.5f,1,16f/9,32f/9})
        foreach(float yaw in new float[]{0,73,219})
        foreach(float roll in new float[]{0,37,90})
        {
            configurations++;camera.fieldOfView=fov;Shape(aspect);
            Quaternion rotation=Quaternion.Euler(pitch,yaw,roll);w.SetObserverPose(eye,rotation);
            double tv=Math.Tan(fov*Math.PI/360),th=tv*aspect;
            foreach(float u in new float[]{-1,-.5f,0,.5f,1})
            foreach(float v in new float[]{-1,-.5f,0,.5f,1})
            foreach(float depth in new float[]{256,4096,32768})
            {
                // Independent geometric oracle: a point along a pinhole camera
                // ray at NDC(u,v) lies inside the frustum; its enclosing tile AABB
                // therefore intersects that frustum regardless of center heading.
                Vector3 ray=rotation*new Vector3((float)(u*th),(float)(v*tv),1);
                double x=eye.X+ray.x*depth,y=eye.Y+ray.y*depth,z=eye.Z+ray.z*depth;
                foreach(int level in new[]{0,3,6,9})
                {
                    rayTiles++;LodTile tile=Containing(x,y,z,level);
                    if(!Legacy(camera,eye,tile))legacyMisses++;
                    if(!visible(tile))
                    {
                        misses++;
                        if(firstMiss==null)firstMiss=$"pitch={pitch}, yaw={yaw}, roll={roll}, fov={fov}, aspect={aspect}, ndc={u}/{v}, depth={depth}, level={level}";
                    }
                }
            }
        }
        Check(misses==0,"every sampled visible-frustum ray's containing tile remains a view candidate");
        Check(legacyMisses>0,"the ray sweep detects misses in the installed horizontal-only classifier");

        int levelComparisons=0,levelDifferences=0,rearRejected=0;
        foreach(float fov in new float[]{45,75,110})foreach(float aspect in new float[]{.5f,1,16f/9,32f/9})
        foreach(float yaw in new float[]{0,73,219})
        {
            camera.fieldOfView=fov;Shape(aspect);w.SetObserverPose(eye,Quaternion.Euler(0,yaw,0));
            for(int angle=0;angle<360;angle+=5)
            {
                double radians=(yaw+angle)*Math.PI/180;
                LodTile tile=Containing(eye.X+Math.Sin(radians)*10000,eye.Y,eye.Z+Math.Cos(radians)*10000,0);
                levelComparisons++;if(visible(tile)!=Legacy(camera,eye,tile))levelDifferences++;
            }
            Vector3 f=camera.transform.forward;
            LodTile rear=Containing(eye.X-f.x*10000,eye.Y,eye.Z-f.z*10000,5);
            if(!visible(rear)&&quads(rear)==32&&Math.Abs(priority(rear)-distance(rear)*16)<.1)rearRejected++;
        }
        Check(levelDifferences==0,"level-camera visibility agrees with the installed guard cone across headings and display shapes");
        Check(rearRejected==36,"level-camera rear terrain retains32-column selection and lower build priority");
        camera.fieldOfView=75;Shape(16f/9);w.SetObserverPose(new WorldPoint(0,1000,0),Quaternion.Euler(60,0,0));
        for(int i=0;i<1000;i++)visible(known);
        long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<10000;i++)visible(known);
        long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
        Check(allocated==0,"ten thousand warmed classifications allocate zero managed bytes");
        UnityEngine.Object.Destroy(root);
        var report=new{status=passed?"PASS":"FAIL",checks,configurations,rayTiles,misses,legacyMisses,firstMiss,levelComparisons,levelDifferences,rearRejected,allocated,
            scope="Actual runtime visibility/quality/priority methods with numerical Unity substitutes; independent frustum-ray/AABB oracle across pitch/FOV/aspect/yaw/roll. No native rendering or screenshot attribution."};
        File.WriteAllText(args[0],JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"{rayTiles} ray/tile cases across {configurations} camera configurations; candidate misses={misses}; legacy misses={legacyMisses}.");
        if(!passed)Environment.ExitCode=1;
    }
}
