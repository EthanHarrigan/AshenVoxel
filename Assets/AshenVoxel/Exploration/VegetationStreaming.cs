using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using AshenVoxel.Core.Trees;
using AshenVoxel.WorldScale;
using UnityEngine;
using UnityEngine.Rendering;
namespace AshenVoxel.Exploration
{
    /// <summary>Shared baked10cm prototype meshes, spatially paged instancing and
    /// independent exact hard-tissue collision. Root calls SetObserver then Tick
    /// after the camera's origin update; there is intentionally no private Update.</summary>
    public sealed partial class VegetationStreaming:MonoBehaviour,IExplorationObstacles,IExplorationResistance
    {
        public float TreeDistance=1400,TreeDetailDistance=100,TreeMidDistance=300,TreeCoarseDistance=500,TreeFarDistance=1000,RedwoodDetailDistance=40;
        public float GroundCoverDistance=64,RockDistance=240;
        public float TreeDensity=1,PlantDensity=.72f,RockDensity=1;
        public int TriangleBudget=32000000,MaxResidentCells=2200,MaxInstances=60000,CellsPerFrame=2,MatricesPerFrame=5000;
        public float WorkMilliseconds=2;
        public float HorizonDistance=30000;
        public bool EnableForestHorizon=true;
        public bool UseTreeImpostors=true;
        public bool UseGpuForestHorizon=true;
        public ForestHorizon Horizon {get;private set;}
        [Range(0,4)] public int TreeShadowLod=3;
        [Range(0,3)] public int RockShadowLod=1;
        [Range(0,3)] public int GroundCoverShadowLod=0;
        public bool Ready {get;private set;}
        public string Status {get;private set;}="Population is not initialized.";
        public int LoadedModels=>models.Count;
        public int TotalModels {get;private set;}
        public int CompletedModels {get;private set;}
        public string PrototypePackIdentity {get;private set;}="";
        public int ResidentCells=>cells.Count;
        public int ResidentInstances {get;private set;}
        public int PendingCells=>queue.Count;
        public int DrawCalls {get;private set;}
        public int VisibleInstances {get;private set;}
        public long VisibleTriangles {get;private set;}
        public int ShadowOnlyDrawCalls {get;private set;}
        public int ShadowOnlyInstances {get;private set;}
        long modelMeshBytes;public long MeshBytes=>modelMeshBytes+rootPayloadBytes;
        public double LastWorkMilliseconds {get;private set;}
        public int BudgetDemotions {get;private set;}
        public int BudgetOmittedSoftInstances {get;private set;}
        public int EffectiveInstanceLimit=>Math.Max(4096,MaxInstances);
        sealed class Model {public ExplorationPrototype Data;public Mesh[] Meshes;public int[] Triangles,UpperTriangles,BaseTriangles;public bool[] Split;}
        long nextCellEpoch;sealed class CachedCell {public PopulationCell Data;public bool Published;public long Epoch;}
        readonly struct PageKey:IEquatable<PageKey>
        {
            public readonly int Model,Lod,Part,Variant;public readonly long X,Z;
            public PageKey(int model,int lod,long x,long z,int part=0,int variant=0){Model=model;Lod=lod;X=x;Z=z;Part=part;Variant=variant;}
            public bool Equals(PageKey b)=>Model==b.Model&&Lod==b.Lod&&X==b.X&&Z==b.Z&&Part==b.Part&&Variant==b.Variant;
            public override bool Equals(object value)=>value is PageKey p&&Equals(p);
            public override int GetHashCode(){unchecked{return ((((Model*397^Lod)*397^X.GetHashCode())*397^Z.GetHashCode())*397^Part)*397^Variant;}}
        }
        sealed class Page {public PageKey Key;public readonly List<Matrix4x4> Matrices=new List<Matrix4x4>();public readonly List<WorldPoint> Anchors=new List<WorldPoint>();public Bounds Bounds;public bool HasBounds;public Mesh Mesh;public int Submesh,Triangles;public bool CountsInstance;}
        sealed class Batch {public Mesh Mesh;public Matrix4x4[] Matrices=Array.Empty<Matrix4x4>();public WorldPoint[] Anchors=Array.Empty<WorldPoint>();public Bounds Bounds;public bool Shadows;public int TriangleCount,Count,ModelId,Lod,Submesh;public bool CountsInstance;}
        readonly Dictionary<int,Model> models=new Dictionary<int,Model>();
        readonly Dictionary<PopulationKey,CachedCell> cells=new Dictionary<PopulationKey,CachedCell>();
        readonly HashSet<PopulationKey> desired=new HashSet<PopulationKey>(),deferred=new HashSet<PopulationKey>();
        readonly HashSet<PopulationKey> queued=new HashSet<PopulationKey>();
        readonly List<PopulationKey> queue=new List<PopulationKey>(),remove=new List<PopulationKey>();
        List<Batch> batches=new List<Batch>(),nextBatches=new List<Batch>();
        readonly List<Page> finalizePages=new List<Page>();int finalizePage,finalizeOffset;bool finalizing;
        readonly Stack<Batch> batchPool=new Stack<Batch>();readonly Stack<Page> pagePool=new Stack<Page>();
        public long MatrixBufferBytes {get{long bytes=0;foreach(var b in batches)bytes+=b.Matrices.LongLength*64+b.Anchors.LongLength*24;foreach(var b in nextBatches)bytes+=b.Matrices.LongLength*64+b.Anchors.LongLength*24;foreach(var b in batchPool)bytes+=b.Matrices.LongLength*64+b.Anchors.LongLength*24;return bytes;}}
        readonly Dictionary<PageKey,Page> buildingPages=new Dictionary<PageKey,Page>();
        readonly Plane[] planes=new Plane[6];
        readonly List<CachedCell> matrixCells=new List<CachedCell>();
        Material material,impostorMaterial;ExplorationPopulation population;SurfaceMaterialSampler surface;
        int populationSeed;
        IEnumerator loading;WorldPoint observer,origin;bool haveOrigin,initialized;
        double lastPlanX=double.PositiveInfinity,lastPlanZ=double.PositiveInfinity,lastMatrixX=double.PositiveInfinity,lastMatrixY,lastMatrixZ;
        long lastPlanAt;int matrixCell,matrixInstance;
        bool buildingMatrices,matricesDirty;long coverageReserve,buildTriangles;int buildDemotions;float maximumRadius=24,maximumHeight=90;float lastTreeDensity=1,lastPlantDensity=.72f,lastRockDensity=1;
        static long Now=>Stopwatch.GetTimestamp();
        static double Ms(long ticks)=>ticks*1000.0/Stopwatch.Frequency;

