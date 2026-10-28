from pathlib import Path
import hashlib
HERE=Path(__file__).resolve().parent;source=HERE.parent/'root-contact-mesh-prototype/RootBaseChunkMesher.cs'
assert hashlib.sha256(source.read_bytes()).hexdigest()=='b5b90cd21534fd69a066e0dfdedd60a4a328b8be67d4ca6c31d84c5ae64ff222'
s=source.read_text().replace('\r\n','\n')
s=s.replace('using System.Collections.Generic;','using System;\nusing System.Threading;').replace('using UnityEngine;\n','').replace('using UnityEngine.Rendering;\n','')
s=s.replace('namespace AshenVoxel.Rendering','namespace RootContactArrayMesher').replace('public sealed class RootBaseChunkMesher','public static class ArrayRootMesher')
a=s.index('        const int Side');b=s.index('            for (int axis = 0;',a)
s=s[:a]+'''        sealed class Halt:Exception { public readonly BuildStatus Status; public Halt(BuildStatus status){Status=status;} }
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
        void Check(){if(cancellation.IsCancellationRequested)throw new Halt(BuildStatus.Cancelled);if(!current())throw new Halt(BuildStatus.Stale);if(cancellation.IsCancellationRequested)throw new Halt(BuildStatus.Cancelled);}
        public MeshData Execute()
        {
            int bx=input.Coordinate.x*Side,by=input.Coordinate.y*Side,bz=input.Coordinate.z*Side;
            int maximumEmittedCellY=input.MaximumEmittedCellY;
'''+s[b:]
s=s.replace('                {\n                    for (int j = 0; j < Side; j++)','                {\n                    Check();\n                    for (int j = 0; j < Side; j++)',1)
s=s.replace('                        i += width;\n                    }','                        i += width;\n                    }\n                    Planes++;',1)
a=s.index('            destination.Clear();');b=s.index('\n        int FaceKey(',a)
s=s[:a]+'''            Check();
            var output=new MeshData(input.Identity,Trim(vertices,vertexCount),Trim(normals,vertexCount),Trim(colors,vertexCount),Trim(triangles,indexCount),vertexCount,indexCount);
            Check();return output;
        }
        static T[] Trim<T>(T[] source,int length)
        {if(length==source.Length)return source;if(length==0)return Array.Empty<T>();var result=new T[length];Array.Copy(source,result,length);return result;}
        MaterialId Sample(int x,int y,int z)=>input.Material(x,y,z);
        uint SampleColor(int x,int y,int z)=>input.Color(x,y,z);
'''+s[b:]
s=s.replace('Vector3','Float3').replace('Color32','Rgba32').replace('world.VoxelSize','input.VoxelSize')
s=s.replace('int start = vertices.Count;\n            vertices.Add(origin); vertices.Add(origin + du); vertices.Add(origin + du + dv); vertices.Add(origin + dv);','if(vertexCount+4>vertices.Length)throw new Halt(BuildStatus.CapacityRejected);\n            int start=vertexCount;\n            vertices[vertexCount++]=origin;vertices[vertexCount++]=origin+du;vertices[vertexCount++]=origin+du+dv;vertices[vertexCount++]=origin+dv;')
s=s.replace('normals.Add(normal); colors.Add(color);','normals[start+k]=normal;colors[start+k]=color;')
s=s.replace('triangles.Add(a); triangles.Add(positive ? b : c); triangles.Add(positive ? c : b);','triangles[indexCount++]=a;triangles[indexCount++]=positive?b:c;triangles[indexCount++]=positive?c:b;')
s=s.rstrip()[:-1]+'    }\n}\n'
(HERE/'ArrayRootMesher.cs').write_text('// Mechanically adapted from frozen RootBaseChunkMesher; see derive.py.\n'+s,encoding='utf8')
assert source.read_bytes() and 'UnityEngine' not in s and 'new Mesh(' not in s and 'destination.' not in s
print('Derived bounded managed-array mesher; frozen source untouched.')
