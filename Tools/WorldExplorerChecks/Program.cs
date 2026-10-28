using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using AshenVoxel.WorldScale;
using AshenVoxel.WorldScaleRuntime;
using AshenVoxel.Core;
using UnityEngine;

static class Program
{
    static int checks;
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    static void Set(object value,string name,object data)=>value.GetType().GetField(name,Flags).SetValue(value,data);
    static object Get(object value,string name)=>value.GetType().GetField(name,Flags|BindingFlags.Public).GetValue(value);
    static object Call(object value,string name,params object[] args)=>value.GetType().GetMethod(name,Flags).Invoke(value,args);
    static void Check(bool value,string name){if(!value)throw new Exception("FAIL "+name);checks++;Console.WriteLine("PASS "+name);}
    sealed class PlaneField:IWorldFieldBounds
    {
        public string SourceIdentity=>"test-plane";
        public double OffsetX,OffsetZ;
        public bool Slope;
        public SurfaceSample Sample(double x,double z)=>new SurfaceSample(6500+(Slope?(x-OffsetX)*.2+(z-OffsetZ)*.1:0),6,6,true);
        public HeightBounds QueryBounds(double x0,double z0,double x1,double z1)=>new HeightBounds(Sample(x0,z0).HeightMeters,Sample(x1,z1).HeightMeters);
    }
    sealed class SpikeField:IWorldFieldBounds
    {
        public string SourceIdentity=>"6200m-shared-edge-spike";
        public SurfaceSample Sample(double x,double z)=>new SurfaceSample(Math.Abs(x-128)<.25&&Math.Abs(z-2)<.25?6200:1000,6,6,true);
        public HeightBounds QueryBounds(double x0,double z0,double x1,double z1)=>new HeightBounds(1000,x0<=128.25&&x1>=127.75&&z0<=2.25&&z1>=1.75?6200:1000);
    }
    sealed class ModelBoundsField:IWorldFieldBounds
    {
        public string SourceIdentity=>"unknown-model-interiors";
        public SurfaceSample Sample(double x,double z)=>new SurfaceSample(6500,6,6,true);
        public HeightBounds QueryBounds(double x0,double z0,double x1,double z1)=>new HeightBounds(-12000,48000);
    }
    sealed class WarmForestField:IWorldField,IClimateWorldField
    {
        public string SourceIdentity=>"controlled-warm-forest-integration";
        public SurfaceSample Sample(double x,double z)=>new SurfaceSample(1.05,6,6,true);
        public bool TrySampleClimate(double x,double z,out SurfaceClimate climate)
        {climate=new SurfaceClimate(14,2,1300,30,-.0065);return true;}
    }
    static Vector3 Cross(Vector3 a,Vector3 b)=>new Vector3(a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x);
    static void Main(string[] args)
    {
        var sharedTemplate=new Material(null);
        var ownerA=new WorldExplorer{gameObject=new GameObject("Material owner A"),TerrainMaterial=sharedTemplate};
        var ownerB=new WorldExplorer{gameObject=new GameObject("Material owner B"),TerrainMaterial=sharedTemplate};
        Call(ownerA,"EnsureCameraAndMaterial");Call(ownerB,"EnsureCameraAndMaterial");
        var materialA=(Material)Get(ownerA,"ownedMaterial");var materialB=(Material)Get(ownerB,"ownedMaterial");
        Check(object.ReferenceEquals(ownerA.TerrainMaterial,sharedTemplate)&&object.ReferenceEquals(ownerB.TerrainMaterial,sharedTemplate)&&
            !object.ReferenceEquals(materialA,materialB)&&object.ReferenceEquals(materialA.Template,sharedTemplate),
            "two explorers retain the inspector template while owning separate mutable material copies");
        Set(ownerA,"cameraX",1e9+.5);Set(ownerB,"cameraX",3000.0);Call(ownerA,"Rebase",true);Call(ownerB,"Rebase",true);
        Check(sharedTemplate.VectorWrites==0&&materialA.VectorWrites==1&&materialB.VectorWrites==1,
            "independent rebasing phases do not mutate a shared terrain material asset");
        Call(ownerA,"OnDestroy");Call(ownerB,"OnDestroy");
        Check(materialA.WasDestroyed&&materialB.WasDestroyed&&!sharedTemplate.WasDestroyed,
            "scene cleanup releases only owned terrain copies and preserves the assigned shared material");
        var explorer = new WorldExplorer { TileQuads=8,TerrainMaterial=new Material(null),MaximumTiles=64,MeshBuildsPerFrame=2,EnableSurfaceSnow=false };
        var field = new PlaneField { OffsetX=1e9,OffsetZ=-1e9,Slope=true };
        Set(explorer,"field",field);Set(explorer,"originX",1e9-64);Set(explorer,"originY",6400.0);Set(explorer,"originZ",-1e9-128);
        var tile = new LodTile(new TileKey(1,2,0),1e9,-1e9,64,6500,6520);
        object surface = Call(explorer,"BuildSurface",tile);
        var mesh = (Mesh)Get(surface,"Mesh"); var go=(GameObject)Get(surface,"Object");
        Check(mesh.Vertices.Count==145&&mesh.Triangles.Count==576,"bounded8x8 mesh includes grid and perimeter skirts");
        Check(go.transform.position.x==64&&go.transform.position.y==100&&go.transform.position.z==128,"tile placement subtracts double world origin before converting to float");
        bool local=true,finite=true;
        foreach(var p in mesh.Vertices){local&=p.x>=0&&p.x<=64&&p.z>=0&&p.z<=64;finite&=!float.IsNaN(p.y)&&Math.Abs(p.y)<400;}
        Check(local&&finite,"mesh retains small tile-local coordinates even a billion meters from world origin");
        bool up=true;for(int i=0;i<8*8*6;i+=3){var a=mesh.Vertices[mesh.Triangles[i]];var b=mesh.Vertices[mesh.Triangles[i+1]];var c=mesh.Vertices[mesh.Triangles[i+2]];up&=Cross(b-a,c-a).y>0;}
        Check(up,"all heightfield surface triangles face upward");
        bool outward=true;
        for(int edge=0;edge<4;edge++)for(int i=0;i<8;i++)
        {
            int at=8*8*6+(edge*8+i)*6;var a=mesh.Vertices[mesh.Triangles[at]];var b=mesh.Vertices[mesh.Triangles[at+1]];var c=mesh.Vertices[mesh.Triangles[at+2]];
            var normal=Cross(b-a,c-a);outward&=edge==0?normal.z<0:edge==1?normal.x>0:edge==2?normal.z>0:normal.x<0;
        }
        Check(outward,"all four skirts face outward instead of being culled from outside");
        bool colors=true;foreach(Color32 color in mesh.Colors)colors&=color.a==255;
        Check(colors,"height proxy appearance supplies opaque vertex colors");
        Check(mesh.SurfaceParameters.Count==mesh.Vertices.Count&&mesh.SurfaceParameters[0].w==1,
            "height proxies expose continuous roughness and wetness attributes without repurposing material colors or AO");
        var snowy = new WorldExplorer { TileQuads=8,TerrainMaterial=new Material(null),EnableSurfaceSnow=true };
        var coldProfile = new SurfaceProfile(seed:714103,climateOverride:new SurfaceClimate(-12,5,1800,35,-.0065));
        var coldBase = new PlaneField();var coldSampler=new SurfaceMaterialSampler(coldBase,coldProfile);
        double snowX=0,snowZ=0;SurfaceColumn snowColumn=default;bool foundSnow=false;
        for(int z=0;z<100&&!foundSnow;z+=2)for(int x=0;x<100&&!foundSnow;x+=2)
        {snowColumn=coldSampler.EvaluateColumn(x,z);if(snowColumn.SnowDepthMeters>=.2){snowX=x;snowZ=z;foundSnow=true;}}
        Check(foundSnow,"cold material fixture contains a physical snow layer rather than an elevation-only white tint");
        Set(snowy,"field",coldBase);Set(snowy,"surfaceProfile",coldProfile);
        object snowSurface=Call(snowy,"BuildSurface",new LodTile(new TileKey(0,0,0),snowX,snowZ,64,6500,6500));
        var snowMesh=(Mesh)Get(snowSurface,"Mesh");double snowAnchor=(double)Get(snowSurface,"AnchorY");
        Check(Math.Abs(snowAnchor-snowColumn.SurfaceHeightMeters)<1e-7&&snowAnchor>snowColumn.GroundHeightMeters,
            "far mesh height includes the same positive snow depth as authoritative surface columns");
        var packed=new VoxelWorld(16,32,16,.1f,714103);Set(snowy,"patch",packed);Set(snowy,"patchX",snowX);Set(snowy,"patchY",6499.2);Set(snowy,"patchZ",snowZ);
        Set(snowy,"inspectionField",coldBase);Set(snowy,"inspectionSampler",coldSampler);Set(snowy,"buildingInspection",true);
        var snowBuild=(IEnumerator)Call(snowy,"BuildInspection",16,32);while(snowBuild.MoveNext()){}
        int snowCells=0,otherCells=0;bool variedTint=false;uint initial=0;
        foreach(var chunk in packed.Chunks.Values)for(int i=0;i<Chunk.Volume;i++)
        {MaterialId material=chunk.Cells[i];if(material==MaterialId.Snow)snowCells++;else if(material!=MaterialId.Air)otherCells++;
         if(material!=MaterialId.Air){Int3 localCell=Chunk.Decode(i);uint tint=chunk.GetTint(localCell.x,localCell.y,localCell.z);if(tint!=0){if(initial==0)initial=tint;else if(tint!=initial)variedTint=true;}}}
        Check(snowCells>0&&otherCells>0&&variedTint,"local10cm inspection contains real snow over non-snow matter with persistent material pigment variation");
        var bounded=new SurfaceWorldField(new PlaneField(),coldProfile);
        var expanded=new LodSettings(maxTiles:64,minimumHeightMeters:-12000,maximumHeightMeters:48000+coldProfile.MaxSnowDepthMeters);
        Check(LodPlanner.Build(bounded,new WorldPoint(0,6740,0),expanded).Tiles.Count>0,
            "snow-adjusted source bounds remain legal for LOD selection rather than dropping the model world");
        var modelWorld=new WorldExplorer { MaximumTiles=64,TerrainMaterial=new Material(null) };
        Set(modelWorld,"field",new SurfaceWorldField(new ModelBoundsField(),coldProfile));Set(modelWorld,"surfaceProfile",coldProfile);
        Set(modelWorld,"cameraY",6740.0);Call(modelWorld,"RequestPlan");
        Check(modelWorld.PendingTiles>0&&modelWorld.PendingTiles<=64,
            "actual explorer plan accepts remote48000m conservative bounds plus maximum snow depth");
        var spike=new WorldExplorer { TileQuads=32,TerrainMaterial=new Material(null),EnableSurfaceSnow=false };
        Set(spike,"field",new SpikeField());
        object fine=Call(spike,"BuildSurface",new LodTile(new TileKey(2,0,0),128,0,64,1000,6200));
        object coarse=Call(spike,"BuildSurface",new LodTile(new TileKey(0,0,1),0,0,128,1000,6200));
        var fineMesh=(Mesh)Get(fine,"Mesh");var coarseMesh=(Mesh)Get(coarse,"Mesh");
        double fineEdge=fineMesh.Vertices[33].y,coarseEdge=coarseMesh.Vertices[32].y;
        double bottom=double.MaxValue;foreach(var vertex in fineMesh.Vertices)if(vertex.x==0&&vertex.z==2)bottom=Math.Min(bottom,vertex.y);
        Check(fineEdge==5200&&coarseEdge==0&&bottom<=coarseEdge,"bounds-aware fine/coarse skirts cover the6200m narrow shared-edge peak missed by coarse vertices");
        Check(bottom>-10000,"local neighbor bounds avoid unnecessary global-depth skirts for ordinary bounded fields");
        var map=new HeightAtlas(2,2,-900000,-450000,1800000,900000,new float[4],new byte[4],new bool[4],"whole-map");
        WorldExplorer.ValidateLiveAtlas(map,true,1);Check(true,"native centered whole-map dimensions remain eligible as a live rough source");
        bool blocked=false;try{WorldExplorer.ValidateLiveAtlas(map,false,1);}catch(InvalidOperationException){blocked=true;}Check(blocked,"live mode rejects client-side map scaling that cannot transform service coordinates");
        blocked=false;try{WorldExplorer.ValidateLiveAtlas(map,true,2);}catch(InvalidOperationException){blocked=true;}Check(blocked,"live mode rejects independent vertical rescaling of only its rough fallback");
        var crop=new HeightAtlas(2,2,33600,18240,960,960,new float[4],new byte[4],new bool[4],"{\"provider\":\"InfiniteDiffusion\"}");
        blocked=false;try{WorldExplorer.ValidateLiveAtlas(crop,true,1);}catch(InvalidOperationException){blocked=true;}Check(blocked,"recentered neural study crop cannot masquerade as a geographically aligned live fallback");
        Set(explorer,"view",new Camera());Set(explorer,"cameraX",1e9+.125);Set(explorer,"cameraY",6500.25);Set(explorer,"cameraZ",-1e9+.375);
        Call(explorer,"Rebase",true);var camera=(Camera)Get(explorer,"view");
        Check(Math.Abs(camera.transform.position.x-.125)<1e-6&&Math.Abs(camera.transform.position.y-100.25)<1e-6&&Math.Abs(camera.transform.position.z-.375)<1e-6,"origin rebasing preserves submeter camera coordinates at large global distance");
        field=new PlaneField();Set(explorer,"field",field);Set(explorer,"cameraX",0.0);Set(explorer,"cameraY",6740.0);Set(explorer,"cameraZ",0.0);
        Call(explorer,"RequestPlan");int queued=explorer.PendingTiles;
        Check(queued>0&&queued<=64,"initial terrain request obeys the explicit tile cap");
        Call(explorer,"Update");Check(explorer.ResidentTiles<=2&&explorer.PendingTiles>=queued-2,"one frame builds only the configured number of tile meshes");
        for(int i=0;i<200&&explorer.PendingTiles>0;i++)Call(explorer,"Update");
        Check(explorer.PendingTiles==0&&explorer.ResidentTiles<=64,"complete terrain plan drains without exceeding residency");
        var active=(HashSet<TileKey>)Get(explorer,"active");
        Check(active.Count==explorer.ResidentTiles,"complete plan becomes visible only after all requested tiles exist");
        Call(explorer,"ClearTerrain");Check(explorer.ResidentTiles==0&&explorer.PendingTiles==0,"source rebuild releases cache and cancels queued work");
        var patch = new VoxelWorld(16,16,16,.1f,714103);
        Set(explorer,"patch",patch);Set(explorer,"patchX",0.0);Set(explorer,"patchY",6499.2);Set(explorer,"patchZ",0.0);Set(explorer,"inspectionField",field);
        Set(explorer,"buildingInspection",true);
        var build=(IEnumerator)Call(explorer,"BuildInspection",16,16);
        Check(build.MoveNext()&&patch.Chunks.Count==1&&patch.Chunks[new Int3(0,0,0)].NonAirCount<=128*16,"inspection generation yields after at most128 bounded-height columns");
        Check(build.MoveNext()&&!build.MoveNext()&&!(bool)Get(explorer,"buildingInspection"),"inspection materialization completes and unlocks editing");
        int before=patch.Chunks[new Int3(0,0,0)].NonAirCount;
        Set(explorer,"cameraX",.8);Set(explorer,"cameraY",6502.0);Set(explorer,"cameraZ",.8);
        camera.transform.rotation=Quaternion.Euler(90,0,0);
        Call(explorer,"DigPatch");
        Check(patch.Chunks[new Int3(0,0,0)].NonAirCount<before&&patch.Get(8,0,8)==MaterialId.Stone,"grid targeting deforms actual10cm matter at6.5km altitude while preserving protected floor");
        Call(explorer,"OnDestroy");
        var covered=new WorldExplorer{EnableSurfaceSnow=false,GroundCoverDensity=1};
        var coverField=new WarmForestField();var coverWorld=new VoxelWorld(128,64,128,.1f,714103);
        Set(covered,"patch",coverWorld);Set(covered,"patchX",0.0);Set(covered,"patchY",0.0);Set(covered,"patchZ",0.0);
        Set(covered,"inspectionField",coverField);Set(covered,"inspectionSampler",new SurfaceMaterialSampler(coverField,new SurfaceProfile(enableSnow:false,forcedBedrockMaterial:MaterialId.Granite)));
        Set(covered,"buildingInspection",true);int coverYields=0;
        var coverBuild=(IEnumerator)Call(covered,"BuildInspection",128,64);while(coverBuild.MoveNext())coverYields++;
        Check(coverYields==129&&!(bool)Get(covered,"buildingInspection"),"full inspection yields128 terrain batches and a separate clutter phase before editing");
        GroundCoverBuildResult coverResult=covered.LastGroundCover;
        Check(coverResult!=null&&coverResult.Plants>10&&coverResult.WrittenCells>100&&coverResult.WrittenCells<=60000&&coverResult.GeneratorCandidates<=1000000,
            "actual WorldExplorer build materializes habitat-selected plants under aggregate cell/candidate caps");
        int softMatter=0;foreach(var pair in coverWorld.Chunks)foreach(MaterialId id in pair.Value.Cells)if(id==MaterialId.Leaves||id==MaterialId.Fungus||id==MaterialId.Petal)softMatter++;
        Check(softMatter>100&&coverWorld.Get(0,0,0)==MaterialId.Stone,"clutter is real targetable soft matter and does not replace the protected floor");
        Call(covered,"CloseInspection");Check(covered.LastGroundCover==null,"inspection exit clears clutter state with temporary matter");
        ExternalChecks.Run(Check);
        if(args.Length>0)RemoteChecks.Run(args[0],Check);
        if(args.Length>1&&!string.IsNullOrEmpty(args[1]))RemoteChecks.Live(args[1],Check);
        if(args.Length>3)
        {
            var baked=new WorldExplorer { gameObject=new GameObject("Baked climate probe"),BakedWorldAtlas=new TextAsset{bytes=File.ReadAllBytes(args[2])},
                BakedClimateAtlas=new TextAsset{bytes=File.ReadAllBytes(args[3])},MaximumTiles=64,ViewDistanceKm=4 };
            baked.Regenerate();
            var decorated=Get(baked,"field") as SurfaceWorldField;
            Check(decorated!=null&&decorated.BaseField is ClimateAtlasField,"actual baked sample binds its checked physical climate archive before material generation");
            SurfaceColumn column=decorated.Sampler.EvaluateColumn(0,0);
            Check(column.ClimateFromField&&column.GroundHeightMeters>5000&&column.GroundHeightMeters<6000,"actual5km learned terrain supplies model climate to surface materials instead of the biome fallback");
            var climateField=(IClimateWorldField)decorated.BaseField;
            Check(climateField.TrySampleClimate(0,0,out SurfaceClimate climate)&&climate.SeasonalityC>10.7&&climate.SeasonalityC<10.9,"actual Ceteviles archive preserves10.7C seasonality rather than raw1070centi-C");
            Call(baked,"OnDestroy");
            Console.WriteLine("BAKED_CLIMATE_PROBE {\"ground_height_m\":"+column.GroundHeightMeters.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+
                ",\"seasonality_c\":"+climate.SeasonalityC.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+",\"source_biome\":"+column.SourceBiome+",\"climate_from_field\":true}");
        }
        Console.WriteLine("All "+checks+" WorldExplorer checks passed with Unity API substitutes; native import/rendering remains unverified.");
    }
}
