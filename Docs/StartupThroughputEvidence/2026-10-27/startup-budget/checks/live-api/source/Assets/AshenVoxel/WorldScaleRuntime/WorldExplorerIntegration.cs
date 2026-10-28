using System;
using System.Collections.Generic;
using System.Buffers;
using System.Threading.Tasks;
using AshenVoxel.Core;
using AshenVoxel.WorldScale;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace AshenVoxel.WorldScaleRuntime
{
    public sealed partial class WorldExplorer
    {
        IWorldField externalSource;
        SurfaceProfile externalProfile;
        bool externalInitialized, coarseStartup, planForced, planActivationPending;
        Quaternion externalRotation;
        WorldPoint plannedObserver;
        double plannedBaseTileSize = double.NaN;
        readonly Stopwatch meshClock = new Stopwatch();
        SurfaceBuild currentBuild;
        readonly HashSet<TileKey> externallyDirty = new HashSet<TileKey>();
        Task<LodPlan> planJob;
        int planJobRevision;
        double planJobBaseSize;
        public bool PlanningInProgress => planJob != null;
        public int PlannerJobsStarted { get; private set; }
        readonly struct TerrainVertex
        {
            public readonly double Height;
            public readonly uint Tint;
            public readonly Vector4 Appearance;
            public TerrainVertex(SurfaceColumn column)
            {
                Height=column.SurfaceHeightMeters;Tint=column.SurfaceTint;
                Appearance=new Vector4(SurfaceRoughness(column.SurfaceMaterial,column.Wetness),(float)column.Wetness,column.SurfaceMaterial==MaterialId.Snow?1:0,(128+(int)column.SurfaceMaterial)/255f);
            }
        }
        sealed class SurfaceBuild
        {
            public Work Work;
            public IEnumerator<Surface> Steps;
        }
        public Camera ViewCamera => view;
        public bool ShowResearchHud { get => ShowResearchOverlay; set => ShowResearchOverlay = value; }
        public Camera MainCamera => view;
        public WorldPoint RenderOrigin => new WorldPoint(originX, originY, originZ);
        public IWorldField TerrainField => field;
        public IWorldField BareTerrainField => BareField(field);
        public SurfaceMaterialSampler SurfaceSampler => (field as SurfaceWorldField)?.Sampler;
        /// <summary>Only completed voxel pages replace the ordinary URP proxy.</summary>
        public void SetVoxelTerrainMask(Vector4 grid,Vector4[] readyPages)
        {
            Material target=EffectiveMaterial;if(target==null)return;
            target.SetVector("_VoxelTerrainGrid",grid);target.SetVectorArray("_VoxelTerrainMask",readyPages);
        }
        public bool HasActiveCoverage => field != null && view != null && active.Count > 0;
        public int LastMeshWorkUnits { get; private set; }
        public double LastMeshBuildMilliseconds { get; private set; }
        public int MeshUploadsLastFrame { get; private set; }
        double EffectiveViewDistanceMeters => Math.Max(Mathf.Clamp(ViewDistanceKm, 4, 100), Mathf.Clamp(FarHorizonDistanceKm, 0, 100)) * 1000.0;

        /// <summary>Bind an immutable caller-owned field. This path never silently
        /// substitutes analytic terrain and does not start a model service.</summary>
        public void ConfigureExternal(IWorldField bareSource, SurfaceProfile profile, WorldPoint observer, Quaternion rotation)
        {
            if (bareSource == null) throw new ArgumentNullException(nameof(bareSource));
            ValidateObserver(observer);
            if (bareSource is SurfaceWorldField decorated)
            { externalSource = decorated.BaseField; externalProfile = profile ?? decorated.Profile; }
            else { externalSource = bareSource; externalProfile = profile ?? CreateSurfaceProfile(); }
            ExternalNavigation = true; ShowResearchOverlay = false; UseRemoteDiffusion = false;
            cameraX = observer.X; cameraY = observer.Y; cameraZ = observer.Z; externalRotation = rotation;
            coarseStartup = true; externalInitialized = false;
            Regenerate();
            if (field != null && view != null) view.transform.rotation = rotation;
        }
        public void SetObserverPose(WorldPoint observer, Quaternion rotation)
        {
            ValidateObserver(observer);
            cameraX = observer.X; cameraY = observer.Y; cameraZ = observer.Z; externalRotation = rotation;
            if (view != null) { Rebase(false); view.transform.rotation = rotation; UpdateWaterFrame(); }
        }
        void UpdateWaterFrame()
        {
            if (visualWater == null || field == null || view == null) return;
            visualWater.Visible = ShowVisualWater;
            if (remote != null && remote.Revision != waterRemoteRevision)
            { waterRemoteRevision = remote.Revision; visualWater.InvalidateDepthHints(); }
            IWorldField cameraBed = inspection && inspectionField != null && InsideInspection(cameraX,cameraZ) ? inspectionField : null;
            visualWater.SetFrame(BareField(field), GlobalCamera, new WorldPoint(originX, originY, originZ),
                CurrentSurfaceProfile.SeaLevelMeters, EffectiveViewDistanceMeters, view, inspection, cameraBed);
        }
        static void ValidateObserver(WorldPoint observer)
        {
            if (Math.Abs(observer.X)>1e12 || Math.Abs(observer.Y)>1e12 || Math.Abs(observer.Z)>1e12)
                throw new ArgumentOutOfRangeException(nameof(observer),"Exploration coordinates retain precision within +/-1e12 meters.");
        }
        public void ForceReplan()
        {
            planForced = true; nextPlan = 0;
            if (view != null) view.farClipPlane = (float)Math.Max(10000, EffectiveViewDistanceMeters * 1.8);
        }
        public bool IsTerrainReady(double xMeters, double zMeters)
        {
            Surface surface = ActiveSurfaceAt(xMeters, zMeters);
            return surface != null && (!ExternalNavigation || surface.Tile.SizeMeters / surface.Quads <= Math.Max(1.001, Mathf.Clamp(MinimumTileSizeMeters,16,128)/(double)Mathf.Clamp(NearTileQuads,8,64)+.001));
        }
        public bool IsTerrainReadyArea(double xMeters, double zMeters, double radiusMeters = 2)
        {
            if (double.IsNaN(radiusMeters) || double.IsInfinity(radiusMeters) || radiusMeters < 0 || radiusMeters > 64)
                throw new ArgumentOutOfRangeException(nameof(radiusMeters));
            for (int z = -1; z <= 1; z++) for (int x = -1; x <= 1; x++)
                if (!IsTerrainReady(xMeters + x * radiusMeters, zMeters + z * radiusMeters)) return false;
            return true;
        }
        /// <summary>Exact interpolation of current visible proxy triangles, which
        /// remains a render-cache query rather than edited material authority.</summary>
        public bool TrySampleRenderedHeight(double xMeters, double zMeters, out double heightMeters)
        {
            heightMeters = 0; Surface surface = ActiveSurfaceAt(xMeters, zMeters);
            if (surface == null || surface.LocalHeights == null) return false;
            double px = (xMeters - surface.Tile.OriginXMeters) / surface.Tile.SizeMeters * surface.Quads;
            double pz = (zMeters - surface.Tile.OriginZMeters) / surface.Tile.SizeMeters * surface.Quads;
            int x = Math.Min(surface.Quads - 1, Math.Max(0, (int)Math.Floor(px)));
            int z = Math.Min(surface.Quads - 1, Math.Max(0, (int)Math.Floor(pz)));
            if(surface.VoxelColumns)
            {heightMeters=surface.AnchorY+surface.LocalHeights[x+z*surface.Quads];return true;}
            double fx = px - x, fz = pz - z; int stride = surface.Quads + 1, a = x + z * stride;
            double h00 = surface.LocalHeights[a], h10 = surface.LocalHeights[a + 1];
            double h01 = surface.LocalHeights[a + stride], h11 = surface.LocalHeights[a + stride + 1];
            double local = fx + fz <= 1 ? h00 + (h10 - h00) * fx + (h01 - h00) * fz
                : h10 * (1 - fz) + h01 * (1 - fx) + h11 * (fx + fz - 1);
            heightMeters = surface.AnchorY + local; return true;
        }
        Surface ActiveSurfaceAt(double x, double z)
        {
            if (double.IsNaN(x) || double.IsInfinity(x) || double.IsNaN(z) || double.IsInfinity(z)) return null;
            if(contactSurfaces.TryGetValue(new TileKey((long)Math.Floor(x/VoxelLodSurface.BaseTileMeters),(long)Math.Floor(z/VoxelLodSurface.BaseTileMeters),0),out Surface contact))return contact;
            foreach (TileKey key in active)
            {
                if (!surfaces.TryGetValue(key, out Surface surface)) continue;
                LodTile tile = surface.Tile;
                if (x >= tile.OriginXMeters && x < tile.OriginXMeters + tile.SizeMeters && z >= tile.OriginZMeters && z < tile.OriginZMeters + tile.SizeMeters) return surface;
            }
            return null;
        }
        int QuadsFor(LodTile tile)
        {
            if(UseVoxelLodTerrain)return VoxelQuadsFor(tile);
            if (!ExternalNavigation) return Mathf.Clamp(TileQuads, 8, 64);
            if (coarseStartup) return 8;
            double distance = Math.Sqrt(DistanceSquared(tile));
            if (tile.SizeMeters <= Math.Max(128, Mathf.Clamp(MinimumTileSizeMeters, 16, 128) * 2) && distance < NearDetailRadiusMeters)
            {
                // A bounded plan can retain a64/128m leaf near an origin seam.
                // Tessellate that leaf enough for walking instead of permanently
                // withholding readiness merely because it is not a32m leaf.
                double target = Math.Max(.5, Mathf.Clamp(MinimumTileSizeMeters,16,128)/(double)Mathf.Clamp(NearTileQuads,8,64));
                return Math.Min(128, Math.Max(Mathf.Clamp(NearTileQuads,8,64),(int)Math.Ceiling(tile.SizeMeters/target)));
            }
            if (distance > Math.Max(40000, ViewDistanceKm * 1000.0)) return 8;
            // The former 16-quad ceiling discarded visible ridges and made
            // material patches span huge triangles. This changes mesh sampling,
            // never learned source resolution or authoritative ground heights.
            return distance<10000?Mathf.Clamp(TileQuads,24,32):24;
        }
        bool ShouldReplan()
        {
            if (planForced) return true;
            if(ViewTurned())return true;
            double dx = cameraX - plannedObserver.X, dy = cameraY - plannedObserver.Y, dz = cameraZ - plannedObserver.Z;
            double movedSquared = dx * dx + dy * dy + dz * dz;
            // Do not reset a slow build every second. Flight can replace stale
            // desired work while retaining the old complete visible coverage.
            double threshold = pending.Count > 0 || currentBuild != null
                ? Math.Max(128, ReplanMovementMeters * 4) : Math.Max(4, ReplanMovementMeters);
            return movedSquared >= threshold * threshold;
        }
        static IWorldField CaptureField(IWorldField source)
        {
            if(source is SurfaceWorldField surface)
                return new SurfaceWorldField(CaptureField(surface.BaseField),surface.Profile,
                    new HeightBounds(RemoteTerrainField.MinimumSupportedHeight,RemoteTerrainField.MaximumSupportedHeight));
            return source is IWorldFieldSnapshotProvider snapshots?snapshots.CaptureSnapshot():source;
        }
        static LodPlan BuildCapturedPlan(IWorldField source,WorldPoint observer,LodSettings requested,bool deriveBounds)
        {
            LodSettings settings=requested;
            if(deriveBounds&&source is IWorldFieldBounds bounded)
            {
                double root=requested.BaseTileSizeMeters*Math.Pow(2,requested.MaxLevel);
                double cx=Math.Floor(observer.X/root)*root,cz=Math.Floor(observer.Z/root)*root;
                HeightBounds range=bounded.QueryBounds(cx-root,cz-root,cx+root*2,cz+root*2);
                double pad=Math.Max(1,(range.Maximum-range.Minimum)*.00001);
                settings=new LodSettings(requested.BaseTileSizeMeters,requested.MaxLevel,requested.FarDistanceMeters,requested.SplitFactor,requested.MaxTiles,
                    range.Minimum-pad,range.Maximum+pad);
            }
            return LodPlanner.Build(source,observer,settings);
        }
        /// <summary>Call after a safe, atomic source publication outside the
        /// player-protected area. Existing complete coverage remains visible.</summary>
        public void RefreshExternalRegion(double minimumX,double minimumZ,double maximumX,double maximumZ)
        {
            var bounds=new ExplorationRefinementBounds(minimumX,minimumZ,maximumX,maximumZ);
            if(!ExternalNavigation||field==null)return;
            revision++;CancelBuild();pending.Clear();refining.Clear();
            ResetVoxelRefinementAdmission();
            foreach(var surface in surfaces.Values)if(Intersects(surface.Tile,bounds))
            {externallyDirty.Add(surface.Tile.Key);voxelCapacityQuads.Remove(surface.Tile.Key);}
            foreach(var tile in desiredTiles.Values)
            {
                if(Intersects(tile,bounds))externallyDirty.Add(tile.Key);
                surfaces.TryGetValue(tile.Key,out Surface existing);
                if(UseVoxelLodTerrain)QueueVoxelCoverage(tile,existing);
                else if(existing==null||externallyDirty.Contains(tile.Key)||existing.Quads!=QuadsFor(tile))
                    pending.Enqueue(new Work(tile,revision,existing!=null));
            }
            remove.Clear();foreach(var key in externallyDirty)if(!active.Contains(key)&&!desired.Contains(key))remove.Add(key);
            foreach(var key in remove)externallyDirty.Remove(key);
            if(visualWater!=null)visualWater.InvalidateDepthHints();ForceReplan();
        }
        static bool Intersects(LodTile tile,ExplorationRefinementBounds b)=>tile.OriginXMeters+tile.SizeMeters>=b.MinimumX&&tile.OriginXMeters<=b.MaximumX&&
            tile.OriginZMeters+tile.SizeMeters>=b.MinimumZ&&tile.OriginZMeters<=b.MaximumZ;
        void PollPlan()
        {
            if (planJob == null || !planJob.IsCompleted) return;
            Task<LodPlan> completed = planJob; int sourceRevision = planJobRevision; double baseSize = planJobBaseSize;
            planJob = null;
            if (completed.IsFaulted)
            {
                Status = "Terrain planning failed: " + completed.Exception.GetBaseException().Message;
                nextPlan = Time.unscaledTime + 5; planForced = true; return;
            }
            if (completed.IsCanceled || sourceRevision != revision)
            { planForced = true; nextPlan = 0; return; }
            ApplyPlan(completed.Result, baseSize);
        }
        void AbandonPlan()
        {
            Task<LodPlan> abandoned = planJob; planJob = null;
            if (abandoned != null)
                abandoned.ContinueWith(task => { var observed = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        }
        void CancelBuild()
        {
            if (currentBuild != null) currentBuild.Steps.Dispose();
            currentBuild = null;
        }
        Work NextWork()
        {
            Work best = pending.Dequeue(); double distance = WorkPriority(best.Tile);
            int count = pending.Count;
            for (int i = 0; i < count; i++)
            {
                Work next = pending.Dequeue(); double candidate = WorkPriority(next.Tile);
                if (candidate < distance) { pending.Enqueue(best); best = next; distance = candidate; }
                else pending.Enqueue(next);
            }
            return best;
        }
        void AdvanceBuilds()
        {
            LastMeshWorkUnits = 0; MeshUploadsLastFrame = 0;
            RefreshVoxelBudget();
            if(UseVoxelLodTerrain&&Time.unscaledTime<voxelRetryAt){LastMeshBuildMilliseconds=0;return;}
            int workCap = Mathf.Clamp(MeshWorkUnitsPerFrame, 32, 2048), uploadCap = Mathf.Clamp(MeshBuildsPerFrame, 1, 8);
            double milliseconds = Mathf.Clamp(MeshBuildMilliseconds, .25f, 8);
            meshClock.Restart();
            while (LastMeshWorkUnits < workCap && MeshUploadsLastFrame < uploadCap)
            {
                if (currentBuild == null)
                {
                    if (pending.Count == 0 && contactPending.Count==0) break;
                    bool contactFirst=contactPending.Count>0&&(pending.Count==0||consecutiveContactBuilds<2||HasUrgentContactWork());
                    Work work=contactFirst?NextContactWork():NextWork();
                    if(!work.Contact)consecutiveContactBuilds=0;
                    if (work.Revision != revision || !(work.Contact?contactDesired.Contains(work.Tile.Key):desired.Contains(work.Tile.Key))) continue;
                    if (!work.Contact&&!work.Replace && surfaces.ContainsKey(work.Tile.Key)) continue;
                    int quads=work.Contact?256:work.Quads>0?work.Quads:QuadsFor(work.Tile);
                    if(UseVoxelLodTerrain&&!work.Contact&&TerrainDetailDebtBytes>0)quads=8;
                    if(UseVoxelLodTerrain&&!work.Contact&&voxelCapacityQuads.TryGetValue(work.Tile.Key,out int cap))quads=Math.Min(quads,cap);
                    currentBuild = new SurfaceBuild { Work = work, Steps = UseVoxelLodTerrain ? BuildVoxelSurfaceSteps(work.Tile,quads,work.Contact) : BuildSurfaceSteps(work.Tile,quads) };
                }
                LastMeshWorkUnits++;
                bool more;
                try{more=currentBuild.Steps.MoveNext();}
                catch(Exception error) when(UseVoxelLodTerrain)
                {Work failed=currentBuild.Work;CancelBuild();DeferFailedVoxelBuild(failed,error);break;}
                Surface built = more ? currentBuild.Steps.Current : null;
                if (built != null)
                {
                    Work work = currentBuild.Work;
                    if(work.Contact)
                    {PublishContact(built,work);consecutiveContactBuilds=Math.Min(2,consecutiveContactBuilds+1);voxelRefinementScanPending=true;}
                    else
                    {
                        if(built.VoxelColumns&&!AdmitVoxelMesh(work.Tile,built.Quads,SurfaceBytes(built)))
                        {
                            Destroy(built.Object);Destroy(built.Mesh);MeshUploadsLastFrame++;
                            CancelBuild();RecordVoxelStatistics();continue;
                        }
                        bool visible = active.Contains(work.Tile.Key);
                        long previousDetailBytes=voxelDetailMeshBytes;
                        if (surfaces.ContainsKey(work.Tile.Key)) Release(work.Tile.Key);
                        surfaces.Add(work.Tile.Key, built); refining.Remove(work.Tile.Key); externallyDirty.Remove(work.Tile.Key);
                        if(built.VoxelColumns)
                        {TerrainResidentMeshBytes+=SurfaceBytes(built);voxelDetailMeshBytes+=DetailBytes(SurfaceBytes(built));voxelRefinementScanPending=true;}
                        WakeAdmissibleVoxelRefinements(previousDetailBytes);
                        built.Object.SetActive(visible && !inspection);
                    }
                    MeshUploadsLastFrame++;
                    CancelBuild();
                    if(UseVoxelLodTerrain)RecordVoxelStatistics();
                }
                else if (!more){CancelBuild();if(UseVoxelLodTerrain)RecordVoxelStatistics();}
                if(voxelBuildWaiting)break;
                if (meshClock.Elapsed.TotalMilliseconds >= milliseconds) break;
            }
            meshClock.Stop(); LastMeshBuildMilliseconds = meshClock.Elapsed.TotalMilliseconds;
        }
        IEnumerator<Surface> BuildSurfaceSteps(LodTile tile, int n)
        {
            bool neuralReady = remote != null && TileReady(tile);
            IWorldField samples = remote == null ? CaptureField(BareField(field)) : !neuralReady ? remote.RoughField : remote.ReadySnapshot(
                tile.OriginXMeters - SurfaceHalo, tile.OriginZMeters - SurfaceHalo,
                tile.OriginXMeters + tile.SizeMeters + SurfaceHalo, tile.OriginZMeters + tile.SizeMeters + SurfaceHalo);
            var sampler = new SurfaceMaterialSampler(samples, CurrentSurfaceProfile);
            int stride = n + 1; double spacing = tile.SizeMeters / n;
            var columns = ArrayPool<TerrainVertex>.Shared.Rent(stride * stride);
            try
            {
            for (int z = 0; z <= n; z++) for (int x = 0; x <= n; x++)
            {
                columns[x + z * stride] = new TerrainVertex(sampler.EvaluateColumn(tile.OriginXMeters + x * spacing, tile.OriginZMeters + z * spacing));
                yield return null;
            }
            double anchor = columns[0].Height;
            var vertices = new List<Vector3>(stride * stride + n * 8);
            var normals = new List<Vector3>(vertices.Capacity);
            var colors = new List<Color32>(vertices.Capacity);
            var appearance = new List<Vector4>(vertices.Capacity);
            var triangles = new List<int>(n * n * 6 + n * 24);
            var localHeights = new float[stride * stride];
            for (int z = 0; z <= n; z++)
            {
                for (int x = 0; x <= n; x++)
                {
                    TerrainVertex column = columns[x + z * stride];
                    int x0 = Math.Max(0, x - 1), x1 = Math.Min(n, x + 1), z0 = Math.Max(0, z - 1), z1 = Math.Min(n, z + 1);
                    double dx = (columns[x1 + z * stride].Height - columns[x0 + z * stride].Height) / ((x1 - x0) * spacing);
                    double dz = (columns[x + z1 * stride].Height - columns[x + z0 * stride].Height) / ((z1 - z0) * spacing);
                    localHeights[x + z * stride] = (float)(column.Height - anchor);
                    vertices.Add(new Vector3((float)(x * spacing), localHeights[x + z * stride], (float)(z * spacing)));
                    normals.Add(new Vector3((float)-dx, 1, (float)-dz).normalized);
                    uint tint = column.Tint; colors.Add(new Color32((byte)(tint >> 16), (byte)(tint >> 8), (byte)tint, 255));
                    appearance.Add(column.Appearance);
                }
                yield return null;
            }
            for (int z = 0; z < n; z++)
            {
                for (int x = 0; x < n; x++)
                {
                    int a = x + z * stride, b = a + stride;
                    triangles.Add(a); triangles.Add(b); triangles.Add(a + 1); triangles.Add(a + 1); triangles.Add(b); triangles.Add(b + 1);
                }
                yield return null;
            }
            float skirt = (float)SkirtDepth(SurfaceField(samples), tile, spacing);
            for (int edge = 0; edge < 4; edge++)
            {
                for (int i = 0; i < n; i++)
                {
                    int a, b;
                    if (edge == 0) { a = i; b = i + 1; }
                    else if (edge == 1) { a = i * stride + n; b = (i + 1) * stride + n; }
                    else if (edge == 2) { a = n * stride + n - i; b = a - 1; }
                    else { a = (n - i) * stride; b = a - stride; }
                    int c = vertices.Count;
                    vertices.Add(vertices[a] + Vector3.down * skirt); vertices.Add(vertices[b] + Vector3.down * skirt);
                    normals.Add(normals[a]); normals.Add(normals[b]); colors.Add(colors[a]); colors.Add(colors[b]);
                    appearance.Add(appearance[a]); appearance.Add(appearance[b]);
                    triangles.Add(a); triangles.Add(b); triangles.Add(c); triangles.Add(b); triangles.Add(c + 1); triangles.Add(c);
                }
                yield return null;
            }
            var mesh = new Mesh { name = "Height proxy " + tile.Key };
            mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetColors(colors); mesh.SetUVs(0, appearance);
            mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
            // Visible-height queries retain the compact LocalHeights array.
            // This derived render mesh does not need duplicate CPU vertex data.
            mesh.UploadMeshData(true);
            var go = new GameObject(mesh.name); go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = EffectiveMaterial;
            var surface = new Surface { Object = go, Mesh = mesh, Renderer = renderer, Tile = tile, AnchorY = anchor,
                NeuralReady = neuralReady, Quads = n, LocalHeights = localHeights, TriangleCount=triangles.Count/3 };
            PositionSurface(surface); go.SetActive(false); yield return surface;
            }
            finally { ArrayPool<TerrainVertex>.Shared.Return(columns); }
        }
    }
}