        public void Initialize(IWorldField bareField,SurfaceProfile profile,TextAsset prototypePack,Material materialTemplate,int seed)
        {
            DisposeRuntime();
            if(bareField==null||prototypePack==null)throw new ArgumentNullException(bareField==null?nameof(bareField):nameof(prototypePack));
            surface=new SurfaceMaterialSampler(bareField,profile);population=new ExplorationPopulation(surface,seed);
            populationSeed=seed;
            Material template=materialTemplate!=null?materialTemplate:Resources.Load<Material>("VoxelSurface");
            if(template==null)throw new InvalidOperationException("The instanced voxel surface material is missing.");
            material=new Material(template);material.enableInstancing=true;
            // The marker is authored with the atlas, so missing/unfinished bakes
            // retain the ordinary renderer. Headless tests exercise both paths.
            if(UseTreeImpostors&&Resources.Load<TextAsset>("ForestAtlasVersion")!=null)
            {
                Material sprite=Resources.Load<Material>("ForestImpostor");
                if(sprite!=null){impostorMaterial=new Material(sprite);impostorMaterial.enableInstancing=true;}
            }
            initialized=true;Ready=false;Status="Loading shared voxel prototypes…";var packBytes=prototypePack.bytes;InitializeRoots(bareField,packBytes);loading=Load(packBytes);
        }
        IEnumerator Load(byte[] bytes)
        {
            using(var reader=new ExplorationPrototypeReader(bytes))
            {
                TotalModels=reader.Count;CompletedModels=0;
                while(reader.TryRead(out ExplorationPrototype data))
                {
                    var model=new Model{Data=data,Meshes=new Mesh[data.LodCount],Triangles=new int[data.LodCount],UpperTriangles=new int[data.LodCount],BaseTriangles=new int[data.LodCount],Split=new bool[data.LodCount]};models.Add(data.Id,model);
                    for(int lod=0;lod<data.LodCount;lod++)
                    {
                        if(data.Kind==0&&lod>=3&&impostorMaterial!=null)
                        {
                            model.Meshes[lod]=ForestBillboard.Create(data);model.Triangles[lod]=2;modelMeshBytes+=136;
                            yield return null;continue;
                        }
                        PrototypeMeshData source=data.Mesh(lod);
                        RootContactUploadPrototype.IPreparedUpload partition=null;
                        if(RootPartition(data.Id,lod))
                        {
                            try{partition=RootContactUploadPrototype.UploadPreparation.PrepareCanonicalPacked(source,lod,RootPackIdentity+"/model="+data.Id,new RootContactUploadPrototype.UploadLimits(8000000,12000000,512L*1024*1024));}
                            catch(ArgumentException){partition=null;}
                        }
                        if(partition!=null)
                        {
                            var upload=new RootContactUploadPrototype.UnityMeshUpload(System.Threading.Thread.CurrentThread.ManagedThreadId);
                            if(!upload.TryCreate(partition,data.Name+" split LOD"+lod,ExplorationWind.MaximumDisplacementMeters,()=>!rootClosed,new RootContactUploadPrototype.UploadReservation(partition.Identity,partition.GpuPayloadBytes*2+65536),out var splitMesh,out var failed,out string failure))
                            {if(failed!=null)failed.TryRequestDestroy(out _);throw new InvalidOperationException("Canonical partition upload: "+failure);}
                            model.Meshes[lod]=splitMesh;model.UpperTriangles[lod]=partition.Triangles(0);model.BaseTriangles[lod]=partition.Triangles(1);model.Triangles[lod]=model.UpperTriangles[lod]+model.BaseTriangles[lod];model.Split[lod]=true;modelMeshBytes+=partition.GpuPayloadBytes;
                            yield return null;continue;
                        }
                        var vertices=new Vector3[source.VertexCount];var normals=new Vector3[source.VertexCount];var colors=new Color32[source.VertexCount];
                        for(int i=0;i<source.VertexCount;i++)
                        {
                            vertices[i]=new Vector3(source.Positions[i*3]*.1f,source.Positions[i*3+1]*.1f,source.Positions[i*3+2]*.1f);
                            byte n=source.Normals[i];normals[i]=n==0?Vector3.right:n==1?Vector3.left:n==2?Vector3.up:n==3?Vector3.down:n==4?Vector3.forward:Vector3.back;
                            uint c=source.Colors[i];colors[i]=new Color32((byte)c,(byte)(c>>8),(byte)(c>>16),(byte)(c>>24));
                        }
                        var mesh=new Mesh{name=data.Name+" LOD"+lod,indexFormat=IndexFormat.UInt32};
                        mesh.vertices=vertices;mesh.normals=normals;mesh.colors32=colors;mesh.SetTriangles(source.Indices,0,true);mesh.RecalculateBounds();
#if UNITY_5_3_OR_NEWER
                        // Wind is render-only; pad cached draw bounds, never source/collision geometry.
                        Bounds windBounds=mesh.bounds;windBounds.Expand(ExplorationWind.MaximumDisplacementMeters*2);mesh.bounds=windBounds;
#endif
                        mesh.UploadMeshData(true);
                        model.Meshes[lod]=mesh;model.Triangles[lod]=source.Indices.Length/3;
                        modelMeshBytes+=(long)source.VertexCount*28+(long)source.Indices.Length*4;
                        Status="Loading "+data.Name+" · LOD "+(lod+1)+"/"+data.LodCount+" · "+models.Count+"/"+reader.Count+" models";
                        yield return null;
                    }
                    data.ReleaseMeshData();maximumRadius=Math.Max(maximumRadius,data.Radius*1.415f+2);maximumHeight=Math.Max(maximumHeight,data.MaxY);CompletedModels++;
                }
            }
            if(impostorMaterial!=null)
            {
                var prototypes=new Dictionary<int,ExplorationPrototype>();var sprites=new Dictionary<int,Mesh>();
                foreach(var pair in models)if(pair.Value.Data.Kind==0){prototypes.Add(pair.Key,pair.Value.Data);sprites.Add(pair.Key,pair.Value.Meshes[3]);}
                Horizon=new ForestHorizon(surface,populationSeed,impostorMaterial,prototypes,sprites,UseGpuForestHorizon);
            }
            Ready=true;matricesDirty=true;Status="Shared voxel prototypes ready.";
        }
        public void SetObserver(WorldPoint eyeGlobal,WorldPoint renderOrigin)
        {
            observer=eyeGlobal;
            if(haveOrigin&&(origin.X!=renderOrigin.X||origin.Y!=renderOrigin.Y||origin.Z!=renderOrigin.Z))
            {
                Vector3 delta=new Vector3((float)(origin.X-renderOrigin.X),(float)(origin.Y-renderOrigin.Y),(float)(origin.Z-renderOrigin.Z));
                if(anchorHasBounds)anchorBounds.center+=delta;
                // Shift every already-built matrix and its culling bounds in this
                // same call; camera and vegetation cannot occupy different origins.
                foreach(Batch batch in AllActiveBatches()){for(int i=0;i<batch.Count;i++){Matrix4x4 m=batch.Matrices[i];m.m03+=delta.x;m.m13+=delta.y;m.m23+=delta.z;batch.Matrices[i]=m;}batch.Bounds.center+=delta;}
                foreach(Page page in buildingPages.Values){for(int i=0;i<page.Matrices.Count;i++){Matrix4x4 m=page.Matrices[i];m.m03+=delta.x;m.m13+=delta.y;m.m23+=delta.z;page.Matrices[i]=m;}if(page.HasBounds)page.Bounds.center+=delta;}
            }
            origin=renderOrigin;haveOrigin=true;
        }
        IEnumerable<Batch> AllActiveBatches(){foreach(Batch batch in batches)yield return batch;foreach(Batch batch in nextBatches)yield return batch;}
        public void Tick(Camera camera)
        {
            if(!initialized||camera==null)return;long started=Now;
            if(loading!=null)
            {
                try{int steps=0;do{if(!loading.MoveNext()){loading=null;break;}}while(++steps<8&&Ms(Now-started)<Math.Max(.25,WorkMilliseconds));}
                catch(Exception e){Status="Prototype loading failed: "+e.Message;Ready=false;loading=null;throw;}
                LastWorkMilliseconds=Ms(Now-started);return;
            }
            TickRoots();TickShrubLease();TickClipmapAnchors(started);
            float treeDensity=Clamp(TreeDensity,0,2),plantDensity=Clamp(PlantDensity,0,2),rockDensity=Clamp(RockDensity,0,2);
            if(treeDensity!=lastTreeDensity||plantDensity!=lastPlantDensity||rockDensity!=lastRockDensity)ReconfigureDensity();
            lastTreeDensity=treeDensity;lastPlantDensity=plantDensity;lastRockDensity=rockDensity;
            population.TreeDensity=treeDensity;population.PlantDensity=plantDensity;population.RockDensity=rockDensity;
            double planTravel=(observer.X-lastPlanX)*(observer.X-lastPlanX)+(observer.Z-lastPlanZ)*(observer.Z-lastPlanZ);
            if(planTravel>64||Ms(Now-lastPlanAt)>700){Plan();lastPlanAt=Now;lastPlanX=observer.X;lastPlanZ=observer.Z;}
            // Advance the captured snapshot first, then let cell generation use the remaining budget.
            bool advancedMatrices=buildingMatrices;
            if(advancedMatrices)BuildMatrices(started,Math.Max(.25,WorkMilliseconds)*.5);
            int builds=0;
            while(queue.Count>0&&builds<Math.Max(1,CellsPerFrame)&&Ms(Now-started)<Math.Max(.25,WorkMilliseconds))
            {
                PopulationKey key=queue[queue.Count-1];queue.RemoveAt(queue.Count-1);queued.Remove(key);
                if(!desired.Contains(key)||cells.ContainsKey(key))continue;
                PopulationCell data=population.Build(key);builds++;
                if(ResidentInstances+data.Instances.Length>EffectiveInstanceLimit)
                {
                    if(Distance(key)>96*96){deferred.Add(key);continue;}
                    EvictFarthest(data.Instances.Length);
                    if(ResidentInstances+data.Instances.Length>EffectiveInstanceLimit)
                    {
                        int capacity=EffectiveInstanceLimit-ResidentInstances;var hard=new List<PopulationInstance>();var soft=new List<PopulationInstance>();
                        foreach(PopulationInstance instance in data.Instances)
                        {if(models.TryGetValue(instance.PrototypeId,out Model model)&&model.Data.HasCollision)hard.Add(instance);else soft.Add(instance);}
                        if(hard.Count>capacity){Status="Near hard-prop population is waiting for a larger instance budget.";break;}
                        soft.Sort((a,b)=>a.StableKey.CompareTo(b.StableKey));int keep=Math.Min(soft.Count,capacity-hard.Count);
                        for(int i=0;i<keep;i++)hard.Add(soft[i]);BudgetOmittedSoftInstances+=soft.Count-keep;data=new PopulationCell(data.Key,hard.ToArray(),data.SampledCandidates);
                    }
                }
                cells.Add(key,new CachedCell{Data=data,Epoch=++nextCellEpoch});ResidentInstances+=data.Instances.Length;matricesDirty=true;
                // Readiness belongs to this cell's own publication, not far queue activity.
            }
            double matrixTravel=(observer.X-lastMatrixX)*(observer.X-lastMatrixX)+(observer.Y-lastMatrixY)*(observer.Y-lastMatrixY)+(observer.Z-lastMatrixZ)*(observer.Z-lastMatrixZ);
            if(!buildingMatrices&&(matricesDirty||matrixTravel>16))BeginMatrices();
            if(buildingMatrices&&!advancedMatrices)BuildMatrices(started,Math.Max(.25,WorkMilliseconds));
            LastWorkMilliseconds=Ms(Now-started);Draw(camera);
            Status="Population · "+ResidentInstances+" instances / "+ResidentCells+" cells · "+PendingCells+" queued · "+DrawCalls+" visible draws";
        }
        void Plan()
        {
            desired.Clear();
            AddDesired(0,64);AddDesired(1,64);AddDesired(2,Clamp(GroundCoverDistance,20,32));
            AddDesired(0,Clamp(TreeDistance,64,4000));AddDesired(1,Clamp(RockDistance,64,800));AddDesired(2,Clamp(GroundCoverDistance,20,128));
            if(ResidentInstances<EffectiveInstanceLimit*.75)deferred.Clear();
            deferred.RemoveWhere(k=>!desired.Contains(k)||Distance(k)<128*128);
            remove.Clear();foreach(var pair in cells)if(!desired.Contains(pair.Key)&&!ShrubRetains(pair.Key))remove.Add(pair.Key);
            foreach(PopulationKey key in remove){ResidentInstances-=cells[key].Data.Instances.Length;cells.Remove(key);matricesDirty=true;}
            queue.Clear();queued.Clear();foreach(PopulationKey key in desired)if(!cells.ContainsKey(key)&&!deferred.Contains(key)){queue.Add(key);queued.Add(key);}
            queue.Sort((a,b)=>Distance(b).CompareTo(Distance(a)));
        }
        void AddDesired(int layer,double radius)
        {
            if(layer==0&&TreeDensity<=0||layer==1&&RockDensity<=0||layer==2&&PlantDensity<=0)return;
            double size=layer==2?16:64;int reach=(int)Math.Ceiling(radius/size)+1;long cx=(long)Math.Floor(observer.X/size),cz=(long)Math.Floor(observer.Z/size);
            var candidates=new List<PopulationKey>();
            for(int z=-reach;z<=reach;z++)for(int x=-reach;x<=reach;x++)
            {var key=new PopulationKey(cx+x,cz+z,layer);if(Distance(key)<=radius*radius)candidates.Add(key);}
            candidates.Sort((a,b)=>Distance(a).CompareTo(Distance(b)));
            // Reserve room for the near rock/plant collision layers even when
            // the user pushes the far-tree radius beyond the residency cap.
            int limit=layer==0?Math.Max(64,Math.Max(128,MaxResidentCells)-200):Math.Max(128,MaxResidentCells);
            foreach(var key in candidates){if(desired.Count>=limit)break;desired.Add(key);}
        }
        double Distance(PopulationKey key)
        {
            double size=key.SizeMeters,dx=Math.Max(key.X*size-observer.X,Math.Max(0,observer.X-(key.X+1)*size));
            double dz=Math.Max(key.Z*size-observer.Z,Math.Max(0,observer.Z-(key.Z+1)*size));return dx*dx+dz*dz;
        }
        void EvictFarthest(int incoming)
        {
            while(ResidentInstances+incoming>EffectiveInstanceLimit&&cells.Count>0)
            {
                PopulationKey far=default;double maximum=-1;bool found=false;
                foreach(var pair in cells){double d=Distance(pair.Key);if(d>maximum&&d>64*64&&!ShrubRetains(pair.Key)){maximum=d;far=pair.Key;found=true;}}
                if(!found)break;ResidentInstances-=cells[far].Data.Instances.Length;cells.Remove(far);deferred.Add(far);matricesDirty=true;
            }
        }
        void BeginMatrices()
        {
            ReleaseNextBatches();CaptureRootSelection();CaptureShrubSelection();fastHidden.Clear();
            matrixCells.Clear();foreach(var cell in cells.Values)matrixCells.Add(cell);
            matrixCells.Sort((a,b)=>Distance(a.Data.Key).CompareTo(Distance(b.Data.Key)));
            // Reserve the actual cheapest representation of every captured entry, including hard props.
            coverageReserve=0;
            if(impostorMaterial!=null)foreach(var cell in matrixCells)foreach(var instance in cell.Data.Instances)
                if(!SkipShrubNext(cell.Data,instance)&&models.TryGetValue(instance.PrototypeId,out Model model))coverageReserve+=RootTriangleCost(model,model.Meshes.Length-1,SelectedRoot(cell.Data,instance,true));
            matrixCell=matrixInstance=0;ReleaseBuildingPages();buildingMatrices=true;matricesDirty=false;buildTriangles=0;buildDemotions=0;finalizing=false;finalizePage=finalizeOffset=0;finalizePages.Clear();
            lastMatrixX=observer.X;lastMatrixY=observer.Y;lastMatrixZ=observer.Z;
        }
        void BuildMatrices(long frameStart,double milliseconds)
        {
            int visited=0;
            while(!finalizing&&matrixCell<matrixCells.Count&&visited<Math.Max(256,MatricesPerFrame)&&Ms(Now-frameStart)<milliseconds)
            {
                PopulationCell cell=matrixCells[matrixCell].Data;
                if(matrixInstance>=cell.Instances.Length){matrixCell++;matrixInstance=0;continue;}
                PopulationInstance instance=cell.Instances[matrixInstance++];visited++;
                if(SkipShrubNext(cell,instance))continue;
                if(!models.TryGetValue(instance.PrototypeId,out Model model))continue;
                // Retire this entry before range filtering; skipped entries must release their reservation too.
                if(impostorMaterial!=null)coverageReserve-=RootTriangleCost(model,model.Meshes.Length-1,SelectedRoot(cell,instance,true));
                double distance=InstanceDistance(instance,model.Data);
                double maximum=model.Data.Kind==0?TreeDistance:model.Data.Kind==1?GroundCoverDistance:RockDistance;
                if(distance>maximum)continue;
                RootBinding root=SelectedRoot(cell,instance,true);int lod=Lod(instance,model,distance),original=lod;
                long available=Math.Max(0,Math.Max(100000,TriangleBudget)-coverageReserve);
                while(lod<model.Meshes.Length-1&&buildTriangles+RootTriangleCost(model,lod,root)>available)lod++;
                if(lod!=original)buildDemotions++;
                if(buildTriangles+RootTriangleCost(model,lod,root)>available&&distance>80)continue;
                buildTriangles+=RootTriangleCost(model,lod,root);
                double pageSize=model.Data.Kind==1?32:model.Data.Kind==2?64:lod<2?128:512;
                AddRootAwarePages(instance,model,lod,pageSize,root);
            }
            if(matrixCell<matrixCells.Count)return;
            if(!finalizing){finalizePages.Clear();foreach(Page page in buildingPages.Values)finalizePages.Add(page);finalizing=true;finalizePage=finalizeOffset=0;}
            int finished=0;
            while(finalizePage<finalizePages.Count&&finished<32&&Ms(Now-frameStart)<milliseconds)
            {
                Page page=finalizePages[finalizePage];Model model=models[page.Key.Model];
                if(finalizeOffset>=page.Matrices.Count){finalizePage++;finalizeOffset=0;continue;}
                int count=Math.Min(1023,page.Matrices.Count-finalizeOffset);Batch batch=batchPool.Count>0?batchPool.Pop():new Batch();if(batch.Matrices.Length<count){int capacity=16;while(capacity<count)capacity*=2;batch.Matrices=new Matrix4x4[Math.Min(1023,capacity)];}if(batch.Anchors.Length<batch.Matrices.Length)batch.Anchors=new WorldPoint[batch.Matrices.Length];page.Matrices.CopyTo(finalizeOffset,batch.Matrices,0,count);page.Anchors.CopyTo(finalizeOffset,batch.Anchors,0,count);
                batch.Mesh=page.Mesh;batch.Submesh=page.Submesh;batch.CountsInstance=page.CountsInstance;batch.ModelId=page.Key.Model;batch.Lod=page.Key.Lod;batch.Count=count;batch.Bounds=page.Bounds;batch.Shadows=page.Key.Lod<=(model.Data.Kind==0?TreeShadowLod:model.Data.Kind==2?RockShadowLod:GroundCoverShadowLod);batch.TriangleCount=page.Triangles;nextBatches.Add(batch);finalizeOffset+=count;finished++;
            }
            if(finalizePage<finalizePages.Count)return;
            if(!ValidateShrubSelection()||!CommitRootSelection())
            {ReleaseNextBatches();ReleaseBuildingPages();matrixCells.Clear();finalizePages.Clear();finalizing=false;buildingMatrices=false;matricesDirty=true;rootNext=null;return;}
            ReleaseBatches();var previous=batches;batches=nextBatches;nextBatches=previous;ReapplyFastHidden();PublishShrubSelection();
            foreach(CachedCell captured in matrixCells)if(cells.TryGetValue(captured.Data.Key,out CachedCell current)&&object.ReferenceEquals(current,captured))captured.Published=true;
            BudgetDemotions=buildDemotions;ReleaseBuildingPages();matrixCells.Clear();finalizePages.Clear();finalizing=false;buildingMatrices=false;
        }
        double InstanceDistance(PopulationInstance p,ExplorationPrototype data)
        {
            double radius=data.Radius*1.415,dx=Math.Max(Math.Abs(observer.X-p.Position.X)-radius,0),dz=Math.Max(Math.Abs(observer.Z-p.Position.Z)-radius,0);
            double dy=Math.Max(p.Position.Y+data.MinY-observer.Y,Math.Max(0,observer.Y-p.Position.Y-data.MaxY));return Math.Sqrt(dx*dx+dy*dy+dz*dz);
        }
        int Lod(PopulationInstance instance,Model model,double distance)
        {
            if(model.Data.Kind==1)return Math.Min(model.Meshes.Length-1,distance<24?0:distance<48?1:2);
            if(model.Data.Kind==2)return Math.Min(model.Meshes.Length-1,distance<35?0:distance<90?1:distance<160?2:3);
            bool redwood=instance.PrototypeId<100&&instance.PrototypeId/4==(int)TreeSpecies.Redwood;
            int lod=redwood?(distance<RedwoodDetailDistance?0:distance<Math.Max(100,RedwoodDetailDistance*2)?1:distance<300?2:distance<600?3:4):
                distance<TreeDetailDistance?0:distance<TreeMidDistance?1:distance<TreeCoarseDistance?2:distance<TreeFarDistance?3:4;
            return Math.Min(model.Meshes.Length-1,lod);
        }
        void Draw(Camera camera)
        {
            DrawCalls=VisibleInstances=ShadowOnlyDrawCalls=ShadowOnlyInstances=0;VisibleTriangles=0;GeometryUtility.CalculateFrustumPlanes(camera,planes);
            double shadowDistance=Clamp(QualitySettings.shadowDistance,0,1000);
            foreach(Batch batch in batches)
            {
                bool visible=GeometryUtility.TestPlanesAABB(planes,batch.Bounds);
                if(!visible)
                {
                    if(!batch.Shadows||BoundsDistance(batch.Bounds)>shadowDistance||MinimumLodDistance(batch)>shadowDistance)continue;
                }
                bool sprite=impostorMaterial!=null&&batch.ModelId<100&&batch.Lod>=3;
                if(sprite&&!visible)continue;
                var render=new RenderParams(sprite?impostorMaterial:material){camera=camera,worldBounds=batch.Bounds,shadowCastingMode=sprite?ShadowCastingMode.Off:!visible?ShadowCastingMode.ShadowsOnly:batch.Shadows?ShadowCastingMode.On:ShadowCastingMode.Off,receiveShadows=visible&&!sprite,layer=gameObject.layer,lightProbeUsage=LightProbeUsage.Off};
#if UNITY_5_3_OR_NEWER
                render.motionVectorMode=MotionVectorGenerationMode.Object;
#endif
                Graphics.RenderMeshInstanced(render,batch.Mesh,batch.Submesh,batch.Matrices,batch.Count);DrawCalls++;
                if(visible){if(batch.CountsInstance)VisibleInstances+=batch.Count;VisibleTriangles+=(long)batch.TriangleCount*batch.Count;}
                else{ShadowOnlyDrawCalls++;if(batch.CountsInstance)ShadowOnlyInstances+=batch.Count;}
            }
            if(EnableForestHorizon&&Horizon!=null)Horizon.Tick(camera,observer,origin,HorizonNear(),Math.Max(TreeDistance,HorizonDistance),TreeDensity,TerrainView);
        }
        /// <summary>Where impostor canopy takes over. Normally just inside the tree radius, but when
        /// the resident-instance budget defers far tree cells (dense forests), the horizon starts at
        /// the nearest deferred cell instead, so no bare ring appears between real trees and canopy.
        /// Quantized to 128 m so small budget changes do not replan the horizon.</summary>
        public double TreeCoverageRadius {get;private set;}
        double HorizonNear()
        {
            double full=TreeDistance*.97,nearest=double.MaxValue;
            foreach(PopulationKey key in deferred)if(key.Layer==0)nearest=Math.Min(nearest,Distance(key));
            double coverage=nearest==double.MaxValue?full:Math.Max(256,Math.Floor((Math.Sqrt(nearest)-64)/128)*128);
            TreeCoverageRadius=Math.Min(full,coverage);return TreeCoverageRadius;
        }
        double BoundsDistance(Bounds bounds)
        {
            double x=observer.X-origin.X-bounds.center.x,y=observer.Y-origin.Y-bounds.center.y,z=observer.Z-origin.Z-bounds.center.z;
            x=Math.Max(Math.Abs(x)-bounds.size.x*.5,0);y=Math.Max(Math.Abs(y)-bounds.size.y*.5,0);z=Math.Max(Math.Abs(z)-bounds.size.z*.5,0);return Math.Sqrt(x*x+y*y+z*z);
        }
        double MinimumLodDistance(Batch batch)
        {
            if(batch.Lod==0)return 0;
            int kind=models.TryGetValue(batch.ModelId,out Model owner)?owner.Data.Kind:batch.ModelId<100?0:batch.ModelId<200?1:2;
            if(kind==0){bool redwood=batch.ModelId<100&&batch.ModelId/4==(int)TreeSpecies.Redwood;return redwood?(batch.Lod==1?RedwoodDetailDistance:batch.Lod==2?Math.Max(100,RedwoodDetailDistance*2):batch.Lod==3?300:600):batch.Lod==1?TreeDetailDistance:batch.Lod==2?TreeMidDistance:batch.Lod==3?TreeCoarseDistance:TreeFarDistance;}
            return kind==1?(batch.Lod==1?24:48):batch.Lod==1?35:batch.Lod==2?90:160;
        }
        public bool IsReady(double x,double z,double radius)
        {
            GlobalQuery(x,z,radius,64);
            if(!Ready)return false;
            // At high flight altitude no hard prototype can reach the body. Begin
            // loading/gating well before descending into even a75m crown/trunk.
            if(surface!=null&&observer.Y>surface.BaseField.Sample(x,z).HeightMeters+Math.Max(160,maximumHeight+48))return true;
            for(int layer=0;layer<3;layer++)
            {
                if(layer==0&&TreeDensity<=0||layer==1&&RockDensity<=0||layer==2&&PlantDensity<=0)continue;
                double size=layer==2?16:64,margin=radius+(layer==2?3:maximumRadius);
                long x0=(long)Math.Floor((x-margin)/size),x1=(long)Math.Floor((x+margin)/size),z0=(long)Math.Floor((z-margin)/size),z1=(long)Math.Floor((z+margin)/size);
                for(long iz=z0;iz<=z1;iz++)for(long ix=x0;ix<=x1;ix++)
                {
                    double dx=Math.Max(ix*size-x,Math.Max(0,x-(ix+1)*size)),dz=Math.Max(iz*size-z,Math.Max(0,z-(iz+1)*size));
                    if(dx*dx+dz*dz>margin*margin)continue;
                    if(!cells.TryGetValue(new PopulationKey(ix,iz,layer),out CachedCell cell)||!cell.Published)return false;
                }
            }
            return true;
        }
        public bool BlocksBodyExcludingShrub(double x0,double y0,double z0,double x1,double y1,double z1)
        {
            GlobalQuery(x0,z0,0);GlobalQuery(x1,z1,0);
            if(x0>=x1||z0>=z1||y0>=y1||double.IsNaN(y0)||double.IsNaN(y1)||double.IsInfinity(y0)||double.IsInfinity(y1)||x1-x0>10||z1-z0>10||y1-y0>10)throw new ArgumentOutOfRangeException("body query");
            for(int layer=0;layer<3;layer++)
            {
                double size=layer==2?16:64,margin=layer==2?3:maximumRadius;
                long cx0=(long)Math.Floor((x0-margin)/size),cx1=(long)Math.Floor((x1+margin)/size),cz0=(long)Math.Floor((z0-margin)/size),cz1=(long)Math.Floor((z1+margin)/size);
                for(long cz=cz0;cz<=cz1;cz++)for(long cx=cx0;cx<=cx1;cx++)if(cells.TryGetValue(new PopulationKey(cx,cz,layer),out CachedCell cell))foreach(PopulationInstance instance in cell.Data.Instances)
                {
                    if(!models.TryGetValue(instance.PrototypeId,out Model model)||!model.Data.HasCollision)continue;
                    if(SkipShrubCurrent(cell.Data,instance))continue;
                    var p=model.Data;var root=SelectedRoot(cell.Data,instance,false);double radius=(root?.Cpu.Collision.Radius??p.Radius)*1.415;
                    if(x1<=instance.Position.X-radius||x0>=instance.Position.X+radius||z1<=instance.Position.Z-radius||z0>=instance.Position.Z+radius||y1<=instance.Position.Y+(root?.Cpu.Collision.MinY??p.MinY)||y0>=instance.Position.Y+p.MaxY)continue;
                    double angle=instance.YawDegrees*Math.PI/180,co=Math.Cos(angle),si=Math.Sin(angle);
                    double centerX=(x0+x1)*.5-instance.Position.X,centerZ=(z0+z1)*.5-instance.Position.Z,halfX=(x1-x0)*.5,halfZ=(z1-z0)*.5;
                    double localX=centerX*co-centerZ*si,localZ=centerX*si+centerZ*co,extentX=Math.Abs(co)*halfX+Math.Abs(si)*halfZ,extentZ=Math.Abs(si)*halfX+Math.Abs(co)*halfZ;
                    if(root!=null?root.Cpu.Collision.BlocksLocal(localX-extentX,y0-instance.Position.Y,localZ-extentZ,localX+extentX,y1-instance.Position.Y,localZ+extentZ):p.BlocksLocal(localX-extentX,y0-instance.Position.Y,localZ-extentZ,localX+extentX,y1-instance.Position.Y,localZ+extentZ))return true;
                }
            }
            return false;
        }
        public double WeightedSoftVoxels(WorldPoint feet,double radius,double height)
        {
            GlobalQuery(feet.X,feet.Z,radius,2);
            if(radius==0)return 0;
            if(double.IsNaN(feet.Y)||double.IsInfinity(feet.Y)||height<=0||height>5)throw new ArgumentOutOfRangeException("soft body query");
            double total=ShrubSoft(feet,radius,height);
            // Query only nearby existing tree/plant cells. Horizon graphics never
            // register here, and no query allocates or touches a Unity collider.
            for(int layer=0;layer<=2;layer+=2)
            {
                double size=layer==2?16:64,margin=radius+(layer==2?3:maximumRadius);
                long x0=(long)Math.Floor((feet.X-margin)/size),x1=(long)Math.Floor((feet.X+margin)/size),z0=(long)Math.Floor((feet.Z-margin)/size),z1=(long)Math.Floor((feet.Z+margin)/size);
                for(long z=z0;z<=z1;z++)for(long x=x0;x<=x1;x++)if(cells.TryGetValue(new PopulationKey(x,z,layer),out CachedCell cell))foreach(PopulationInstance instance in cell.Data.Instances)
                {
                    if(!models.TryGetValue(instance.PrototypeId,out Model model)||!model.Data.HasResistance)continue;
                    if(SkipShrubCurrent(cell.Data,instance))continue;
                    var p=model.Data;var root=SelectedRoot(cell.Data,instance,false);double dx=feet.X-instance.Position.X,dz=feet.Z-instance.Position.Z,y=feet.Y-instance.Position.Y;
                    if(Math.Abs(dx)>p.Radius*1.415+radius||Math.Abs(dz)>p.Radius*1.415+radius||y>=p.MaxY||y+height<=(root?.Cpu.Collision.MinY??p.MinY))continue;
                    double angle=instance.YawDegrees*Math.PI/180,co=Math.Cos(angle),si=Math.Sin(angle),extent=radius*(Math.Abs(co)+Math.Abs(si));
                    double lx=dx*co-dz*si,lz=dx*si+dz*co;
                    // Normalize the conservative rotated AABB area so yaw alone
                    // does not double resistance through homogeneous foliage.
                    total+=(root!=null?root.Cpu.Collision.ResistanceLocal(lx-extent,y,lz-extent,lx+extent,y+height,lz+extent):p.ResistanceLocal(lx-extent,y,lz-extent,lx+extent,y+height,lz+extent))*radius*radius/(extent*extent);
                }
            }
            return total;
        }
        public bool TryGetSupportHeight(double x,double z,double radius,double minY,double maxY,out double height)
        {
            GlobalQuery(x,z,radius);if(double.IsNaN(minY)||double.IsNaN(maxY)||double.IsInfinity(minY)||double.IsInfinity(maxY)||minY>maxY)throw new ArgumentOutOfRangeException("support heights");
            height=double.NegativeInfinity;
            for(int layer=0;layer<3;layer++)
            {
                double size=layer==2?16:64,margin=radius+(layer==2?3:maximumRadius);
                long x0=(long)Math.Floor((x-margin)/size),x1=(long)Math.Floor((x+margin)/size),z0=(long)Math.Floor((z-margin)/size),z1=(long)Math.Floor((z+margin)/size);
                for(long iz=z0;iz<=z1;iz++)for(long ix=x0;ix<=x1;ix++)if(cells.TryGetValue(new PopulationKey(ix,iz,layer),out CachedCell cell))foreach(PopulationInstance instance in cell.Data.Instances)
                {
                    if(!models.TryGetValue(instance.PrototypeId,out Model model)||!model.Data.HasCollision)continue;
                    if(SkipShrubCurrent(cell.Data,instance))continue;
                    var p=model.Data;var root=SelectedRoot(cell.Data,instance,false);if(Math.Abs(x-instance.Position.X)>p.Radius*1.415+radius||Math.Abs(z-instance.Position.Z)>p.Radius*1.415+radius||maxY<instance.Position.Y+(root?.Cpu.Collision.MinY??p.MinY)||minY>instance.Position.Y+p.MaxY)continue;
                    double angle=instance.YawDegrees*Math.PI/180,co=Math.Cos(angle),si=Math.Sin(angle),dx=x-instance.Position.X,dz=z-instance.Position.Z;
                    double local;bool supported=root!=null?root.Cpu.Collision.SupportLocal(dx*co-dz*si,dx*si+dz*co,radius,minY-instance.Position.Y,maxY-instance.Position.Y,out local):p.SupportLocal(dx*co-dz*si,dx*si+dz*co,radius,minY-instance.Position.Y,maxY-instance.Position.Y,out local);if(supported)height=Math.Max(height,instance.Position.Y+local);
                }
            }
            ShrubSupport(x,z,radius,minY,maxY,ref height);
            return !double.IsNegativeInfinity(height);
        }
        static void GlobalQuery(double x,double z,double radius,double maxRadius=5)
        {if(double.IsNaN(x)||double.IsInfinity(x)||double.IsNaN(z)||double.IsInfinity(z)||Math.Abs(x)>1e12||Math.Abs(z)>1e12||double.IsNaN(radius)||double.IsInfinity(radius)||radius<0||radius>maxRadius)throw new ArgumentOutOfRangeException("population query");}
        /// <summary>Invalidate newly activated fine-source regions only beyond the
        /// player safety guard. Keep complete old drawing until rebuilt pages publish;
        /// immutable near collision cells are never removed during movement.</summary>
        public void InvalidateRegion(double minimumX,double minimumZ,double maximumX,double maximumZ)
            =>InvalidateSourceRegion(minimumX,minimumZ,maximumX,maximumZ,false);
        // Only Bootstrap's verified airborne source-publication event uses this seam.
        // The field has already checked old AND new terrain +512m clearance.
        internal void InvalidateVerifiedAirborneSourceRegion(double minimumX,double minimumZ,double maximumX,double maximumZ)
        {
            if(HasReservedShrubMatter)throw new InvalidOperationException("Cannot replace source while shrub matter owns a cell.");
            InvalidateSourceRegion(minimumX,minimumZ,maximumX,maximumZ,true);
        }
        void InvalidateSourceRegion(double minimumX,double minimumZ,double maximumX,double maximumZ,bool includeProtected)
        {
            Horizon?.Invalidate();
            GlobalQuery(minimumX,minimumZ,0);GlobalQuery(maximumX,maximumZ,0);
            if(minimumX>=maximumX||minimumZ>=maximumZ)throw new ArgumentException("Population invalidation bounds must be positive.");
            remove.Clear();
            foreach(var pair in cells)
            {
                PopulationKey key=pair.Key;if(ShrubRetains(key))continue;double size=key.SizeMeters;
                if((key.X+1)*size<=minimumX||key.X*size>=maximumX||(key.Z+1)*size<=minimumZ||key.Z*size>=maximumZ||(!includeProtected&&Distance(key)<=512*512))continue;
                remove.Add(key);
            }
            foreach(PopulationKey key in remove)
            {
                ResidentInstances-=cells[key].Data.Instances.Length;cells.Remove(key);deferred.Remove(key);
                if(desired.Contains(key)&&queued.Add(key))queue.Add(key);
            }
            if(remove.Count>0)
            {
                queue.Sort((a,b)=>Distance(b).CompareTo(Distance(a)));matricesDirty=true;
                buildingMatrices=false;coverageReserve=0;finalizing=false;ReleaseNextBatches();finalizePages.Clear();ReleaseBuildingPages();matrixCells.Clear();
            }
        }
        void ReconfigureDensity()
        {
            // Quality edits must not create/delete a trunk inside the current body.
            // Keep the published near neighborhood stable until normal eviction;
            // far cells adopt the changed density immediately as they rebuild.
            remove.Clear();foreach(var pair in cells)if(Distance(pair.Key)>64*64&&!ShrubRetains(pair.Key))remove.Add(pair.Key);
            foreach(PopulationKey key in remove){ResidentInstances-=cells[key].Data.Instances.Length;cells.Remove(key);}
            desired.Clear();deferred.Clear();queue.Clear();queued.Clear();buildingMatrices=false;coverageReserve=0;finalizing=false;
            ReleaseNextBatches();finalizePages.Clear();ReleaseBuildingPages();matrixCells.Clear();matricesDirty=true;lastPlanX=lastPlanZ=lastMatrixX=double.PositiveInfinity;lastPlanAt=0;
        }
        public void RefreshPopulation()
        {
            ResetClipmapAnchors();CloseShrub();
            cells.Clear();desired.Clear();deferred.Clear();queue.Clear();queued.Clear();ReleaseBatches();ReleaseNextBatches();finalizePages.Clear();finalizing=false;ReleaseBuildingPages();matrixCells.Clear();ResidentInstances=0;BudgetOmittedSoftInstances=0;buildingMatrices=false;matricesDirty=true;lastPlanX=lastPlanZ=lastMatrixX=double.PositiveInfinity;lastPlanAt=0;
        }
        void ReleaseBatches(){foreach(Batch batch in batches)batchPool.Push(batch);batches.Clear();}
        void ReleaseNextBatches(){rootNext=null;foreach(Batch batch in nextBatches)batchPool.Push(batch);nextBatches.Clear();}
        void ReleaseBuildingPages(){foreach(Page page in buildingPages.Values){page.Matrices.Clear();page.HasBounds=false;pagePool.Push(page);}buildingPages.Clear();}
        public void DisposeRuntime()
        {
            ResetClipmapAnchors();CloseShrub();
            DisposeRoots();
            Horizon?.Dispose();Horizon=null;
            if(loading is IDisposable disposable)disposable.Dispose();loading=null;
            foreach(Model model in models.Values)foreach(Mesh mesh in model.Meshes)if(mesh!=null)Destroy(mesh);
            models.Clear();cells.Clear();desired.Clear();deferred.Clear();queue.Clear();queued.Clear();ReleaseBatches();ReleaseNextBatches();finalizePages.Clear();finalizing=false;ReleaseBuildingPages();matrixCells.Clear();batchPool.Clear();pagePool.Clear();
            if(material!=null)Destroy(material);material=null;population=null;surface=null;Ready=false;initialized=false;haveOrigin=false;ResidentInstances=0;modelMeshBytes=0;
            if(impostorMaterial!=null)Destroy(impostorMaterial);impostorMaterial=null;
            lastPlanX=lastPlanZ=lastMatrixX=double.PositiveInfinity;lastPlanAt=0;buildingMatrices=false;coverageReserve=0;
        }
        void OnDestroy()=>DisposeRuntime();
        static float Clamp(float value,float low,float high)=>float.IsNaN(value)||float.IsInfinity(value)?low:Math.Max(low,Math.Min(high,value));
    }
}
