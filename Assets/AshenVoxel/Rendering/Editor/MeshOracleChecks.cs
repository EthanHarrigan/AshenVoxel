#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using AshenVoxel.Core;
using AshenVoxel.Rendering;
using UnityEngine;
namespace AshenVoxel.Rendering.Editor
{
public static class MeshOracleChecks {
 static readonly int[][] D={new[]{1,0,0},new[]{-1,0,0},new[]{0,1,0},new[]{0,-1,0},new[]{0,0,1},new[]{0,0,-1}};
 static string Face(int x,int y,int z,int axis,int sign,int material,uint rgb)=>$"{x},{y},{z},{axis},{sign},{material},{rgb:X8}";
 [UnityEditor.MenuItem("Ashen Voxel/Validation/Run randomized mesh oracle")]
 public static void Run(){
  var mesher=new GreedyChunkMesher();var mesh=new Mesh();int checkedFaces=0,checkedCorners=0;
  try {
  for(int seed=0;seed<12;seed++){
   var rng=new System.Random(seed);var w=new VoxelWorld(19,7,18,1,seed);
   for(int z=0;z<w.SizeZ;z++)for(int y=0;y<w.SizeY;y++)for(int x=0;x<w.SizeX;x++)if(rng.NextDouble()<0.19)
   {
    MaterialId material=(MaterialId)(1+rng.Next(10));
    uint tint=rng.Next(3)==0?VoxelColor.Pack((byte)rng.Next(256),(byte)rng.Next(256),(byte)rng.Next(256)):0;
    w.Set(x,y,z,material,tint);
   }
   var expected=new HashSet<string>();
   for(int z=0;z<w.SizeZ;z++)for(int y=0;y<w.SizeY;y++)for(int x=0;x<w.SizeX;x++)if(w.Get(x,y,z)!=MaterialId.Air)
    foreach(var d in D)if(w.Get(x+d[0],y+d[1],z+d[2])!=w.Get(x,y,z)){int axis=d[0]!=0?0:d[1]!=0?1:2;expected.Add(Face(x,y,z,axis,d[axis],(int)w.Get(x,y,z),w.GetColor(x,y,z)));}
   var actual=new HashSet<string>();
   foreach(var c in w.Chunks.Keys){
    mesher.Build(w,c,mesh); var p=mesh.vertices;var normals=mesh.normals;var colors=mesh.colors32;
    for(int q=0;q<p.Length;q+=4){
     int axis=normals[q].x!=0?0:normals[q].y!=0?1:2,sign=(int)normals[q][axis],u=(axis+1)%3,v=(axis+2)%3;
     int plane=(int)p[q][axis],u0=(int)p[q][u],u1=(int)p[q+2][u],v0=(int)p[q][v],v1=(int)p[q+2][v];
     uint faceColor=VoxelColor.Pack(colors[q].r,colors[q].g,colors[q].b);
     for(int k=1;k<4;k++)
      if(colors[q+k].r!=colors[q].r||colors[q+k].g!=colors[q].g||colors[q+k].b!=colors[q].b)
       throw new Exception("A quad must have one persistent RGB color; AO uses alpha separately.");
     int[] owner=new int[3];owner[axis]=plane-(sign>0?1:0);
     for(int j=v0;j<v1;j++)for(int i=u0;i<u1;i++){
      owner[u]=i;owner[v]=j; int material=(int)w.Get(owner[0],owner[1],owner[2]);
      if(VertexAppearance.Material(colors[q].a)!=material)throw new Exception("Material identity mismatch");
      string f=Face(owner[0],owner[1],owner[2],axis,sign,material,faceColor);if(!actual.Add(f))throw new Exception("Duplicate mesh face "+f);
     }
     for(int k=0;k<4;k++){
      int su=k==0||k==3?-1:1,sv=k<2?-1:1;
      owner[u]=su<0?u0:u1-1;owner[v]=sv<0?v0:v1-1;owner[axis]=plane-(sign>0?1:0)+sign;
      owner[u]+=su; bool a=w.Get(owner[0],owner[1],owner[2])!=MaterialId.Air;
      owner[u]-=su;owner[v]+=sv; bool b=w.Get(owner[0],owner[1],owner[2])!=MaterialId.Air;
      owner[u]+=su; bool diagonal=w.Get(owner[0],owner[1],owner[2])!=MaterialId.Air;
      int ao=a&&b?0:3-(a?1:0)-(b?1:0)-(diagonal?1:0);
      if(VertexAppearance.Occlusion(colors[q+k].a) != (byte)(64+ao*191/3))throw new Exception("AO mismatch seed "+seed);
      checkedCorners++;
     }
    }
   }
   if(!expected.SetEquals(actual))throw new Exception($"Surface mismatch seed{seed} expected{expected.Count} actual{actual.Count}");
   checkedFaces+=actual.Count;
  }
  Debug.Log($"PASS actual C# greedy mesh: {checkedFaces} unit faces and RGB colors match independent world oracle; {checkedCorners} vertex AO values match independent neighborhood oracle across 12 worlds.");
  } finally { UnityEngine.Object.DestroyImmediate(mesh); }
 }
}

}
#endif
