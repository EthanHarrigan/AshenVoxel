using System;
using System.Collections;
using System.IO;
using System.Reflection;
using AshenVoxel.WorldScale;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;

namespace AshenVoxel.Exploration.Editor
{
    /// <summary>Capture the existing Play session without launching a second Editor.
    /// Optional local request contains only a filename label, never code/commands.</summary>
    [InitializeOnLoad]
    public static class QualityPatchReview
    {
        const string Request=".terrain-cache/quality-review.request";
        static double nextPoll;
        const string JobPath=".terrain-cache/quality-review-job.json";
        [Serializable] sealed class Job { public string action,label; }
        [Serializable] sealed class EditorStatus { public bool playing,changing,compiling,ready;public string utc;public PopulationDiagnostic population;public WorldLoadingSnapshot loading; }
        // Passive, bounded observations. Never Tick, replan, read GPU buffers or
        // change readiness here; absent private fields stay explicitly unknown.
        [Serializable] sealed class PopulationDiagnostic
        {
            public string utc,error,status,source,horizon_error;
            public bool present,boot_failed,ready,observer_valid,camera_valid,horizon_present,horizon_gpu,horizon_enabled;
            public int loaded_models,resident_cells,resident_instances,pending_cells,draw_calls,visible_instances,shadow_instances,budget_demotions,omitted_soft;
            public long visible_triangles;
            public double work_ms,x,y,z,origin_x,origin_y,origin_z;
            public float camera_forward_x,camera_forward_y,camera_forward_z,camera_far,tree_distance,plant_distance,rock_distance,tree_density,plant_density;
            public int triangle_budget,instance_limit;
            public int building_matrices=-1,finalizing=-1,matrix_cell=-1,matrix_instance=-1,matrix_cells=-1,finalize_page=-1,finalize_pages=-1,batches=-1,deferred_cells=-1;
            public int scanned_cells;public bool cell_scan_available,cell_scan_truncated;
            // Layer order: trees, rocks, ground cover. Counts cover scanned cells.
            public int[] cells_by_layer=new int[3],published_by_layer=new int[3],empty_by_layer=new int[3],instances_by_layer=new int[3];
            public int horizon_pending,horizon_resident,horizon_draws,horizon_visible;
            public bool horizon_worker_pending,horizon_visible_available;
            public double horizon_published_far,horizon_farthest;
        }
        static object Private(object owner,string name)=>owner?.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)?.GetValue(owner);
        static int PrivateInt(object owner,string name){object value=Private(owner,name);return value is int i?i:value is bool b?b?1:0:-1;}
        static int PrivateCount(object owner,string name)=>Private(owner,name) is ICollection collection?collection.Count:-1;
        static PopulationDiagnostic ObservePopulation(ExplorationBootstrap root)
        {
            var d=new PopulationDiagnostic{utc=DateTime.UtcNow.ToString("o")};
            try
            {
                if(root==null)return d;d.boot_failed=root.Failed;d.source=root.WorldField?.SourceIdentity;
                var p=root.Population;if(p==null)return d;d.present=true;d.ready=p.Ready;d.status=p.Status;
                d.loaded_models=p.LoadedModels;d.resident_cells=p.ResidentCells;d.resident_instances=p.ResidentInstances;d.pending_cells=p.PendingCells;
                d.draw_calls=p.DrawCalls;d.visible_instances=p.VisibleInstances;d.visible_triangles=p.VisibleTriangles;d.shadow_instances=p.ShadowOnlyInstances;
                d.budget_demotions=p.BudgetDemotions;d.omitted_soft=p.BudgetOmittedSoftInstances;d.work_ms=p.LastWorkMilliseconds;
                d.tree_distance=p.TreeDistance;d.plant_distance=p.GroundCoverDistance;d.rock_distance=p.RockDistance;d.tree_density=p.TreeDensity;d.plant_density=p.PlantDensity;
                d.triangle_budget=p.TriangleBudget;d.instance_limit=p.EffectiveInstanceLimit;d.horizon_enabled=p.EnableForestHorizon;
                if(Private(p,"observer") is WorldPoint eye){d.observer_valid=true;d.x=eye.X;d.y=eye.Y;d.z=eye.Z;}
                if(Private(p,"origin") is WorldPoint origin){d.origin_x=origin.X;d.origin_y=origin.Y;d.origin_z=origin.Z;}
                var camera=root.Vista!=null?root.Vista.MainCamera:null;
                if(camera!=null){d.camera_valid=true;var forward=camera.transform.forward;d.camera_forward_x=forward.x;d.camera_forward_y=forward.y;d.camera_forward_z=forward.z;d.camera_far=camera.farClipPlane;}
                var h=p.Horizon;d.horizon_present=h!=null;
                if(h!=null)
                {
                    d.horizon_error=h.Error;d.horizon_gpu=h.UsingGpu;d.horizon_pending=h.PendingSamples;d.horizon_worker_pending=h.PendingSamples==-1;
                    d.horizon_resident=h.ResidentStands;d.horizon_draws=h.DrawCalls;d.horizon_visible=h.VisibleStands;d.horizon_visible_available=h.VisibleStands>=0;
                    d.horizon_published_far=h.PublishedFarDistance;d.horizon_farthest=h.FarthestStandDistance;
                }
                d.building_matrices=PrivateInt(p,"buildingMatrices");d.finalizing=PrivateInt(p,"finalizing");d.matrix_cell=PrivateInt(p,"matrixCell");d.matrix_instance=PrivateInt(p,"matrixInstance");
                d.matrix_cells=PrivateCount(p,"matrixCells");d.finalize_page=PrivateInt(p,"finalizePage");d.finalize_pages=PrivateCount(p,"finalizePages");d.batches=PrivateCount(p,"batches");
                // HashSet does not implement non-generic ICollection.
                if(Private(p,"deferred") is System.Collections.Generic.ICollection<PopulationKey> deferred)d.deferred_cells=deferred.Count;
                if(Private(p,"cells") is IDictionary cells)
                {
                    d.cell_scan_available=true;FieldInfo dataField=null,publishedField=null;
                    foreach(DictionaryEntry entry in cells)
                    {
                        if(d.scanned_cells>=4096){d.cell_scan_truncated=true;break;}
                        if(dataField==null){dataField=entry.Value.GetType().GetField("Data");publishedField=entry.Value.GetType().GetField("Published");}
                        if(dataField==null||publishedField==null){d.cell_scan_available=false;break;}
                        var cell=(PopulationCell)dataField.GetValue(entry.Value);int layer=cell.Key.Layer;d.scanned_cells++;
                        if(layer<0||layer>2)continue;d.cells_by_layer[layer]++;d.instances_by_layer[layer]+=cell.Instances.Length;
                        if(cell.Instances.Length==0)d.empty_by_layer[layer]++;if((bool)publishedField.GetValue(entry.Value))d.published_by_layer[layer]++;
                    }
                }
            }
            catch(Exception e){d.error=e.GetType().Name+": "+e.Message;}
            return d;
        }
        [Serializable] sealed class Patch { public string id;public PatchFile[] files; }
        [Serializable] sealed class PatchFile { public string path,sha256,before; }
        static string Hash(string file){using(var hash=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(file))).Replace("-","").ToLowerInvariant();}
        static void ApplyStagedPatch()
        {
            const string manifest=".terrain-cache/quality-patch-staging/manifest.json";
            if(!File.Exists(manifest)||EditorApplication.isPlayingOrWillChangePlaymode)return;
            Patch patch=JsonUtility.FromJson<Patch>(File.ReadAllText(manifest));string root=Path.GetFullPath(".")+Path.DirectorySeparatorChar;
            foreach(var f in patch.files)
            {
                string path=Path.GetFullPath(f.path);if(!path.StartsWith(root,StringComparison.OrdinalIgnoreCase)||f.path.Contains("..")||!(f.path.StartsWith("Assets/")||f.path.StartsWith("Packages/")||f.path.StartsWith("ProjectSettings/")))throw new InvalidDataException("Patch path outside project inputs.");
                if((File.Exists(path)?Hash(path):"")!=f.before)throw new InvalidDataException("Concurrent edit blocks patch: "+f.path);
                if(Hash(".terrain-cache/quality-patch-staging/"+f.path)!=f.sha256)throw new InvalidDataException("Staging hash mismatch: "+f.path);
            }
            AssetDatabase.StartAssetEditing();
            try{foreach(var f in patch.files){string backup=".terrain-cache/quality-patch-"+patch.id+"-backup/"+f.path;if(File.Exists(f.path)){Directory.CreateDirectory(Path.GetDirectoryName(backup));File.Copy(f.path,backup,false);}Directory.CreateDirectory(Path.GetDirectoryName(f.path));File.Copy(".terrain-cache/quality-patch-staging/"+f.path,f.path,true);}File.Move(manifest,manifest+".applied-"+patch.id);}
            finally{AssetDatabase.StopAssetEditing();}
            AssetDatabase.Refresh();Debug.Log("QUALITY_PATCH_APPLIED "+patch.id);
        }
        [Serializable] sealed class Report
        {
            public string utc,unity,gpu,api,source,status,upscaling;
            public int material_catalog_version;public bool world_shader_error;
            public bool ready,ground_shader_error,tree_shader_error;
            public int contact_ready,contact_pending,distant_pending;
            public long contact_bytes,contact_triangles,total_allocated_bytes;
            public double x,y,z,worker_ms,build_ms;
            public PopulationDiagnostic population_before_capture;
        }
        static QualityPatchReview(){EditorApplication.update+=Tick;}
        static void Tick()
        {
            if(EditorApplication.timeSinceStartup<nextPoll)return;nextPoll=EditorApplication.timeSinceStartup+2;
            var boot=UnityEngine.Object.FindFirstObjectByType<ExplorationBootstrap>();
            // A diagnostics failure must not prevent patch/job/request polling.
            try
            {
                Directory.CreateDirectory(".terrain-cache");
                File.WriteAllText(".terrain-cache/quality-editor-state.json",JsonUtility.ToJson(new EditorStatus{playing=EditorApplication.isPlaying,changing=EditorApplication.isPlayingOrWillChangePlaymode,compiling=EditorApplication.isCompiling,ready=boot!=null&&boot.Ready,utc=DateTime.UtcNow.ToString("o"),population=ObservePopulation(boot),loading=boot?.LoadingSnapshot}));
            }
            catch(Exception e){Debug.LogWarning("Quality heartbeat could not be written: "+e.Message);}
            if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;
            try
            {
                ApplyStagedPatch();
                if(File.Exists(JobPath)&&!EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    Job job=JsonUtility.FromJson<Job>(File.ReadAllText(JobPath));File.Move(JobPath,JobPath+".running");
                    if(job.action=="bake-atlas")ForestAtlasBaker.Bake();
                    else if(job.action=="mesh-regression")Type.GetType("AshenVoxel.Rendering.Editor.MeshRegressionChecks, AshenVoxel.Rendering.Editor",true).GetMethod("Run").Invoke(null,null);
                    else if(job.action=="coarse-source-check")CoarseOverviewNativeCheck.Run();
                    else throw new InvalidDataException("Unknown bounded quality review action.");
                    File.Move(JobPath+".running",JobPath+".completed-"+DateTime.UtcNow.ToString("yyyyMMddHHmmss"));
                }
                if(!EditorApplication.isPlaying||boot==null||!boot.Ready||!File.Exists(Request))return;
                string label=File.ReadAllText(Request).Trim();File.Delete(Request);
                if(label.Length<1||label.Length>80||System.Text.RegularExpressions.Regex.IsMatch(label,"[^a-zA-Z0-9_-]"))return;
                Capture(label);
            }
            catch(Exception e){File.WriteAllText(".terrain-cache/quality-review-error.txt",e.ToString());Debug.LogException(e);}
        }
        [MenuItem("Ashen Voxel/Exploration/Capture current quality patch")]
        public static void CaptureCurrent()=>Capture("manual-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        static void Capture(string label)
        {
            var root=UnityEngine.Object.FindFirstObjectByType<ExplorationBootstrap>();
            if(root==null||root.Vista==null||root.Vista.MainCamera==null)throw new InvalidOperationException("Enter Exploration Play first.");
            string output="Logs/QualityPatches/"+label;Directory.CreateDirectory(output);
            var populationBeforeCapture=ObservePopulation(root);
            File.WriteAllText(output+"/population-before-capture.json",JsonUtility.ToJson(populationBeforeCapture,true));
            var camera=root.Vista.MainCamera;var original=camera.targetTexture;var previous=RenderTexture.active;
            var rt=new RenderTexture(1600,900,24);var image=new Texture2D(1600,900,TextureFormat.RGB24,false);
            try
            {
                camera.targetTexture=rt;if(root.Population!=null)root.Population.Tick(camera);camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1600,900),0,0);image.Apply();
                File.WriteAllBytes(output+"/view.png",image.EncodeToPNG());
            }
            finally{camera.targetTexture=original;RenderTexture.active=previous;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(image);}
            var eye=root.Vista.GlobalCamera;
            var report=new Report{utc=DateTime.UtcNow.ToString("o"),unity=Application.unityVersion,gpu=SystemInfo.graphicsDeviceName,api=SystemInfo.graphicsDeviceType.ToString(),
                source=root.WorldField.SourceIdentity,status=root.Status,ready=root.Ready,upscaling=root.Upscaler!=null?root.Upscaler.Status:"unavailable",material_catalog_version=AshenVoxel.Core.MaterialCatalog.Version,world_shader_error=ShaderUtil.ShaderHasError(Shader.Find("AshenVoxel/WorldSurface")),ground_shader_error=ShaderUtil.ShaderHasError(Shader.Find("AshenVoxel/GroundSurface")),tree_shader_error=ShaderUtil.ShaderHasError(Shader.Find("AshenVoxel/ForestImpostor")),
                contact_ready=root.Vista.ContactTiles,contact_pending=root.Vista.ContactPendingTiles,distant_pending=root.Vista.PendingTiles,contact_bytes=root.Vista.ContactMeshBytes,contact_triangles=root.Vista.ContactTriangles,
                total_allocated_bytes=Profiler.GetTotalAllocatedMemoryLong(),x=eye.X,y=eye.Y,z=eye.Z,worker_ms=root.Vista.LastVoxelWorkerMilliseconds,build_ms=root.Vista.LastMeshBuildMilliseconds,population_before_capture=populationBeforeCapture};
            File.WriteAllText(output+"/native.json",JsonUtility.ToJson(report,true));Debug.Log("QUALITY_PATCH_CAPTURE "+output);
        }
    }
}
