#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using CompressionLevel=System.IO.Compression.CompressionLevel;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AshenVoxel.WorldScale;
using UnityEditor;
using UnityEngine;

namespace AshenVoxel.Exploration.Editor
{
    public static class FarTerrainBake
    {
        const string Native="0647786260a982ef6793a9e77fac850f0d9e23b8ae8ec3319fe6978b6538c84a";
        const string Output="Assets/AshenVoxel/Exploration/Samples/ExplorationFar40.bytes";
        const int Columns=7489,Rows=3745;const double OriginX=-149760,OriginZ=-74880,Spacing=40;
        static readonly Encoding Utf8=new UTF8Encoding(false,true);
        [Serializable] sealed class Window{public int row0,col0,rows,cols,spacing_m;}
        [Serializable] sealed class Meta
        {public string schema,native_model_world_id,query_kind,height_sha256,height_units,source_id;public Window window;public int row0,col0,rows,cols,spacing_m;public double origin_x_m,origin_z_m;}
        internal sealed class Patch
        {
            public long X,Z;public int Spacing,Columns;public float[] Heights;public string FileHash,HeightHash;
            public double Height(double x,double z)
            {
                double px=Math.Max(0,Math.Min(Columns-1,(x-(X*2560-320))/Spacing)),pz=Math.Max(0,Math.Min(Columns-1,(z-(Z*2560-320))/Spacing));
                int ix=Math.Min(Columns-2,(int)Math.Floor(px)),iz=Math.Min(Columns-2,(int)Math.Floor(pz));double fx=px-ix,fz=pz-iz;int a=ix+iz*Columns;
                return (Heights[a]*(1-fx)+Heights[a+1]*fx)*(1-fz)+(Heights[a+Columns]*(1-fx)+Heights[a+Columns+1]*fx)*fz;
            }
        }
        [Serializable] sealed class Report
        {
            public string status="RUNNING",utc=DateTime.UtcNow.ToString("o"),unity,identity,base_identity,source_fingerprint,cache_manifest,error;
            public string scope="Native Edit-mode cache bake/readback and bounded format/sparse sampling checks; no runtime renderer adoption or Play acceptance.";
            public int files,rejected,filtered,cells,spacing40,spacing80,spacing160,land_samples,format_checks;
            public long selected_height_bytes,payload_bytes,estimated_peak_array_bytes,output_bytes;
            public double minimum_height=double.MaxValue,maximum_height=double.MinValue,max_grid_error,max_land_error,seconds;
            public List<string> input_sha256=new List<string>(),code_sha256=new List<string>(),reject_reasons=new List<string>();
        }
        static Task<Report> running;static int completedRows;static string stage,root,reportFolder;static bool baking;
        static byte[] Hash(byte[] bytes){using(var sha=SHA256.Create())return sha.ComputeHash(bytes);}
        static string Hex(byte[] bytes)=>BitConverter.ToString(bytes).Replace("-","").ToLowerInvariant();
        static bool IsHash(string s)=>s!=null&&s.Length==64&&s.All(c=>c>='0'&&c<='9'||c>='a'&&c<='f');
        static void Require(bool value,string message){if(!value)throw new InvalidDataException(message);}
        internal static Patch ReadPatch(string path)
        {
            long length=new FileInfo(path).Length;Require(length>=16&&length<=256*1024,"AVOV file size");byte[] raw=File.ReadAllBytes(path);
            Require(raw.Length==length&&Encoding.ASCII.GetString(raw,0,8)=="AVOV0001"&&AtlasIO.Crc32(raw,raw.Length-4)==BitConverter.ToUInt32(raw,raw.Length-4),"AVOV magic/checksum");
            uint jsonLength=BitConverter.ToUInt32(raw,8);Require(jsonLength>0&&jsonLength<=65536&&jsonLength<=raw.Length-16,"AVOV JSON length");
            var m=JsonUtility.FromJson<Meta>(Utf8.GetString(raw,12,(int)jsonLength));Require(m!=null&&m.window!=null,"AVOV metadata");
            if(m.native_model_world_id!=Native||m.query_kind!="pinned-coarse-base-model")return null;
            Window w=m.window;int s=w.spacing_m;
            Require(m.schema=="ashen-regional-learned-overview-v1"&&m.height_units=="meters"&&IsHash(m.height_sha256)&&IsHash(m.source_id),"AVOV source metadata");
            Require((s==40||s==80||s==160)&&w.rows==w.cols&&w.cols==3200/s+1,"AVOV canonical grid");
            Require(m.row0==w.row0&&m.col0==w.col0&&m.rows==w.rows&&m.cols==w.cols&&m.spacing_m==s,"AVOV duplicate grid metadata");
            long ax=(long)w.col0*s+320,az=(long)w.row0*s+320;
            Require(ax%2560==0&&az%2560==0&&Math.Abs(ax/2560)<=1000000&&Math.Abs(az/2560)<=1000000,"AVOV alignment/bounds");
            Require(m.origin_x_m==(long)w.col0*s&&m.origin_z_m==(long)w.row0*s,"AVOV origin");
            int count=w.rows*w.cols,offset=12+(int)jsonLength;Require(raw.Length==offset+count*4+4,"AVOV payload length");
            using(var sha=SHA256.Create())Require(Hex(sha.ComputeHash(raw,offset,count*4))==m.height_sha256,"AVOV height SHA256");
            var h=new float[count];Buffer.BlockCopy(raw,offset,h,0,count*4);
            foreach(float value in h)Require(!float.IsNaN(value)&&!float.IsInfinity(value)&&value>=-12000&&value<=48000,"AVOV finite supported heights");
            return new Patch{X=ax/2560,Z=az/2560,Spacing=s,Columns=w.cols,Heights=h,FileHash=Hex(Hash(raw)),HeightHash=m.height_sha256};
        }
        internal static Dictionary<(long,long),Patch> ReadPatches(string folder,Action<int> progress=null)
        {return ReadPatches(folder,new Report(),progress);}
        static Dictionary<(long,long),Patch> ReadPatches(string folder,Report report,Action<int> progress)
        {
            var selected=new Dictionary<(long,long),Patch>();string[] files=Directory.GetFiles(folder,"*.avov");Require(files.Length<=32768,"AVOV file-count limit");Array.Sort(files,StringComparer.Ordinal);
            foreach(string file in files)
            {
                report.files++;Patch patch;
                try{patch=ReadPatch(file);}catch(Exception e)when(e is InvalidDataException||e is IOException||e is ArgumentException||e is OverflowException)
                {report.rejected++;if(report.reject_reasons.Count<16)report.reject_reasons.Add(Path.GetFileName(file)+": "+e.Message);continue;}
                if(patch==null){report.filtered++;continue;}
                var key=(patch.X,patch.Z);
                if(selected.TryGetValue(key,out var old))
                {
                    Require(old.Spacing!=patch.Spacing||old.HeightHash==patch.HeightHash,"Conflicting equal-spacing AVOV content");
                    if(patch.Spacing>old.Spacing||patch.Spacing==old.Spacing&&StringComparer.Ordinal.Compare(patch.FileHash,old.FileHash)>=0)continue;
                }
                selected[key]=patch;Require(selected.Count<=8192,"AVOV selected-cell limit");progress?.Invoke(report.files);
            }
            Require(selected.Count>0,"No accepted AVOV terrain");report.cells=selected.Count;
            foreach(var p in selected.Values)
            {
                if(p.Spacing==40)report.spacing40++;else if(p.Spacing==80)report.spacing80++;else report.spacing160++;
                report.selected_height_bytes+=p.Heights.LongLength*4;
                foreach(float h in p.Heights){report.minimum_height=Math.Min(report.minimum_height,h);report.maximum_height=Math.Max(report.maximum_height,h);}
            }
            return selected;
        }
        internal static double Overlay(Dictionary<(long,long),Patch> patches,double x,double z,out double coverage)
        {
            long cx=(long)Math.Floor(x/2560),cz=(long)Math.Floor(z/2560);double sum=0,total=0;
            for(int dz=-1;dz<=1;dz++)for(int dx=-1;dx<=1;dx++)if(patches.TryGetValue((cx+dx,cz+dz),out var p))
            {
                double outsideX=Math.Max(p.X*2560-x,Math.Max(0,x-(p.X+1)*2560)),outsideZ=Math.Max(p.Z*2560-z,Math.Max(0,z-(p.Z+1)*2560));
                double t=Math.Max(0,Math.Min(1,1-Math.Max(outsideX,outsideZ)/320)),w=t*t*(3-2*t);sum+=p.Height(x,z)*w;total+=w;
            }
            coverage=Math.Min(1,total);return total>0?sum/total:0;
        }
        static byte[] Fingerprint(string basis,string[] inputHashes,Dictionary<(long,long),Patch> patches)
        {
            using var ms=new MemoryStream();using(var w=new BinaryWriter(ms,Utf8,true))
            {
                w.Write("far-bake-source-v2");w.Write(basis);foreach(string hash in inputHashes)w.Write(hash);
                foreach(var p in patches.Values.OrderBy(p=>p.X).ThenBy(p=>p.Z)){w.Write(p.X);w.Write(p.Z);w.Write(p.Spacing);w.Write(p.FileHash);}
            }
            return Hash(ms.ToArray());
        }
        static string CacheManifest()
        {
            string[] files=Directory.GetFiles(Path.Combine(root,".terrain-cache/regional-overview-cache"),"*.avov");
            Require(files.Length<=32768,"AVOV manifest file-count limit");Array.Sort(files,StringComparer.Ordinal);
            using var manifest=new MemoryStream();using(var writer=new BinaryWriter(manifest,Utf8,true))
            foreach(string file in files)
            {
                Require(new FileInfo(file).Length<=256*1024,"AVOV manifest file-size limit");
                writer.Write(Path.GetFileName(file));using(var stream=File.OpenRead(file))using(var sha=SHA256.Create())writer.Write(sha.ComputeHash(stream));
            }
            return Hex(Hash(manifest.ToArray()));
        }
        internal static ExplorationAtlasField ReadBasis(byte[][] bytes,double blend)
        {
            HeightAtlas Height(int i){using(var ms=new MemoryStream(bytes[i],false))return AtlasIO.Read(ms);}
            ClimateAtlas Climate(int i){using(var ms=new MemoryStream(bytes[i],false))return ClimateArchiveIO.Read(ms);}
            var macro=Height(0);ExplorationBiomeAtlas biomes;using(var ms=new MemoryStream(bytes[4],false))biomes=ExplorationBiomeAtlasIO.Read(ms);
            Require(macro.ExtentX==299520&&macro.ExtentZ==149760&&macro.OriginX==OriginX&&macro.OriginZ==OriginZ,"Unsupported imported far-bake domain");
            return new ExplorationAtlasField(macro,Climate(1),Height(2),Climate(3),blend,macro.ExtentX,macro.ExtentZ,1,biomes,macro.ExtentX,macro.ExtentZ);
        }
        static void Verify(BakedFarTerrain far,Dictionary<(long,long),Patch> patches,ExplorationAtlasField basis,Report report)
        {
            var random=new System.Random(714103);
            for(int i=0;i<1000;i++)
            {
                int ix=random.Next(Columns),iz=random.Next(Rows);double x=OriginX+ix*Spacing,z=OriginZ+iz*Spacing;
                double expected=Overlay(patches,x,z,out double coverage),actual=far.Sample(x,z,out double actualCoverage);
                Require(Math.Abs(coverage-actualCoverage)<=1.0/255+.000001,"Far grid coverage mismatch");
                if(coverage>0)report.max_grid_error=Math.Max(report.max_grid_error,Math.Abs(actual-expected));
            }
            for(int attempt=0;attempt<100000&&report.land_samples<1000;attempt++)
            {
                double x=OriginX+random.NextDouble()*(Columns-1)*Spacing,z=OriginZ+random.NextDouble()*(Rows-1)*Spacing;
                double expected=Overlay(patches,x,z,out double coverage);if(coverage<=0||!basis.Sample(x,z).IsLand)continue;
                double actual=far.Sample(x,z,out _);report.max_land_error=Math.Max(report.max_land_error,Math.Abs(actual-expected));report.land_samples++;
            }
            Require(report.land_samples==1000&&report.max_grid_error<=.250001&&report.max_land_error<=.6,"Far terrain quantization/resampling gate");
        }
        static Report Work(byte[][] bytes,string[] inputs,double blend,string unity,bool bake)
        {
            var watch=System.Diagnostics.Stopwatch.StartNew();var report=new Report{unity=unity};report.input_sha256.AddRange(inputs);
            try
            {
                foreach(string file in new[]{"Assets/AshenVoxel/WorldScaleData/BakedFarTerrain.cs","Assets/AshenVoxel/Exploration/Editor/FarTerrainBake.cs"})report.code_sha256.Add(file+" "+Hex(Hash(File.ReadAllBytes(Path.Combine(root,file)))));
                stage="Checking sparse coverage and corrupt cache rejection";CheckFormat(report);
                stage="Reading imported source atlases";var basis=ReadBasis(bytes,blend);report.base_identity=basis.SourceIdentity;
                stage="Hashing overview input snapshot";report.cache_manifest=CacheManifest();
                stage="Validating cached overview patches";var patches=ReadPatches(Path.Combine(root,".terrain-cache/regional-overview-cache"),report,n=>stage="Validated "+n+" overview files");
                byte[] fingerprint=Fingerprint(basis.SourceIdentity,inputs,patches);report.source_fingerprint=Hex(fingerprint);
                report.payload_bytes=(long)Columns*Rows*5;
                // Two far payloads coexist during readback; budget source arrays and
                // imported asset byte snapshots conservatively at 4x input bytes.
                report.estimated_peak_array_bytes=report.selected_height_bytes+2*report.payload_bytes+4*bytes.Sum(b=>(long)b.Length)+2*1024*1024;
                Require(report.estimated_peak_array_bytes<700L*1024*1024,"Far bake temporary array budget");
                string path=bake?Path.Combine(reportFolder,"ExplorationFar40.candidate.bytes"):Path.Combine(root,Output);
                if(bake)
                {
                    stage="Baking fixed 40 metre grid";var heights=new int[Columns*Rows];var coverage=new byte[heights.Length];
                    Parallel.For(0,Rows,new ParallelOptions{MaxDegreeOfParallelism=Math.Min(4,Environment.ProcessorCount)},z=>
                    {
                        for(int x=0;x<Columns;x++)
                        {
                            double h=Overlay(patches,OriginX+x*Spacing,OriginZ+z*Spacing,out double c);int i=x+z*Columns;
                            coverage[i]=(byte)Math.Round(c*255);heights[i]=coverage[i]==0?0:checked((int)Math.Round(h/BakedFarTerrain.HeightUnitMeters));
                        }
                        Interlocked.Increment(ref completedRows);
                    });
                    stage="Writing AVFAR002 with integrity metadata";Directory.CreateDirectory(Path.GetDirectoryName(path));
                    using(var stream=File.Create(path))BakedFarTerrainIO.Write(stream,Columns,Rows,OriginX,OriginZ,Spacing,basis.SourceIdentity,fingerprint,heights,coverage);
                }
                stage="Reading back and comparing with source patches";BakedFarTerrain far;using(var stream=File.OpenRead(path))far=BakedFarTerrainIO.Read(stream);
                Require(far.BaseIdentity==basis.SourceIdentity&&far.SourceFingerprint==report.source_fingerprint,"Far terrain input identity mismatch");
                Verify(far,patches,basis,report);ValidateSources(report);report.identity=far.Identity;report.output_bytes=new FileInfo(path).Length;report.status="PASS";
            }
            catch(Exception e){report.error=e.ToString();report.status="FAIL";}
            report.seconds=watch.Elapsed.TotalSeconds;return report;
        }
        static void ValidateSources(Report report)
        {
            Require(CacheManifest()==report.cache_manifest,"Overview cache changed during bake/verification");
            foreach(string entry in report.input_sha256.Concat(report.code_sha256))
            {
                int split=entry.LastIndexOf(' ');using(var file=File.OpenRead(Path.Combine(root,entry.Substring(0,split))))using(var sha=SHA256.Create())
                    Require(Hex(sha.ComputeHash(file))==entry.Substring(split+1),"Bake input/source changed during verification");
            }
        }
        static byte[] Zip(byte[] raw){using var ms=new MemoryStream();using(var gz=new GZipStream(ms,CompressionLevel.Optimal,true))gz.Write(raw,0,raw.Length);return ms.ToArray();}
        static byte[] Unzip(byte[] zipped){using var source=new MemoryStream(zipped);using var gz=new GZipStream(source,CompressionMode.Decompress);using var output=new MemoryStream();gz.CopyTo(output);return output.ToArray();}
        static BakedFarTerrain Load(byte[] data){using var ms=new MemoryStream(data);return BakedFarTerrainIO.Read(ms);}
        static void CheckFormat(Report report)
        {
            using var stream=new MemoryStream();BakedFarTerrainIO.Write(stream,2,2,0,0,40,"synthetic-source",new byte[32],new[]{200,0,0,0},new byte[]{255,0,0,0});
            byte[] bytes=stream.ToArray(),raw=Unzip(bytes);var far=Load(bytes);
            Require(far.Sample(20,20,out double coverage)==100&&Math.Abs(coverage-.25)<1e-9,"Sparse height must weight covered corners");report.format_checks++;
            Require(far.Sample(40,40,out coverage)==0&&coverage==0&&far.Sample(-1,0,out coverage)==0&&coverage==0,"Uncovered and outside samples");report.format_checks++;
            void Reject(byte[] value,string label)
            {bool rejected=false;try{Load(value);}catch(Exception e)when(e is InvalidDataException||e is IOException||e is ArgumentException){rejected=true;}Require(rejected,label);report.format_checks++;}
            Reject(bytes.Take(bytes.Length/2).ToArray(),"Reject truncated gzip");
            Reject(bytes.Take(bytes.Length-1).ToArray(),"Reject truncated gzip footer");
            foreach(int offset in new[]{bytes.Length-8,bytes.Length-4}){var corrupt=(byte[])bytes.Clone();corrupt[offset]^=1;Reject(corrupt,"Reject gzip CRC/size corruption");}
            bool seekable=false;using(var packed=new MemoryStream(bytes))using(var forwardOnly=new GZipStream(packed,CompressionMode.Decompress))
            {try{BakedFarTerrainIO.Read(forwardOnly);}catch(ArgumentException){seekable=true;}}
            Require(seekable,"Require bounded seekable cache input");report.format_checks++;
            foreach(int offset in new[]{16,raw.Length-37,raw.Length-20,raw.Length-1}){var corrupt=(byte[])raw.Clone();corrupt[offset]^=1;Reject(Zip(corrupt),"Reject header/payload/SHA/CRC corruption");}
            var invalid=(byte[])raw.Clone();Buffer.BlockCopy(BitConverter.GetBytes(double.NaN),0,invalid,16,8);Reject(Zip(invalid),"Reject nonfinite grid");
            invalid=(byte[])raw.Clone();Buffer.BlockCopy(BitConverter.GetBytes(int.MaxValue),0,invalid,8,4);Reject(Zip(invalid),"Reject excessive allocation request");
            invalid=(byte[])raw.Clone();Buffer.BlockCopy(BitConverter.GetBytes(int.MaxValue),0,invalid,48,4);Reject(Zip(invalid),"Reject oversized identity");
            invalid=(byte[])raw.Clone();invalid[7]=(byte)'1';Reject(Zip(invalid),"Reject AVFAR001 and require rebake");
            Reject(Zip(raw.Concat(new byte[]{1}).ToArray()),"Reject extra decoded payload");
            bool domain=false;try{BakedFarTerrainIO.Write(Stream.Null,2,2,0,0,40,"synthetic",new byte[32],new[]{96001,0,0,0},new byte[]{255,0,0,0});}catch(InvalidDataException){domain=true;}
            Require(domain,"Never silently clamp unsupported elevations");report.format_checks++;
        }
        [MenuItem("Ashen Voxel/Terrain/Bake static far terrain (40 m)")]
        public static void Bake()=>Start(true);
        [MenuItem("Ashen Voxel/Terrain/Verify static far terrain (40 m)")]
        public static void Verify()=>Start(false);
        static void Start(bool bake)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||running!=null)throw new InvalidOperationException("Far bake requires idle Edit mode");
            var settings=AssetDatabase.LoadAssetAtPath<ExplorationSettings>(ExplorationProjectSetup.SettingsPath);
            Require(settings!=null&&settings.UseImportedDimensions&&settings.VerticalScale==1,"Far bake requires imported dimensions and vertical scale1");
            var assets=new[]{settings.MacroWorld,settings.MacroClimate,settings.FineWorld,settings.FineClimate,settings.SourceBiomes};Require(assets.All(a=>a!=null),"Missing far-bake source assets");
            var bytes=assets.Select(a=>a.bytes).ToArray();var inputs=assets.Select((a,i)=>AssetDatabase.GetAssetPath(a)+" "+Hex(Hash(bytes[i]))).ToArray();
            root=Path.GetFullPath(Path.Combine(Application.dataPath,".."));reportFolder=Path.Combine(root,"Logs/FarTerrainBake",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));Directory.CreateDirectory(reportFolder);
            completedRows=0;stage="Starting";baking=bake;double blend=settings.FineBlendMeters;string unity=Application.unityVersion;
            EditorApplication.LockReloadAssemblies();running=Task.Run(()=>Work(bytes,inputs,blend,unity,bake));EditorApplication.update+=Poll;
        }
        static void Poll()
        {
            if(running==null)return;
            if(!running.IsCompleted){EditorUtility.DisplayProgressBar("Static far terrain",stage,Volatile.Read(ref completedRows)/(float)Rows);return;}
            EditorApplication.update-=Poll;EditorUtility.ClearProgressBar();var completed=running;running=null;
            Report report=null;
            try
            {
                report=completed.GetAwaiter().GetResult();
                if(report.status=="PASS"&&baking)
                {
                    ValidateSources(report);
                    string target=Path.Combine(root,Output),pending=Path.Combine(reportFolder,"ExplorationFar40.candidate.bytes");
                    if(File.Exists(target))File.Replace(pending,target,null);else File.Move(pending,target);
                    AssetDatabase.ImportAsset(Output,ImportAssetOptions.ForceSynchronousImport);
                }
                File.WriteAllText(Path.Combine(reportFolder,"report.json"),JsonUtility.ToJson(report,true));
                if(report.status=="PASS")Debug.Log("STATIC_FAR_TERRAIN_PASS "+report.identity+"; max error "+report.max_land_error+" m; "+reportFolder);
                else Debug.LogError("STATIC_FAR_TERRAIN_FAIL "+report.error+"; "+reportFolder);
            }
            catch(Exception e)
            {
                if(report==null)report=new Report();report.status="FAIL";report.error=e.ToString();
                File.WriteAllText(Path.Combine(reportFolder,"report.json"),JsonUtility.ToJson(report,true));Debug.LogError("STATIC_FAR_TERRAIN_FAIL "+report.error);
            }
            finally{EditorApplication.UnlockReloadAssemblies();}
        }
    }
}
#endif
