// Mechanically adapted from frozen RootBaseChunkMesher; see derive.py.
using System;
using System.Threading;
using AshenVoxel.Core;

namespace RootContactArrayMesher
{
    /// <summary>Material-interface surface extraction preserving material, persistent RGB, and corner AO; no per-voxel Unity objects.</summary>
    public static class ArrayRootMesher
    {
        sealed class Halt:Exception { public readonly BuildStatus Status; public Halt(BuildStatus status){Status=status;} }
        // One synchronous chunk per call. Checkpoints bound cancellation latency only;
        // mask/merge work is not an incremental Step or a frame-time guarantee.
        public static MeshAttempt Build(ChunkInput input,MeshCapacity capacity,Func<bool> isCurrent,CancellationToken cancellation=default)
        {
            if(input==null||capacity==null||isCurrent==null)throw new ArgumentNullException();
            Worker worker=null;
            try
            {
                if(cancellation.IsCancellationRequested)return new MeshAttempt(BuildStatus.Cancelled,null,"Cancelled",0);
                if(!isCurrent())return new MeshAttempt(BuildStatus.Stale,null,"Stale",0);
                worker=new Worker(input,capacity,isCurrent,cancellation);
                return new MeshAttempt(BuildStatus.Complete,worker.Execute(),"",worker.Planes);
            }
            catch(Halt h){return new MeshAttempt(h.Status,null,h.Status.ToString(),worker?.Planes??0);}
            catch(Exception e){return new MeshAttempt(BuildStatus.Failed,null,e.GetType().Name,worker?.Planes??0);}
        }
        sealed class Worker
        {
        const int Side=16;
        readonly int[] mask=new int[Side*Side];readonly uint[] colorMask=new uint[Side*Side];readonly int[] p=new int[3];
        readonly ChunkInput input;readonly Func<bool> current;readonly CancellationToken cancellation;
        readonly Float3[] vertices,normals;readonly Rgba32[] colors;readonly int[] triangles;
        int vertexCount,indexCount;public int Planes{get;private set;}
        public Worker(ChunkInput source,MeshCapacity capacity,Func<bool> isCurrent,CancellationToken token)
        {
            input=source;current=isCurrent;cancellation=token;
            vertices=new Float3[capacity.Quads*4];normals=new Float3[vertices.Length];colors=new Rgba32[vertices.Length];triangles=new int[capacity.Quads*6];
        }
        void Check(){if(cancellation.IsCancellationRequested)throw new Halt(BuildStatus.Cancelled);if(!current())throw new Halt(BuildStatus.Stale);}
        public MeshData Execute()
        {
            int bx=input.Coordinate.x*Side,by=input.Coordinate.y*Side,bz=input.Coordinate.z*Side;
            int maximumEmittedCellY=input.MaximumEmittedCellY;
            for (int axis = 0; axis < 3; axis++)
            {
                int u = (axis + 1) % 3, v = (axis + 2) % 3;
                for (int slice = -1; slice < Side; slice++)
                for (int facing = 0; facing < 2; facing++)
                {
                    Check();
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
                        Float3 origin = new Float3(bx + p[0], by + p[1], bz + p[2]) * input.VoxelSize;
                        Float3 du = Float3.zero, dv = Float3.zero, normal = Float3.zero;
                        du[u] = width * input.VoxelSize; dv[v] = height * input.VoxelSize;
                        normal[axis] = key > 0 ? 1 : -1;
                        AddQuad(origin, du, dv, normal, key, faceColor);
                        for (int y = 0; y < height; y++)
                        for (int x = 0; x < width; x++) mask[i + x + (j + y) * Side] = 0;
                        i += width;
                    }
                    Planes++;
                }
            }
            Check();
            var output=new MeshData(input.Identity,vertices,normals,colors,triangles,vertexCount,indexCount);
            Check();return output;
        }
        MaterialId Sample(int x,int y,int z)=>input.Material(x,y,z);
        uint SampleColor(int x,int y,int z)=>input.Color(x,y,z);

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

        void AddQuad(Float3 origin, Float3 du, Float3 dv, Float3 normal, int key, uint faceColor)
        {
            if(vertexCount+4>vertices.Length)throw new Halt(BuildStatus.CapacityRejected);
            int start=vertexCount;
            vertices[vertexCount++]=origin;vertices[vertexCount++]=origin+du;vertices[vertexCount++]=origin+du+dv;vertices[vertexCount++]=origin+dv;
            int packed = System.Math.Abs(key);
            // Packed RGB is persistent appearance data; alpha packs material identity and the unchanged2-bit AO.
            Rgba32 color = new Rgba32((byte)(faceColor >> 16), (byte)(faceColor >> 8), (byte)faceColor, 255);
            int occlusion = packed >> 8;
            for (int k = 0; k < 4; k++)
            {
                color.a = VertexAppearance.Pack((MaterialId)(packed&255),(occlusion >> (k * 2)) & 3);
                normals[start+k]=normal;colors[start+k]=color;
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
            triangles[indexCount++]=a;triangles[indexCount++]=positive?b:c;triangles[indexCount++]=positive?c:b;
        }
    }
    }
}
