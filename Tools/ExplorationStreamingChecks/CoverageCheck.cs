using System;using System.Collections;using System.Collections.Generic;using System.Linq;using System.Reflection;
using AshenVoxel.WorldScale;using AshenVoxel.Exploration;using UnityEngine;
static class CoverageCheck
{
    const BindingFlags F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static object Get(object x,string n)=>x.GetType().GetField(n,F).GetValue(x);
    static object Call(object x,string n,params object[] a)=>x.GetType().GetMethod(n,F).Invoke(x,a);
    sealed class Forest:IWorldField,IClimateWorldField
    {public string SourceIdentity=>"coverage-budget-fixture";public SurfaceSample Sample(double x,double z)=>new SurfaceSample(120,6,6,true);public bool TrySampleClimate(double x,double z,out SurfaceClimate c){c=new SurfaceClimate(15,3,1300,30,-.0065);return true;}}
    public static void Run(string pack)
    {
        Resources.UseImpostors=true;var v=new VegetationStreaming{TreeDistance=1400,RockDistance=220,GroundCoverDistance=64,PlantDensity=1,MaxInstances=40000,EnableForestHorizon=false};
        v.Initialize(new Forest(),new SurfaceProfile(enableSnow:false),new TextAsset{bytes=System.IO.File.ReadAllBytes(pack)},new Material(),714103);var camera=new Camera();int frames=0;
        while(frames++<8000)
        {v.SetObserver(new WorldPoint(31,121.65,31),new WorldPoint(0,0,0));v.Tick(camera);if(v.Ready&&v.ResidentCells>100&&v.PendingCells==0&&!(bool)Get(v,"buildingMatrices"))break;}
        if(frames>=8000)throw new Exception("Coverage did not settle");
        var models=(IDictionary)Get(v,"models");var cells=(IDictionary)Get(v,"cells");var expected=new HashSet<(int,float,float,float)>();
        foreach(object cached in cells.Values)foreach(var i in ((PopulationCell)Get(cached,"Data")).Instances)
        {
            if(i.PrototypeId>=100)continue;var model=models[i.PrototypeId];var data=(ExplorationPrototype)Get(model,"Data");
            if((double)Call(v,"InstanceDistance",i,data)>v.TreeDistance)continue;
            if(!expected.Add((i.PrototypeId,(float)i.Position.X,(float)i.Position.Y,(float)i.Position.Z)))throw new Exception("Duplicate expected tree");
        }
        var actual=new HashSet<(int,float,float,float)>();
        foreach(object batch in (IList)Get(v,"batches"))
        {
            int id=(int)Get(batch,"ModelId");if(id>=100)continue;
            var matrices=(Matrix4x4[])Get(batch,"Matrices");int count=(int)Get(batch,"Count");
            for(int n=0;n<count;n++)if(!actual.Add((id,matrices[n].m03,matrices[n].m13,matrices[n].m23)))throw new Exception("Duplicate submitted tree");
        }
        if(!expected.SetEquals(actual))throw new Exception($"Fallback coverage expected{expected.Count} actual{actual.Count}");
        if(v.VisibleTriangles>v.TriangleBudget)throw new Exception("Coverage exceeded triangle budget");
        if(v.MatrixBufferBytes>32*1024*1024)throw new Exception("Compact matrix buffers exceeded fixture cap: "+v.MatrixBufferBytes);
        if((long)Get(v,"coverageReserve")!=0)throw new Exception("Completed snapshot retained coverage reservations");
        long minimum=0;foreach(object c in cells.Values)foreach(var i in ((PopulationCell)Get(c,"Data")).Instances)
        {var costs=(int[])Get(models[i.PrototypeId],"Triangles");minimum+=costs[costs.Length-1];}
        if(minimum>v.TriangleBudget)throw new Exception("Fixture fallback guarantee is not feasible");
        // Exhaustible detail budget with a near collidable rock and a later tree.
        // The rock must remain visible while both later minimum costs are reserved.
        int treeId=models.Keys.Cast<int>().First(i=>i<100),rockId=models.Keys.Cast<int>().First(i=>i>=200&&((ExplorationPrototype)Get(models[i],"Data")).HasCollision);
        var treeCosts=(int[])Get(models[treeId],"Triangles");for(int i=0;i<treeCosts.Length-1;i++)treeCosts[i]=99900;treeCosts[treeCosts.Length-1]=2;
        var rockCosts=(int[])Get(models[rockId],"Triangles");for(int i=0;i<rockCosts.Length;i++)rockCosts[i]=1000;
        Type cellType=cells.Values.Cast<object>().First().GetType();cells.Clear();object single=Activator.CreateInstance(cellType,true);
        var key=new PopulationKey(0,0,0);cellType.GetField("Data").SetValue(single,new PopulationCell(key,new[]{
            new PopulationInstance(treeId,new WorldPoint(31,120,31),0,1,1),new PopulationInstance(rockId,new WorldPoint(32,120,31),0,1,2),new PopulationInstance(treeId,new WorldPoint(300,120,31),0,1,3)},3));cells[key]=single;
        v.TriangleBudget=100000;Call(v,"BeginMatrices");for(int i=0;i<50&&(bool)Get(v,"buildingMatrices");i++)Call(v,"BuildMatrices",System.Diagnostics.Stopwatch.GetTimestamp(),1000d);
        var budgetBatches=((IList)Get(v,"batches")).Cast<object>().ToArray();
        if(budgetBatches.Sum(b=>(int)Get(b,"Count"))!=3||!budgetBatches.Any(b=>(int)Get(b,"ModelId")==rockId))throw new Exception("Budget reserve hid a near collidable rock");
        // A source/density reset must clear partial snapshots and their reserves.
        v.RefreshPopulation();if((long)Get(v,"coverageReserve")!=0||v.ResidentCells!=0)throw new Exception("Refresh did not reset coverage reservation");
        v.DisposeRuntime();Resources.UseImpostors=false;
        Console.WriteLine($"PASS exact fallback tree placement set ({actual.Count}), no duplicates, same triangle cap, compact buffer cap, near hard-prop visibility, reserve cleanup; minimum fallback{minimum} triangles; {frames} scheduling frames");
    }
}
