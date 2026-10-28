using System.Collections.Generic;
using AshenVoxel.Core;
using UnityEngine;

namespace AshenVoxel.Rendering
{
    /// <summary>Budgeted chunk surface cache. VoxelWorld is the authority, meshes are disposable.</summary>
    public sealed class ChunkWorldRenderer : MonoBehaviour
    {
        [Tooltip("Optional override. Default is the included URP vertex-color shader.")]
        public Material SurfaceMaterial;
        [Tooltip("Optional Unity physics proxy. Grid queries do not require this.")]
        public bool GenerateColliders = false;
        [Min(0)] public int ColliderBuildsPerFrame = 1;
        public int MeshedChunks { get; private set; }
        public int FaceCount { get; private set; }
        public int PendingChunks => queued.Count + (world == null ? 0 : world.DirtyChunks.Count);
        public int PendingColliders => colliderQueued.Count;
        public int LastRebuiltChunks { get; private set; }
        public int TotalTriangles => FaceCount * 2;

        sealed class Surface
        {
            public GameObject Object;
            public Mesh Mesh;
            public MeshCollider Collider;
            public int Faces;
        }
        readonly Dictionary<Int3, Surface> surfaces = new Dictionary<Int3, Surface>();
        readonly Queue<Int3> pending = new Queue<Int3>();
        readonly HashSet<Int3> queued = new HashSet<Int3>();
        readonly Queue<Int3> colliderPending = new Queue<Int3>();
        readonly HashSet<Int3> colliderQueued = new HashSet<Int3>();
        readonly GreedyChunkMesher mesher = new GreedyChunkMesher();
        VoxelWorld world;
        Material ownedMaterial;

        public void Initialize(VoxelWorld source)
        {
            ReleaseSurfaces(); world = source;
            if (SurfaceMaterial == null)
            {
                // The Resources material retains its shader dependency in player builds.
                Material template = Resources.Load<Material>("VoxelSurface");
                if (template != null) ownedMaterial = new Material(template);
                else
                {
                    Shader shader = Shader.Find("AshenVoxel/WorldSurface");
                    if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
                    if (shader == null) throw new System.InvalidOperationException("Voxel surface shader is missing. Include Resources/VoxelSurface.mat in the build.");
                    ownedMaterial = new Material(shader);
                }
                ownedMaterial.name = "Ashen terrain runtime material";
                SurfaceMaterial = ownedMaterial;
            }
            if (SurfaceMaterial == ownedMaterial) ownedMaterial.SetFloat("_VoxelSize", world.VoxelSize);
            EnqueueAll();
        }

        void EnqueueAll()
        {
            if (world == null) return;
            // Sparse worlds must scale with residency, not the finite domain's volume.
            foreach (Int3 coordinate in world.Chunks.Keys) Enqueue(coordinate);
            // Removed chunks can still have cached surfaces that need to become empty.
            foreach (Int3 coordinate in surfaces.Keys) Enqueue(coordinate);
        }

        void Enqueue(Int3 c)
        {
            if (c.x < 0 || c.y < 0 || c.z < 0 || c.x * 16 >= world.SizeX || c.y * 16 >= world.SizeY || c.z * 16 >= world.SizeZ) return;
            if (queued.Add(c)) pending.Enqueue(c);
        }

        void IngestDirty()
        {
            foreach (Int3 c in world.DirtyChunks) Enqueue(c);
            world.ClearDirty();
        }

        public void RebuildBudget(int maxChunks)
        {
            LastRebuiltChunks = 0;
            if (world == null) return;
            IngestDirty();
            while (LastRebuiltChunks < Mathf.Max(0, maxChunks) && pending.Count > 0)
            {
                Int3 c = pending.Dequeue(); queued.Remove(c);
                Rebuild(c); LastRebuiltChunks++;
            }
            BakeColliders(ColliderBuildsPerFrame);
        }

        /// <summary>Blocking startup/debug operation. Use RebuildBudget during play.</summary>
        public void RebuildAll()
        {
            if (world == null) return;
            EnqueueAll(); IngestDirty();
            while (pending.Count > 0) { Int3 c = pending.Dequeue(); queued.Remove(c); Rebuild(c); }
            BakeColliders(int.MaxValue);
        }

        void Rebuild(Int3 coordinate)
        {
            if (!surfaces.TryGetValue(coordinate, out Surface surface))
            {
                // Unallocated chunks contain only air and have no owned boundary faces.
                if (!world.Chunks.ContainsKey(coordinate)) return;
                var go = new GameObject($"Chunk {coordinate.x},{coordinate.y},{coordinate.z}");
                go.transform.SetParent(transform, false);
                var mesh = new Mesh { name = go.name + " surface" };
                mesh.MarkDynamic();
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = SurfaceMaterial;
                surface = new Surface { Object = go, Mesh = mesh };
                surfaces.Add(coordinate, surface);
            }
            bool hadFaces = surface.Faces > 0;
            FaceCount -= surface.Faces;
            surface.Faces = mesher.Build(world, coordinate, surface.Mesh);
            FaceCount += surface.Faces;
            if (hadFaces != (surface.Faces > 0)) MeshedChunks += surface.Faces > 0 ? 1 : -1;
            surface.Object.GetComponent<MeshRenderer>().enabled = surface.Faces > 0;
            // Prevent stale geometry from blocking an excavated passage while baking is deferred.
            if (surface.Collider != null) surface.Collider.enabled = false;
            if (GenerateColliders && colliderQueued.Add(coordinate)) colliderPending.Enqueue(coordinate);
        }

        void BakeColliders(int budget)
        {
            if (!GenerateColliders) return;
            for (int count = 0; count < budget && colliderPending.Count > 0; count++)
            {
                Int3 c = colliderPending.Dequeue(); colliderQueued.Remove(c);
                if (!surfaces.TryGetValue(c, out Surface s) || s.Faces == 0) continue;
                if (s.Collider == null) s.Collider = s.Object.AddComponent<MeshCollider>();
                s.Collider.sharedMesh = null; s.Collider.sharedMesh = s.Mesh;
                s.Collider.enabled = true;
            }
        }

        void ReleaseSurfaces()
        {
            foreach (Surface s in surfaces.Values) { Destroy(s.Mesh); Destroy(s.Object); }
            surfaces.Clear(); pending.Clear(); queued.Clear(); colliderPending.Clear(); colliderQueued.Clear();
            FaceCount = 0; MeshedChunks = 0;
        }
        void OnDestroy()
        {
            ReleaseSurfaces();
            if (ownedMaterial != null) Destroy(ownedMaterial);
        }
    }
}
