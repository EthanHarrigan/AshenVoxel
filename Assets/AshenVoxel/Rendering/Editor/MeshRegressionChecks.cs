#if UNITY_EDITOR
using System;
using AshenVoxel.Core;
using UnityEditor;
using UnityEngine;

namespace AshenVoxel.Rendering.Editor
{
    /// <summary>Real mesh regression checks, callable interactively or with -executeMethod.</summary>
    public static class MeshRegressionChecks
    {
        [MenuItem("Ashen Voxel/Validation/Run mesh regression checks")]
        public static void Run()
        {
            var builder = new GreedyChunkMesher();
            var mesh = new Mesh();
            try
            {
                var world = new VoxelWorld(32, 16, 16, 0.125f, 91);
                Expect(builder.Build(world, new Int3(0,0,0), mesh) == 0, "Empty grid has no geometry");
                world.Set(2,3,4, MaterialId.Stone);
                Expect(builder.Build(world, new Int3(0,0,0), mesh) == 6, "Isolated cell has six faces");
                Expect(mesh.vertexCount == 24, "Flat cell uses four vertices per face");
                CheckWinding(mesh);
                foreach (Color32 color in mesh.colors32) Expect(VertexAppearance.Occlusion(color.a) == 255, "Isolated cell has unoccluded corners");
                Expect(Mathf.Abs(SurfaceArea(mesh) - 6 * 0.125f * 0.125f) < 0.00001f, "Cell area scales with voxel size");
                world.Set(3,3,4, MaterialId.Stone);
                Expect(builder.Build(world, new Int3(0,0,0), mesh) == 6, "Identical adjacent cells greedily merge");
                Expect(Mathf.Abs(SurfaceArea(mesh) - 10 * 0.125f * 0.125f) < 0.00001f, "Hidden shared face removed");
                world.Set(3,3,4, MaterialId.Wood);
                Expect(builder.Build(world, new Int3(0,0,0), mesh) == 12, "Both material-interface sides remain available for opacity changes");
                CheckWinding(mesh);

                world = new VoxelWorld(32, 16, 16, 1f, 0);
                world.Set(15,4,5, MaterialId.Stone); world.Set(16,4,5, MaterialId.Stone);
                Expect(builder.Build(world, new Int3(0,0,0), mesh) == 5, "Left chunk suppresses internal seam");
                CheckWinding(mesh);
                Expect(builder.Build(world, new Int3(1,0,0), mesh) == 5, "Right chunk suppresses internal seam");
                CheckWinding(mesh);
                world.Set(16,4,5, MaterialId.Air);
                Expect(builder.Build(world, new Int3(0,0,0), mesh) == 6, "Deletion exposes neighboring chunk face");
                Expect(builder.Build(world, new Int3(1,0,0), mesh) == 0, "Empty chunk never owns a neighbor face");
                Expect(world.DirtyChunks.Contains(new Int3(0,0,0)) && world.DirtyChunks.Contains(new Int3(1,0,0)), "Boundary edit invalidates both chunks");

                world = new VoxelWorld(17, 3, 3, 1f, 0);
                for (int z = 0; z < 3; z++)
                for (int y = 0; y < 3; y++)
                for (int x = 0; x < 17; x++) world.Set(x,y,z, MaterialId.Stone);
                Expect(builder.Build(world, new Int3(0,0,0), mesh) == 5, "Full chunk merges its five visible faces");
                Expect(builder.Build(world, new Int3(1,0,0), mesh) == 5, "Partial chunk clips correctly at world limits");
                CheckWinding(mesh);
                world = new VoxelWorld(32, 16, 32, 1f, 0);
                world.Set(15,5,15, MaterialId.Stone);
                world.Set(14,6,15, MaterialId.Stone);
                world.Set(15,6,14, MaterialId.Stone);
                builder.Build(world, new Int3(0,0,0), mesh);
                Expect(AlphaAt(mesh, new Vector3(15,6,15), new Vector3(0,1,0)) == 64, "Two side occluders darken the common corner");
                CheckWinding(mesh);

                world = new VoxelWorld(32, 16, 32, 1f, 0);
                world.Set(15,5,15, MaterialId.Stone);
                world.Set(16,6,16, MaterialId.Stone);
                builder.Build(world, new Int3(0,0,0), mesh);
                byte seamAO = AlphaAt(mesh, new Vector3(16,6,16), new Vector3(0,1,0));
                Expect(seamAO < 255, "Diagonal neighbor chunk contributes corner AO");
                world.ClearDirty();
                world.Set(16,6,16, MaterialId.Air);
                Expect(world.DirtyChunks.Contains(new Int3(0,0,0)), "Diagonal AO owner chunk invalidated on deletion");
                builder.Build(world, new Int3(0,0,0), mesh);
                Expect(AlphaAt(mesh, new Vector3(16,6,16), new Vector3(0,1,0)) == 255, "Diagonal deletion removes stale AO");
                world = new VoxelWorld(16,16,16,1f,0);
                world.Set(5,5,5,MaterialId.Stone); world.Set(6,6,6,MaterialId.Stone);
                builder.Build(world,new Int3(0,0,0),mesh);
                Expect(AlphaAt(mesh,new Vector3(6,6,6),new Vector3(0,1,0)) == seamAO, "AO is invariant across chunk seams");
                CheckWinding(mesh);
                CheckPersistentColors(builder, mesh);
                CheckWorkerExtraction(builder,mesh);
                MeshOracleChecks.Run();
                Debug.Log("Ashen Voxel: all mesh regression checks passed.");
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }
        static void Expect(bool success, string description)
        {
            if (!success) throw new InvalidOperationException("Mesh regression failed: " + description);
        }
        static void CheckWorkerExtraction(GreedyChunkMesher builder,Mesh mesh)
        {
            var world=new VoxelWorld(32,16,32,.1f,714103);
            for(int z=0;z<32;z++)for(int y=0;y<8;y++)for(int x=0;x<32;x++)
            {
                int pattern=(x*17+y*11+z*23)%9;if(pattern>3)continue;
                world.Set(x,y,z,pattern==0?MaterialId.Leaves:MaterialId.Wood,VoxelColor.Pack((byte)(80+x*3),(byte)(90+y*7),(byte)(100+z*3)));
            }
            foreach(var coordinate in new[]{new Int3(0,0,0),new Int3(1,0,0),new Int3(0,0,1),new Int3(1,0,1)})
            {
                var materials=new MaterialId[18*18*18];var colors=new uint[materials.Length];
                for(int z=-1;z<=16;z++)for(int y=-1;y<=16;y++)for(int x=-1;x<=16;x++)
                {
                    int index=x+1+18*(y+1+18*(z+1)),wx=coordinate.x*16+x,wy=coordinate.y*16+y,wz=coordinate.z*16+z;
                    materials[index]=world.Get(wx,wy,wz);colors[index]=materials[index]==MaterialId.Air?0:world.GetColor(wx,wy,wz);
                }
                int quads=builder.Build(world,coordinate,mesh);
                var data=System.Threading.Tasks.Task.Run(()=>new GreedyChunkMesher().ExtractData(materials,colors,coordinate,.1f)).GetAwaiter().GetResult();
                var vertices=mesh.vertices;var normals=mesh.normals;var tint=mesh.colors32;var indices=mesh.triangles;
                Expect(data.Quads==quads&&data.Vertices.Length==vertices.Length&&data.Normals.Length==normals.Length&&data.Colors.Length==tint.Length&&data.Triangles.Length==indices.Length,"Worker snapshot preserves exact geometry lengths");
                for(int i=0;i<vertices.Length;i++)
                    Expect(data.Vertices[i].x==vertices[i].x&&data.Vertices[i].y==vertices[i].y&&data.Vertices[i].z==vertices[i].z&&data.Normals[i].x==normals[i].x&&data.Normals[i].y==normals[i].y&&data.Normals[i].z==normals[i].z&&data.Colors[i].r==tint[i].r&&data.Colors[i].g==tint[i].g&&data.Colors[i].b==tint[i].b&&data.Colors[i].a==tint[i].a,"Worker snapshot preserves every vertex/normal/intrinsic color/material/AO byte");
                for(int i=0;i<indices.Length;i++)Expect(data.Triangles[i]==indices[i],"Worker snapshot preserves exact winding and AO diagonal");
            }
        }
        static void CheckPersistentColors(GreedyChunkMesher builder, Mesh mesh)
        {
            var world=new VoxelWorld(32,16,32,0.1f,0);
            uint red=VoxelColor.Pack(255,0,0),blue=VoxelColor.Pack(0,0,255),green=VoxelColor.Pack(0,255,0);
            world.Set(2,3,4,MaterialId.Wood,red);
            world.Set(3,3,4,MaterialId.Wood,blue);
            Expect(builder.Build(world,new Int3(0,0,0),mesh)==10,"Same material with different RGB cannot merge");
            int redVertices=0,blueVertices=0;
            foreach(Color32 color in mesh.colors32)
            {
                if(color.r==255 && color.g==0 && color.b==0) redVertices++;
                if(color.r==0 && color.g==0 && color.b==255) blueVertices++;
            }
            Expect(redVertices==20 && blueVertices==20,"Packed red and blue channels survive mesh emission without swapping");
            world.Set(3,3,4,MaterialId.Wood,red);
            Expect(builder.Build(world,new Int3(0,0,0),mesh)==6,"Identical material/color/AO still greedily merges");
            world.Set(2,3,4,MaterialId.Wood,0);
            world.Set(3,3,4,MaterialId.Wood,VoxelColor.Base(MaterialId.Wood));
            Expect(builder.Build(world,new Int3(0,0,0),mesh)==6,"Implicit and explicit identical effective RGB merge");

            world=new VoxelWorld(32,16,32,0.1f,0);
            world.Set(16,5,16,MaterialId.Fruit,red);
            world.ClearDirty();
            long revision=world.Revision;
            world.Set(16,5,16,MaterialId.Fruit,green);
            Expect(world.Revision==revision+1,"Tint-only edits advance render revision");
            Expect(world.DirtyChunks.Contains(new Int3(1,0,1)) && world.DirtyChunks.Contains(new Int3(0,0,0)),"Tint-only edits invalidate owner and halo seam caches");
            builder.Build(world,new Int3(1,0,1),mesh);
            foreach(Color32 color in mesh.colors32)
                Expect(color.r==0 && color.g==255 && color.b==0,"Tint-only edit updates every emitted RGB channel");
            Expect(builder.Build(world,new Int3(0,0,0),mesh)==0,"Neighbor tint never gives empty chunk ownership");

            world=new VoxelWorld(32,16,16,0.1f,0);
            world.Set(15,4,5,MaterialId.Leaves,red);
            world.Set(16,4,5,MaterialId.Leaves,blue);
            Expect(builder.Build(world,new Int3(0,0,0),mesh)==5,"Tinted left seam has no hidden interface");
            foreach(Color32 color in mesh.colors32) Expect(color.r==255 && color.b==0,"Left seam retains its cell RGB");
            Expect(builder.Build(world,new Int3(1,0,0),mesh)==5,"Tinted right seam has no hidden interface");
            foreach(Color32 color in mesh.colors32) Expect(color.r==0 && color.b==255,"Right seam retains its cell RGB");
            world.Set(16,4,5,MaterialId.Air);
            Expect(builder.Build(world,new Int3(0,0,0),mesh)==6,"Exposed seam face uses remaining owner's RGB");
            foreach(Color32 color in mesh.colors32) Expect(color.r==255 && color.b==0,"Exposed seam never borrows removed tint");
        }

        static byte AlphaAt(Mesh mesh, Vector3 position, Vector3 normal)
        {
            Vector3[] vertices = mesh.vertices, normals = mesh.normals;
            Color32[] colors = mesh.colors32;
            for (int i=0; i<vertices.Length; i++)
                if ((vertices[i]-position).magnitude < 0.0001f && (normals[i]-normal).magnitude < 0.0001f)
                    return VertexAppearance.Occlusion(colors[i].a);
            throw new InvalidOperationException("AO test corner not found");
        }
        static float SurfaceArea(Mesh mesh)
        {
            float area = 0;
            Vector3[] p = mesh.vertices; int[] t = mesh.triangles;
            for (int i = 0; i < t.Length; i += 3)
                area += Vector3.Cross(p[t[i+1]] - p[t[i]], p[t[i+2]] - p[t[i]]).magnitude * 0.5f;
            return area;
        }
        static void CheckWinding(Mesh mesh)
        {
            Vector3[] p = mesh.vertices, n = mesh.normals; int[] t = mesh.triangles;
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 cross = Vector3.Cross(p[t[i+1]] - p[t[i]], p[t[i+2]] - p[t[i]]);
                Expect(Vector3.Dot(cross, n[t[i]]) > 0, "Triangle winding matches outward normal");
            }
        }
    }
}
#endif
