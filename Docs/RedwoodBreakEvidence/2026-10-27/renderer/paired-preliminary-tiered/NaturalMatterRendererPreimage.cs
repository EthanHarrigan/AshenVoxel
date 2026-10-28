using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AshenVoxel.Core;
using AshenVoxel.Core.Experimental;
using UnityEngine;
using UnityEngine.Rendering;
using AshenVoxel.Rendering;
using NV=System.Numerics.Vector3;
using NQ=System.Numerics.Quaternion;

namespace AshenVoxel.Exploration.Experimental
{
    public sealed class NaturalMatterRenderSourcePreimage
    {
        public readonly struct Part
        {
            public readonly Mesh Mesh;public readonly int Submesh;public readonly bool Shadows;
            public Part(Mesh mesh,int submesh,bool shadows){Mesh=mesh;Submesh=submesh;Shadows=shadows;}
        }
        public readonly Part[] Parts;public readonly Material Material;
        public NaturalMatterRenderSourcePreimage(Part[] parts,Material material){Parts=(Part[])parts.Clone();Material=material;}
    }
    /// <summary>Replaceable, bounded visual cache; exact10cm ownership stays in
    /// NaturalTreePhysics. Nine rigid meshes move by transforms, never by rewriting
    /// every member vertex each frame. Large specimens explicitly use render LOD.</summary>
    public sealed class NaturalMatterRendererPreimage:IDisposable
    {
        const int GroupCount=9,MaximumBins=32768,MaximumDebris=4096,BatchSize=1023;
        readonly GameObject[] objects=new GameObject[GroupCount];
        readonly MeshFilter[] filters=new MeshFilter[GroupCount];
        readonly MeshRenderer[] renderers=new MeshRenderer[GroupCount];
        readonly Mesh[] published=new Mesh[GroupCount],staging=new Mesh[GroupCount];
        Geometry[] geometry=new Geometry[GroupCount];
        Task<BuildResult> worker;CancellationTokenSource cancellation;
        readonly Matrix4x4[] matrices=new Matrix4x4[BatchSize];
        readonly Vector4[] tints=new Vector4[BatchSize];
        readonly MaterialPropertyBlock properties=new MaterialPropertyBlock();
        readonly Material solidMaterial,debrisMaterial;
        readonly Mesh debrisMesh;
        NaturalMatterRenderSourcePreimage borrowed;readonly Material[] borrowedMaterials=new Material[GroupCount];
        readonly MaterialPropertyBlock borrowedProperties=new MaterialPropertyBlock();Texture3D ownerMask;bool maskEnabled;
        Vector4 maskOrigin,maskSize;NV[] canonicalCenters=new NV[8];
        const int MaximumChunkEntries=262144,MaximumPatchVertices=262144;
        readonly int drawLayer;
        NaturalTreePhysics physics;
        int factor=1,uploadCursor,buildingRevision=-1,publishedRevision=-1,geometryBins;
        bool building,visible,disposed;
        public bool Ready {get;private set;}
        public string Error {get;private set;}
        public Camera DrawCamera {get;set;}
        public bool Current=>Ready&&physics!=null&&!physics.StructureBusy&&publishedRevision==physics.StructureRevision;
        public float RenderingCellSize=>factor*.1f;
        public int DrawnDebris {get;private set;}
        public int DebrisCandidates=>physics?.PendingDebris??0;
        public bool DebrisSampled=>DebrisCandidates>MaximumDebris;
        public int GeometryBins=>geometryBins;
        public double LastSnapshotMilliseconds {get;private set;}
        public double LastWorkerMilliseconds {get;private set;}
        public long LastWorkerCandidates {get;private set;}
        public int LastBuildCandidates {get;private set;}

