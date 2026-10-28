using System.Collections.Generic;
using AshenVoxel.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace AshenVoxel.Rendering
{
    /// <summary>Material-interface surface extraction preserving material, persistent RGB, and corner AO; no per-voxel Unity objects.</summary>
    public sealed class RootBaseChunkMesher
    {
        const int Side = 16;
        readonly int[] mask = new int[Side * Side];
        readonly uint[] colorMask = new uint[Side * Side];
        // One-cell halo makes all six boundary tests local array reads.
        readonly MaterialId[] halo = new MaterialId[18 * 18 * 18];
        readonly uint[] colorHalo = new uint[18 * 18 * 18];
        readonly int[] p = new int[3];
        readonly List<Vector3> vertices = new List<Vector3>(4096);
        readonly List<Vector3> normals = new List<Vector3>(4096);
        readonly List<Color32> colors = new List<Color32>(4096);
        readonly List<int> triangles = new List<int>(6144);


        public int Build(VoxelWorld world, Int3 coordinate, Mesh destination, int maximumEmittedCellY = int.MaxValue)
        {
            vertices.Clear(); normals.Clear(); colors.Clear(); triangles.Clear();
            int bx = coordinate.x * Side, by = coordinate.y * Side, bz = coordinate.z * Side;
            for (int z = -1; z <= Side; z++)
            for (int y = -1; y <= Side; y++)
            for (int x = -1; x <= Side; x++)
            {
                int index = (x + 1) + 18 * ((y + 1) + 18 * (z + 1));
                halo[index] = world.Get(bx + x, by + y, bz + z);
                colorHalo[index] = halo[index] == MaterialId.Air ? 0 : world.GetColor(bx + x, by + y, bz + z);
            }
            for (int axis = 0; axis < 3; axis++)
            {
                int u = (axis + 1) % 3, v = (axis + 2) % 3;
                for (int slice = -1; slice < Side; slice++)
                for (int facing = 0; facing < 2; facing++)
                {
                    for (int j = 0; j < Side; j++)
                    for (int i = 0; i < Side; i++)
                    {
                        p[axis] = slice; p[u] = i; p[v] = j;
                        MaterialId a = Sample(p[0], p[1], p[2]);
                        p[axis]++;
                        MaterialId b = Sample(p[0], p[1], p[2]);
                        // Only the chunk containing a solid cell owns its exposed face.
                        int maskIndex = i + j * Side;
                        mask[maskIndex] = 0; colorMask[maskIndex] = 0;
                        if (facing == 0 && a != MaterialId.Air && a != b && slice >= 0)
                        {
                            p[axis] = slice;
                            if (by + p[1] >= maximumEmittedCellY) continue;
                            colorMask[maskIndex] = SampleColor(p[0], p[1], p[2]);
                            mask[maskIndex] = FaceKey(a, true, axis, u, v, slice, i, j);
                        }
                        else if (facing == 1 && b != MaterialId.Air && a != b && slice < Side - 1)
                        {
                            if (by + p[1] >= maximumEmittedCellY) continue;
                            colorMask[maskIndex] = SampleColor(p[0], p[1], p[2]);
                            mask[maskIndex] = FaceKey(b, false, axis, u, v, slice, i, j);
                        }
                    }
                    for (int j = 0; j < Side; j++)
                    for (int i = 0; i < Side;)
                    {
                        int key = mask[i + j * Side];
                        uint faceColor = colorMask[i + j * Side];
                        if (key == 0) { i++; continue; }
                        // Nonuniform AO retains cell corners: merging would erase interior creases.
                        bool mergeable = UniformOcclusion(key);
                        int width = 1;
                        while (mergeable && i + width < Side && mask[i + width + j * Side] == key && colorMask[i + width + j * Side] == faceColor) width++;
                        int height = 1;
                        bool stop = false;
                        while (mergeable && j + height < Side && !stop)
                        {
                            for (int x = 0; x < width; x++)
                                if (mask[i + x + (j + height) * Side] != key || colorMask[i + x + (j + height) * Side] != faceColor) { stop = true; break; }
                            if (!stop) height++;
                        }
                        p[axis] = slice + 1; p[u] = i; p[v] = j;
                        Vector3 origin = new Vector3(bx + p[0], by + p[1], bz + p[2]) * world.VoxelSize;
                        Vector3 du = Vector3.zero, dv = Vector3.zero, normal = Vector3.zero;
                        du[u] = width * world.VoxelSize; dv[v] = height * world.VoxelSize;
                        normal[axis] = key > 0 ? 1 : -1;
                        AddQuad(origin, du, dv, normal, key, faceColor);
                        for (int y = 0; y < height; y++)
                        for (int x = 0; x < width; x++) mask[i + x + (j + y) * Side] = 0;
                        i += width;
                    }
                }
            }
            destination.Clear();
            destination.indexFormat = IndexFormat.UInt32;
            destination.SetVertices(vertices); destination.SetNormals(normals);
            destination.SetColors(colors); destination.SetTriangles(triangles, 0, true);
            return triangles.Count / 6;
        }

        MaterialId Sample(int x, int y, int z) => halo[(x + 1) + 18 * ((y + 1) + 18 * (z + 1))];

        uint SampleColor(int x, int y, int z) => colorHalo[(x + 1) + 18 * ((y + 1) + 18 * (z + 1))];

        int FaceKey(MaterialId material, bool positive, int axis, int u, int v, int slice, int i, int j)
        {
            int packed = (int)material;
            p[axis] = positive ? slice + 1 : slice;
            p[u] = i; p[v] = j;
            int x = p[0], y = p[1], z = p[2];
            for (int corner = 0; corner < 4; corner++)
            {
                int su = corner == 0 || corner == 3 ? -1 : 1;
                int sv = corner < 2 ? -1 : 1;
                p[0] = x; p[1] = y; p[2] = z;
                p[u] += su;
                bool sideA = Sample(p[0], p[1], p[2]) != MaterialId.Air;
                p[u] -= su; p[v] += sv;
                bool sideB = Sample(p[0], p[1], p[2]) != MaterialId.Air;
                p[u] += su;
                bool diagonal = Sample(p[0], p[1], p[2]) != MaterialId.Air;
                int ao = sideA && sideB ? 0 : 3 - (sideA ? 1 : 0) - (sideB ? 1 : 0) - (diagonal ? 1 : 0);
                packed |= ao << (8 + corner * 2);
            }
            return positive ? packed : -packed;
        }

        static bool UniformOcclusion(int key)
        {
            int bits = System.Math.Abs(key) >> 8;
            int first = bits & 3;
            return ((bits >> 2) & 3) == first && ((bits >> 4) & 3) == first && ((bits >> 6) & 3) == first;
        }

        void AddQuad(Vector3 origin, Vector3 du, Vector3 dv, Vector3 normal, int key, uint faceColor)
        {
            int start = vertices.Count;
            vertices.Add(origin); vertices.Add(origin + du); vertices.Add(origin + du + dv); vertices.Add(origin + dv);
            int packed = System.Math.Abs(key);
            // Packed RGB is persistent appearance data; alpha packs material identity and the unchanged2-bit AO.
            Color32 color = new Color32((byte)(faceColor >> 16), (byte)(faceColor >> 8), (byte)faceColor, 255);
            int occlusion = packed >> 8;
            for (int k = 0; k < 4; k++)
            {
                color.a = VertexAppearance.Pack((MaterialId)(packed&255),(occlusion >> (k * 2)) & 3);
                normals.Add(normal); colors.Add(color);
            }
            // Pick the diagonal with the lower AO interpolation discontinuity.
            bool flip = (occlusion & 3) + ((occlusion >> 4) & 3) > ((occlusion >> 2) & 3) + ((occlusion >> 6) & 3);
            if (flip)
            {
                AddTriangle(start, start + 1, start + 3, key > 0);
                AddTriangle(start + 1, start + 2, start + 3, key > 0);
            }
            else
            {
                AddTriangle(start, start + 1, start + 2, key > 0);
                AddTriangle(start, start + 2, start + 3, key > 0);
            }
        }

        void AddTriangle(int a, int b, int c, bool positive)
        {
            triangles.Add(a); triangles.Add(positive ? b : c); triangles.Add(positive ? c : b);
        }
    }
}
