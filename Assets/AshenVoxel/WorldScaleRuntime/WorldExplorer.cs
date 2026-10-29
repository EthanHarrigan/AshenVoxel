using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AshenVoxel.Core;
using AshenVoxel.Rendering;
using AshenVoxel.WorldScale;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AshenVoxel.WorldScaleRuntime
{
    /// <summary>Free-flight large-world research scene. Far terrain is a heightfield
    /// cache; P opens a separate finite editable 10cm inspection region.</summary>
    public sealed partial class WorldExplorer : MonoBehaviour
    {
        public TextAsset BakedWorldAtlas;
        [Tooltip("Optional checked climate archive paired with the height atlas. Empty uses explicit source-biome climate heuristics.")]
        public TextAsset BakedClimateAtlas;
        public int Seed = 714103;
        public bool UseImportedDimensions = true;
        [Tooltip("Width of the rough/baked preview. Live model scale is configured in the immutable service job.")]
        [Range(10, 20000)] public float LandmassWidthKm = 100;
        [Tooltip("Zero preserves the imported atlas aspect ratio; a positive value explicitly stretches its depth.")]
        [Range(0, 1000)] public float LandmassDepthKm;
        [Range(1000, 12000)] public float MountainHeightMeters = 6500;
        [Range(.1f, 4)] public float ImportedVerticalScale = 1;
        public bool ForestBiomeOverride = true;
        [Header("Surface generation")]
        public bool EnableSurfaceSnow = true;
        [Tooltip("Snowline reference for biome-derived fallback climate. Actual model climate uses its physical temperature instead.")]
        [Range(-1000, 20000)] public float SnowlineMeters = 4200;
        [Range(0, 8)] public float MaximumSnowDepthMeters = 2.4f;
        [Range(0, 2)] public float SurfaceWindStrength = .7f;
        [Range(0, 360)] public float SurfaceWindAzimuthDegrees = 36.8699f;
        [Tooltip("Average climatic sun exposure direction; this does not move the scene light.")]
        [Range(0, 360)] public float SurfaceSolarAzimuthDegrees = 287.354f;
        [Range(-1, 1)] public float WinterFraction = .2f;
        [Tooltip("Temperature bias for both actual model climate and the biome fallback.")]
        [Range(-60, 60)] public float TemperatureBiasC;
        [Range(-12000, 12000)] public float SeaLevelMeters;
        [Tooltip("Inspection tool strength; rock hardness still determines excavation.")]
        [Range(.1f, 20)] public float InspectionToolPower = 10;
        public bool ShowVisualWater = true;
        [Header("Local ground cover")]
        [Tooltip("Generate actual10cm plants and weathered rocks inside the finite P inspection region. Distant flora streaming is separate work.")]
        public bool GenerateGroundCover = true;
        [Range(0, 2)] public float GroundCoverDensity = 1;
        [Range(1000, 200000)] public int GroundCoverCellBudget = 60000;
        public GroundCoverBuildResult LastGroundCover { get; private set; }
        public bool UseRemoteDiffusion;
        public string DiffusionEndpoint = "http://127.0.0.1:8787";
        [Range(256, 4096)] public float NeuralRefinementTileLimitMeters = 2048;
        [Range(4, 100)] public float ViewDistanceKm = 32;
        [Range(64, LodSettings.MaximumSupportedTiles)] public int MaximumTiles = 384;
        public const int StartupCoverageMaximumTiles=128;
        public bool StartupCoveragePending=>ExternalNavigation&&UseVoxelLodTerrain&&coarseStartup;
        public int CurrentPlanTileLimit=>StartupCoveragePending?Math.Min(StartupCoverageMaximumTiles,Mathf.Clamp(MaximumTiles,64,LodSettings.MaximumSupportedTiles)):Mathf.Clamp(MaximumTiles,64,LodSettings.MaximumSupportedTiles);
        [Range(1, 8)] public int MeshBuildsPerFrame = 2;
        [Range(8, 64)] public int TileQuads = 32;
        [Range(10, 2000)] public float FlightSpeedMetersPerSecond = 80;
        [Header("Exploration detail and work budgets")]
        [Range(16, 128)] public float MinimumTileSizeMeters = 64;
        [Range(8, 64)] public int NearTileQuads = 64;
        [Range(32, 256)] public float NearDetailRadiusMeters = 96;
        [Range(0, 100)] public float FarHorizonDistanceKm;
        [Range(.25f, 8)] public float MeshBuildMilliseconds = 2;
        [Range(32, 2048)] public int MeshWorkUnitsPerFrame = 256;
        [Range(.25f, 5)] public float PlanIntervalSeconds = 1;
        [Range(4, 256)] public float ReplanMovementMeters = 16;
        public bool ExternalNavigation;
        [Tooltip("External immutable worlds plan terrain on one background worker; no Unity objects are created there.")]
        public bool AsyncPlanning = true;
        public bool ShowResearchOverlay = true;
        public Material TerrainMaterial;
        public string Status { get; private set; } = "Press Play to explore.";
        public WorldPoint GlobalCamera => new WorldPoint(cameraX, cameraY, cameraZ);
        public int ResidentTiles => surfaces.Count;
        public int PendingTiles => pending.Count + contactPending.Count + contactDeferred.Count + UnqueuedVoxelRefinementTiles + (currentBuild != null ? 1 : 0) + (planJob != null ? 1 : 0);
        public bool InspectingVoxels => inspection;
        public const float VoxelSize = .1f;

        sealed class Surface
        {
            public GameObject Object;
            public Mesh Mesh;
            public Renderer Renderer;
            public LodTile Tile;
            public double AnchorY;
            public bool NeuralReady;
            public int Quads;
            public float[] LocalHeights;
            public bool VoxelColumns;
            public bool HeightProxy;
            public long MeshBytes;
            public int TriangleCount;
            public int SourceRevision;
        }
        readonly struct Work
        {
            public readonly LodTile Tile;
            public readonly int Revision;
            public readonly bool Replace;
            public readonly bool Contact;
            public readonly int Quads;
            public readonly bool Progressive;
            public Work(LodTile tile, int revision, bool replace = false, bool contact = false, int quads = 0, bool progressive = false)
            { Tile = tile; Revision = revision; Replace = replace; Contact=contact; Quads=quads; Progressive=progressive; }
        }
        readonly Dictionary<TileKey, Surface> surfaces = new Dictionary<TileKey, Surface>();
        readonly Queue<Work> pending = new Queue<Work>();
        int consecutiveCoverageBuilds;
        bool TerrainWorkWanted(Work work)=>desired.Contains(work.Tile.Key)||(work.Progressive&&active.Contains(work.Tile.Key));
        readonly HashSet<TileKey> active = new HashSet<TileKey>();
        readonly HashSet<TileKey> desired = new HashSet<TileKey>();
        readonly Dictionary<TileKey, LodTile> desiredTiles = new Dictionary<TileKey, LodTile>();
        readonly HashSet<TileKey> refining = new HashSet<TileKey>();
        readonly List<TileKey> remove = new List<TileKey>();
        IWorldField field;
        SurfaceProfile surfaceProfile;
        VisualWater visualWater;
        int waterRemoteRevision = -1;
        Camera view;
        Material ownedMaterial;
        Material materialTemplate;
        Material EffectiveMaterial => ownedMaterial != null ? ownedMaterial : TerrainMaterial;
        double cameraX, cameraY, cameraZ, originX, originY, originZ;
        float yaw = 20, pitch = 14, nextPlan;
        int revision;
        GUIStyle label;
        bool inspection;
        VoxelWorld patch;
        ChunkWorldRenderer patchSurfaces;
        GameObject patchObject;
        double patchX, patchY, patchZ;
        string sourceName;
        double actualMapWidth;
        int limitedSplits;
        Coroutine inspectionBuild;
        bool buildingInspection;
        RemoteTerrainField remote;
        Task remoteInitialization;
        readonly List<RemoteWork> remoteWork = new List<RemoteWork>();
        readonly Dictionary<TileKey, float> remoteRetry = new Dictionary<TileKey, float>();
        string remoteStatus = "";
        Task inspectionPrefetch;
        IWorldField inspectionField;
        SurfaceMaterialSampler inspectionSampler;
        sealed class RemoteWork
        {
            public LodTile Tile;
            public Task Task;
            public int Revision;
        }

        void Start() { if (!externalInitialized) Regenerate(); }
        public void Regenerate()
        {
            if (!Application.isPlaying) return;
            revision++;
            ClearTerrain(); CloseInspection();
            if (visualWater != null) visualWater.SetFrame(null, GlobalCamera, new WorldPoint(originX, originY, originZ), SeaLevelMeters,
                Math.Max(100, ViewDistanceKm * 1000.0), view);
            if (remote != null) remote.Dispose();
            remote = null; remoteInitialization = null; remoteWork.Clear(); remoteRetry.Clear(); remoteStatus = ""; inspectionPrefetch = null;
            try
            {
                if (externalSource != null)
                {
                    field = new SurfaceWorldField(externalSource, externalProfile ?? CreateSurfaceProfile(),
                        new HeightBounds(RemoteTerrainField.MinimumSupportedHeight, RemoteTerrainField.MaximumSupportedHeight));
                    surfaceProfile = ((SurfaceWorldField)field).Profile;
                    sourceName = externalSource.SourceIdentity;
                    EnsureCameraAndMaterial(); Rebase(true); nextPlan = 0; RequestPlan();
                    externalInitialized = true;
                    Status = "Building the exploration view…";
                    return;
                }
                actualMapWidth = LandmassWidthKm * 1000.0;
                if (BakedWorldAtlas != null)
                {
                    using (var input = new MemoryStream(BakedWorldAtlas.bytes, false))
                    {
                        HeightAtlas atlas = AtlasIO.Read(input);
                        if (UseRemoteDiffusion) ValidateLiveAtlas(atlas, UseImportedDimensions, ImportedVerticalScale);
                        actualMapWidth = UseImportedDimensions ? atlas.ExtentX : LandmassWidthKm * 1000.0;
                        double depth = UseImportedDimensions ? atlas.ExtentZ : LandmassDepthKm > 0 ? LandmassDepthKm * 1000.0 : actualMapWidth * atlas.ExtentZ / atlas.ExtentX;
                        var map = new AtlasWorldField(atlas, new WorldMapSettings(
                            actualMapWidth, depth, ImportedVerticalScale, ForestBiomeOverride));
                        field = map;
                        if (BakedClimateAtlas != null)
                            using (var climateInput = new MemoryStream(BakedClimateAtlas.bytes, false))
                                field = new ClimateAtlasField(map, ClimateArchiveIO.Read(climateInput));
                    }
                    sourceName = "Baked atlas: " + BakedWorldAtlas.name;
                }
                else
                {
                    field = new ProceduralMountainField(Seed, MountainHeightMeters,
                        LandmassWidthKm * 1000.0, ForestBiomeOverride);
                    sourceName = "Procedural fallback (not diffusion inference)";
                }
                if (UseRemoteDiffusion)
                {
                    if (!UseImportedDimensions || Math.Abs(ImportedVerticalScale - 1) > 1e-7)
                        throw new InvalidOperationException("Live model dimensions and vertical scale belong to the service job. Keep native imported dimensions and vertical scale1, or prepare a different job.");
                    remote = new RemoteTerrainField(field, DiffusionEndpoint, ForestBiomeOverride);
                    remoteInitialization = remote.InitializeAsync();
                    field = remote;
                    sourceName = "Remote diffusion + provisional rough preview; live scale is set by service job";
                    remoteStatus = "Connecting model service…";
                }
                surfaceProfile = CreateSurfaceProfile();
                field = new SurfaceWorldField(field, surfaceProfile,
                    new HeightBounds(RemoteTerrainField.MinimumSupportedHeight, RemoteTerrainField.MaximumSupportedHeight));
                EnsureCameraAndMaterial();
                // Deterministically find an observation point, rather than spawning
                // at sea level inside a multi-kilometer mountain.
                double best = double.MinValue, span = actualMapWidth;
                for (int z = -4; z <= 4; z++) for (int x = -4; x <= 4; x++)
                {
                    double px = x * span / 12, pz = z * span / 12;
                    double h = field.Sample(px, pz).HeightMeters;
                    if (h > best) { best = h; cameraX = px; cameraZ = pz; }
                }
                cameraY = best + 240;
                Rebase(true); nextPlan = 0; RequestPlan();
                Status = "Building the observation view…";
            }
            catch (Exception exception)
            {
                field = null; Status = "World source failed: " + exception.Message;
                Debug.LogException(exception, this);
            }
        }
        SurfaceProfile CreateSurfaceProfile() => new SurfaceProfile(seed: Seed,
            seaLevelMeters: SeaLevelMeters, snowlineMeters: SnowlineMeters,
            maxSnowDepthMeters: MaximumSnowDepthMeters, enableSnow: EnableSurfaceSnow,
            windStrength: SurfaceWindStrength, winterFraction: WinterFraction, temperatureBiasC: TemperatureBiasC,
            windAzimuthDegrees: SurfaceWindAzimuthDegrees, solarAzimuthDegrees: SurfaceSolarAzimuthDegrees);
        SurfaceProfile CurrentSurfaceProfile => surfaceProfile ?? (surfaceProfile = CreateSurfaceProfile());
        static IWorldField BareField(IWorldField source) => source is SurfaceWorldField surface ? surface.BaseField : source;
        IWorldField SurfaceField(IWorldField source) => source is SurfaceWorldField ? source :
            new SurfaceWorldField(source, CurrentSurfaceProfile,
                new HeightBounds(RemoteTerrainField.MinimumSupportedHeight, RemoteTerrainField.MaximumSupportedHeight));
        void EnsureCameraAndMaterial()
        {
            if (view == null)
            {
                var cameraObject = new GameObject("World explorer camera");
                cameraObject.transform.SetParent(transform, false);
                view = cameraObject.AddComponent<Camera>(); view.tag = "MainCamera";
                view.clearFlags = CameraClearFlags.SolidColor;
                view.backgroundColor = new Color(.24f, .31f, .37f);
                view.nearClipPlane = .3f; view.fieldOfView = 62;
            }
            view.farClipPlane = (float)Math.Max(10000, EffectiveViewDistanceMeters * 1.8);
            Material template = TerrainMaterial;
            if (template == null)
            {
                template = Resources.Load<Material>("GroundSurface");
                if (template == null) template = Resources.Load<Material>("VoxelSurface");
                if (template == null) throw new InvalidOperationException("VoxelSurface material is missing.");
            }
            // Never write origin phases or runtime overrides into the inspector's
            // shared asset. Every explorer owns its own mutable material copy.
            if (ownedMaterial == null || !object.ReferenceEquals(materialTemplate, template))
            {
                if (ownedMaterial != null) Destroy(ownedMaterial);
                ownedMaterial = new Material(template); materialTemplate = template; ownedMaterial.name = "World proxy surface";
                ownedMaterial.SetFloat("_GrainStrength", 0);
                ownedMaterial.SetFloat("_FineGrainStrength", 0);
            }
            if (visualWater == null) visualWater = gameObject.AddComponent<VisualWater>();
        }
        void RequestPlan()
        {
            if (field == null || planJob != null) return;
            double baseSize = UseVoxelLodTerrain ? VoxelLodSurface.BaseTileMeters : Mathf.Clamp(MinimumTileSizeMeters, 16, 128);
            double split=UseVoxelLodTerrain?Mathf.Clamp(VoxelPlanSplitFactor,1,LodSettings.MaximumSplitFactor):2.2;
            // First earn complete whole-view coverage with a small plan. Keep the
            // requested distance and stored quality untouched; activation already
            // forces the next plan and retains this coverage during replacement.
            if(StartupCoveragePending)split=Math.Min(split,2.2);
            var settings = new LodSettings(baseTileSizeMeters: baseSize, maxLevel: 14,
                farDistanceMeters: EffectiveViewDistanceMeters, splitFactor: split,
                maxTiles: CurrentPlanTileLimit,
                minimumHeightMeters: RemoteTerrainField.MinimumSupportedHeight,
                maximumHeightMeters: RemoteTerrainField.MaximumSupportedHeight + CurrentSurfaceProfile.MaxSnowDepthMeters);
            IWorldField planSource = CaptureField(SurfaceField(field)); WorldPoint observer = GlobalCamera;
            plannedObserver = observer; planForced = false;
            plannedForward = view == null ? Vector3.forward : view.transform.forward;
            nextPlan = Time.unscaledTime + Mathf.Clamp(PlanIntervalSeconds, .25f, 5);
            if (ExternalNavigation && AsyncPlanning)
            {
                planJobRevision = revision; planJobBaseSize = baseSize; PlannerJobsStarted++;
                planJob = Task.Run(() => BuildCapturedPlan(planSource, observer, settings, true));
                Status = "Planning terrain coverage…";
                return;
            }
            ApplyPlan(BuildCapturedPlan(planSource, observer, settings, ExternalNavigation), baseSize);
        }
        void ApplyPlan(LodPlan plan, double baseSize)
        {
            // Preserve a still-useful immutable build across a view/topology plan.
            // The desired set is rebuilt below before deciding whether to cancel.
            bool preserveQueues=preserveTerrainQueuesOnPlan;preserveTerrainQueuesOnPlan=false;
            Work[] retained=preserveQueues?pending.ToArray():Array.Empty<Work>();
            if(!preserveQueues&&!UseVoxelLodTerrain&&(currentBuild==null||!currentBuild.Work.Contact))CancelBuild();
            if (!double.IsNaN(plannedBaseTileSize) && baseSize != plannedBaseTileSize)
            {
                // TileKey level coordinates depend on base size. Reusing them
                // after this setting changes would place old geometry at new keys.
                revision++; ClearTerrain(); coarseStartup = ExternalNavigation;
            }
            plannedBaseTileSize = baseSize;
            voxelPlanBoundsRevision = revision;
            limitedSplits = plan.PendingSplits;
            desired.Clear(); desiredTiles.Clear(); pending.Clear();if(!preserveQueues)refining.Clear();
            var sorted = new List<LodTile>(plan.Tiles);
            sorted.Sort((a, b) => WorkPriority(a).CompareTo(WorkPriority(b)));
            foreach (LodTile tile in sorted) { desired.Add(tile.Key); desiredTiles.Add(tile.Key, tile); }
            var queued=new HashSet<TileKey>();
            if(preserveQueues)
            {
                refining.RemoveWhere(key=>!desired.Contains(key)&&!active.Contains(key));
                foreach(Work work in retained)
                {
                    if(work.Revision!=revision||!TerrainWorkWanted(work)||queued.Contains(work.Tile.Key))continue;
                    if(currentBuild!=null&&!currentBuild.Work.Contact&&currentBuild.Work.Tile.Key.Equals(work.Tile.Key))continue;
                    LodTile tile=desiredTiles.TryGetValue(work.Tile.Key,out LodTile updated)?updated:work.Tile;
                    pending.Enqueue(new Work(tile,work.Revision,work.Replace,work.Contact,work.Quads,work.Progressive));queued.Add(tile.Key);
                }
            }
            RetireCoverageOutsideDemand();
            PrepareCoverageGroups();
            // Cache residency is at most active + desired: two bounded plans.
            remove.Clear();
            foreach (var item in surfaces)
                if (!active.Contains(item.Key) && !desired.Contains(item.Key)) remove.Add(item.Key);
            foreach (TileKey key in remove) Release(key);
            remove.Clear();foreach(var key in voxelCapacityQuads.Keys)if(!active.Contains(key)&&!desired.Contains(key))remove.Add(key);
            foreach(var key in remove)voxelCapacityQuads.Remove(key);
            remove.Clear();
            foreach (TileKey key in externallyDirty) if (!active.Contains(key) && !desired.Contains(key)) remove.Add(key);
            foreach (TileKey key in remove) externallyDirty.Remove(key);
            remove.Clear();
            foreach (TileKey key in remoteRetry.Keys) if (!desired.Contains(key)) remove.Add(key);
            foreach (TileKey key in remove) remoteRetry.Remove(key);
            if(currentBuild!=null&&!currentBuild.Work.Contact)
            {
                Work work=currentBuild.Work;
                if(work.Revision!=revision||!TerrainWorkWanted(work)||
                    (UseVoxelLodTerrain&&work.Progressive&&work.Quads>VoxelQuadsFor(work.Tile)))CancelBuild();
            }
            if(!preserveQueues)ResetVoxelRefinementAdmission();else voxelRefinementScanPending=true;
            foreach (LodTile tile in sorted)
            {
                if(currentBuild!=null&&!currentBuild.Work.Contact&&currentBuild.Work.Tile.Key.Equals(tile.Key))continue;
                if(queued.Contains(tile.Key))continue;
                surfaces.TryGetValue(tile.Key,out Surface existing);
                if(UseVoxelLodTerrain)QueueVoxelCoverage(tile,existing);
                else if(existing==null||existing.Quads!=QuadsFor(tile)||externallyDirty.Contains(tile.Key))
                    pending.Enqueue(new Work(tile,revision,existing!=null));
            }
            planActivationPending = true;
            if(UseVoxelLodTerrain||pending.Count==0)ActivateReadyPlan();
            RecordVoxelStatistics();
        }
        double DistanceSquared(LodTile tile)
        {
            double dx = Math.Max(tile.OriginXMeters - cameraX, Math.Max(0, cameraX - tile.OriginXMeters - tile.SizeMeters));
            double dz = Math.Max(tile.OriginZMeters - cameraZ, Math.Max(0, cameraZ - tile.OriginZMeters - tile.SizeMeters));
            double dy = Math.Max(tile.MinimumHeight - cameraY, Math.Max(0, cameraY - tile.MaximumHeight));
            return dx * dx + dy * dy + dz * dz;
        }
        const double SurfaceHalo = SurfaceProfile.SampleRadiusMax;
        bool TileReady(LodTile tile) => remote != null && remote.IsReady(tile.OriginXMeters - SurfaceHalo, tile.OriginZMeters - SurfaceHalo,
            tile.OriginXMeters + tile.SizeMeters + SurfaceHalo, tile.OriginZMeters + tile.SizeMeters + SurfaceHalo);
        void QueueRefinement(LodTile tile)
        {
            if (surfaces.TryGetValue(tile.Key, out Surface surface) && !surface.NeuralReady && refining.Add(tile.Key))
                pending.Enqueue(new Work(tile, revision, true));
        }
        void ProcessRemote()
        {
            if (remote == null) return;
            if (remoteInitialization != null && remoteInitialization.IsCompleted)
            {
                remoteStatus = remoteInitialization.IsFaulted ? "Model unavailable; rough preview only: " + remoteInitialization.Exception.GetBaseException().Message :
                    remoteInitialization.IsCanceled ? "Model connection canceled; rough preview only" : "Model service ready";
                remoteInitialization = null;
            }
            if (!remote.Connected)
            {
                if (!string.IsNullOrEmpty(remote.TerminalFailure)) remoteStatus = "Model stopped; rough preview only: " + remote.TerminalFailure;
                return;
            }
            string issue = "";
            for (int i = remoteWork.Count - 1; i >= 0; i--)
            {
                RemoteWork work = remoteWork[i];
                if (!work.Task.IsCompleted) continue;
                remoteWork.RemoveAt(i);
                if (work.Revision != revision) continue;
                if (work.Task.IsFaulted || work.Task.IsCanceled)
                {
                    remoteRetry[work.Tile.Key] = Time.unscaledTime + 10;
                    issue = " · inference retry delayed";
                }
                else if (desired.Contains(work.Tile.Key)) QueueRefinement(work.Tile);
            }
            if (inspectionPrefetch != null && inspectionPrefetch.IsCompleted)
            {
                if (inspectionPrefetch.IsFaulted) issue = " · inspection fetch failed: " + inspectionPrefetch.Exception.GetBaseException().Message;
                else if (!inspectionPrefetch.IsCanceled) Status = "Model inspection region ready; press P to inspect";
                inspectionPrefetch = null;
            }
            if (!inspection)
            {
                foreach (LodTile tile in desiredTiles.Values)
                {
                    if (tile.SizeMeters > Mathf.Clamp(NeuralRefinementTileLimitMeters, 256, 4096)) continue;
                    if (surfaces.TryGetValue(tile.Key, out Surface surface) && surface.NeuralReady) continue;
                    if (TileReady(tile)) { QueueRefinement(tile); continue; }
                    if (remoteWork.Count >= 2) break;
                    bool waiting = false;
                    foreach (RemoteWork work in remoteWork) if (work.Tile.Key.Equals(tile.Key)) waiting = true;
                    if (waiting || (remoteRetry.TryGetValue(tile.Key, out float retryAt) && Time.unscaledTime < retryAt)) continue;
                    try
                    {
                        Task task = remote.PrefetchAsync(tile.OriginXMeters - SurfaceHalo, tile.OriginZMeters - SurfaceHalo,
                            tile.OriginXMeters + tile.SizeMeters + SurfaceHalo, tile.OriginZMeters + tile.SizeMeters + SurfaceHalo);
                        remoteWork.Add(new RemoteWork { Tile = tile, Task = task, Revision = revision });
                    }
                    catch (Exception exception)
                    { remoteRetry[tile.Key] = Time.unscaledTime + 10; issue = " · model request deferred: " + exception.Message; }
                }
            }
            int neural = 0, rough = 0;
            foreach (TileKey key in active)
                if (surfaces.TryGetValue(key, out Surface surface)) { if (surface.NeuralReady) neural++; else rough++; }
            remoteStatus = "Model " + remote.Status.world_id.Substring(0, 8) + " · neural tiles " + neural + " / provisional rough " + rough +
                " · requests " + remoteWork.Count + " · regions " + remote.ResidentRegions + issue;
        }
        void ActivateReadyPlan()
        {
            if (!planActivationPending) return;
            foreach (TileKey key in desired) if (!surfaces.ContainsKey(key))
            {PublishReadyCoverageGroups();return;}
            active.Clear(); active.UnionWith(desired); planActivationPending = false;
            coverageGroups.Clear();coveragePublishedCount=0;
            long previousDetailBytes=voxelDetailMeshBytes;
            remove.Clear();
            foreach (var item in surfaces)
            {
                item.Value.Object.SetActive(!inspection && active.Contains(item.Key));
                if(active.Contains(item.Key))TerrainSeamChanged(item.Value);else TerrainSeamRemoved(item.Value);
                if (!active.Contains(item.Key)) remove.Add(item.Key);
            }
            foreach (TileKey key in remove) Release(key);
            WakeAdmissibleVoxelRefinements(previousDetailBytes);
            RecordVoxelStatistics();
            Status = "Ready · " + active.Count + (UseVoxelLodTerrain ? " voxel LOD tiles" : " heightfield tiles") + (limitedSplits > 0 ? " · detail capacity reached" : "");
            if (coarseStartup)
            {
                coarseStartup = false; planForced = true; nextPlan = 0;
                Status = "World visible · refining nearby terrain…";
            }
        }
        sealed class CoverageGroup
        {
            public TileKey Root;public readonly List<TileKey> Old=new List<TileKey>(),Next=new List<TileKey>();
            public bool Exact=true,Published;public int Checked;
        }
        readonly List<CoverageGroup> coverageGroups=new List<CoverageGroup>();
        int coverageGroupCursor,coveragePublishedCount,coverageCheckRevision=-1;
        double coverageBudgetTime=double.NaN;
        int coverageChecksLeft,coveragePublishesLeft;
        public int CoverageGroupsPublished {get;private set;}
        public int ActiveTerrainTiles=>active.Count;
        public bool TerrainPlanCoverageComplete=>active.Count>0&&!planActivationPending;
        public int PendingCoverageGroups=>coverageGroups.Count-coveragePublishedCount;
        public int LastCoveragePublicationChecks {get;private set;}
        public int LastCoverageGroupPublications {get;private set;}
        static bool TryCoverageAncestor(TileKey key,ISet<TileKey> set,out TileKey ancestor)
        {
            for(int level=key.Level;level<=24;level++)
            {
                int divisor=1<<(level-key.Level);
                ancestor=new TileKey(WorldCell.FloorDivide(key.X,divisor),WorldCell.FloorDivide(key.Z,divisor),level);
                if(set.Contains(ancestor))return true;
            }
            ancestor=default;return false;
        }
        static int CoverageLeader(List<int> parents,int index)
        {
            while(parents[index]!=index){parents[index]=parents[parents[index]];index=parents[index];}return index;
        }
        void PrepareCoverageGroups()
        {
            coverageGroups.Clear();coverageGroupCursor=coveragePublishedCount=0;coverageCheckRevision=revision;
            if(!UseVoxelLodTerrain||coarseStartup)return;
            var raw=new List<CoverageGroup>();var roots=new Dictionary<TileKey,int>();
            var oldGroup=new Dictionary<TileKey,int>();var nextGroup=new Dictionary<TileKey,int>();
            int Group(TileKey root)
            {
                if(roots.TryGetValue(root,out int index))return index;
                index=raw.Count;roots.Add(root,index);raw.Add(new CoverageGroup{Root=root});return index;
            }
            // Both plans are disjoint dyadic leaves: intersecting footprints are
            // nested. Refinement groups belong to an old ancestor; coarsening
            // groups to a new ancestor. No floating coordinate tolerances.
            foreach(TileKey key in desired)
            {
                if(active.Contains(key))continue;
                int index=Group(TryCoverageAncestor(key,active,out TileKey ancestor)?ancestor:key);
                raw[index].Next.Add(key);nextGroup.Add(key,index);
            }
            foreach(TileKey key in active)
            {
                if(desired.Contains(key))continue;int index;
                if(TryCoverageAncestor(key,desired,out TileKey ancestor))index=Group(ancestor);
                else if(!roots.TryGetValue(key,out index))continue;
                raw[index].Old.Add(key);oldGroup.Add(key,index);
            }
            var parents=new List<int>(raw.Count);
            for(int i=0;i<raw.Count;i++)
            {
                parents.Add(i);long before=0,after=0,area=1L<<(raw[i].Root.Level*2);
                foreach(var key in raw[i].Old)before+=1L<<(key.Level*2);
                foreach(var key in raw[i].Next)after+=1L<<(key.Level*2);
                raw[i].Exact=after==area&&(before==area||raw[i].Old.Count==0);
            }
            void Join(int a,int b)
            {a=CoverageLeader(parents,a);b=CoverageLeader(parents,b);if(a!=b)parents[b]=a;}
            // A mixed active plan must retain the planner's2:1 shared-edge rule.
            // Merge the dependent neighboring swaps into one atomic closure.
            foreach(var key in desired)
            {
                if(!nextGroup.TryGetValue(key,out int next))continue;
                for(int axis=0;axis<2;axis++)for(int sign=-1;sign<=1;sign+=2)
                {
                    var across=new TileKey(key.X+(axis==0?sign:0),key.Z+(axis==1?sign:0),key.Level);
                    if(!TryCoverageAncestor(across,active,out TileKey old)||old.Level<=key.Level+1||!surfaces.TryGetValue(old,out Surface surface)||!LodPlanner.Adjacent(desiredTiles[key],surface.Tile))continue;
                    if(oldGroup.TryGetValue(old,out int previous))Join(next,previous);else raw[next].Exact=false;
                }
            }
            foreach(var key in active)
            {
                if(!oldGroup.TryGetValue(key,out int old)||!surfaces.TryGetValue(key,out Surface surface))continue;
                for(int axis=0;axis<2;axis++)for(int sign=-1;sign<=1;sign+=2)
                {
                    var across=new TileKey(key.X+(axis==0?sign:0),key.Z+(axis==1?sign:0),key.Level);
                    if(!TryCoverageAncestor(across,desired,out TileKey next)||next.Level<=key.Level+1||!LodPlanner.Adjacent(surface.Tile,desiredTiles[next]))continue;
                    if(nextGroup.TryGetValue(next,out int replacement))Join(old,replacement);else raw[old].Exact=false;
                }
            }
            var merged=new Dictionary<int,CoverageGroup>();
            for(int i=0;i<raw.Count;i++)
            {
                int leader=CoverageLeader(parents,i);
                if(!merged.TryGetValue(leader,out CoverageGroup group)){group=new CoverageGroup();merged.Add(leader,group);}
                group.Exact&=raw[i].Exact;group.Old.AddRange(raw[i].Old);group.Next.AddRange(raw[i].Next);
            }
            foreach(var group in merged.Values)if(group.Exact&&group.Next.Count>0)coverageGroups.Add(group);
        }
        void PublishReadyCoverageGroups()
        {
            if(!UseVoxelLodTerrain||coarseStartup||coverageGroups.Count==0)return;
            double now=Time.unscaledTimeAsDouble;
            if(coverageBudgetTime!=now)
            {coverageBudgetTime=now;coverageChecksLeft=512;coveragePublishesLeft=4;LastCoveragePublicationChecks=LastCoverageGroupPublications=0;}
            if(coverageCheckRevision!=revision)
            {foreach(var group in coverageGroups)group.Checked=0;coverageCheckRevision=revision;}
            int visits=0;long previousBytes=voxelDetailMeshBytes;
            while(coverageChecksLeft>0&&coveragePublishesLeft>0&&visits++<coverageGroups.Count)
            {
                if(coverageGroupCursor>=coverageGroups.Count)coverageGroupCursor=0;
                CoverageGroup group=coverageGroups[coverageGroupCursor++];if(group.Published)continue;
                bool missing=false;
                while(group.Checked<group.Next.Count&&coverageChecksLeft>0)
                {
                    TileKey key=group.Next[group.Checked];coverageChecksLeft--;LastCoveragePublicationChecks++;
                    if(!surfaces.ContainsKey(key)||externallyDirty.Contains(key)){group.Checked=0;missing=true;break;}group.Checked++;
                }
                if(missing||group.Checked<group.Next.Count)continue;
                int nextCount=active.Count-group.Old.Count+group.Next.Count;
                if(nextCount>Mathf.Clamp(MaximumTiles,64,LodSettings.MaximumSupportedTiles)&&nextCount>active.Count)continue;
                // Main-thread atomic cutover: disable every prior footprint before
                // enabling its exact, balanced replacement. Never layer both plans.
                foreach(TileKey key in group.Old)
                {
                    active.Remove(key);
                    if(surfaces.TryGetValue(key,out Surface old)){old.Object.SetActive(false);TerrainSeamRemoved(old);}
                    if(!desired.Contains(key))Release(key);
                }
                foreach(TileKey key in group.Next){active.Add(key);surfaces[key].Object.SetActive(!inspection);TerrainSeamChanged(surfaces[key]);}
                group.Published=true;coveragePublishedCount++;CoverageGroupsPublished++;coveragePublishesLeft--;LastCoverageGroupPublications++;
            }
            if(LastCoverageGroupPublications>0){WakeAdmissibleVoxelRefinements(previousBytes);voxelRefinementScanPending=true;RecordVoxelStatistics();}
        }
        void RetireCoverageOutsideDemand()
        {
            if(!UseVoxelLodTerrain||coarseStartup||active.Count==0)return;
            var ancestors=new HashSet<TileKey>();
            foreach(TileKey key in desired)
                for(int level=key.Level;level<=24;level++)
                {int divisor=1<<(level-key.Level);ancestors.Add(new TileKey(WorldCell.FloorDivide(key.X,divisor),WorldCell.FloorDivide(key.Z,divisor),level));}
            remove.Clear();foreach(TileKey key in active)
                if(!ancestors.Contains(key)&&!TryCoverageAncestor(key,desired,out _))remove.Add(key);
            foreach(TileKey key in remove)
            {active.Remove(key);if(surfaces.TryGetValue(key,out Surface old))old.Object.SetActive(false);Release(key);}
        }
        void Update()
        {
            if (field == null || view == null) return;
            ProcessRemote(); PollPlan();
            var keyboard = Keyboard.current; var mouse = Mouse.current;
            if (!ExternalNavigation && keyboard != null && keyboard.rKey.wasPressedThisFrame) { Regenerate(); return; }
            if (!ExternalNavigation && keyboard != null && keyboard.pKey.wasPressedThisFrame)
            { if (inspection) CloseInspection(); else OpenInspection(); }
            if (!ExternalNavigation && mouse != null && mouse.rightButton.isPressed)
            {
                Vector2 delta = mouse.delta.ReadValue(); yaw += delta.x * .12f;
                pitch = Mathf.Clamp(pitch - delta.y * .12f, -85, 85);
            }
            Quaternion rotation = ExternalNavigation ? externalRotation : Quaternion.Euler(pitch, yaw, 0);
            if (!ExternalNavigation && keyboard != null)
            {
                Vector3 local = new Vector3((keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0),
                    (keyboard.eKey.isPressed || keyboard.spaceKey.isPressed ? 1 : 0) - (keyboard.qKey.isPressed || keyboard.leftCtrlKey.isPressed ? 1 : 0),
                    (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0));
                float speed = inspection ? 4 : FlightSpeedMetersPerSecond;
                if (keyboard.leftShiftKey.isPressed) speed *= 8;
                Vector3 move = rotation * local.normalized * (speed * Time.unscaledDeltaTime);
                cameraX += move.x; cameraY += move.y; cameraZ += move.z;
                if (keyboard.homeKey.wasPressedThisFrame)
                    cameraY = (inspection && inspectionSampler != null && InsideInspection(cameraX,cameraZ)
                        ? inspectionSampler.EvaluateColumn(cameraX,cameraZ).SurfaceHeightMeters
                        : field.Sample(cameraX, cameraZ).HeightMeters) + (inspection ? 2 : 80);
            }
            Rebase(false); view.transform.rotation = rotation;
            UpdateWaterFrame();
            if (inspection)
            {
                if (patchSurfaces != null) patchSurfaces.RebuildBudget(Mathf.Clamp(MeshBuildsPerFrame, 1, 8));
                if (!buildingInspection && mouse != null && mouse.leftButton.wasPressedThisFrame) DigPatch();
                return;
            }
            UpdateVoxelTravelMotion();
            RefreshContactPlan();
            QueueNextVoxelRefinement();
            AdvanceBuilds();
            // Coverage publication is independent of the remaining quality queue.
            if(UseVoxelLodTerrain||(pending.Count==0&&currentBuild==null))ActivateReadyPlan();
            TickTerrainSeams();
            if (Time.unscaledTime >= nextPlan && ShouldReplan()) RequestPlan();
        }
        bool InsideInspection(double x,double z) => patch != null && x >= patchX && x < patchX + patch.SizeX * .1 &&
            z >= patchZ && z < patchZ + patch.SizeZ * .1;
        void Rebase(bool force)
        {
            if (force || Math.Abs(cameraX - originX) > 512 || Math.Abs(cameraY - originY) > 512 || Math.Abs(cameraZ - originZ) > 512)
            {
                originX = Math.Floor(cameraX / 256) * 256;
                originY = Math.Floor(cameraY / 256) * 256;
                originZ = Math.Floor(cameraZ / 256) * 256;
                foreach (Surface surface in surfaces.Values) PositionSurface(surface);
                foreach (Surface surface in contactSurfaces.Values) PositionSurface(surface);
                RebaseTerrainSeams();
                UpdateContactMask();
                PositionPatch();
                if (ownedMaterial != null) ownedMaterial.SetVector("_SurfaceOriginPhase", new Vector4(
                    (float)PositivePhase(originX), (float)PositivePhase(originY), (float)PositivePhase(originZ), 0));
            }
            view.transform.position = new Vector3((float)(cameraX - originX), (float)(cameraY - originY), (float)(cameraZ - originZ));
            UpdateStepShaderGlobals();
        }
        static double PositivePhase(double value) { double phase = value % 4096; return phase < 0 ? phase + 4096 : phase; }
        void PositionSurface(Surface surface)
        { surface.Object.transform.position = new Vector3((float)(surface.Tile.OriginXMeters - originX), (float)(surface.AnchorY - originY), (float)(surface.Tile.OriginZMeters - originZ)); }

        Surface BuildSurface(LodTile tile)
        {
            using (var steps = BuildSurfaceSteps(tile, QuadsFor(tile)))
                while (steps.MoveNext()) if (steps.Current != null) return steps.Current;
            throw new InvalidOperationException("Terrain mesh generation produced no surface.");
        }
        static float SurfaceRoughness(MaterialId material, double wetness)
        {
            if (material == MaterialId.Ice) return .18f;
            if (material == MaterialId.Snow) return .92f;
            double dry = material == MaterialId.Mud || material == MaterialId.Clay ? .82 : .95;
            return (float)Math.Max(.45, dry - wetness * .24);
        }
        internal static double SkirtDepth(IWorldField source, LodTile tile, double spacing)
        {
            // 2:1 balancing means an adjacent coarser tile is at most twice this
            // tile's width. Include its complete possible footprint, not merely
            // this tile's nine samples: a narrow edge peak can be missed by the
            // coarser edge's vertices. Both edge interpolants then lie inside this
            // range. Unknown model interiors use the validated global model range.
            HeightBounds bounds = source is IWorldFieldBounds bounded
                ? bounded.QueryBounds(tile.OriginXMeters - tile.SizeMeters * 2, tile.OriginZMeters - tile.SizeMeters * 2,
                    tile.OriginXMeters + tile.SizeMeters * 3, tile.OriginZMeters + tile.SizeMeters * 3)
                : new HeightBounds(RemoteTerrainField.MinimumSupportedHeight, RemoteTerrainField.MaximumSupportedHeight);
            return Math.Max(16, Math.Max(spacing * 6, bounds.Maximum - bounds.Minimum + spacing * 2 + 1));
        }
        [Serializable] sealed class AtlasIdentityHints { public string provider = null; }
        internal static void ValidateLiveAtlas(HeightAtlas atlas, bool nativeDimensions, double verticalScale)
        {
            if (!nativeDimensions || Math.Abs(verticalScale - 1) > 1e-7)
                throw new InvalidOperationException("Live source scale is set by its immutable service job; client atlas rescaling cannot transform canonical model coordinates.");
            if (Math.Abs(atlas.OriginX + atlas.ExtentX * .5) > 1e-5 || Math.Abs(atlas.OriginZ + atlas.ExtentZ * .5) > 1e-5)
                throw new InvalidOperationException("A live rough atlas must be a centered whole-map control atlas. This cropped/noncentered atlas has different canonical coordinates.");
            if (atlas.SourceIdentity.TrimStart().StartsWith("{", StringComparison.Ordinal))
            {
                AtlasIdentityHints hints = JsonUtility.FromJson<AtlasIdentityHints>(atlas.SourceIdentity);
                if (string.Equals(hints.provider, "InfiniteDiffusion", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("A recentered baked neural crop is a local study, not a geographically aligned live fallback. Use its baked mode, or use a centered whole-map control atlas matching the service job.");
            }
        }
        void OpenInspection()
        {
            // This explicit mode is intentionally separate from the far proxies. It
            // tests materialization/editing at a large global altitude without claiming
            // that regional streaming and terrain/voxel transitions are implemented.
            const int width = 128, height = 256;
            patchX = Math.Floor((cameraX - width * .05) / .1) * .1;
            patchZ = Math.Floor((cameraZ - width * .05) / .1) * .1;
            inspectionField = BareField(field);
            if (UseRemoteDiffusion)
            {
                if (remote == null || !remote.Connected) { Status = "Voxel inspection requires a ready model service; rough previews are not material authority."; return; }
                if (!remote.IsReady(patchX - SurfaceHalo, patchZ - SurfaceHalo, patchX + width * .1 + SurfaceHalo, patchZ + width * .1 + SurfaceHalo))
                {
                    if (inspectionPrefetch == null || inspectionPrefetch.IsCompleted)
                        inspectionPrefetch = remote.PrefetchAsync(patchX - SurfaceHalo, patchZ - SurfaceHalo, patchX + width * .1 + SurfaceHalo, patchZ + width * .1 + SurfaceHalo);
                    Status = "Fetching the full model region; press P again when the model status is ready."; return;
                }
                inspectionField = remote.ReadySnapshot(patchX - SurfaceHalo, patchZ - SurfaceHalo, patchX + width * .1 + SurfaceHalo, patchZ + width * .1 + SurfaceHalo);
            }
            inspectionSampler = new SurfaceMaterialSampler(inspectionField, CurrentSurfaceProfile);
            double centerHeight = inspectionSampler.EvaluateColumn(cameraX, cameraZ).SurfaceHeightMeters;
            patchY = Math.Floor((centerHeight - 12.8) / .1) * .1;
            patch = new VoxelWorld(width, height, width, VoxelSize, Seed);
            patchObject = new GameObject("Finite 10cm inspection patch"); patchObject.transform.SetParent(transform, false);
            patchSurfaces = patchObject.AddComponent<ChunkWorldRenderer>();
            if (EffectiveMaterial != null) patchSurfaces.SurfaceMaterial = EffectiveMaterial;
            patchSurfaces.Initialize(patch);
            inspection = true; buildingInspection = true; PositionPatch();
            foreach (Surface surface in surfaces.Values) surface.Object.SetActive(false);
            cameraY = centerHeight + 2; view.nearClipPlane = .04f; Rebase(true);
            inspectionBuild = StartCoroutine(BuildInspection(width, height));
        }
        IEnumerator BuildInspection(int width, int height)
        {
            if (inspectionSampler == null) inspectionSampler = new SurfaceMaterialSampler(BareField(inspectionField), CurrentSurfaceProfile);
            int columns = 0, clipped = 0;
            for (int z = 0; z < width; z++) for (int x = 0; x < width; x++)
            {
                double px = patchX + (x + .5) * .1, pz = patchZ + (z + .5) * .1;
                SurfaceColumn column = inspectionSampler.EvaluateColumn(px, pz);
                double surface = column.SurfaceHeightMeters;
                int top = (int)Math.Floor((surface - patchY) / .1);
                if (top < 1 || top >= height) clipped++;
                for (int y = 0; y <= Math.Min(height - 1, top); y++)
                {
                    MatterSample sample = inspectionSampler.SampleMatter(new WorldPoint(px, patchY + (y + .5) * .1, pz), column);
                    patch.Set(x, y, z, y == 0 ? MaterialId.Stone : sample.Material, y == 0 ? 0 : sample.Tint);
                }
                if (++columns % 128 == 0)
                {
                    Status = "Building finite voxel inspection · " + columns + "/" + width * width + " columns";
                    yield return null;
                }
            }
            // Keep the largest specimen envelope inside this temporary finite region.
            // Planner neighbor tests are hash-only, preserving the same32m source halo.
            const double coverMargin=1.7;
            if(GenerateGroundCover&&width*.1>coverMargin*2)
            {
                Status="Generating habitat-directed plants and weathered rocks…";
                yield return null;
                var scatter=new GroundCoverScatter(inspectionSampler,new GroundCoverProfile(Seed,Mathf.Clamp(GroundCoverDensity,0,2)));
                GroundCoverPlan plan=scatter.Plan(patchX+coverMargin,patchZ+coverMargin,patchX+width*.1-coverMargin,patchZ+width*.1-coverMargin);
                LastGroundCover=GroundCoverScatter.Materialize(patch,new WorldCell((long)Math.Round(patchX*10),(long)Math.Round(patchY*10),(long)Math.Round(patchZ*10)),plan,
                    new GroundCoverBuildBudget(maxWrittenCells:Mathf.Clamp(GroundCoverCellBudget,1000,200000)));
            }
            buildingInspection = false; inspectionBuild = null;
            Status = "Finite 12.8m inspection · LMB digs · P returns" + (clipped > 0 ? " · vertical extent clipped; choose gentler terrain" : "");
        }
        void DigPatch()
        {
            Vector3 direction = view.transform.forward;
            if (VoxelRaycast.TryCast(patch, (float)(cameraX - patchX), (float)(cameraY - patchY), (float)(cameraZ - patchZ),
                direction.x, direction.y, direction.z, 12, out VoxelHit hit))
            {
                WorldEdits.CarveSphere(patch, (hit.Cell.x + .5f) * VoxelSize, (hit.Cell.y + .5f) * VoxelSize,
                    (hit.Cell.z + .5f) * VoxelSize, .65f, InspectionToolPower);
            }
        }
        void PositionPatch()
        {
            if (patchObject != null) patchObject.transform.position = new Vector3((float)(patchX - originX), (float)(patchY - originY), (float)(patchZ - originZ));
        }
        void CloseInspection()
        {
            if (inspectionBuild != null) StopCoroutine(inspectionBuild);
            inspectionBuild = null; buildingInspection = false;
            inspection = false; patch = null; patchSurfaces = null; inspectionField = null; inspectionSampler = null;LastGroundCover=null;
            if (patchObject != null) Destroy(patchObject);
            patchObject = null;
            foreach (var item in surfaces) item.Value.Object.SetActive(active.Contains(item.Key));
            RebaseTerrainSeams();
            if (view != null) view.nearClipPlane = .3f;
        }
        void Release(TileKey key)
        {
            if (!surfaces.TryGetValue(key, out Surface surface)) return;
            TerrainSeamRemoved(surface);
            if (surface.Object != null) Destroy(surface.Object);
            if (surface.Mesh != null) Destroy(surface.Mesh);
            if(surface.VoxelColumns||surface.HeightProxy)
            {TerrainResidentMeshBytes-=SurfaceBytes(surface);voxelDetailMeshBytes-=DetailBytes(SurfaceBytes(surface));if(surface.HeightProxy)HeightProxyMeshBytes-=SurfaceBytes(surface);}
            surfaces.Remove(key);
        }
        void ClearTerrain()
        {
            ClearTerrainSeams();
            ResetVoxelTravelMotion();
            ResetDistantSweepState();
            CancelBuild();
            ClearContactTerrain();
            remove.Clear(); remove.AddRange(surfaces.Keys);
            foreach (TileKey key in remove) Release(key);
            pending.Clear(); active.Clear(); desired.Clear(); desiredTiles.Clear(); refining.Clear(); externallyDirty.Clear(); planActivationPending = false;preserveTerrainQueuesOnPlan=false;
            coverageGroups.Clear();coveragePublishedCount=0;coverageBudgetTime=double.NaN;
            voxelCapacityQuads.Clear();voxelRetryAt=0;voxelDeferredQuads.Clear();
            TerrainResidentMeshBytes=voxelDetailMeshBytes=HeightProxyMeshBytes=0;VoxelRefinementPendingTiles=0;voxelRefinementScanPending=false;consecutiveContactBuilds=consecutiveCoverageBuilds=0;
            VisibleTerrainSpacingTiles=VisibleTerrainAtRequestedSpacingTiles=0;VisibleTerrainMeanCurrentSpacingMeters=VisibleTerrainMeanRequestedSpacingMeters=0;
        }
        void OnGUI()
        {
            if (!ShowResearchOverlay) return;
            if (label == null) label = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
            GUI.Box(new Rect(16, 16, 790, 260), GUIContent.none);
            GUI.Label(new Rect(30, 26, 760, 25), "WORLD EXPLORER / HEIGHTFIELD LOD RESEARCH", label);
            GUI.Label(new Rect(30, 52, 760, 42), sourceName + " · " + Status, label);
            GUI.Label(new Rect(30, 92, 760, 30), $"Global {cameraX:F1}, {cameraY:F1}, {cameraZ:F1} m · tiles {ResidentTiles} / queued {PendingTiles}", label);
            GUI.Label(new Rect(30, 122, 760, 24), "WASD / Q,E fly · RMB look · Shift fast · Home above ground · R rebuild · P voxel inspection", label);
            GUI.Label(new Rect(30, 147, 760, 26), inspection ? "10 cm cells in a finite local patch; edits are temporary and discarded on exit." : "Far terrain is a derived heightfield; finest proxy spacing 2 m at default settings. No 100 m microvoxel ring yet.", label);
            GUI.Label(new Rect(30, 174, 760, 27), remoteStatus, label);
            if (visualWater != null && visualWater.Underwater)
                GUI.Label(new Rect(30, 202, 760, 24), "Below the visual sea surface · " + visualWater.UnderwaterDepthMeters.ToString("F1") + " m · no fluid simulation", label);
            if(inspection&&LastGroundCover!=null)
                GUI.Label(new Rect(30,228,760,24),"Ground cover · "+LastGroundCover.Plants+" plants / "+LastGroundCover.Rocks+" rocks · "+LastGroundCover.WrittenCells+" cells"+(LastGroundCover.Truncated?" · finite-region limit reached":""),label);
        }
        void OnDestroy()
        {
            ClearTerrain(); CloseInspection(); AbandonPlan(); DisposeVoxelWorker();
            if (visualWater != null) visualWater.Dispose();
            if (remote != null) remote.Dispose();
            if (view != null) Destroy(view.gameObject);
            if (ownedMaterial != null) Destroy(ownedMaterial);
        }
    }
}
