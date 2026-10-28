using System;
using System.Collections.Generic;
using AshenVoxel.WorldScale;
using UnityEngine;

namespace AshenVoxel.WorldScaleRuntime
{
    /// <summary>A visual mean-sea plane, not simulated fluid matter. Terrain depth
    /// occludes land; a bounded coarse bed hint controls color only. All coordinates
    /// are camera-relative after double subtraction. No global fog is modified.</summary>
    public sealed class VisualWater : MonoBehaviour, IDisposable
    {
        public bool Visible = true;
        [Tooltip("Optional shared template. The component owns a clone, never the asset.")]
        public Material MaterialTemplate;
        [Tooltip("Coarse visual depth-color range; not a measured per-pixel water depth.")]
        [Range(1, 128)] public float DepthColorRangeMeters = 24;
        public Color UnderwaterTint = new Color(.025f, .20f, .24f, 1);
        public bool Underwater { get; private set; }
        public double UnderwaterDepthMeters { get; private set; }
        public double PlaneHalfExtentMeters { get; private set; }
        public double WorldCenterX { get; private set; }
        public double WorldCenterZ { get; private set; }
        public int MeshRebuildCount { get; private set; }
        public int LastBedSamples { get; private set; }
        public Mesh SurfaceMesh => mesh;
        public GameObject SurfaceObject => surface;
        public const int QuadsPerSide = 32;
        const double Tau = Math.PI * 2;
        static readonly string[] PhaseProperties = { "_WavePhase1", "_WavePhase2", "_WavePhase3", "_WavePhase4" };
        GameObject surface;
        MeshFilter filter;
        Mesh mesh;
        Material ownedMaterial;
        Camera targetCamera;
        string sourceIdentity;
        double builtSeaLevel, builtDepthRange;
        bool hintsInvalidated;
        bool disposed;

