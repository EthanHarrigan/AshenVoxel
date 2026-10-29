#if UNITY_5_3_OR_NEWER
using System;
using System.Runtime.InteropServices;
using AshenVoxel.WorldScale;
using UnityEngine;
using UnityEngine.Rendering;

namespace AshenVoxel.WorldScaleRuntime
{
    /// <summary>
    /// GPU boundary for complete, immutable clipmap publications. One spare slice
    /// permits an atomic replacement without doubling every resident texture.
    /// Dispatch/copy/draw ordering is the graphics queue's ordering; this is not
    /// a GPU completion fence or a measured frame-time guarantee.
    /// </summary>
    public sealed class TerrainClipmapResources : IDisposable
    {
        public const int MaximumLevels = 16;
        public const int UploadCapacity = 262144;
        [StructLayout(LayoutKind.Sequential)]
        struct Update
        {
            public uint Texel;
            public float Height;
            public uint Tint, Appearance;
        }

        readonly int size, levels;
        readonly int[] slices;
        readonly bool[] published;
        readonly Update[] updates = new Update[UploadCapacity];
        readonly Vector4[] visibleLevels = new Vector4[MaximumLevels];
        readonly Vector4[] visibleOrigins = new Vector4[MaximumLevels];
        readonly Vector3[] farCorners = new Vector3[4];
        readonly MaterialPropertyBlock[] blocks;
        ComputeShader scatter;
        ComputeBuffer updateBuffer;
        RenderTexture heights, tint, appearance;
        int kernel, spareSlice, stagingLevel = -1, expectedSamples, uploadedSamples;
        bool disposed;

        public Material Material { get; private set; }
        public int Size => size;
        public int Levels => levels;
        public int StagingLevel => stagingLevel;
        public long TrianglesLastDraw { get; private set; }
        public int DrawCallsLastFrame { get; private set; }
        // Payload accounting, excluding driver allocation granularity and shader code.
        // There are no geometry, index, validity or fallback/backdrop GPU buffers.
        public long TextureBytes => (long)size * size * (levels + 1) * 12;
        public long GpuBytes => TextureBytes + UploadCapacity * 16L;
        public long OwnBytes => GpuBytes;
        public long CpuStagingBytes => UploadCapacity * 16L + MaximumLevels * 32L + 48;
        public static long EstimateGpuBytes(int texels, int levelCount)
            => checked((long)texels * texels * (levelCount + 1) * 12 + UploadCapacity * 16L);

