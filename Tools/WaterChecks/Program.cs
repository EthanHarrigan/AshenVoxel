using System;
using System.Reflection;
using System.Linq;
using AshenVoxel.WorldScale;
using AshenVoxel.WorldScaleRuntime;
using UnityEngine;
static class Program
{
    static int checks;
    sealed class Bed:IWorldField { public double Height=-20;public int Samples;public string Identity="water-bed-v1";public string SourceIdentity=>Identity;public SurfaceSample Sample(double x,double z){Samples++;return new SurfaceSample(Height,0,0,false);} }
    static void Check(bool v,string n){if(!v)throw new Exception("FAIL "+n);checks++;Console.WriteLine("PASS "+n);}
    static void Reject(Action f,string n){try{f();}catch(ArgumentException){Check(true,n);return;}throw new Exception("FAIL accepts "+n);}
    static void Call(object o,string method)=>o.GetType().GetMethod(method,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(o,null);
    static void Main()
    {
        Check(!VisualWater.IsSubmerged(1,-10,0)&&VisualWater.IsSubmerged(-1,-10,0),"water occupancy distinguishes above and submerged camera");
        Check(!VisualWater.IsSubmerged(-11,-10,0)&&!VisualWater.IsSubmerged(-10,-10,0),"camera inside or exactly on solid bed is not treated as underwater");
        Check(!VisualWater.IsSubmerged(-.04,-10,0)&&!VisualWater.IsSubmerged(-9.99,-10,0),"mean-surface and bed margins prevent boundary flicker");
        Reject(()=>VisualWater.IsSubmerged(double.NaN,0,0),"nonfinite camera height rejected");
        bool phase=true,stable=true;double[] kx={2*Math.PI/32,2*Math.PI/128,-2*Math.PI/16,2*Math.PI/8},kz={2*Math.PI/64,-2*Math.PI/32,2*Math.PI/128,2*Math.PI/16};
        for(int wave=0;wave<4;wave++)for(int i=-10;i<=10;i++)
        {
            double ox=1e12+i*100,oz=-1e12-i*200,x=1e12+12.375,z=-1e12-7.125;
            double p=VisualWater.WavePhase(ox,oz,73.5,wave);phase&=p>=0&&p<Math.PI*2;
            double a=Math.Sin((x-ox)*kx[wave]+(z-oz)*kz[wave]+p);
            double b=Math.Sin((x-1e12)*kx[wave]+(z+1e12)*kz[wave]+VisualWater.WavePhase(1e12,-1e12,73.5,wave));
            stable&=Math.Abs(a-b)<1e-11;
        }
        Check(phase&&stable,"wave phase preserves the same global pattern through negative continental rebases");
        Reject(()=>VisualWater.WavePhase(0,0,0,5),"unknown wave rejected");
        var water=new VisualWater();var bed=new Bed();var camera=new Camera();var origin=new WorldPoint(1e9,-100,-1e9);var eye=new WorldPoint(1e9,-1,-1e9);
        water.SetFrame(bed,eye,origin,0,32000,camera);
        Mesh mesh=water.SurfaceMesh;GameObject go=water.SurfaceObject;
        Check(mesh.Vertices.Count==1089&&mesh.Triangles.Count==6144,"one32x32 mean-sea grid has bounded vertex and triangle counts");
        Check(water.LastBedSamples==1090&&bed.Samples==1090,"initial coarse depth hints and underwater query obey the bounded sample count");
        Check(go.transform.position.x==0&&go.transform.position.y==100&&go.transform.position.z==0,"mean sea is placed after double origin subtraction at a billion meters");
        Check(mesh.Vertices.All(v=>v.y==0&&Math.Abs(v.x)<=64000&&Math.Abs(v.z)<=64000),"water geometry remains a flat mean plane within the view-dependent cap");
        bool up=true;for(int i=0;i<mesh.Triangles.Count;i+=3){var a=mesh.Vertices[mesh.Triangles[i]];var b=mesh.Vertices[mesh.Triangles[i+1]];var c=mesh.Vertices[mesh.Triangles[i+2]];up&=(b.z-a.z)*(c.x-a.x)-(b.x-a.x)*(c.z-a.z)>0;}
        Check(up,"all water triangles face upward while shader deliberately supports both sides");
        Check(mesh.Colors.All(c=>c.r==212&&c.a==255)&&water.Underwater&&water.UnderwaterDepthMeters==1,"coarse visual depth colors and mean-level submergence are independent from fluid cells");
        int count=water.MeshRebuildCount;water.SetFrame(bed,eye,origin,0,32000,camera);
        Check(water.MeshRebuildCount==count&&ReferenceEquals(mesh,water.SurfaceMesh)&&water.LastBedSamples==1,"idle submerged frame samples only the bed at the camera and reuses mesh");
        water.SetFrame(bed,new WorldPoint(1e9,1,-1e9),origin,0,32000,camera);
        Check(!water.Underwater&&water.LastBedSamples==0,"above-water idle frame performs no bed resampling");
        water.SetFrame(bed,eye,new WorldPoint(1e9+100,-80,-1e9+200),0,32000,camera);
        Check(ReferenceEquals(mesh,water.SurfaceMesh)&&go.transform.position.x==-100&&go.transform.position.y==80&&go.transform.position.z==-200,"origin shift repositions existing water without rebuilding or losing phase");
        bed.Identity="changed-source";water.SetFrame(bed,eye,origin,0,32000,camera);
        Check(mesh.Destroyed&&water.MeshRebuildCount==count+1&&ReferenceEquals(go,water.SurfaceObject),"world identity change replaces owned mesh without duplicating surface objects");
        bed.Height=-10;water.InvalidateDepthHints();water.InvalidateDepthHints();water.SetFrame(bed,eye,origin,0,32000,camera);
        Check(water.MeshRebuildCount==count+2&&water.SurfaceMesh.Colors.All(c=>c.r==106),"bed refinement invalidations coalesce and refresh colors even with the same source identity");
        water.SetFrame(bed,new WorldPoint(1e9+9000,-1,-1e9),origin,0,32000,camera);
        Check(water.WorldCenterX==1e9+8000&&water.MeshRebuildCount==count+3,"moving beyond a snapped region updates the bounded water grid");
        water.SetFrame(bed,eye,origin,0,1e7,camera);
        Check(water.PlaneHalfExtentMeters==200000&&water.SurfaceMesh.Vertices.Count==1089,"extreme view range cannot create an unbounded grid");
        water.SetFrame(bed,eye,origin,0,32000,camera);Color before=new Color(.9f,.8f,.7f,.6f);GUI.color=before;GUI.depth=7;int draws=GUI.DrawCount;Call(water,"OnGUI");
        Check(GUI.DrawCount==draws+1&&GUI.LastRect.width==Screen.width&&GUI.LastRect.height==Screen.height,"underwater state draws one scoped full-camera tint");
        Check(GUI.color.r==before.r&&GUI.color.a==before.a&&GUI.depth==7&&GUI.LastColor.a>.13f,"underwater overlay restores shared GUI color and depth after drawing");
        water.Visible=false;water.SetFrame(bed,eye,origin,0,32000,camera);Call(water,"OnGUI");
        Check(!water.Underwater&&!go.activeSelf&&GUI.DrawCount==draws+1,"disabling water hides its surface and tint without touching other state");
        water.Visible=true;bed.Height=0;water.SetFrame(bed,new WorldPoint(1e9,-1,-1e9),origin,0,32000,camera);
        Check(!water.Underwater,"being below mean sea inside land does not enable underwater tint");
        var retainedBed=new Bed { Height=-10,Identity="retained-model-bed" };bed.Height=-10;
        var submergedEye=new WorldPoint(1e9,-5,-1e9);
        water.SetFrame(bed,submergedEye,origin,0,32000,camera,true,retainedBed);bed.Height=10;
        water.SetFrame(bed,submergedEye,origin,0,32000,camera,true,retainedBed);
        Check(water.Underwater&&water.UnderwaterDepthMeters==5&&retainedBed.Samples==2,"immutable inspection bed retains underwater occupancy when the wide remote source changes after eviction");
        water.SetFrame(bed,submergedEye,origin,0,32000,camera);
        Check(!water.Underwater,"outside inspection the current global bed remains the occupancy source");
        var lastMesh=water.SurfaceMesh;var ownMaterial=go.Components.OfType<MeshRenderer>().Single().sharedMaterial;water.Dispose();int destroyed=UnityEngine.Object.DestroyCount;water.Dispose();
        Check(lastMesh.Destroyed&&go.Destroyed&&ownMaterial.Destroyed&&UnityEngine.Object.DestroyCount==destroyed&&!water.Underwater&&!Resources.Template.Destroyed,"teardown releases owned resources once and preserves shared template asset");
        var missing=new VisualWater();Resources.Template=null;bool failed=false;try{missing.SetFrame(bed,eye,origin,0,32000,camera);}catch(InvalidOperationException){failed=true;}Check(failed&&missing.SurfaceObject==null,"missing retained material fails explicitly before allocating water objects");missing.Dispose();
        Reject(()=>new VisualWater().SetFrame(bed,eye,origin,double.NaN,32000,camera),"invalid sea level rejected");
        Console.WriteLine("All "+checks+" visual-water checks passed with test Unity substitutes; native shader/import unverified.");
    }
}
