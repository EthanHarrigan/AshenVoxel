using System;
using System.Collections;
using AshenVoxel.Core;
using AshenVoxel.Core.Trees;
using AshenVoxel.Rendering;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AshenVoxel.Authoring
{
    public enum TreeWorkshopLayout { SingleTree, FourSpecies }

    /// <summary>Play-mode authoring preview; the generated material world remains at 10 cm.</summary>
    public sealed class TreeShowcase : MonoBehaviour
    {
        public TreeWorkshopLayout Layout = TreeWorkshopLayout.FourSpecies;
        [Tooltip("Single Tree uses this asset. Four Species uses the profiles below, in order.")]
        public TreeProfile SelectedProfile;
        [Tooltip("Used for a single tree when no profile asset is assigned.")]
        public TreeSpecies Species = TreeSpecies.Oak;
        public TreeProfile[] SpeciesProfiles = new TreeProfile[4];
        [Range(1, 24)] public int MeshesPerFrame = 8;
        [Tooltip("Start a new seed without modifying any profile asset.")]
        public int SeedOffset;
        public VoxelWorld World { get; private set; }
        public bool Generating { get; private set; }
        public string Status { get; private set; } = "Press Play to grow the trees.";
        public const float CellSize = .1f;
        ChunkWorldRenderer surfaces;
        GameObject surfaceObject;
        WorkshopOrbitCamera orbit;
        Coroutine generation;
        TreeParameters[] plans;
        TreeSpecies[] species;
        int[] seeds;
        GUIStyle label;
        long wood, foliage, fruit;
        bool reachedLimit;

        void Start() { Regenerate(); }
        public void Regenerate()
        {
            if (!Application.isPlaying)
            {
                Debug.Log("Tree Workshop previews grow in Play mode. Press Play, then Regenerate.", this);
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
                    Status = "Tree preview failed: " + exception.Message;
                    Debug.LogException(exception, this); more = false; next = null;
                }
                if (!more) yield break;
                yield return next;
            }
        }
        IEnumerator Generate()
        {
            Generating = true; Status = "Preparing tree profiles…";
            yield return null;
            int count = Layout == TreeWorkshopLayout.SingleTree ? 1 : 4;
            plans = new TreeParameters[count]; species = new TreeSpecies[count]; seeds = new int[count];
            
            for (int i = 0; i < count; i++)
            {
                TreeProfile profile = Layout == TreeWorkshopLayout.SingleTree ? SelectedProfile :
                    SpeciesProfiles != null && i < SpeciesProfiles.Length ? SpeciesProfiles[i] : null;
                species[i] = profile != null ? profile.Species : count == 1 ? Species : (TreeSpecies)i;
                plans[i] = profile != null ? profile.GetParameters() : TreePresets.Get(species[i]);
                seeds[i] = unchecked((profile != null ? profile.Seed : 714103 + i * 977) + SeedOffset);
            }
            // Bounds include seed variation and age; only local root patches allocate ground.
            PreviewBounds bounds = CalculateBounds(plans, count == 1);
            float spacing = bounds.Spacing, side = bounds.Side, tallest = bounds.Height;
            int width = Mathf.Clamp(Mathf.CeilToInt(side / CellSize), 96, 4096);
            int height = Mathf.Clamp(Mathf.CeilToInt((tallest + 4) / CellSize), 96, 2048);
            bool domainLimited = side > width * CellSize || tallest + 4 > height * CellSize;
            World = new VoxelWorld(width, height, width, CellSize, seeds[0]);
            if (surfaceObject != null) Destroy(surfaceObject);
            surfaceObject = new GameObject("Tree Workshop surfaces");
            surfaceObject.transform.SetParent(transform, false);
            surfaces = surfaceObject.AddComponent<ChunkWorldRenderer>();
            wood = foliage = fruit = 0; reachedLimit = domainLimited;
            if (domainLimited) Debug.LogWarning("Tree Workshop domain cap reached; reduce specimen size or compare in Single Tree layout.", this);
            float center = width * CellSize * .5f;
            for (int i = 0; i < count; i++)
            {
                Status = "Growing " + species[i] + " (" + (i + 1) + "/" + count + ")…";
                yield return null;
                float x = center + (count == 1 ? 0 : ((i & 1) == 0 ? -.5f : .5f) * spacing);
                float z = center + (count == 1 ? 0 : ((i & 2) == 0 ? -.5f : .5f) * spacing);
                AddRootPatch(x, z, Mathf.Max(Mathf.Min(plans[i].CrownRadius * .7f + .7f, 3.5f),
                    RootReach(plans[i])));
                TreeResult result = TreeGenerator.Generate(World, plans[i], x, .5f, z, seeds[i],
                    new TreeGenerationBudget(maxBranches: 4096, maxVoxelCandidates: 16000000, maxFoliageClusters: 4096));
                wood += result.WoodCells; foliage += result.LeafCells; fruit += result.FruitCells;
                if (result.Truncated)
                { reachedLimit = true; Debug.LogWarning(species[i] + ": " + result.TruncationReason, this); }
            }
            surfaces.Initialize(World);
            SetupCamera(new Vector3(center, tallest * .43f + .5f, center), Mathf.Max(side * .9f, tallest * 1.55f));
            while (surfaces.PendingChunks > 0)
            {
                surfaces.RebuildBudget(Mathf.Max(1, MeshesPerFrame));
                Status = "Preparing the view… " + surfaces.PendingChunks + " regions remaining";
                yield return null;
            }
            Status = "Ready · " + wood.ToString("N0") + " wood / " + foliage.ToString("N0") + " foliage / " + fruit.ToString("N0") + " fruit cells" + (reachedLimit ? " · preview limit reached (see Console)" : "");
            Generating = false; generation = null;
        }
        internal readonly struct PreviewBounds
        {
            public readonly float Spacing, Side, Height;
            public PreviewBounds(float spacing, float side, float height)
            { Spacing = spacing; Side = side; Height = height; }
        }
        internal static PreviewBounds CalculateBounds(TreeParameters[] parameters, bool single)
        {
            float tallest = 1, widest = 1;
            foreach (TreeParameters p in parameters)
            {
                float matureHeight = p.Height * p.Age * (1 + p.Variation * .08f);
                tallest = Mathf.Max(tallest, matureHeight * 1.1f + 2);
                float crown = p.CrownRadius * (float)Math.Pow(p.Age, .85) * (1 + p.Variation * .1f);
                float leafReach = p.LeafClusterRadius * (float)Math.Pow(p.Age, .65) * 3;
                // Include tapered crown envelopes, foliage sprays, and a weak leader's lateral bend.
                float crownEnvelope = crown * 1.18f * (float)Math.Pow(1.25, p.ConeTaper);
                float lateral = Mathf.Max(crownEnvelope + leafReach, matureHeight * p.Gnarl * .14f);
                widest = Mathf.Max(widest, Mathf.Max(lateral, RootReach(p)) + p.BarkRelief + 1);
            }
            float spacing = Mathf.Max(8, widest * 2.3f + 2);
            float side = widest * 2 + 8 + (single ? 0 : spacing);
            return new PreviewBounds(spacing, side, tallest);
        }
        static float RootReach(TreeParameters p)
            => p.TrunkRadius * (float)Math.Pow(p.Age, 1.15) * (1 + p.Variation * .12f)
                * (1.7f + p.RootFlare * 1.1f) * 1.13f + .3f;
        void AddRootPatch(float centerX, float centerZ, float radius)
        {
            int cx = Mathf.FloorToInt(centerX / CellSize), cz = Mathf.FloorToInt(centerZ / CellSize);
            int cells = Mathf.CeilToInt(radius / CellSize);
            for (int z = -cells; z <= cells; z++) for (int x = -cells; x <= cells; x++)
            {
                if (x * x + z * z > cells * cells) continue;
                for (int y = 0; y < 4; y++) World.Set(cx + x, y, cz + z, y == 0 ? MaterialId.Stone : MaterialId.Dirt);
                World.Set(cx + x, 4, cz + z, MaterialId.Grass, VoxelColor.Pack(70, 88, 58));
            }
        }
        void SetupCamera(Vector3 target, float distance)
        {
            if (orbit == null)
            {
                var go = new GameObject("Tree Workshop camera"); go.transform.SetParent(transform, false);
                var cam = go.AddComponent<Camera>(); cam.tag = "MainCamera";
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.055f, .075f, .09f);
                cam.nearClipPlane = .04f; cam.farClipPlane = 1000; cam.fieldOfView = WorkshopOrbitCamera.FieldOfView;
                orbit = go.AddComponent<WorkshopOrbitCamera>();
            }
            orbit.Frame(target, distance);
        }
        void Update()
        {
            if (!Application.isPlaying) return;
            var k = Keyboard.current;
            if (k != null && k.gKey.wasPressedThisFrame && !Generating) Regenerate();
        }
        void OnGUI()
        {
            if (label == null) label = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
            GUI.Box(new Rect(16, 16, 700, 122), GUIContent.none);
            GUI.Label(new Rect(30, 25, 670, 28), "TREE WORKSHOP / 10 CM", label);
            GUI.Label(new Rect(30, 53, 670, 42), Status, label);
            GUI.Label(new Rect(30, 99, 670, 30), "RMB orbit · MMB pan · wheel zoom · Home reset view · G regrow", label);
        }
        void OnDestroy()
        {
            if (generation != null) StopCoroutine(generation);
            if (surfaceObject != null) Destroy(surfaceObject);
            if (orbit != null) Destroy(orbit.gameObject);
        }
    }
}