        sealed class Geometry
        {
            public readonly List<Vector3> Vertices=new List<Vector3>(),Normals=new List<Vector3>();
            public readonly List<Color32> Colors=new List<Color32>();
            public readonly List<int> Indices=new List<int>();
            public void Clear()
            {
                Vertices.Clear();Normals.Clear();Colors.Clear();Indices.Clear();
                // A formerly large island must not retain a whole-tree list
                // capacity in every one of the eight reusable group slots.
                if(Vertices.Capacity>8192)Vertices.TrimExcess();if(Normals.Capacity>8192)Normals.TrimExcess();
                if(Colors.Capacity>8192)Colors.TrimExcess();if(Indices.Capacity>12288)Indices.TrimExcess();
            }
        }
        readonly struct BinKey:IEquatable<BinKey>
        {
            public readonly int X,Y,Z,Group,Factor;
            public BinKey(int x,int y,int z,int group,int factor){X=x;Y=y;Z=z;Group=group;Factor=factor;}
            public BinKey Neighbour(int axis,int sign)=>new BinKey(X+(axis==0?sign:0),Y+(axis==1?sign:0),Z+(axis==2?sign:0),Group,Factor);
            public bool Equals(BinKey b)=>X==b.X&&Y==b.Y&&Z==b.Z&&Group==b.Group&&Factor==b.Factor;
            public override bool Equals(object b)=>b is BinKey key&&Equals(key);
            public override int GetHashCode(){unchecked{return ((((X*397)^Y)*397^Z)*397^Group)*397^Factor;}}
        }
        struct Bin
        {
            public BinKey Key;public Vector3 Center;public MaterialId Material;public Color32 Color;
        }
        static Vector3 Unity(NV v)=>new Vector3(v.X,v.Y,v.Z);
        static Quaternion Unity(NQ q)=>new Quaternion(q.X,q.Y,q.Z,q.W);
        static int Floor(int value,int divisor){int q=value/divisor;return value<0&&value%divisor!=0?q-1:q;}
        static Color32 Color(CargoKey matter)
        {
            uint rgb=matter.Tint==0?VoxelColor.Base(matter.Material):matter.Tint;
            return new Color32((byte)(rgb>>16),(byte)(rgb>>8),(byte)rgb,VertexAppearance.Pack(matter.Material,3));
        }
        public NaturalMatterRendererPreimage(Transform parentTransform,Material template)
        {
            if(parentTransform==null)throw new ArgumentNullException(nameof(parentTransform));
            if(template==null)throw new ArgumentNullException(nameof(template));
            drawLayer=parentTransform.gameObject.layer;
            solidMaterial=new Material(template){name="Natural matter rigid surfaces"};solidMaterial.SetFloat("_MatterInstances",0);
            debrisMaterial=new Material(template){name="Natural matter weighted debris",enableInstancing=true};debrisMaterial.SetFloat("_MatterInstances",1);
            solidMaterial.SetFloat("_VoxelWindStaticInstances",0);debrisMaterial.SetFloat("_VoxelWindStaticInstances",1);
            solidMaterial.SetFloat("_MatterRigid",1);debrisMaterial.SetFloat("_MatterRigid",1);
            debrisMesh=new Mesh{name="Natural matter shared unit debris cube",indexFormat=IndexFormat.UInt32};
            var cube=new Geometry();AppendCube(cube,Vector3.zero,1,new Color32(255,255,255,VertexAppearance.Pack(MaterialId.Wood,3)),null);
            Upload(debrisMesh,cube);
            for(int i=0;i<GroupCount;i++)
            {
                objects[i]=new GameObject(i==0?"Natural matter source":"Natural matter section "+(i-1));
                objects[i].layer=drawLayer;
                objects[i].transform.SetParent(parentTransform,false);filters[i]=objects[i].AddComponent<MeshFilter>();
                renderers[i]=objects[i].AddComponent<MeshRenderer>();renderers[i].sharedMaterial=solidMaterial;
                renderers[i].shadowCastingMode=ShadowCastingMode.On;renderers[i].receiveShadows=true;renderers[i].lightProbeUsage=LightProbeUsage.Off;renderers[i].enabled=false;
            }
        }
        public void SetPhysics(NaturalTreePhysics value,NaturalMatterRenderSourcePreimage source=null)
        {
            if(disposed)throw new ObjectDisposedException(nameof(NaturalMatterRendererPreimage));
            CancelBuild();ReleaseBorrowed();physics=value;borrowed=source;Ready=false;Error=null;geometryBins=0;publishedRevision=-1;factor=1;DrawnDebris=0;
            if(source!=null)
            {
                for(int i=0;i<GroupCount;i++){borrowedMaterials[i]=new Material(source.Material);borrowedMaterials[i].SetFloat("_MatterChunkMaskEnabled",0);if(i>0)borrowedMaterials[i].SetFloat("_MatterRigid",1);}
                if(physics!=null&&!physics.HasCut){Ready=true;publishedRevision=physics.StructureRevision;}
            }
            renderers[0].sharedMaterial=source!=null?borrowedMaterials[0]:solidMaterial;
            if(physics!=null&&borrowed==null)while((long)physics.Count/(factor*factor*factor)>MaximumBins)factor*=2;
            for(int i=0;i<GroupCount;i++){filters[i].sharedMesh=null;Destroy(published[i]);published[i]=null;renderers[i].enabled=false;}
        }
        public void SetVisible(bool value)
        {
            visible=value;if(!value)for(int i=0;i<GroupCount;i++)renderers[i].enabled=false;
        }
        public void Tick(Vector3 renderOriginOffset)
        {
            if(disposed)return;LastBuildCandidates=0;DrawnDebris=0;if(physics==null)return;
            // A support commit changes local membership before its final revision;
            // do not capture that intermediate state, or chase per-cell release.
            if(physics.StructureBusy){if(building)CancelBuild();}
            else
            {
                if(building&&buildingRevision!=physics.StructureRevision)CancelBuild();
                if(!building&&Error==null&&publishedRevision!=physics.StructureRevision&&(worker==null||worker.IsCompleted))BeginBuild();
                if(building)AdvanceBuild();
            }
            objects[0].transform.SetPositionAndRotation(renderOriginOffset,Unity(physics.SourceRotation));
            renderers[0].enabled=visible&&Ready&&published[0]!=null&&published[0].vertexCount>0&&physics.SourceCells>0;
            for(int i=1;i<GroupCount;i++)
            {
                bool active=physics.BodyPose(i-1,out var position,out var rotation);
                objects[i].transform.SetPositionAndRotation(renderOriginOffset+Unity(position),Unity(rotation));
                renderers[i].enabled=visible&&Ready&&active&&published[i]!=null&&published[i].vertexCount>0;
            }
            if(visible&&Ready){DrawBorrowed(renderOriginOffset);DrawDebris(renderOriginOffset);}
        }
        sealed class BuildResult
        {
            public Geometry[] Geometry;public int Factor,Bins,Revision;public byte[] Mask;public int ChunkX,ChunkY,ChunkZ;public Vector4 Origin,Extent;public NV[] Centers;public long Candidates;public double Milliseconds;
        }
        void BeginBuild()
        {
            // Capture only compact membership on the owner thread. Cell/material
            // records are immutable and shared; no full Cube[] or voxel scan here.
            if(worker!=null){try{worker.GetAwaiter().GetResult();}catch(Exception){/* Superseded worker: observe without publishing. */}worker=null;}
            cancellation?.Dispose();cancellation=new CancellationTokenSource();
            long started=Stopwatch.GetTimestamp();var snapshot=physics.CaptureRenderSnapshot();
            LastSnapshotMilliseconds=(Stopwatch.GetTimestamp()-started)*1000.0/Stopwatch.Frequency;
            building=true;buildingRevision=snapshot.Revision;uploadCursor=0;
            int initial=factor;var token=cancellation.Token;bool exact=borrowed!=null;worker=Task.Run(()=>exact?BuildPrototype(snapshot,token):Build(snapshot,initial,token),token);
        }
        BuildResult pendingResult;
        void CancelBuild()
        {
            building=false;cancellation?.Cancel();
            for(int i=0;i<GroupCount;i++){Destroy(staging[i]);staging[i]=null;}
            geometry=new Geometry[GroupCount];pendingResult=null;
            // A cancelled worker keeps its private buffers until it observes the
            // token. Reassignment never starts a second worker alongside it.
        }
        static BuildResult Build(NaturalTreePhysics.RenderSnapshot snapshot,int initial,CancellationToken token)
        {
            long started=Stopwatch.GetTimestamp(),candidates=0;int factor=initial;
            var lookup=new Dictionary<BinKey,int>(MaximumBins);var bins=new List<Bin>(MaximumBins);
            while(true)
            {
                lookup.Clear();bins.Clear();bool retry=false;
                for(int cursor=0;cursor<snapshot.Count;cursor++)
                {
                    if((cursor&127)==0)token.ThrowIfCancellationRequested();candidates++;
                    int group=snapshot.GroupAt(cursor);if(group<0)continue;
                    if(group>=GroupCount)throw new InvalidOperationException("Invalid rigid matter ownership");
                    var cube=snapshot.CellAt(cursor);int cellFactor=factor;
                    if(snapshot.HasCut)
                    {
                        var cut=snapshot.LastCutCell;
                        if(Math.Abs(cube.Cell.x-cut.x)<=4&&Math.Abs(cube.Cell.y-cut.y)<=4&&Math.Abs(cube.Cell.z-cut.z)<=4)cellFactor=1;
                        else if(factor>16&&Floor(cube.Cell.x,factor)==Floor(cut.x,factor)&&Floor(cube.Cell.y,factor)==Floor(cut.y,factor)&&Floor(cube.Cell.z,factor)==Floor(cut.z,factor))cellFactor=16;
                    }
                    var key=new BinKey(Floor(cube.Cell.x,cellFactor),Floor(cube.Cell.y,cellFactor),Floor(cube.Cell.z,cellFactor),group,cellFactor);
                    if(lookup.TryGetValue(key,out int existing))
                    {
                        if(MaterialCatalog.BlocksActorBody(cube.Matter.Material)&&!MaterialCatalog.BlocksActorBody(bins[existing].Material))
                        {var b=bins[existing];b.Material=cube.Matter.Material;b.Color=Color(cube.Matter);bins[existing]=b;}continue;
                    }
                    if(bins.Count==MaximumBins){factor=checked(factor*2);retry=true;break;}
                    var local=snapshot.LocalPosition(cursor);
                    var delta=new Vector3((float)((key.X*(double)cellFactor+cellFactor*.5-cube.Cell.x-.5)*.1),
                        (float)((key.Y*(double)cellFactor+cellFactor*.5-cube.Cell.y-.5)*.1),(float)((key.Z*(double)cellFactor+cellFactor*.5-cube.Cell.z-.5)*.1));
                    lookup.Add(key,bins.Count);bins.Add(new Bin{Key=key,Center=Unity(local)+delta,Material=cube.Matter.Material,Color=Color(cube.Matter)});
                }
                if(retry)continue;
                var output=new Geometry[GroupCount];for(int i=0;i<GroupCount;i++)output[i]=new Geometry();
                for(int cursor=0;cursor<bins.Count;cursor++)
                {
                    if((cursor&127)==0)token.ThrowIfCancellationRequested();candidates++;var bin=bins[cursor];
                    AppendCube(output[bin.Key.Group],bin.Center,bin.Key.Factor*.1f,bin.Color,(axis,sign)=>
                        lookup.TryGetValue(bin.Key.Neighbour(axis,sign),out int neighbour)&&bins[neighbour].Material==bin.Material);
                }
                token.ThrowIfCancellationRequested();return new BuildResult{Geometry=output,Factor=factor,Bins=bins.Count,Revision=snapshot.Revision,Candidates=candidates,
                    Milliseconds=(Stopwatch.GetTimestamp()-started)*1000.0/Stopwatch.Frequency};
            }
        }
        static BuildResult BuildPrototype(NaturalTreePhysics.RenderSnapshot snapshot,CancellationToken token)
        {
            long started=Stopwatch.GetTimestamp(),candidates=0;int nx=(snapshot.SizeX+15)/16,ny=(snapshot.SizeY+15)/16,nz=(snapshot.SizeZ+15)/16;
            long count=(long)nx*ny*nz;if(count>MaximumChunkEntries)throw new InvalidOperationException("Matter chunk-mask capacity exceeded; original retained");
            var owners=new int[(int)count];for(int i=0;i<owners.Length;i++)owners[i]=-2;
            int Key(Int3 p)=>(p.x>>4)+nx*((p.y>>4)+ny*(p.z>>4));
            for(int i=0;i<snapshot.Count;i++)
            {
                if((i&127)==0)token.ThrowIfCancellationRequested();var cell=snapshot.CellAt(i);int key=Key(cell.Cell),group=snapshot.GroupAt(i);candidates++;
                if(owners[key]==-2)owners[key]=group;else if(owners[key]!=group)owners[key]=-3;
            }
            // Uniform chunks beside a changed owner need new cut caps/AO too;
            // the borrowed surface never contained their old internal interfaces.
            var boundary=new bool[owners.Length];
            for(int z=0;z<nz;z++)for(int y=0;y<ny;y++)for(int x=0;x<nx;x++)
            {
                int k=x+nx*(y+ny*z);if(owners[k]<0)continue;
                for(int dz=-1;dz<=1;dz++)for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
                {
                    int a=x+dx,b=y+dy,c=z+dz;if(a<0||a>=nx||b<0||b>=ny||c<0||c>=nz)continue;
                    int neighbour=owners[a+nx*(b+ny*c)];if(neighbour!=-2&&neighbour!=owners[k])boundary[k]=true;
                }
            }
            for(int i=0;i<owners.Length;i++)if(boundary[i])owners[i]=-3;
            var mask=new byte[owners.Length];var mixed=new List<Int3>();var needed=new bool[owners.Length];
            for(int z=0;z<nz;z++)for(int y=0;y<ny;y++)for(int x=0;x<nx;x++)
            {
                int k=x+nx*(y+ny*z);if(owners[k]>=0)mask[k]=(byte)(owners[k]+1);if(owners[k]!=-3)continue;
                mixed.Add(new Int3(x,y,z));
                for(int dz=-1;dz<=1;dz++)for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
                {int a=x+dx,b=y+dy,c=z+dz;if(a>=0&&a<nx&&b>=0&&b<ny&&c>=0&&c<nz)needed[a+nx*(b+ny*c)]=true;}
            }
            var selected=new Dictionary<Int3,int>();
            for(int i=0;i<snapshot.Count;i++)
            {if((i&127)==0)token.ThrowIfCancellationRequested();var c=snapshot.CellAt(i);if(snapshot.GroupAt(i)>=0&&needed[Key(c.Cell)])selected.Add(c.Cell,i);candidates++;}
            var output=new Geometry[GroupCount];for(int i=0;i<GroupCount;i++)output[i]=new Geometry();
            var mesher=new GreedyChunkMesher();var materials=new MaterialId[18*18*18];var colors=new uint[materials.Length];int vertices=0;
            foreach(var chunk in mixed)
            {
                for(int group=0;group<GroupCount;group++)
                {
                    token.ThrowIfCancellationRequested();bool occupied=false;
                    for(int z=-1;z<=16;z++)for(int y=-1;y<=16;y++)for(int x=-1;x<=16;x++)
                    {
                        int at=(x+1)+18*((y+1)+18*(z+1));var p=new Int3(chunk.x*16+x,chunk.y*16+y,chunk.z*16+z);materials[at]=MaterialId.Air;colors[at]=0;
                        if(!selected.TryGetValue(p,out int index)||snapshot.GroupAt(index)!=group)continue;
                        var c=snapshot.CellAt(index);materials[at]=c.Matter.Material;colors[at]=c.Matter.Tint==0?VoxelColor.Base(c.Matter.Material):c.Matter.Tint;
                        if(x>=0&&x<16&&y>=0&&y<16&&z>=0&&z<16)occupied=true;
                    }
                    if(!occupied)continue;var data=mesher.ExtractData(materials,colors,chunk,.1f);vertices=checked(vertices+data.Vertices.Length);
                    if(vertices>MaximumPatchVertices)throw new InvalidOperationException("Exact mixed-chunk patch capacity exceeded; original retained");
                    var geometry=output[group];int first=geometry.Vertices.Count;var center=group==0?NV.Zero:snapshot.CanonicalCenter(group-1);
                    var offset=new Vector3(snapshot.Anchor.x*.1f+center.X,snapshot.Anchor.y*.1f+center.Y,snapshot.Anchor.z*.1f+center.Z);
                    foreach(var v in data.Vertices)geometry.Vertices.Add(new Vector3(v.x-offset.x,v.y-offset.y,v.z-offset.z));
                    geometry.Normals.AddRange(data.Normals);geometry.Colors.AddRange(data.Colors);foreach(int index in data.Triangles)geometry.Indices.Add(first+index);
                }
            }
            token.ThrowIfCancellationRequested();var centers=new NV[8];for(int i=0;i<8;i++)centers[i]=snapshot.CanonicalCenter(i);
            return new BuildResult{Geometry=output,Factor=1,Bins=mixed.Count,Revision=snapshot.Revision,Mask=mask,ChunkX=nx,ChunkY=ny,ChunkZ=nz,
                Origin=new Vector4(snapshot.Anchor.x*.1f,snapshot.Anchor.y*.1f,snapshot.Anchor.z*.1f,0),Extent=new Vector4(nx*1.6f,ny*1.6f,nz*1.6f,0),Centers=centers,
                Candidates=candidates,Milliseconds=(Stopwatch.GetTimestamp()-started)*1000.0/Stopwatch.Frequency};
        }
        void DrawBorrowed(Vector3 origin)
        {
            if(borrowed==null)return;
            for(int group=0;group<GroupCount;group++)
            {
                if(group>0&&!maskEnabled)continue;Vector3 position=origin;Quaternion rotation=Unity(physics.SourceRotation);
                if(group>0){if(!physics.BodyPose(group-1,out var p,out var q))continue;rotation=Unity(q);position=origin+Unity(p-NV.Transform(canonicalCenters[group-1],q));}
                borrowedProperties.SetFloat("_MatterChunkMaskEnabled",maskEnabled?1:0);borrowedProperties.SetFloat("_MatterChunkOwner",group+1);
                if(ownerMask!=null)borrowedProperties.SetTexture("_MatterChunkMask",ownerMask);
                borrowedProperties.SetVector("_MatterChunkMaskOrigin",maskOrigin);borrowedProperties.SetVector("_MatterChunkMaskSize",maskSize);
                foreach(var part in borrowed.Parts)Graphics.DrawMesh(part.Mesh,Matrix4x4.TRS(position,rotation,Vector3.one),borrowedMaterials[group],drawLayer,DrawCamera,part.Submesh,borrowedProperties,part.Shadows?ShadowCastingMode.On:ShadowCastingMode.Off,true,null,LightProbeUsage.Off);
            }
        }
        void ReleaseBorrowed()
        {
            for(int i=0;i<GroupCount;i++){Destroy(borrowedMaterials[i]);borrowedMaterials[i]=null;}
            Destroy(ownerMask);ownerMask=null;maskEnabled=false;borrowed=null;
        }
        void AdvanceBuild()
        {
            if(worker!=null)
            {
                if(!worker.IsCompleted)return;
                BuildResult result;try{result=worker.GetAwaiter().GetResult();}catch(OperationCanceledException){worker=null;CancelBuild();return;}
                catch(Exception e){worker=null;Error=e.GetBaseException().Message;CancelBuild();return;}
                worker=null;
                if(physics.StructureBusy||result.Revision!=physics.StructureRevision){CancelBuild();return;}
                geometry=result.Geometry;pendingResult=result;factor=result.Factor;geometryBins=result.Bins;LastWorkerCandidates=result.Candidates;LastWorkerMilliseconds=result.Milliseconds;
            }
            // Empty slots need no Mesh allocation/upload and consume no frames.
            while(uploadCursor<GroupCount&&(geometry[uploadCursor]==null||geometry[uploadCursor].Vertices.Count==0)){geometry[uploadCursor]=null;uploadCursor++;}
            if(uploadCursor<GroupCount)
            {
                var mesh=new Mesh{name="Natural matter rigid cache "+uploadCursor,indexFormat=IndexFormat.UInt32};
                staging[uploadCursor]=mesh;Upload(mesh,geometry[uploadCursor]);geometry[uploadCursor]=null;uploadCursor++;
                while(uploadCursor<GroupCount&&(geometry[uploadCursor]==null||geometry[uploadCursor].Vertices.Count==0)){geometry[uploadCursor]=null;uploadCursor++;}
                if(uploadCursor<GroupCount)return;
            }
            if(physics.StructureBusy||buildingRevision!=physics.StructureRevision){CancelBuild();return;}
            if(pendingResult?.Mask!=null)
            {
                var result=pendingResult;var nextMask=new Texture3D(result.ChunkX,result.ChunkY,result.ChunkZ,TextureFormat.R8,false){filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
                nextMask.SetPixelData(result.Mask,0);nextMask.Apply(false,true);Destroy(ownerMask);ownerMask=nextMask;
                maskOrigin=result.Origin;maskSize=result.Extent;canonicalCenters=result.Centers;maskEnabled=true;
            }
            pendingResult=null;
            for(int i=0;i<GroupCount;i++){filters[i].sharedMesh=staging[i];Destroy(published[i]);published[i]=staging[i];staging[i]=null;}
            publishedRevision=buildingRevision;building=false;Ready=true;
        }
        static void Upload(Mesh mesh,Geometry g)
        {
            mesh.SetVertices(g.Vertices);mesh.SetNormals(g.Normals);mesh.SetColors(g.Colors);mesh.SetTriangles(g.Indices,0);mesh.RecalculateBounds();mesh.UploadMeshData(true);
        }
        static void AppendCube(Geometry g,Vector3 center,float size,Color32 color,Func<int,int,bool> hidden)
        {
            float half=size*.5f;
            for(int axis=0;axis<3;axis++)for(int sign=-1;sign<=1;sign+=2)
            {
                if(hidden!=null&&hidden(axis,sign))continue;
                int start=g.Vertices.Count,u=(axis+1)%3,v=(axis+2)%3;var normal=Vector3.zero;normal[axis]=sign;
                for(int corner=0;corner<4;corner++)
                {
                    var p=Vector3.zero;p[axis]=sign*half;p[u]=(corner==1||corner==2)?half:-half;p[v]=corner>=2?half:-half;
                    g.Vertices.Add(center+p);g.Normals.Add(normal);g.Colors.Add(color);
                }
                g.Indices.Add(start);g.Indices.Add(start+(sign>0?1:3));g.Indices.Add(start+2);
                g.Indices.Add(start);g.Indices.Add(start+2);g.Indices.Add(start+(sign>0?3:1));
            }
        }
        void DrawDebris(Vector3 origin)
        {
            int candidates=physics.PendingDebris,samples=Math.Min(candidates,MaximumDebris),batch=0;
            for(int sample=0;sample<samples;sample++)
            {
                int index=physics.LooseIndex((int)((long)sample*candidates/samples));var cube=physics.ReadLocal(index);
                if(cube.Owner!=NaturalTreePhysics.Ownership.Individual)continue;
                int weight=physics.LooseWeight(index);if(weight<1)continue;
                // Weight carries exact matter count; visual particles retain the
                // physics10cm contact size rather than sinking oversized cubes.
                var color=Color(cube.Matter);
                matrices[batch]=Matrix4x4.TRS(origin+Unity(cube.Position),Unity(cube.Rotation),Vector3.one*.1f);
                tints[batch]=new Vector4(color.r/255f,color.g/255f,color.b/255f,color.a/255f);batch++;DrawnDebris++;
                if(batch==BatchSize){DrawBatch(batch);batch=0;}
            }
            if(batch>0)DrawBatch(batch);
        }
        void DrawBatch(int count)
        {
            properties.SetVectorArray("_MatterTint",tints);
            Graphics.DrawMeshInstanced(debrisMesh,0,debrisMaterial,matrices,count,properties,ShadowCastingMode.Off,true,drawLayer,DrawCamera);
        }
        static void Destroy(UnityEngine.Object value)
        {if(value!=null){if(Application.isPlaying)UnityEngine.Object.Destroy(value);else UnityEngine.Object.DestroyImmediate(value);}}
        public void Dispose()
        {
            if(disposed)return;disposed=true;CancelBuild();physics=null;Ready=false;DrawnDebris=0;
            for(int i=0;i<GroupCount;i++){Destroy(published[i]);published[i]=null;Destroy(objects[i]);}
            if(worker!=null)worker.ContinueWith(t=>{var observed=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);
            cancellation?.Dispose();ReleaseBorrowed();Destroy(debrisMesh);Destroy(solidMaterial);Destroy(debrisMaterial);
        }
    }
}