        /// <summary>The caller supplies the base bed field, not the expensive surface
        /// classifier. Inspection is visual only; this API never writes world cells.</summary>
        public void SetFrame(IWorldField field, WorldPoint globalCamera, WorldPoint renderOrigin,
            double seaLevelMeters, double viewDistanceMeters, Camera camera, bool inspection = false, IWorldField cameraBedField = null)
        {
            if (disposed) throw new ObjectDisposedException(nameof(VisualWater));
            Finite(seaLevelMeters, nameof(seaLevelMeters));
            Finite(viewDistanceMeters, nameof(viewDistanceMeters));
            if (viewDistanceMeters <= 0) throw new ArgumentOutOfRangeException(nameof(viewDistanceMeters));
            if (float.IsNaN(DepthColorRangeMeters) || float.IsInfinity(DepthColorRangeMeters) || DepthColorRangeMeters < 1 || DepthColorRangeMeters > 128)
                throw new ArgumentOutOfRangeException(nameof(DepthColorRangeMeters));
            targetCamera = camera;
            if (!Visible || field == null)
            {
                if (surface != null) surface.SetActive(false);
                Underwater = false; UnderwaterDepthMeters = 0; LastBedSamples = 0;
                return;
            }
            EnsureSurface();
            double radius = Math.Max(512, Math.Min(200000, viewDistanceMeters * 2));
            double snap = Math.Max(64, radius / 8);
            double centerX = Math.Floor(globalCamera.X / snap) * snap;
            double centerZ = Math.Floor(globalCamera.Z / snap) * snap;
            string identity = field.SourceIdentity;
            bool rebuild = hintsInvalidated || mesh == null || radius != PlaneHalfExtentMeters || centerX != WorldCenterX || centerZ != WorldCenterZ ||
                seaLevelMeters != builtSeaLevel || DepthColorRangeMeters != builtDepthRange || !string.Equals(sourceIdentity, identity, StringComparison.Ordinal);
            LastBedSamples = 0;
            if (rebuild)
            {
                Mesh replacement = BuildMesh(field, centerX, centerZ, radius, seaLevelMeters, DepthColorRangeMeters);
                filter.sharedMesh = replacement;
                if (mesh != null) Destroy(mesh);
                mesh = replacement; sourceIdentity = identity;
                WorldCenterX = centerX; WorldCenterZ = centerZ; PlaneHalfExtentMeters = radius;
                builtSeaLevel = seaLevelMeters; builtDepthRange = DepthColorRangeMeters; MeshRebuildCount++;
                hintsInvalidated = false;
            }
            surface.transform.position = new Vector3((float)(centerX - renderOrigin.X), (float)(seaLevelMeters - renderOrigin.Y), (float)(centerZ - renderOrigin.Z));
            surface.SetActive(true);
            // The wave phase is evaluated in double using short independent periods.
            // Rebasing never restarts the pattern or feeds huge floats to the shader.
            for (int i = 0; i < 4; i++) ownedMaterial.SetFloat(PhaseProperties[i], (float)WavePhase(renderOrigin.X, renderOrigin.Z, Time.unscaledTime, i));
            ownedMaterial.SetFloat("_MaximumDepthHint", DepthColorRangeMeters);
            double depth = seaLevelMeters - globalCamera.Y;
            if (depth > .04)
            {
                // Local matter can use a retained immutable model snapshot while
                // the wide visual hint field is an evictable remote cache. Keep
                // the camera occupancy decision paired with that matter source.
                double bed = (cameraBedField ?? field).Sample(globalCamera.X, globalCamera.Z).HeightMeters; LastBedSamples++;
                Underwater = IsSubmerged(globalCamera.Y, bed, seaLevelMeters);
            }
            else Underwater = false;
            UnderwaterDepthMeters = Underwater ? depth : 0;
        }
        void EnsureSurface()
        {
            if (surface != null) return;
            Material template = MaterialTemplate != null ? MaterialTemplate : Resources.Load<Material>("VisualWater");
            if (template == null) throw new InvalidOperationException("VisualWater material is missing. Include WorldScaleRuntime/Resources/VisualWater.mat.");
            ownedMaterial = new Material(template) { name = "Visual water runtime material" };
            surface = new GameObject("Mean sea visual surface"); surface.transform.SetParent(transform, false);
            filter = surface.AddComponent<MeshFilter>();
            var renderer = surface.AddComponent<MeshRenderer>(); renderer.sharedMaterial = ownedMaterial;
        }
        Mesh BuildMesh(IWorldField field, double centerX, double centerZ, double radius, double sea, double range)
        {
            int n = QuadsPerSide, stride = n + 1;
            var vertices = new List<Vector3>(stride * stride); var normals = new List<Vector3>(stride * stride);
            var colors = new List<Color32>(stride * stride); var triangles = new List<int>(n * n * 6);
            for (int z = 0; z <= n; z++) for (int x = 0; x <= n; x++)
            {
                double px = -radius + x * (radius * 2 / n), pz = -radius + z * (radius * 2 / n);
                double bed = field.Sample(centerX + px, centerZ + pz).HeightMeters; LastBedSamples++;
                double hint = Math.Max(0, Math.Min(range, sea - bed));
                vertices.Add(new Vector3((float)px, 0, (float)pz)); normals.Add(Vector3.up);
                // Vertex red is a normalized LINEAR depth hint, not an sRGB pigment.
                colors.Add(new Color32((byte)Math.Round(hint / range * 255), 0, 0, 255));
            }
            for (int z = 0; z < n; z++) for (int x = 0; x < n; x++)
            {int a = x + z * stride, b = a + stride; triangles.Add(a); triangles.Add(b); triangles.Add(a + 1); triangles.Add(a + 1); triangles.Add(b); triangles.Add(b + 1);}
            var result = new Mesh { name = "Bounded mean sea grid" };
            result.SetVertices(vertices); result.SetNormals(normals); result.SetColors(colors); result.SetTriangles(triangles, 0); result.RecalculateBounds();
            return result;
        }
        public static bool IsSubmerged(double cameraHeight, double bedHeight, double seaLevel)
        {
            Finite(cameraHeight, nameof(cameraHeight)); Finite(bedHeight, nameof(bedHeight)); Finite(seaLevel, nameof(seaLevel));
            return cameraHeight < seaLevel - .04 && cameraHeight > bedHeight + .02;
        }
        /// <summary>Call after a model-region revision or other bed-data update.
        /// Multiple invalidations coalesce into one bounded rebuild on SetFrame.</summary>
        public void InvalidateDepthHints() { hintsInvalidated = true; }
        public static double WavePhase(double originX, double originZ, double seconds, int wave)
        {
            Finite(originX, nameof(originX)); Finite(originZ, nameof(originZ)); Finite(seconds, nameof(seconds));
            double px, pz, directionX, directionZ, speed;
            switch (wave)
            {
                case 0: px = 32; pz = 64; directionX = directionZ = 1; speed = .7; break;
                case 1: px = 128; pz = 32; directionX = 1; directionZ = -1; speed = .42; break;
                case 2: px = 16; pz = 128; directionX = -1; directionZ = 1; speed = 1.1; break;
                case 3: px = 8; pz = 16; directionX = directionZ = 1; speed = 1.7; break;
                default: throw new ArgumentOutOfRangeException(nameof(wave));
            }
            return Mod(Mod(originX, px) / px * Tau * directionX + Mod(originZ, pz) / pz * Tau * directionZ + Mod(seconds * speed, Tau), Tau);
        }
        static double Mod(double value, double period) => value - Math.Floor(value / period) * period;
        static void Finite(double value, string name) { if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentOutOfRangeException(name); }
        void OnGUI()
        {
            if (!Visible || !Underwater || targetCamera == null) return;
            Color before = GUI.color; int depth = GUI.depth;
            try
            {
                GUI.depth = 1000; // Draw behind normal scene HUDs rather than tinting their text.
                GUI.color = new Color(UnderwaterTint.r, UnderwaterTint.g, UnderwaterTint.b,
                    (float)Math.Min(.55, .13 + UnderwaterDepthMeters * .025));
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            }
            finally { GUI.color = before; GUI.depth = depth; }
        }
        void OnDisable() { Underwater = false; UnderwaterDepthMeters = 0; if (surface != null) surface.SetActive(false); }
        void OnDestroy() { Dispose(); }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            Underwater = false; UnderwaterDepthMeters = 0; targetCamera = null;
            if (surface != null) Destroy(surface); if (mesh != null) Destroy(mesh); if (ownedMaterial != null) Destroy(ownedMaterial);
            surface = null; filter = null; mesh = null; ownedMaterial = null;
        }
    }
}
