using System;
using System.IO;
using System.Linq;
using AshenVoxel.WorldScale;
#if UNITY_EDITOR
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
#endif

namespace AshenVoxel.Exploration.Editor
{
    public static class BakedOverviewChecks
    {
        const string Id="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        static void Require(bool valid,string message){if(!valid)throw new InvalidDataException(message);}
        static HeightAtlas Atlas(int n,double x,double z,double extent,float height)
        {var h=Enumerable.Repeat(height,n*n).ToArray();return new HeightAtlas(n,n,x,z,extent,extent,h,Enumerable.Repeat((byte)6,h.Length).ToArray(),Enumerable.Repeat(height>=0,h.Length).ToArray(),"{\"world_id\":\""+Id+"\"}");}
        static ClimateAtlas Climate(HeightAtlas h)
        {int n=h.Columns*h.Rows;var v=new float[n*5];for(int i=0;i<n;i++){v[i]=20;v[n+i]=4;v[2*n+i]=1000;v[3*n+i]=30;v[4*n+i]=-.006f;}return new ClimateAtlas(h.Columns,h.Rows,h.OriginX,h.OriginZ,h.ExtentX,h.ExtentZ,ClimateArchiveIO.HeightHash(h),h.SourceIdentity,v);}
        static ExplorationAtlasField Basis(float height,bool fine=true)
        {var macro=Atlas(17,-25600,-25600,51200,height);var detail=fine?Atlas(9,10500,100,40,150):null;return new ExplorationAtlasField(macro,Climate(macro),detail,detail==null?null:Climate(detail),10,51200,51200,1,null,51200,51200);}
        static BakedFarTerrain Cache(ExplorationAtlasField basis,int cols,int rows,double x,double z,int[] h,byte[] coverage)
        {using var stream=new MemoryStream();BakedFarTerrainIO.Write(stream,cols,rows,x,z,40,basis.SourceIdentity,new byte[32],h,coverage);stream.Position=0;return BakedFarTerrainIO.Read(stream);}
        static void Contains(IWorldFieldBounds field,double x0,double z0,double x1,double z1,int samples=9)
        {
            var b=field.QueryBounds(x0,z0,x1,z1);
            for(int z=0;z<samples;z++)for(int x=0;x<samples;x++)
            {double h=field.Sample(x0+(x1-x0)*x/(samples-1),z0+(z1-z0)*z/(samples-1)).HeightMeters;Require(h>=b.Minimum&&h<=b.Maximum,"Query bounds omitted a sampled height");}
        }
        public static int RunSynthetic()
        {
            int checks=0;void Check(bool value,string text){Require(value,text);checks++;}
            void Reject(Action action,string text){bool rejected=false;try{action();}catch(ArgumentException){rejected=true;}Check(rejected,text);}
            var basis=Basis(100);var live=new ExplorationRefinementField(basis,Id);
            var far=Cache(basis,129,65,9920,-320,Enumerable.Repeat(400,129*65).ToArray(),Enumerable.Repeat((byte)255,129*65).ToArray());
            var field=new BakedOverviewField(basis,live,far);var snapshot=field.CaptureSnapshot();string oldIdentity=snapshot.SourceIdentity;
            Check(field.Sample(12000,1000).HeightMeters==200&&field.ResolutionMetersAt(12000,1000)==40,"Far core height and source spacing");
            Check(field.Sample(10520,120).HeightMeters==basis.Sample(10520,120).HeightMeters,"Imported fine interior remains exact");
            Check(field.TrySampleClimate(12000,1000,out var c)&&Math.Abs(c.TemperatureC-19.4)<1e-6&&c.PrecipitationMm==1000,"One lapse adjustment preserves other climate channels");
            Check(field.Sample(12000,1000).SourceBiome==6,"Biome identity preserved");
            Check(field.Sample(20000,1000).HeightMeters==100,"Outside far coverage uses base");
            var patch=ExplorationRefinementPatch.CopySnapshot(basis,"test-live",Id,new ExplorationRefinementBounds(11000,1000,11040,1040),40,32,5);
            var observer=new WorldPoint(0,200,0);Check(live.TryPublish(patch,observer,out _),"Publish live fixture");
            Check(field.Sample(11020,1020).HeightMeters==200,"Coordinator requires explicit snapshot refresh");
            field.RefreshBaseSnapshot();Check(field.Sample(11020,1020).HeightMeters==100&&field.ResolutionMetersAt(11020,1020)==5,"Live fine core overrides baked far");
            Check(snapshot.Sample(11020,1020).HeightMeters==200&&snapshot.SourceIdentity==oldIdentity&&field.SourceIdentity!=oldIdentity,"Old snapshot retains values and identity");
            var active=field.CaptureSnapshot();Check(live.TryRetire("test-live",observer,out _),"Retire live fixture");field.RefreshBaseSnapshot();
            Check(field.Sample(11020,1020).HeightMeters==200&&active.Sample(11020,1020).HeightMeters==100,"Retirement preserves retained snapshots");
            Check(field.SourceIdentity==oldIdentity,"Identical content restores deterministic identity");
            Reject(()=>new BakedOverviewField(Basis(101),live,far),"Mismatched far atlas rejected");
            Reject(()=>new BakedOverviewField(basis,new ExplorationRefinementField(Basis(101),Id),far),"Mismatched live base rejected independently of snapshot identity");
            var h=new int[130*130];var cov=new byte[h.Length];
            h[64+64*130]=2000;cov[64+64*130]=255;h[128+128*130]=-4000;cov[128+128*130]=255;
            var sparse=Cache(basis,130,130,-2600,-2600,h,cov);var edges=new BakedOverviewField(basis,new ExplorationRefinementField(basis,Id),sparse);
            Check(sparse.Sample(-60,-60,out double weight)==1000&&weight==.25,"Sparse interpolation retains covered height");
            Check(edges.Sample(-60,-60).HeightMeters==325,"Sparse weight blends with base once");
            Check(!edges.Sample(2500,2500).IsLand,"Negative sparse height can cross shore");
            Contains(edges,-80.001,-80.001,-39.999,-39.999);checks++;
            Contains(edges,2479.999,2479.999,2560.001,2560.001);checks++;
            Contains(edges,-2700,-2700,-2600,-2600);checks++;
            Contains(edges,-1e100,-1e100,1e100,1e100,3);checks++;
            Check(!sparse.TryHeightBounds(1e100,1e100,1e101,1e101,out _),"Huge outside query stays bounded");
            Check(sparse.TryHeightBounds(-80,-80,-40,-40,out var bound)&&bound.Maximum>=1000.25,"Neighbor block includes spike and quantization margin");
            Check(!sparse.TryHeightBounds(-2600,2521,-2500,2550,out _),"Uncovered block contributes no fabricated zero height");
            Reject(()=>edges.QueryBounds(1,0,0,1),"Inverted query rejected");Reject(()=>edges.QueryBounds(double.NaN,0,0,1),"Nonfinite query rejected");
            Check(far.BoundsBytes==16&&field.ResidentPayloadBytes==far.PayloadBytes+far.BoundsBytes,"Extrema ownership is included without duplicating far arrays");
            return checks;
        }
#if UNITY_EDITOR
        [Serializable] sealed class Report
        {public string status="FAIL",utc=DateTime.UtcNow.ToString("o"),unity=Application.unityVersion,error,identity;public int synthetic_checks,samples;public double p99_error,max_error;public long resident_bytes;public string scope="Native Edit-mode field comparison and snapshot/bounds fixtures; no renderer or Play acceptance.";public List<string> source_sha256=new List<string>(),patch_sha256=new List<string>();}
        static string Hash(string path){using var file=File.OpenRead(path);using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(file)).Replace("-","").ToLowerInvariant();}
        [MenuItem("Ashen Voxel/Terrain/Check baked overview field")]
        public static void RunMenu()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Baked overview checks require Edit mode");
            string root=Path.GetFullPath(Path.Combine(Application.dataPath,"..")),folder=Path.Combine(root,"Logs/BakedOverview",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));Directory.CreateDirectory(folder);var report=new Report();
            try
            {
                report.synthetic_checks=RunSynthetic();
                var settings=AssetDatabase.LoadAssetAtPath<ExplorationSettings>(ExplorationProjectSetup.SettingsPath);
                Require(settings!=null&&settings.UseImportedDimensions&&settings.VerticalScale==1,"Imported atlas settings required");
                var assets=new[]{settings.MacroWorld,settings.MacroClimate,settings.FineWorld,settings.FineClimate,settings.SourceBiomes};
                Require(assets.All(a=>a!=null),"Source assets missing");
                var basis=FarTerrainBake.ReadBasis(assets.Select(a=>a.bytes).ToArray(),settings.FineBlendMeters);var live=new ExplorationRefinementField(basis,basis.DetailWorldId);
                string farPath=Path.Combine(root,"Assets/AshenVoxel/Exploration/Samples/ExplorationFar40.bytes");BakedFarTerrain far;using(var file=File.OpenRead(farPath))far=BakedFarTerrainIO.Read(file);
                var candidate=new BakedOverviewField(basis,live,far);var legacy=new ExplorationOverviewField(basis,live,"0647786260a982ef6793a9e77fac850f0d9e23b8ae8ec3319fe6978b6538c84a");
                var patches=new List<FarTerrainBake.Patch>();var keys=new HashSet<(long,long)>();
                foreach(string path in Directory.GetFiles(Path.Combine(root,".terrain-cache/regional-overview-cache"),"*.avov").OrderBy(p=>p,StringComparer.Ordinal))
                {
                    var p=FarTerrainBake.ReadPatch(path);if(p==null||p.Spacing!=40||Math.Abs(p.X)>=57||Math.Abs(p.Z)>=28||!keys.Add((p.X,p.Z)))continue;
                    var source=new ExplorationOverviewPatch(p.X,p.Z,p.Spacing,p.Heights,legacy.NativeModelWorldId);
                    Require(legacy.TryPublish(source,new WorldPoint(1e7,0,1e7),out _,out _),"Publish comparison patch");patches.Add(p);report.patch_sha256.Add(Path.GetFileName(path)+" "+p.FileHash);if(patches.Count==4)break;
                }
                Require(patches.Count==4,"Four interior real patches required");var random=new System.Random(783442);var errors=new double[10000];
                for(int i=0;i<errors.Length;i++)
                {
                    var p=patches[i%patches.Count];double x=p.X*2560+320+random.NextDouble()*1920,z=p.Z*2560+320+random.NextDouble()*1920;
                    errors[i]=Math.Abs(legacy.Sample(x,z).HeightMeters-candidate.Sample(x,z).HeightMeters);Contains(candidate,x-.1,z-.1,x+.1,z+.1,3);
                }
                Array.Sort(errors);report.samples=errors.Length;report.p99_error=errors[9899];report.max_error=errors[9999];report.identity=candidate.SourceIdentity;report.resident_bytes=candidate.ResidentPayloadBytes;
                Require(report.p99_error<=1,"Native 10000-point field comparison p99 exceeds 1 m");
                foreach(string path in assets.Select(a=>AssetDatabase.GetAssetPath(a)).Concat(new[]{"Assets/AshenVoxel/WorldScaleData/BakedFarTerrain.cs","Assets/AshenVoxel/WorldScaleData/BakedOverviewField.cs","Assets/AshenVoxel/WorldScaleData/ExplorationRefinementField.cs","Assets/AshenVoxel/Exploration/Editor/BakedOverviewChecks.cs","Assets/AshenVoxel/Exploration/Editor/FarTerrainBake.cs","Assets/AshenVoxel/Exploration/Samples/ExplorationFar40.bytes"}))report.source_sha256.Add(path+" "+Hash(Path.Combine(root,path)));
                report.status="PASS";
            }
            catch(Exception error){report.error=error.ToString();}
            File.WriteAllText(Path.Combine(folder,"report.json"),JsonUtility.ToJson(report,true));
            if(report.status=="PASS")Debug.Log("BAKED_OVERVIEW_PASS p99="+report.p99_error+" m; "+folder);else Debug.LogError("BAKED_OVERVIEW_FAIL "+report.error+"; "+folder);
        }
#endif
    }
}
