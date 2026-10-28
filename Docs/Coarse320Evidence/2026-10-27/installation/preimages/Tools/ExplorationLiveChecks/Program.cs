using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AshenVoxel.Core;
using AshenVoxel.WorldScale;
using AshenVoxel.WorldScaleRuntime;
using AshenVoxel.Exploration;
using UnityEngine;

static class LiveChecks
{
    const string Id="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    static int checks;
    static object Get(object value,string name)=>value.GetType().GetField(name,Flags).GetValue(value);
    static void Set(object value,string name,object data)=>value.GetType().GetField(name,Flags).SetValue(value,data);
    static object Call(object value,string name,params object[] args)=>value.GetType().GetMethod(name,Flags).Invoke(value,args);
    static void Check(bool valid,string message){if(!valid)throw new Exception("FAIL "+message);checks++;Console.WriteLine("PASS "+message);}
    static void Throws(Action action,string message){try{action();}catch(ArgumentException){Check(true,message);return;}Check(false,message);}
    sealed class Field:IWorldFieldBounds,IClimateWorldField
    {
        public double Height,Temp=20,Beta=-.006,Season=4,Rain=1300,Cv=30;
        public string Identity=Id;
        public byte Biome=6;
        public string SourceIdentity=>"CONTROLLED-FIXTURE-NOT-NEURAL/"+Identity;
        public SurfaceSample Sample(double x,double z)=>new SurfaceSample(Height,Biome,Biome,Height>=0);
        public HeightBounds QueryBounds(double x0,double z0,double x1,double z1)=>new HeightBounds(Height,Height);
        public bool TrySampleClimate(double x,double z,out SurfaceClimate c){c=new SurfaceClimate(Temp,Season,Rain,Cv,Beta);return true;}
    }
    static ExplorationRefinementPatch Patch(string key,double x,double z,double height=200,double blend=40,double size=40,double spacing=5)
        =>ExplorationRefinementPatch.CopySnapshot(new Field{Height=height,Temp=10,Beta=-.01,Biome=12},key,Id,new ExplorationRefinementBounds(x,z,x+size,z+size),blend,32,spacing);
    static void FieldChecks()
    {
        var basis=new Field{Height=100};var field=new ExplorationRefinementField(basis,Id);
        IWorldField initial=field.CaptureSnapshot();
        var near=Patch("near",560,0);Check(!field.TryPublish(near,new WorldPoint(0,120,0),out var noChanges)&&noChanges.Length==0&&field.ResidentPatches==0,
            "blend plus query/prop guard blocks a patch whose core alone appears outside512m");
        var far=Patch("far",1000,0);
        var raw=new Field{Height=200};var copied=ExplorationRefinementPatch.CopySnapshot(raw,"owned",Id,new ExplorationRefinementBounds(1000,0,1040,40),40,32,5);raw.Height=999;
        Check(copied.Sample(1020,20).HeightMeters==200,"published payload is an owned immutable grid independent of its transport cache");
        Check(field.TryPublish(far,new WorldPoint(0,120,0),out var changes)&&changes.Length==1&&changes[0].DistanceSquared(0,0)>512*512,
            "far publication emits its full influence/query/prop bounds outside the protected player disk");
        Check(field.Sample(1020,20).HeightMeters==200&&field.Sample(0,0).HeightMeters==100&&initial.Sample(1020,20).HeightMeters==100,
            "one atomic publication changes only far samples and preserves captured old state");
        Check(field.Sample(1020,20).SourceBiome==6&&field.Sample(1020,20).EffectiveBiome==6,"authored map biome categories survive neural patch blending");
        Check(field.HasActiveDetail(1020,20)&&field.ResolutionMetersAt(1020,20)==5&&!field.HasActiveDetail(0,0),"active fine detail and its5m source resolution are explicitly queryable");
        Check(!field.TryRetire("far",new WorldPoint(1020,210,20),out _),"a decoded patch cannot retire while the observer stands in its influence");
        var farSnapshot=field.CaptureSnapshot();
        Check(field.TryRetire("far",new WorldPoint(0,120,0),out var retired)&&retired.MinimumX==changes[0].MinimumX&&field.Sample(1020,20).HeightMeters==100&&farSnapshot.Sample(1020,20).HeightMeters==200,
            "safe retirement is atomic and previously captured immutable geometry work remains valid");
        Check(field.Revision==2&&field.SourceIdentity==initial.SourceIdentity&&field.SourceIdentity!=farSnapshot.SourceIdentity,"revision advances while content identity returns exactly to the restored base source");
        Throws(()=>ExplorationRefinementPatch.CopySnapshot(new Field{Identity=new string('b',64)},"wrong",Id,new ExplorationRefinementBounds(0,0,10,10)),"snapshot identity mismatch is rejected before retaining samples");
        var wrong=ExplorationRefinementPatch.CopySnapshot(new Field{Identity=new string('b',64)},"wrong",new string('b',64),new ExplorationRefinementBounds(1000,0,1040,40),40,32,5);
        Throws(()=>field.TryPublish(wrong,new WorldPoint(0,0,0),out _),"a different model world cannot be activated");
        Throws(()=>field.TryPublish(Patch("spacing",1000,0,spacing:10),new WorldPoint(0,0,0),out _),"a different physical sample lattice cannot be activated");
        field.TryPublish(far,new WorldPoint(0,0,0),out _);
        double blendX=980;double h=field.Sample(blendX,20).HeightMeters;
        field.TrySampleClimate(blendX,20,out var climate);
        double expectedTemperature=(20+(-.006)*(h-100))*.5+(10+(-.01)*(h-200))*.5;
        Check(Math.Abs(h-150)<1e-9&&Math.Abs(climate.TemperatureC-expectedTemperature)<1e-6,"unequal lapse rates are adjusted to final blended elevation before physical climate mixing");
        Check(Math.Abs(field.Sample(960,20).HeightMeters-100)<1e-9&&Math.Abs(field.Sample(960.001,20).HeightMeters-100)<1e-5,
            "smooth patch blend reaches the unchanged base continuously at its influence edge");
        field.TryPublish(Patch("overlap",1000,0,height:400),new WorldPoint(0,0,0),out _);
        Check(Math.Abs(field.Sample(1020,20).HeightMeters-300)<1e-9,"overlapping unit weights normalize without overshooting either decoded layer");
        field.TrySampleClimate(1020,20,out climate);
        Check(Math.Abs(climate.TemperatureC-10)<1e-6,"fully decoded overlapping climates normalize after elevation correction");
        HeightBounds bounds=field.QueryBounds(1020,20,1020,20);
        Check(bounds.Minimum<=100&&bounds.Maximum>=400&&bounds.Maximum<400.001,"point-query conservative bounds contain base and patch extrema with finite roundoff margin");
        var corner=Patch("corner",550,550);
        Check(field.TryPublish(corner,new WorldPoint(0,0,0),out _),"disk-to-influence AABB test accepts safe diagonal patches without a wasteful enclosing union");
        var cap=new ExplorationRefinementField(basis,Id,maximumPatches:2,maximumBytes:4*1024*1024);
        cap.TryPublish(Patch("one",1000,0),new WorldPoint(0,0,0),out _);cap.TryPublish(Patch("two",2000,0),new WorldPoint(0,0,0),out _);
        Check(cap.TryPublish(Patch("three",3000,0),new WorldPoint(0,0,0),out var eviction)&&cap.ResidentPatches==2&&!cap.ContainsPatch("one")&&eviction.Length==2&&eviction[0].MaximumX<eviction[1].MinimumX,
            "bounded LRU evicts only safe old payloads and emits separate non-unioned dirty rectangles");
        var locked=new ExplorationRefinementField(basis,Id,maximumPatches:1,maximumBytes:4*1024*1024);
        locked.TryPublish(Patch("kept",1000,0),new WorldPoint(0,0,0),out _);int rev=locked.Revision;
        Check(!locked.TryPublish(Patch("new",3000,0),new WorldPoint(1020,120,20),out var refused)&&locked.ContainsPatch("kept")&&locked.Revision==rev&&refused.Length==0,
            "full protected cache refuses atomically instead of changing ground or partially retiring payloads");
        var byteLimited=new ExplorationRefinementField(basis,Id,maximumBytes:1024);
        Check(!byteLimited.TryPublish(far,new WorldPoint(0,0,0),out _)&&byteLimited.ResidentPayloadBytes==0,"oversized patch refuses before resident payload exceeds byte budget");
        // Concurrent pure samplers see a coherent state throughout repeated main-thread publications.
        bool stable=true;int stop=0;
        Task reader=Task.Run(()=>{while(Volatile.Read(ref stop)==0){IWorldField state=cap.CaptureSnapshot();double a=state.Sample(3020,20).HeightMeters;double b=state.Sample(3020,20).HeightMeters;if(a!=b)stable=false;}});
        for(int i=0;i<100;i++){cap.TryPublish(Patch("three",3000,0,200+i),new WorldPoint(0,0,0),out _);if(cap.ResidentPatches>2||cap.ResidentPayloadBytes>4*1024*1024)stable=false;}
        Volatile.Write(ref stop,1);reader.GetAwaiter().GetResult();
        Check(stable,"concurrent readers retain coherent immutable states during100 bounded replacements");
    }
    static HeightAtlas Height(string path){using(var f=File.OpenRead(path))return AtlasIO.Read(f);}
    static ClimateAtlas Climate(string path){using(var f=File.OpenRead(path))return ClimateArchiveIO.Read(f);}
    static ExplorationAtlasField Atlas(string directory,double width=299520,double height=1)
        =>new ExplorationAtlasField(Height(Path.Combine(directory,"ExplorationMacro.bytes")),Climate(Path.Combine(directory,"ExplorationMacro.climate.bytes")),
            Height(Path.Combine(directory,"ExplorationFine.bytes")),Climate(Path.Combine(directory,"ExplorationFine.climate.bytes")),worldWidthMeters:width,verticalScale:height);
    static void RuntimeChecks(string directory)
    {
        var baked=Atlas(directory);var vista=new WorldExplorer{gameObject=new GameObject("live fixture"),TerrainMaterial=new Material(null),ShowVisualWater=false,AsyncPlanning=false,MaximumTiles=64,MinimumTileSizeMeters=32,TileQuads=8};
        var live=new ExplorationLiveRefinement();IWorldField source=live.Configure(baked,vista,enabled:false);
        Check(live.WorldField!=null&&!live.Connected&&live.PendingRequests==0&&live.Status.Contains("off")&&source.Sample(85760,15330).HeightMeters==baked.Sample(85760,15330).HeightMeters,
            "disabled optional client immediately exposes actual bundled decoded terrain without network setup");
        live.Configure(Atlas(directory,width:150000),vista,enabled:true);live.Tick(new WorldPoint(0,100,0),Vector3.forward);
        Check(!live.Connected&&live.PendingRequests==0&&live.Status.Contains("original map dimensions"),"resized horizontal map refuses untransformed server coordinates despite matching baked world ID");
        live.Configure(Atlas(directory,height:3),vista,enabled:true);live.Tick(new WorldPoint(0,100,0),Vector3.forward);
        Check(!live.Connected&&live.Status.Contains("vertical scale"),"vertical height changes refuse unscaled remote patches while the resized baked world remains available");
        live.Configure(baked,vista,"http://127.0.0.1:1",enabled:true);live.Tick(new WorldPoint(85760,140,15330),Vector3.forward);
        for(int i=0;i<400&&live.Status.Contains("Looking");i++){Thread.Sleep(5);live.Tick(new WorldPoint(85760,140,15330),Vector3.forward);}
        Check(!live.Connected&&live.Status.Contains("unavailable")&&live.WorldField.Sample(85760,15330).HeightMeters==baked.Sample(85760,15330).HeightMeters,
            "unavailable optional service leaves the same bundled world usable and reports its status");
        // Terminal failures must be observed before Connected's terminal-failure check returns early.
        live.Configure(baked,vista,enabled:false);
        var remote=new RemoteTerrainField(baked);typeof(RemoteTerrainField).GetProperty("TerminalFailure",Flags).SetValue(remote,"CONTROLLED CRC rejection");
        Set(live,"remote",remote);Set(live,"requested",true);Set(live,"accepted",true);live.Tick(new WorldPoint(85760,140,15330),Vector3.forward);
        Check(Get(live,"remote")==null&&live.Status.Contains("CRC rejection")&&!live.Connected,"terminal transport failure disposes and schedules retry before the disconnected early return");
        live.SetEnabled(false);
        // Scaled macro extrema fall below the original -12000m model bound. Planning must derive from the supplied field.
        vista.ConfigureExternal(Atlas(directory,height:3),new SurfaceProfile(enableSnow:false),new WorldPoint(0,100,0),Quaternion.Euler(0,0,0));
        Check(vista.PendingTiles>0&&vista.PendingTiles<=64,"verticalScale3 macro extrema derive legal LOD bounds instead of failing below -12000m");
        Call(vista,"OnDestroy");Call(live,"OnDestroy");
    }
    static void RendererChecks()
    {
        var basis=new Field{Height=100};var field=new ExplorationRefinementField(basis,Id);var vista=new WorldExplorer{gameObject=new GameObject("snapshot mesh"),TerrainMaterial=new Material(null),ShowVisualWater=false,AsyncPlanning=false,MaximumTiles=64,MinimumTileSizeMeters=32,TileQuads=8};
        vista.ConfigureExternal(field,new SurfaceProfile(enableSnow:false),new WorldPoint(0,110,0),Quaternion.Euler(0,0,0));
        var tile=new LodTile(new TileKey(15,0,0),960,0,64,100,200);
        var steps=(IEnumerator)Call(vista,"BuildSurfaceSteps",tile,8);Check(steps.MoveNext(),"incremental mesh begins on a captured source snapshot");
        field.TryPublish(Patch("late",960,0,size:64),new WorldPoint(0,110,0),out _);
        object finished=null;while(steps.MoveNext())if(steps.Current!=null)finished=steps.Current;
        double anchor=(double)Get(finished,"AnchorY");var mesh=(Mesh)Get(finished,"Mesh");bool flat=anchor==100;
        for(int i=0;i<81;i++)flat&=mesh.Vertices[i].y==0;
        Check(flat&&field.Sample(980,20).HeightMeters==200,"publication during a multi-frame mesh never mixes old and new terrain rows");
        // Build a complete cache, invalidate a far patch, and retain visible coverage until replacement.
        vista.MeshWorkUnitsPerFrame=2048;vista.MeshBuildMilliseconds=8;vista.MeshBuildsPerFrame=8;
        int frames=0;while(!vista.HasActiveCoverage&&frames++<3000){Time.unscaledTime+=1f/60;Call(vista,"Update");}
        var active=(HashSet<TileKey>)Get(vista,"active");int before=active.Count;vista.RefreshExternalRegion(824,-136,1160,200);
        Check(before>0&&active.Count==before&&vista.HasActiveCoverage&&vista.PendingTiles>0,"regional refresh retains complete active terrain while scheduling changed source meshes");
        vista.SetObserverPose(new WorldPoint(150000,100,0),Quaternion.Euler(0,0,0));vista.ForceReplan();Call(vista,"Update");
        Check(((HashSet<TileKey>)Get(vista,"externallyDirty")).Count<=vista.ResidentTiles+64,"regional dirty metadata remains bounded by active plus desired caches after long travel");
        vista.ShowVisualWater=true;vista.SetObserverPose(new WorldPoint(1024,110,-2048),Quaternion.Euler(0,0,0));
        var water=(VisualWater)Get(vista,"visualWater");WorldPoint origin=vista.RenderOrigin;
        Check(water.SurfaceObject!=null&&Math.Abs(water.SurfaceObject.transform.position.x-(water.WorldCenterX-origin.X))<.001&&
            Math.Abs(water.SurfaceObject.transform.position.z-(water.WorldCenterZ-origin.Z))<.001,
            "observer pose synchronously rebases the visible sea plane with camera and terrain before another Update");
        ((IDisposable)steps).Dispose();Call(vista,"OnDestroy");
    }
    static void LiveProbe(string directory,string endpoint)
    {
        var baked=Atlas(directory);IWorldField retained;
        using(var remote=new RemoteTerrainField(baked,endpoint,false,maximumRegions:2,maximumResidentBytes:4*1024*1024))
        {
            remote.InitializeAsync().GetAwaiter().GetResult();Check(remote.Status.world_id==baked.WorldId&&remote.Status.spacing_m==5,"actual local pretrained service matches the bundled5m coordinate-view identity");
            remote.PrefetchAsync(85725,15295,85815,15385).GetAwaiter().GetResult();retained=remote.ReadySnapshot(85725,15295,85815,15385);
            SurfaceSample actual=retained.Sample(85760,15330);baked.TrySampleClimate(85760,15330,out var expected);
            ((IClimateWorldField)retained).TrySampleClimate(85760,15330,out var climate);
            Check(actual.HeightMeters==baked.Sample(85760,15330).HeightMeters&&climate.TemperatureC==expected.TemperatureC&&climate.SeasonalityC==expected.SeasonalityC&&climate.PrecipitationMm==expected.PrecipitationMm&&climate.PrecipitationCvPercent==expected.PrecipitationCvPercent&&climate.LapseRateCPerMeter==expected.LapseRateCPerMeter,
                "actual HTTP cached canonical-owner height and all five physical climate channels exactly match bundled decoder output");
        }
        var patch=ExplorationRefinementPatch.CopySnapshot(retained,"actual-cached-probe",baked.WorldId,new ExplorationRefinementBounds(85750,15320,85790,15360),10,0,5);
        Check(patch.Sample(85760,15330).HeightMeters==baked.Sample(85760,15330).HeightMeters&&patch.PayloadBytes<16384,"verified actual model snapshot becomes a small owned patch after HTTP cache disposal");
        // Remove the starter fine layer only in this fixture, so the real runtime
        // queues its ahead patch entirely within the already decoded provider cache.
        var macroOnly=new ExplorationAtlasField(baked.Macro,baked.MacroClimate);
        var vista=new WorldExplorer{gameObject=new GameObject("actual component probe"),TerrainMaterial=new Material(null),ShowVisualWater=false,MaximumTiles=64,AsyncPlanning=false};
        var live=new ExplorationLiveRefinement();IWorldField authority=live.Configure(macroOnly,vista,endpoint,true);
        var observer=new WorldPoint(83500,140,15330);double unchanged=authority.Sample(observer.X,observer.Z).HeightMeters;
        vista.ConfigureExternal(authority,new SurfaceProfile(enableSnow:false),observer,Quaternion.Euler(0,0,0));
        int events=0;bool safe=true;live.RegionChanged+=b=>{events++;safe&=b.DistanceSquared(observer.X,observer.Z)>512*512;};
        var deadline=System.Diagnostics.Stopwatch.StartNew();
        while(live.WorldField.ResidentPatches==0&&deadline.Elapsed.TotalSeconds<30)
        {Time.unscaledTime+=.1f;live.Tick(observer,Vector3.right);Thread.Sleep(10);}
        Check(live.Connected&&live.WorldField.ResidentPatches>0&&live.PendingRequests<=2&&events>0&&safe,
            "actual runtime connects, queues bounded cached-model work ahead, publishes a guarded owned patch and emits regional invalidation");
        Check(authority.Sample(observer.X,observer.Z).HeightMeters==unchanged&&live.WorldField.ResidentPayloadBytes<=16*1024*1024,
            "actual asynchronous decoded activation keeps the observer ground unchanged within bounded resident payload");
        int kept=live.WorldField.ResidentPatches;live.SetEnabled(false);
        Check(!live.Connected&&live.PendingRequests==0&&live.WorldField.ResidentPatches==kept,"disabling live requests retains already published immutable decoded terrain");
        Call(live,"OnDestroy");Call(vista,"OnDestroy");
        Console.WriteLine("ACTUAL_LIVE_PROBE {\"world_id\":\""+baked.WorldId+"\",\"x_m\":85760,\"z_m\":15330,\"height_m\":"+patch.Sample(85760,15330).HeightMeters.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+",\"scope\":\"cached real pretrained decoder response; no cold inference\"}");
    }
    static void Main(string[] args)
    {
        PredictiveChecks();FieldChecks();RuntimeChecks(args[0]);RendererChecks();if(args.Length>1)LiveProbe(args[0],args[1]);
        Console.WriteLine("All "+checks+" exploration live refinement checks passed");
    }
    static void PredictiveChecks()
    {
        var origin=new WorldPoint(320,100,320);
        var motion=ExplorationSourceDemandPlanner.PredictDestination(origin,new WorldPoint(420,100,320),.5,1600);
        Check(motion.HasValue&&motion.Value.X==1620&&motion.Value.Z==320,"travel predicts six seconds ahead independently of camera heading");
        Check(!ExplorationSourceDemandPlanner.PredictDestination(origin,new WorldPoint(321,100,320),.5,1600).HasValue,"stationary and slow movement preserve view demand");
        Check(!ExplorationSourceDemandPlanner.PredictDestination(origin,new WorldPoint(20000,100,320),.5,1600).HasValue&&!ExplorationSourceDemandPlanner.PredictDestination(origin,new WorldPoint(420,100,320),3,1600).HasValue,"teleports and stale motion samples never create predicted travel corridors");
        var cells=ExplorationSourceDemandPlanner.Build(origin,0,1,1600,200000,200000,false,false);
        Check(Array.Exists(cells,c=>c.Key=="0/-1/640"&&c.Priority==2)&&Array.Exists(cells,c=>c.Key=="-1/0/640"&&c.Priority==2),"near rear and side source cells precede distant visible detail even without airborne neighborhood permission");
        Check(cells.Length<=ExplorationSourceDemandPlanner.MaximumScannedCells&&cells[0].Current,"near coverage remains bounded and current source remains first");
        var field=new ExplorationRefinementField(new Field{Height=100},Id);
        field.TryPublish(Patch("old",10000,0),origin,out _);
        Check(!field.TryRetire("old",origin,out _,ExplorationRefinementPublicationPolicy.HoldForGameplayMatter(origin))&&field.ResidentPatches==1,"gameplay matter hold protects even distant source from retirement");
        var snapshot=field.CaptureSnapshot();
        Check(field.TryRetire("old",origin,out var dirty)&&dirty.Contains(10020,20)&&field.ResidentPatches==0&&snapshot.Sample(10020,20).HeightMeters==200,"distant unload returns dirty influence bounds while in-flight snapshots retain their exact source");
        field.TryPublish(Patch("old-a",10000,0),origin,out _);field.TryPublish(Patch("old-b",12000,0),origin,out _);
        var live=new ExplorationLiveRefinement();var vista=new WorldExplorer();
        Set(live,"<WorldField>k__BackingField",field);Set(live,"vista",vista);int events=0;
        live.RegionChanged+=bounds=>{events++;Check(bounds.MinimumX>9000,"runtime retirement emits the actual distant influence rectangle");};
        Call(live,"RetireDistantSource",origin,ExplorationRefinementPublicationPolicy.HoldForGameplayMatter(origin));
        Check(events==0&&field.ResidentPatches==2,"runtime unloading honors the active gameplay matter hold");
        Call(live,"RetireDistantSource",origin,default(ExplorationRefinementPublicationPolicy));
        Check(events==1&&live.RetiredDistantPatches==1&&field.ResidentPatches==1,"runtime unloading retires only one distant source per planning interval and reports it");
    }
}