        public TerrainClipmapResources(int size, int levels)
        {
            if (size < 16 || size > 1024 || (size & (size - 1)) != 0)
                throw new ArgumentOutOfRangeException(nameof(size));
            if (levels < 1 || levels > MaximumLevels) throw new ArgumentOutOfRangeException(nameof(levels));
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supports2DArrayTextures ||
                !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RFloat) ||
                !SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.RFloat) ||
                !SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.ARGB32) ||
                SystemInfo.copyTextureSupport == CopyTextureSupport.None)
                throw new NotSupportedException("Clipmaps require compute, texture arrays, RFloat/RGBA8 unordered writes and texture copies.");
            this.size = size; this.levels = levels; spareSlice = levels;
            slices = new int[levels]; published = new bool[levels];
            blocks = new MaterialPropertyBlock[levels * 4];
            for (int i = 0; i < levels; i++) slices[i] = i;
            for (int i = 0; i < blocks.Length; i++) blocks[i] = new MaterialPropertyBlock();
            try
            {
                var shader = Resources.Load<Shader>("TerrainClipmap");
                var source = Resources.Load<ComputeShader>("TerrainClipmapScatter");
                if (shader == null || source == null || !shader.isSupported)
                    throw new NotSupportedException("Clipmap shader resources are unavailable or unsupported; retain Legacy terrain.");
                Material = new Material(shader) { name = "Terrain clipmap (owned)", hideFlags = HideFlags.DontSave };
                scatter = UnityEngine.Object.Instantiate(source); scatter.hideFlags = HideFlags.DontSave;
                kernel = scatter.FindKernel("Scatter");
                heights = CreateArray("Clipmap heights", RenderTextureFormat.RFloat);
                tint = CreateArray("Clipmap tint (sRGB bytes)", RenderTextureFormat.ARGB32);
                appearance = CreateArray("Clipmap appearance", RenderTextureFormat.ARGB32);
                updateBuffer = new ComputeBuffer(UploadCapacity, 16, ComputeBufferType.Structured);
                Material.SetTexture("_CmHeights", heights); Material.SetTexture("_CmTint", tint);
                Material.SetTexture("_CmAppearance", appearance);
                scatter.SetBuffer(kernel, "_Updates", updateBuffer);
                scatter.SetTexture(kernel, "_Heights", heights); scatter.SetTexture(kernel, "_Tint", tint);
                scatter.SetTexture(kernel, "_Appearance", appearance);
            }
            catch { Dispose(); throw; }
        }

        RenderTexture CreateArray(string name, RenderTextureFormat format)
        {
            var texture = new RenderTexture(size, size, 0, format, RenderTextureReadWrite.Linear)
            {
                name = name, dimension = TextureDimension.Tex2DArray, volumeDepth = levels + 1,
                enableRandomWrite = true, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear,
                useMipMap = false, autoGenerateMips = false, hideFlags = HideFlags.DontSave
            };
            if (!texture.Create()) { Destroy(texture); throw new InvalidOperationException("Could not allocate " + name); }
            return texture;
        }

        public int ActiveSlice(int level) { CheckLevel(level); return slices[level]; }
        void CheckLevel(int level)
        {
            if (disposed) throw new ObjectDisposedException(nameof(TerrainClipmapResources));
            if (level < 0 || level >= levels) throw new ArgumentOutOfRangeException(nameof(level));
        }

        public void BeginUpdate(int level, bool copyPublished, int expectedSamples = -1)
        {
            CheckLevel(level);
            if (stagingLevel >= 0) throw new InvalidOperationException("Only one clipmap publication may own the spare slice.");
            if (expectedSamples < -1 || expectedSamples > size * size) throw new ArgumentOutOfRangeException(nameof(expectedSamples));
            if (copyPublished && !published[level]) throw new InvalidOperationException("Cannot copy an unpublished clipmap level.");
            if (!copyPublished && expectedSamples != size * size)
                throw new InvalidOperationException("A new slice requires an acknowledged full-window upload.");
            this.expectedSamples = expectedSamples; uploadedSamples = 0; stagingLevel = level;
            if (copyPublished)
            {
                Graphics.CopyTexture(heights, slices[level], 0, heights, spareSlice, 0);
                Graphics.CopyTexture(tint, slices[level], 0, tint, spareSlice, 0);
                Graphics.CopyTexture(appearance, slices[level], 0, appearance, spareSlice, 0);
            }
        }

        /// <summary>Region is in the candidate's absolute lattice. Samples are toroidal.
        /// A region is uploaded in bounded batches; no GPU readback is performed.</summary>
        public void Upload(int level, long ox, long oz, ClipmapRect region, ClipmapSample[] toroidalSamples)
        {
            CheckLevel(level);
            if (stagingLevel != level) throw new InvalidOperationException("No matching clipmap staging transaction.");
            if (toroidalSamples == null || toroidalSamples.Length != size * size) throw new ArgumentException("Wrong mirror size.", nameof(toroidalSamples));
            long dx = region.X - ox, dz = region.Z - oz;
            if (region.Width <= 0 || region.Height <= 0 || dx < 0 || dz < 0 ||
                dx + region.Width > size || dz + region.Height > size)
                throw new ArgumentOutOfRangeException(nameof(region));
            long count = (long)region.Width * region.Height;
            if (expectedSamples >= 0 && uploadedSamples + count > expectedSamples)
                throw new InvalidOperationException("Clipmap upload exceeds the declared transaction.");
            int n = 0, mask = size - 1;
            for (int z = 0; z < region.Height; z++) for (int x = 0; x < region.Width; x++)
            {
                int u = (int)((region.X + x) & mask), v = (int)((region.Z + z) & mask);
                ClipmapSample value = toroidalSamples[u + v * size];
                if (float.IsNaN(value.Height) || float.IsInfinity(value.Height))
                    throw new ArgumentException("Clipmap uploads must contain finite heights.", nameof(toroidalSamples));
                updates[n++] = new Update { Texel = (uint)(u | v << 16), Height = value.Height, Tint = value.Tint, Appearance = value.Appearance };
                if (n == UploadCapacity) { Dispatch(n); n = 0; }
            }
            if (n != 0) Dispatch(n);
            uploadedSamples += (int)count;
        }

        void Dispatch(int count)
        {
            updateBuffer.SetData(updates, 0, 0, count);
            scatter.SetInt("_Count", count); scatter.SetInt("_Level", spareSlice);
            scatter.Dispatch(kernel, (count + 63) / 64, 1, 1);
        }

        public void Publish(int level)
        {
            CheckLevel(level);
            if (stagingLevel != level || expectedSamples < 0 || uploadedSamples != expectedSamples)
                throw new InvalidOperationException("A clipmap slice cannot publish before all declared uploads have been dispatched.");
            int old = slices[level]; slices[level] = spareSlice; spareSlice = old;
            published[level] = true; stagingLevel = -1; expectedSamples = uploadedSamples = 0;
        }

        public void CancelUpdate()
        { stagingLevel = -1; expectedSamples = uploadedSamples = 0; }

        static Vector4 Origin(ClipmapLevelInfo level, WorldPoint renderOrigin, int mask)
            => new Vector4((float)(level.OriginX * level.Spacing - renderOrigin.X),
                (float)(level.OriginZ * level.Spacing - renderOrigin.Z), (float)(level.OriginX & mask), (float)(level.OriginZ & mask));

        /// <summary>Only a complete adjacent chain admitted by TerrainClipmapMath is drawable.
        /// Actual ring rectangles are shared with the independent CPU topology checks.</summary>
        public void Draw(Camera camera, int layer, WorldPoint renderOrigin, ClipmapLevelInfo[] visibleChain)
        {
            TrianglesLastDraw = 0; DrawCallsLastFrame = 0;
            if (camera == null || visibleChain == null || visibleChain.Length == 0) return;
            if (visibleChain.Length > levels) throw new ArgumentException("Invalid visible chain.", nameof(visibleChain));
            for (int i = 0; i < visibleChain.Length; i++)
            {
                var value = visibleChain[i]; CheckLevel(value.Level);
                if (!published[value.Level] || !value.Published || (i > 0 && value.Level != visibleChain[i - 1].Level + 1))
                    throw new ArgumentException("Every visible clipmap ancestor must be published and adjacent.", nameof(visibleChain));
                visibleLevels[i] = new Vector4((float)value.Spacing, slices[value.Level], 0, 0);
                visibleOrigins[i] = Origin(value, renderOrigin, size - 1);
            }
            Material.SetInt("_CmVisibleCount", visibleChain.Length);
            Material.SetVectorArray("_CmVisibleLevel", visibleLevels); Material.SetVectorArray("_CmVisibleOrigin", visibleOrigins);
            Material.SetFloat("_CmOriginY", (float)renderOrigin.Y);
            Material.SetVector("_SurfaceOriginPhase", new Vector4((float)PositivePhase(renderOrigin.X),
                (float)PositivePhase(renderOrigin.Y), (float)PositivePhase(renderOrigin.Z), 0));
            // farClipPlane is camera-space depth, not radial distance. Actual
            // corner rays cover perspective/orthographic and lens-shifted views.
            camera.CalculateFrustumCorners(new Rect(0, 0, 1, 1), camera.farClipPlane,
                Camera.MonoOrStereoscopicEye.Mono, farCorners);
            double viewRadiusSquared = (double)camera.farClipPlane * camera.farClipPlane;
            foreach (var corner in farCorners)
                viewRadiusSquared = Math.Max(viewRadiusSquared, (double)corner.x * corner.x + (double)corner.y * corner.y + (double)corner.z * corner.z);
            double viewRadius = Math.Sqrt(viewRadiusSquared) + 1; // conservative float/culling margin
            viewRadiusSquared = viewRadius * viewRadius;
            int blockIndex = 0;
            for (int i = 0; i < visibleChain.Length; i++)
            {
                var value = visibleChain[i]; bool hasParent = i + 1 < visibleChain.Length;
                var parent = hasParent ? visibleChain[i + 1] : value;
                var rectangles = TerrainClipmapMath.GetRingRects(value, i == 0 ? (ClipmapLevelInfo?)null : visibleChain[i - 1], size);
                Vector4 origin = Origin(value, renderOrigin, size - 1), parentOrigin = Origin(parent, renderOrigin, size - 1);
                double innerHalf = i == 0 ? 0 : (size * .5 - 2) * visibleChain[i - 1].Spacing;
                bool castsShadows = innerHalf < QualitySettings.shadowDistance;
                Vector3 eye = camera.transform.position;
                foreach (var rectangle in rectangles)
                {
                    if (rectangle.Width <= 0 || rectangle.Height <= 0) continue;
                    double x0 = (value.OriginX + rectangle.X) * value.Spacing - renderOrigin.X;
                    double z0 = (value.OriginZ + rectangle.Z) * value.Spacing - renderOrigin.Z;
                    double x1 = x0 + rectangle.Width * value.Spacing, z1 = z0 + rectangle.Height * value.Spacing;
                    double dx = Math.Max(Math.Max(x0 - eye.x, eye.x - x1), 0);
                    double dz = Math.Max(Math.Max(z0 - eye.z, eye.z - z1), 0);
                    // The stationary world anchor can extend hundreds of km.
                    // Horizontal distance is a lower bound on view distance;
                    // keep eligible off-screen shadow casters, never cull by height.
                    bool beyondView = dx * dx + dz * dz > viewRadiusSquared;
                    if (beyondView && !castsShadows) continue;
                    var block = blocks[blockIndex++]; block.Clear();
                    block.SetVector("_CmRect", new Vector4(rectangle.X, rectangle.Z, rectangle.Width, rectangle.Height));
                    block.SetVector("_CmLevel", new Vector4((float)value.Spacing, size, slices[value.Level], slices[parent.Level]));
                    block.SetVector("_CmOrigin", origin); block.SetVector("_CmParentOrigin", parentOrigin);
                    block.SetVector("_CmParent", new Vector4((float)parent.Spacing, hasParent ? 1 : 0, 0, 0));
                    block.SetVector("_CmParentOffset", hasParent
                        ? new Vector4(value.OriginX / 2 - parent.OriginX, value.OriginZ / 2 - parent.OriginZ, 0, 0) : Vector4.zero);
                    block.SetVector("_CmMorph", new Vector4(size / 8f, 2, 0, 0));
                    var render = new RenderParams(Material)
                    {
                        camera = camera, layer = layer, matProps = block,
                        worldBounds = new Bounds(camera.transform.position, Vector3.one * 2000000),
                        shadowCastingMode = beyondView ? ShadowCastingMode.ShadowsOnly : castsShadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
                        receiveShadows = true, lightProbeUsage = LightProbeUsage.Off
                    };
                    int vertices = checked(rectangle.Width * rectangle.Height * 6);
                    Graphics.RenderPrimitives(render, MeshTopology.Triangles, vertices, 1);
                    TrianglesLastDraw += vertices / 3; DrawCallsLastFrame++;
                }
            }
        }

        static void Destroy(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value);
        }
        static double PositivePhase(double value)
        { double phase = value % 4096; return phase < 0 ? phase + 4096 : phase; }
        static void Release(RenderTexture texture)
        { if (texture == null) return; texture.Release(); Destroy(texture); }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            updateBuffer?.Dispose(); updateBuffer = null;
            Release(heights); Release(tint); Release(appearance); heights = tint = appearance = null;
            Destroy(Material); Destroy(scatter); Material = null; scatter = null;
        }
    }
}
#endif
