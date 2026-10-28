// Isolated native benchmark; copied to a throwaway project's Editor directory.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using AshenVoxel.Core;
using AshenVoxel.WorldScale;
using Debug=UnityEngine.Debug;

public static class TerrainNativeBenchmark
{
    [Serializable] public sealed class Row
    {
        public string label,sha256;public int mode,repeat,faces;
        public double generation_ms,sampling_ms,tops_ms,sides_ms,merge_ms,expand_ms,mesh_upload_ms,pack_ms,quad_upload_ms;
        public long expanded_bytes,quad_bytes,allocated_generation_caller,allocated_expand,allocated_pack;
    }
    [Serializable] public sealed class PixelCheck {public string label;public int pass,pixels,changed;public float max_error;}
    [Serializable] public sealed class DrawTiming {public string label;public int repeat,draws=32;public double mesh_completed_ms,quad_completed_ms;}
    [Serializable] public sealed class Report
    {
        public string status="RUNNING",scope="Native Unity actual-source tile pipeline microbenchmark; no forest/full-world/FPS claim",unity,gpu,api,source;
        public List<Row> rows=new List<Row>();public List<PixelCheck> pixels=new List<PixelCheck>();
        public List<DrawTiming> draw_timings=new List<DrawTiming>();
        public long peak_process_bytes;public string error;
    }
    [StructLayout(LayoutKind.Sequential,Pack=1)] struct Vertex {public Vector3 position;public sbyte nx,ny,nz,nw;public Color32 color,surface;}
    [StructLayout(LayoutKind.Sequential,Pack=1)] struct Quad {public Vector3 position;public Vector2 extent;public uint tint,ao,properties;}
    static readonly VertexAttributeDescriptor[] Layout={new VertexAttributeDescriptor(VertexAttribute.Position,VertexAttributeFormat.Float32,3),new VertexAttributeDescriptor(VertexAttribute.Normal,VertexAttributeFormat.SNorm8,4),new VertexAttributeDescriptor(VertexAttribute.Color,VertexAttributeFormat.UNorm8,4),new VertexAttributeDescriptor(VertexAttribute.TexCoord0,VertexAttributeFormat.UNorm8,4)};
    static SurfaceMaterialSampler sampler;static TerrainProbe.Case[] cases;static readonly int[] Modes={1,0,2,4};
    static int index=-1;static Task<Work> job;static Report report;static string output;
    static Vertex[] vertices;static int[] indices;static Quad[] quads;
    static readonly Dictionary<string,string> hashes=new Dictionary<string,string>();
    static Material ordinary,pulled;static Shader depthCopy;static bool warm=true,visualOnly;
    sealed class Work{public VoxelLodSurface data;public Row row;}
    public static void RunVisual(){visualOnly=true;warm=false;Run();}
    public static void Run()
    {
        try
        {
            output=Path.GetFullPath("Logs/TerrainPerformance-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));Directory.CreateDirectory(output);
            AssetDatabase.DesiredWorkerCount=0;AssetDatabase.ForceToDesiredWorkerCount();
            sampler=TerrainProbe.Load(Path.GetFullPath("Samples"));cases=TerrainProbe.Cases();
            report=new Report{unity=Application.unityVersion,gpu=SystemInfo.graphicsDeviceName,api=SystemInfo.graphicsDeviceType.ToString(),source=sampler.SourceIdentity};
            ordinary=new Material(Shader.Find("AshenVoxel/GroundSurface"));pulled=new Material(Shader.Find("AshenVoxel/BenchmarkQuad"));
            depthCopy=Shader.Find("Hidden/BenchmarkDepthCopy");
            var renderer=ScriptableObject.CreateInstance<UniversalRendererData>();var pipeline=UniversalRenderPipelineAsset.Create(renderer);
            GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
            foreach(var s in new[]{ordinary.shader,pulled.shader})foreach(var message in ShaderUtil.GetShaderMessages(s))Debug.Log(s.name+" "+message.message+" "+message.file+":"+message.line);
            if(ShaderUtil.ShaderHasError(ordinary.shader)||ShaderUtil.ShaderHasError(pulled.shader))throw new Exception("Shader compilation error");
            ordinary.SetFloat("_GrainStrength",0);pulled.SetFloat("_GrainStrength",0);
            EditorApplication.update+=Tick;index=0;
        }
        catch(Exception e){Fail(e);}
    }
    static void Tick()
    {
        try
        {
            if(job==null)
            {
                int repeat=index/(cases.Length*Modes.Length),within=index%(cases.Length*Modes.Length);
                int m=within/cases.Length,c=within%cases.Length;
                if(visualOnly&&index>=cases.Length){Finish();return;}
                if(!warm&&repeat>=5){Finish();return;}
                if(warm&&repeat>=1){warm=false;index=0;GC.Collect();return;}
                int mode=Modes[repeat%2==0?m:Modes.Length-1-m];var item=cases[repeat%2==0?c:cases.Length-1-c];
                TerrainProbe.Workers=mode;
                job=Task.Run(()=>{
                    long alloc=GC.GetAllocatedBytesForCurrentThread();var clock=Stopwatch.StartNew();var d=TerrainProbe.Build(sampler,item);clock.Stop();
                    return new Work{data=d,row=new Row{label=item.label,repeat=repeat,mode=mode,faces=d.Faces.Count,generation_ms=clock.Elapsed.TotalMilliseconds,
                        sampling_ms=TerrainProbe.Stages[0],tops_ms=TerrainProbe.Stages[1],sides_ms=TerrainProbe.Stages[2],merge_ms=TerrainProbe.Stages[3],allocated_generation_caller=GC.GetAllocatedBytesForCurrentThread()-alloc}};
                });return;
            }
            if(!job.IsCompleted)return;
            Work result=job.GetAwaiter().GetResult();job=null;var data=result.data;var row=result.row;
            row.sha256=TerrainProbe.Hash(data);
            if(hashes.TryGetValue(row.label,out string expected)&&expected!=row.sha256)throw new Exception("Geometry/appearance mismatch "+row.label);
            hashes[row.label]=row.sha256;
            // Reusable capacity reflects the existing upload path; no per-tile renderer retained.
            int vc=data.Faces.Count*4,ic=data.Faces.Count*6;
            Ensure(ref vertices,vc);Ensure(ref indices,ic);Ensure(ref quads,data.Faces.Count);
            long alloc=GC.GetAllocatedBytesForCurrentThread();var watch=Stopwatch.StartNew();Bounds bounds=Expand(data);watch.Stop();row.expand_ms=watch.Elapsed.TotalMilliseconds;row.allocated_expand=GC.GetAllocatedBytesForCurrentThread()-alloc;
            watch.Restart();Mesh mesh=Upload(vc,ic,bounds);watch.Stop();row.mesh_upload_ms=watch.Elapsed.TotalMilliseconds;
            alloc=GC.GetAllocatedBytesForCurrentThread();watch.Restart();Pack(data);watch.Stop();row.pack_ms=watch.Elapsed.TotalMilliseconds;row.allocated_pack=GC.GetAllocatedBytesForCurrentThread()-alloc;
            watch.Restart();var buffer=new GraphicsBuffer(GraphicsBuffer.Target.Structured,data.Faces.Count,32);buffer.SetData(quads,0,0,data.Faces.Count);watch.Stop();row.quad_upload_ms=watch.Elapsed.TotalMilliseconds;
            row.expanded_bytes=(long)data.Faces.Count*120;row.quad_bytes=(long)data.Faces.Count*32;
            try
            {
                if(!warm&&row.repeat==0&&row.mode==1)
                    for(int pass=0;pass<4;pass++)Compare(mesh,buffer,data,row.label,pass);
            }
            finally{buffer.Dispose();UnityEngine.Object.DestroyImmediate(mesh);}
            if(!warm){report.rows.Add(row);Save();Debug.Log($"TERRAIN_BENCH {row.mode} {row.repeat} {row.label} generation={row.generation_ms:F1} expand={row.expand_ms:F2} upload={row.mesh_upload_ms:F2} pack={row.pack_ms:F2} quad={row.quad_upload_ms:F2}");}
            report.peak_process_bytes=Math.Max(report.peak_process_bytes,Process.GetCurrentProcess().PrivateMemorySize64);index++;
            // Prevent delayed GPU cleanup from accumulating across hundreds of isolated samples.
            if(index%16==0)GC.Collect();
        }
        catch(Exception e){Fail(e);}
    }
    static void Ensure<T>(ref T[] a,int count){if(a!=null&&a.Length>=count)return;int n=1024;while(n<count)n*=2;a=new T[n];}
    static Vector3 Axis(int n)=>n==0?Vector3.right:n==1?Vector3.up:Vector3.forward;
    static byte Rough(VoxelLodFace f){double wet=f.Wetness/255.0;float r=f.Material==MaterialId.Ice?.18f:f.Material==MaterialId.Snow?.92f:(float)Math.Max(.45,(f.Material==MaterialId.Mud||f.Material==MaterialId.Clay?.82:.95)-wet*.24);return (byte)Math.Round(r*255);}
    static Bounds Expand(VoxelLodSurface data)
    {
        int vertex=0,index=0;Vector3 lo=new Vector3(float.MaxValue,float.MaxValue,float.MaxValue),hi=new Vector3(float.MinValue,float.MinValue,float.MinValue);
        foreach(var f in data.Faces)
        {
            int start=vertex;Vector3 p=new Vector3(f.X,f.Y,f.Z),u=Axis((f.Axis+1)%3)*f.U,v=Axis((f.Axis+2)%3)*f.V;
            sbyte sign=(sbyte)(f.Positive?127:-127);var surface=new Color32(Rough(f),f.Wetness,f.Material==MaterialId.Snow?(byte)255:(byte)0,255);
            void Add(Vector3 position,byte ao){vertices[vertex++]=new Vertex{position=position,nx=f.Axis==0?sign:(sbyte)0,ny=f.Axis==1?sign:(sbyte)0,nz=f.Axis==2?sign:(sbyte)0,color=new Color32((byte)(f.Tint>>16),(byte)(f.Tint>>8),(byte)f.Tint,ao),surface=surface};lo=Vector3.Min(lo,position);hi=Vector3.Max(hi,position);}
            Add(p,f.A0);Add(p+u,f.A1);Add(p+u+v,f.A2);Add(p+v,f.A3);
            void Tri(int a,int b,int c){indices[index++]=start+a;indices[index++]=start+(f.Positive?b:c);indices[index++]=start+(f.Positive?c:b);}
            if(f.A0+f.A2>f.A1+f.A3){Tri(0,1,3);Tri(1,2,3);}else{Tri(0,1,2);Tri(0,2,3);}
        }
        return new Bounds((lo+hi)*.5f,hi-lo);
    }
    static Mesh Upload(int vc,int ic,Bounds bounds)
    {
        var mesh=new Mesh{indexFormat=IndexFormat.UInt32};mesh.SetVertexBufferParams(vc,Layout);mesh.SetVertexBufferData(vertices,0,0,vc);
        mesh.SetIndexBufferParams(ic,IndexFormat.UInt32);mesh.SetIndexBufferData(indices,0,0,ic);
        mesh.SetSubMesh(0,new SubMeshDescriptor(0,ic){bounds=bounds,vertexCount=vc},MeshUpdateFlags.DontRecalculateBounds);mesh.bounds=bounds;mesh.UploadMeshData(true);return mesh;
    }
    static void Pack(VoxelLodSurface d)
    {
        for(int i=0;i<d.Faces.Count;i++){var f=d.Faces[i];quads[i]=new Quad{position=new Vector3(f.X,f.Y,f.Z),extent=new Vector2(f.U,f.V),tint=f.Tint,
            ao=(uint)(f.A0|(f.A1<<8)|(f.A2<<16)|(f.A3<<24)),properties=(uint)Rough(f)|((uint)f.Wetness<<8)|((uint)f.Material<<16)|((uint)f.Axis<<24)|(f.Positive?1u<<26:0)};}
    }
    static void Compare(Mesh mesh,GraphicsBuffer buffer,VoxelLodSurface data,string label,int pass)
    {
        // Compare all four original shader passes with identical matrices/globals.
        int width=640,height=360;var rt=new RenderTexture(width,height,24,RenderTextureFormat.ARGBFloat){filterMode=FilterMode.Point};rt.Create();
        var previous=RenderTexture.active;var read=new Texture2D(width,height,TextureFormat.RGBAFloat,false,true);
        var cb=new CommandBuffer{name="Terrain quad equivalence"};Material copy=new Material(depthCopy);
        try
        {
            float span=data.Quads*data.CellStride*.1f;float localY=data.Heights[(data.Quads/2)*(data.Quads+1)];
            Vector3 target=new Vector3(span*.5f,localY,span*.5f),eye=target+new Vector3(span*.65f,span*.55f,-span*.8f);
            Matrix4x4 view=Matrix4x4.Scale(new Vector3(1,1,-1))*Matrix4x4.TRS(eye,Quaternion.LookRotation(target-eye),Vector3.one).inverse;
            Matrix4x4 proj=GL.GetGPUProjectionMatrix(Matrix4x4.Perspective(55,width/(float)height,.01f,span*10+20000),true);
            Color[] Render(bool gpu)
            {
                cb.Clear();cb.SetRenderTarget(rt);cb.ClearRenderTarget(true,true,new Color(.13f,.21f,.31f,1));cb.SetViewProjectionMatrices(view,proj);
                cb.SetGlobalVector("_WorldSpaceCameraPos",eye);cb.SetGlobalVector("_MainLightPosition",new Vector4(.3f,.8f,.4f,0));cb.SetGlobalVector("_MainLightColor",Vector4.one);
                cb.SetGlobalVector("_LightDirection",new Vector4(.3f,.8f,.4f,0));cb.SetGlobalVector("_ShadowBias",Vector4.zero);
                SetLighting(cb);
                if(gpu){pulled.SetBuffer("_Quads",buffer);cb.DrawProcedural(Matrix4x4.identity,pulled,pass,MeshTopology.Triangles,data.Faces.Count*6,1);}
                else cb.DrawMesh(mesh,Matrix4x4.identity,ordinary,0,pass);
                Graphics.ExecuteCommandBuffer(cb);
                RenderTexture.active=rt;
                if(pass==1||pass==2)
                {
                    // Read actual depth via a temporary color target; ColorMask0 cannot prove geometry.
                    var temp=RenderTexture.GetTemporary(width,height,0,RenderTextureFormat.ARGBFloat);
                    copy.SetTexture("_Depth",rt,RenderTextureSubElement.Depth);Graphics.Blit(null,temp,copy);RenderTexture.active=temp;
                    read.ReadPixels(new Rect(0,0,width,height),0,0);read.Apply();RenderTexture.ReleaseTemporary(temp);
                }
                else {read.ReadPixels(new Rect(0,0,width,height),0,0);read.Apply();}
                return read.GetPixels();
            }
            Color[] a=Render(false),b=Render(true);float error=0;int changed=0,nonclear=0;
            for(int i=0;i<a.Length;i++){float d=Math.Max(Math.Abs(a[i].r-b[i].r),Math.Max(Math.Abs(a[i].g-b[i].g),Math.Abs(a[i].b-b[i].b)));error=Math.Max(error,d);if(d>1e-5f)changed++;if(pass==1||pass==2?(a[i].r>0&&a[i].r<1):(Math.Abs(a[i].r-.13f)+Math.Abs(a[i].g-.21f)>.01f))nonclear++;}
            report.pixels.Add(new PixelCheck{label=label,pass=pass,pixels=nonclear,changed=changed,max_error=error});
            if(nonclear<100)throw new Exception("Empty render comparison "+label+" pass"+pass);
            if(pass==0&&a.Count(p=>Math.Max(p.r,Math.Max(p.g,p.b))>.01f&&(Math.Abs(p.r-.13f)+Math.Abs(p.g-.21f)>.01f))<100)
                throw new Exception("Insufficient lit material pixels "+label);
            if(error>.003f||changed>a.Length*.002)throw new Exception("GPU quad pixels differ "+label+" pass"+pass+" max="+error+" changed="+changed);
            if(pass==0&&label=="spawn-10cm"){var png=new Texture2D(width,height,TextureFormat.RGB24,false,true);png.SetPixels(b);png.Apply();File.WriteAllBytes(Path.Combine(output,"quad-spawn.png"),png.EncodeToPNG());UnityEngine.Object.DestroyImmediate(png);}
            if(pass==0)
            {
                double CompletedBatch(bool gpu)
                {
                    cb.Clear();cb.SetRenderTarget(rt);cb.SetViewProjectionMatrices(view,proj);
                    cb.SetGlobalVector("_WorldSpaceCameraPos",eye);cb.SetGlobalVector("_MainLightPosition",new Vector4(.3f,.8f,.4f,0));cb.SetGlobalVector("_MainLightColor",Vector4.one);
                    SetLighting(cb);
                    for(int n=0;n<32;n++)
                    {
                        cb.ClearRenderTarget(true,true,Color.black);
                        if(gpu)cb.DrawProcedural(Matrix4x4.identity,pulled,0,MeshTopology.Triangles,data.Faces.Count*6,1);
                        else cb.DrawMesh(mesh,Matrix4x4.identity,ordinary,0,0);
                    }
                    var timer=Stopwatch.StartNew();Graphics.ExecuteCommandBuffer(cb);
                    var done=AsyncGPUReadback.Request(rt,0,0,1,0,1,0,1,TextureFormat.RGBAFloat);done.WaitForCompletion();timer.Stop();
                    if(done.hasError)throw new Exception("GPU completion readback failed");return timer.Elapsed.TotalMilliseconds;
                }
                CompletedBatch(false);CompletedBatch(true);
                for(int n=0;n<5;n++)
                {
                    var timing=new DrawTiming{label=label,repeat=n};
                    if(n%2==0){timing.mesh_completed_ms=CompletedBatch(false);timing.quad_completed_ms=CompletedBatch(true);}
                    else {timing.quad_completed_ms=CompletedBatch(true);timing.mesh_completed_ms=CompletedBatch(false);}
                    report.draw_timings.Add(timing);
                }
            }
        }
        finally{cb.Dispose();RenderTexture.active=previous;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(read);UnityEngine.Object.DestroyImmediate(copy);}
    }
    static void Save()=>File.WriteAllText(Path.Combine(output,"results.json"),JsonUtility.ToJson(report,true));
    static void SetLighting(CommandBuffer cb)
    {
        // CommandBuffer draws bypass URP camera setup: initialize attenuation
        // and ambient explicitly so forward parity includes visible pigments.
        cb.SetGlobalVector("unity_LightData",new Vector4(0,0,1,0));
        foreach(string channel in new[]{"r","g","b"})cb.SetGlobalVector("unity_SHA"+channel,new Vector4(0,0,0,.4f));
        cb.DisableShaderKeyword("FOG_LINEAR");cb.DisableShaderKeyword("FOG_EXP");cb.DisableShaderKeyword("FOG_EXP2");
    }
    static void Finish(){report.status="PASS";Save();EditorApplication.update-=Tick;Debug.Log("TERRAIN_BENCH_PASS");EditorApplication.Exit(0);}
    static void Fail(Exception e){if(report!=null){report.status="FAIL";report.error=e.ToString();Save();}Debug.LogException(e);EditorApplication.update-=Tick;EditorApplication.Exit(1);}
}
