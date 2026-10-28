using System;
using System.Collections;
using System.Collections.Generic;
using AshenVoxel.Core;
using AshenVoxel.Core.Details;
using AshenVoxel.Core.Rocks;
using AshenVoxel.Rendering;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AshenVoxel.WorldScaleRuntime
{
    public enum GroundCoverWorkshopLayout { FullCollection, RockShapes }

    /// <summary>Small actual-matter specimens for evaluating silhouette, palette and scale.
    /// This static workshop does not simulate plant growth, wind or structural collapse.</summary>
    public sealed class GroundCoverShowcase : MonoBehaviour
    {
        public const float CellSize = .1f;
        public const int SpecimenCount = 15;
        public const int RockShapeSpecimenCount = 6;
        const int GroundTop = 4, SpacingCells = 40, PedestalHalfCells = 14;
        public GroundCoverWorkshopLayout Layout = GroundCoverWorkshopLayout.FullCollection;
        [Tooltip("Rock Shapes compares six explicit shapes using this one material, one seed and equal nominal dimensions.")]
        public MaterialId ShapeStudyMaterial = MaterialId.Granite;
        [Tooltip("Full Collection keeps material/seed-based shapes with Auto, or forces this shape for all five rocks.")]
        public RockShape RockShapeOverride = RockShape.Auto;
        [Tooltip("Every specimen derives a separate deterministic seed from this value.")]
        public int Seed = 714103;
        public int SeedOffset;
        [Range(1, 24)] public int MeshesPerFrame = 8;
        public bool OverviewOnStart = true;
        public VoxelWorld World { get; private set; }
        public bool Generating { get; private set; }
        public string Status { get; private set; } = "Press Play to generate ground-cover specimens.";
        public int SelectedSpecimen { get; private set; }
        public IReadOnlyList<SpecimenInfo> Specimens => specimens;

        public sealed class SpecimenInfo
        {
            public string Name { get; internal set; }
            public string Materials { get; internal set; }
            public int Seed { get; internal set; }
            public int AboveGroundCells { get; internal set; }
            public long CandidateCount { get; internal set; }
            public bool Truncated { get; internal set; }
            public Int3 AnchorCell { get; internal set; }
            public RockShape? GeneratedRockShape { get; internal set; }
            public MaterialId? RockMaterial { get; internal set; }
            public string GenerationProfileIdentity { get; internal set; }
            public Int3 MinimumCell { get; internal set; }
            public Int3 MaximumCell { get; internal set; }
            public Vector3 LabelPosition { get; internal set; }
            public Vector3 Center { get; internal set; }
            public float FocusDistance { get; internal set; }
        }

        static readonly PlantKind[] PlantKinds = {
            PlantKind.Fern, PlantKind.Shrub, PlantKind.GrassTuft, PlantKind.Reed,
            PlantKind.BroadleafHerb, PlantKind.Flower, PlantKind.Mushroom,
            PlantKind.Cactus, PlantKind.Succulent, PlantKind.Cushion
        };
        static readonly MaterialId[] RockKinds = {
            MaterialId.Stone, MaterialId.Granite, MaterialId.Limestone,
            MaterialId.Sandstone, MaterialId.Shale
        };
        static readonly RockShape[] ShapeKinds = {
            RockShape.AngularBoulder, RockShape.Slab, RockShape.Wedge,
            RockShape.Spire, RockShape.SplitOutcrop, RockShape.Cluster
        };
        readonly List<SpecimenInfo> specimens = new List<SpecimenInfo>(SpecimenCount);
        ChunkWorldRenderer surfaces;
        GameObject surfaceObject;
        WorkshopCamera orbit;
        Camera view;
        Coroutine generation;
        GUIStyle label, smallLabel;

        void Start() { Regenerate(); }

        public void Regenerate()
        {
            if (!Application.isPlaying)
            {
                Debug.Log("Ground-cover specimens generate in Play mode. Press Play, then Regenerate.", this);
                return;
            }
            if (generation != null) StopCoroutine(generation);
            generation = StartCoroutine(GenerateSafely());
        }

        IEnumerator GenerateSafely()
        {
            var work = Generate();
            while (true)
            {
                bool more; object next;
                try { more = work.MoveNext(); next = more ? work.Current : null; }
                catch (Exception exception)
                {
                    Generating = false; generation = null;
                    Status = "Ground-cover preview failed: " + exception.Message;
                    Debug.LogException(exception, this); more = false; next = null;
                }
                if (!more) yield break;
                yield return next;
            }
        }

        IEnumerator Generate()
        {
            Generating = true;
            // Keep one coherent plan even if Inspector fields change while yielding.
            bool shapeStudy = Layout == GroundCoverWorkshopLayout.RockShapes;
            int seedBase = unchecked(Seed + SeedOffset), count = shapeStudy ? ShapeKinds.Length : SpecimenCount;
            MaterialId studyMaterial = ShapeStudyMaterial;
            RockShape shapeOverride = RockShapeOverride;
            if (shapeStudy && !RockProfile.IsRock(studyMaterial))
                throw new ArgumentException("Rock Shapes requires basalt, granite, limestone, sandstone or shale.");
            Status = "Preparing the 10 cm specimen grid…";
            specimens.Clear();
            if (surfaceObject != null) Destroy(surfaceObject);
            surfaces = null;
            yield return null;

            World = new VoxelWorld(shapeStudy ? 128 : 208, 64, shapeStudy ? 96 : 128, CellSize, seedBase);
            surfaceObject = new GameObject("Ground-cover specimen surfaces");
            surfaceObject.transform.SetParent(transform, false);
            surfaces = surfaceObject.AddComponent<ChunkWorldRenderer>();
            int columns = shapeStudy ? 3 : 5;
            for (int i = 0; i < count; i++)
            {
                int x = 24 + i % columns * SpacingCells, z = 24 + i / columns * SpacingCells;
                // Same seed in the shape study isolates shape from seed/color differences.
                int seed = shapeStudy ? seedBase : unchecked(seedBase + i * 977);
                bool rock = shapeStudy || i >= PlantKinds.Length;
                MaterialId rockMaterial = shapeStudy ? studyMaterial : rock ? RockKinds[i - PlantKinds.Length] : MaterialId.Air;
                string name = rock ? MaterialCatalog.Get(rockMaterial).Name + " rock" : PlantName(PlantKinds[i]);
                Status = "Generating " + name + " (" + (i + 1) + "/" + count + ")…";
                AddPedestal(x, z, rock ? rockMaterial : GroundMaterial(PlantKinds[i]));
                var firstAir = new Int3(x, GroundTop + 1, z);
                long candidates; bool truncated;
                RockShape? generatedShape = null; string profileIdentity = null;
                if (rock)
                {
                    RockProfile profile = RockPresets.For(rockMaterial, seed);
                    profile = shapeStudy
                        ? profile.With(widthMeters: 2.2, heightMeters: 1.5, depthMeters: 1.85, shape: ShapeKinds[i])
                        : profile.With(shape: shapeOverride);
                    RockResult result = RockGenerator.Generate(World, firstAir, profile);
                    candidates = result.CandidateCount; truncated = result.Truncated;
                    generatedShape = result.ResolvedShape; profileIdentity = profile.Identity;
                    name = ShapeName(result.ResolvedShape) + " · " + MaterialCatalog.Get(rockMaterial).Name;
                }
                else
                {
                    PlantProfile profile = PlantPresets.For(PlantKinds[i]);
                    PlantResult result = PlantGenerator.Generate(World, profile, firstAir, seed,
                        new PlantGenerationBudget(maxCandidates: 200000, maxCells: 12000));
                    candidates = result.Candidates; truncated = result.Truncated;
                }
                SpecimenInfo info = Measure(name, seed, x, z, candidates, truncated);
                info.AnchorCell = firstAir; info.GeneratedRockShape = generatedShape;
                info.RockMaterial = rock ? rockMaterial : (MaterialId?)null;
                info.GenerationProfileIdentity = profileIdentity;
                specimens.Add(info);
                yield return null;
            }
            AddSingleCellReference();
            surfaces.Initialize(World);
            SetupCamera();
            SelectedSpecimen = Math.Min(SelectedSpecimen, specimens.Count - 1);
            if (OverviewOnStart) FrameAll(); else Focus(SelectedSpecimen);
            while (surfaces.PendingChunks > 0)
            {
                surfaces.RebuildBudget(Mathf.Max(1, MeshesPerFrame));
                Status = "Preparing specimen surfaces… " + surfaces.PendingChunks + " chunks remaining";
                yield return null;
            }
            int cells = 0; bool limited = false;
            foreach (SpecimenInfo info in specimens) { cells += info.AboveGroundCells; limited |= info.Truncated; }
            Status = "Ready · " + (shapeStudy ? "6 shapes / same " + MaterialCatalog.Get(studyMaterial).Name + " / same seed and size settings" : "10 plants / 5 rocks")
                + " · " + cells.ToString("N0") + " above-ground specimen cells"
                + (limited ? " · generation limit reached" : "");
            Generating = false; generation = null;
        }

        static string PlantName(PlantKind kind)
        {
            switch (kind)
            {
                case PlantKind.GrassTuft: return "Grass tuft";
                case PlantKind.BroadleafHerb: return "Broadleaf herb";
                case PlantKind.Cushion: return "Alpine cushion";
                default: return kind.ToString();
            }
        }

        static string ShapeName(RockShape shape)
        {
            switch (shape)
            {
                case RockShape.AngularBoulder: return "Angular boulder";
                case RockShape.SplitOutcrop: return "Split outcrop";
                default: return shape.ToString();
            }
        }

        static MaterialId GroundMaterial(PlantKind kind)
        {
            switch (kind)
            {
                case PlantKind.Fern: case PlantKind.Mushroom: return MaterialId.Litter;
                case PlantKind.Reed: return MaterialId.Mud;
                case PlantKind.Cactus: return MaterialId.Sand;
                case PlantKind.Succulent: return MaterialId.BlackSand;
                case PlantKind.Cushion: return MaterialId.Gravel;
                default: return MaterialId.Grass;
            }
        }

        void AddPedestal(int cx, int cz, MaterialId surface)
        {
            for (int z = cz - PedestalHalfCells; z < cz + PedestalHalfCells; z++)
                for (int x = cx - PedestalHalfCells; x < cx + PedestalHalfCells; x++)
                    for (int y = 0; y <= GroundTop; y++)
                    {
                        MaterialId material = y == GroundTop ? surface : y <= 1 ? MaterialId.Stone : MaterialId.Dirt;
                        World.Set(x, y, z, material);
                    }
        }

        void AddSingleCellReference()
        {
            int centerX = World.SizeX / 2;
            for (int z = 3; z <= 7; z++) for (int x = centerX - 2; x <= centerX + 2; x++)
                for (int y = 0; y <= GroundTop; y++) World.Set(x, y, z, MaterialId.Stone);
            World.Set(centerX, GroundTop + 1, 5, MaterialId.Leaves, VoxelColor.Base(MaterialId.Leaves));
        }

        SpecimenInfo Measure(string name, int seed, int cx, int cz, long candidates, bool truncated)
        {
            int count = 0, minX = World.SizeX, minY = World.SizeY, minZ = World.SizeZ;
            int maxX = 0, maxY = GroundTop + 1, maxZ = 0;
            var materials = new SortedSet<MaterialId>();
            for (int z = cz - PedestalHalfCells; z < cz + PedestalHalfCells; z++)
                for (int x = cx - PedestalHalfCells; x < cx + PedestalHalfCells; x++)
                    for (int y = GroundTop + 1; y < World.SizeY; y++)
                    {
                        MaterialId material = World.Get(x, y, z);
                        if (material == MaterialId.Air) continue;
                        count++; materials.Add(material);
                        minX = Math.Min(minX, x); minY = Math.Min(minY, y); minZ = Math.Min(minZ, z);
                        maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y); maxZ = Math.Max(maxZ, z);
                    }
            if (count == 0) { minX = maxX = cx; minY = maxY = GroundTop + 1; minZ = maxZ = cz; }
            string names = "";
            foreach (MaterialId material in materials)
                names += (names.Length == 0 ? "" : ", ") + MaterialCatalog.Get(material).Name;
            float sx = (maxX - minX + 1) * CellSize, sy = (maxY - minY + 1) * CellSize, sz = (maxZ - minZ + 1) * CellSize;
            return new SpecimenInfo {
                Name = name, Seed = seed, AboveGroundCells = count, CandidateCount = candidates, Truncated = truncated,
                MinimumCell = new Int3(minX, minY, minZ), MaximumCell = new Int3(maxX, maxY, maxZ), Materials = names,
                Center = new Vector3((minX + maxX + 1) * CellSize * .5f, (minY + maxY + 1) * CellSize * .5f, (minZ + maxZ + 1) * CellSize * .5f),
                LabelPosition = new Vector3((cx + .5f) * CellSize, (maxY + 1) * CellSize + .16f, (cz + .5f) * CellSize),
                FocusDistance = Math.Max(1.25f, (float)Math.Sqrt(sx * sx + sy * sy + sz * sz) * 1.8f)
            };
        }

        void SetupCamera()
        {
            if (orbit != null) return;
            var go = new GameObject("Ground-cover workshop camera"); go.transform.SetParent(transform, false);
            view = go.AddComponent<Camera>(); view.tag = "MainCamera";
            view.clearFlags = CameraClearFlags.SolidColor; view.backgroundColor = new Color(.06f, .08f, .095f);
            view.nearClipPlane = .02f; view.farClipPlane = 100; view.fieldOfView = WorkshopCamera.FieldOfView;
            orbit = go.AddComponent<WorkshopCamera>();
        }

        public void Focus(int index)
        {
            if (specimens.Count == 0 || orbit == null) return;
            SelectedSpecimen = (index % specimens.Count + specimens.Count) % specimens.Count;
            SpecimenInfo info = specimens[SelectedSpecimen];
            orbit.Frame(transform.TransformPoint(info.Center), info.FocusDistance);
        }

        public void FrameAll()
        {
            if (orbit != null && World != null)
                orbit.Frame(transform.TransformPoint(new Vector3(World.SizeX * CellSize * .5f, .9f, World.SizeZ * CellSize * .5f)),
                    Mathf.Max(World.SizeX, World.SizeZ) * CellSize * 1.15f);
        }

        void Update()
        {
            if (!Application.isPlaying || Generating) return;
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.gKey.wasPressedThisFrame) { SeedOffset = unchecked(SeedOffset + 1); Regenerate(); }
            else if (keyboard.rKey.wasPressedThisFrame) Regenerate();
            else if (keyboard.tabKey.wasPressedThisFrame) Focus(SelectedSpecimen + (keyboard.leftShiftKey.isPressed ? -1 : 1));
            else if (keyboard.fKey.wasPressedThisFrame) Focus(SelectedSpecimen);
            else if (keyboard.oKey.wasPressedThisFrame) FrameAll();
        }

        void OnGUI()
        {
            if (label == null)
            {
                label = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
                smallLabel = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
            }
            float width = Mathf.Min(750, Screen.width - 24);
            GUI.Box(new Rect(12, 12, width, 170), GUIContent.none);
            GUI.Label(new Rect(24, 19, width - 24, 22), "GROUND-COVER WORKSHOP / ACTUAL 10 CM VOXELS", label);
            GUI.Label(new Rect(24, 44, width - 24, 34), Status, label);
            if (specimens.Count > 0 && SelectedSpecimen < specimens.Count)
            {
                SpecimenInfo info = specimens[SelectedSpecimen];
                Int3 span = info.MaximumCell - info.MinimumCell + new Int3(1, 1, 1);
                string size = (span.x * CellSize).ToString("0.0") + " × " + (span.y * CellSize).ToString("0.0") + " × " + (span.z * CellSize).ToString("0.0") + " m";
                GUI.Label(new Rect(24, 78, width - 24, 32), (SelectedSpecimen + 1) + "/" + specimens.Count + " · " + info.Name + " · " + size + " · seed " + info.Seed, label);
                GUI.Label(new Rect(24, 108, width - 24, 24), info.AboveGroundCells + " cells · " + info.Materials, smallLabel);
            }
            GUI.Label(new Rect(24, 133, width - 24, 44), "Tab / Shift+Tab focus next / previous · F focus · O overview · G next seed · R repeat seed\nRMB orbit · MMB pan · wheel zoom · Home reset framing · front reference is one 10 cm leaf cell", smallLabel);
            if (view == null) return;
            foreach (SpecimenInfo info in specimens)
            {
                Vector3 point = view.WorldToScreenPoint(transform.TransformPoint(info.LabelPosition));
                float y = Screen.height - point.y;
                if (point.z <= 0 || y < 190 || y > Screen.height - 30 || point.x < 40 || point.x > Screen.width - 40) continue;
                GUI.Label(new Rect(point.x - 90, y, 180, 34), info.Name, smallLabel);
            }
        }

        void OnDestroy()
        {
            if (generation != null) StopCoroutine(generation);
            if (surfaceObject != null) Destroy(surfaceObject);
            if (orbit != null) Destroy(orbit.gameObject);
        }
    }
}
