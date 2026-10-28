#if UNITY_5_3_OR_NEWER
using System;
using System.Runtime.InteropServices;
using AshenVoxel.WorldScale;
using UnityEngine;
using UnityEngine.Rendering;

namespace AshenVoxel.Exploration
{
    /// <summary>Native graphics boundary. Source placement stays on the CPU worker;
    /// compute compacts visible stands and supplies indirect instance counts.
    /// No readback is performed in gameplay. Numerical test shims use the fallback;
    /// this path must be tested with the actual compute/graphics device.</summary>
    public sealed class ForestGpuRenderer:IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct Instance
        {
            public Vector4 PositionSpan; // snapshot-relative center xyz, square span m
            public Vector4 Style;        // width, prototype ID, yaw radians, reserved
        }
        sealed class Buffers:IDisposable
        {
            public GraphicsBuffer Input,Visible,Args;
            public int Capacity,Count;
            public WorldPoint Anchor;
            public void Dispose(){Input?.Dispose();Visible?.Dispose();Args?.Dispose();Input=Visible=Args=null;Capacity=Count=0;}
        }
        readonly Material material;readonly ComputeShader compute;readonly int kernel;
        readonly MaterialPropertyBlock properties=new MaterialPropertyBlock();
        readonly Plane[] planes=new Plane[6];readonly Vector4[] equations=new Vector4[6];
        readonly GraphicsBuffer.IndirectDrawArgs[] commands=new GraphicsBuffer.IndirectDrawArgs[1];
        Buffers active=new Buffers(),spare=new Buffers();
        public int Count=>active.Count;
        public long BufferBytes=>(active.Capacity+spare.Capacity)*36L+GraphicsBuffer.IndirectDrawArgs.size*2L;
        ForestGpuRenderer(Material template,ComputeShader source,Shader shader)
        {
            compute=UnityEngine.Object.Instantiate(source);kernel=compute.FindKernel("CullForest");
            material=new Material(shader){enableInstancing=true};material.mainTexture=template.mainTexture;
            material.SetTexture("_NormalMap",template.GetTexture("_NormalMap"));material.SetFloat("_AtlasLightingVersion",template.GetFloat("_AtlasLightingVersion"));
        }
        public static ForestGpuRenderer TryCreate(Material template)
        {
            if(!SystemInfo.supportsComputeShaders||!SystemInfo.supportsInstancing||!SystemInfo.supportsIndirectArgumentsBuffer)return null;
            var source=Resources.Load<ComputeShader>("ForestHorizonCull");var shader=Resources.Load<Shader>("ForestHorizonGpu");
            if(source==null||shader==null||!shader.isSupported)return null;
            return new ForestGpuRenderer(template,source,shader);
        }
        public void Publish(Instance[] data,WorldPoint anchor)
        {
            int needed=Mathf.NextPowerOfTwo(Math.Max(1,data.Length));
            if(spare.Capacity<needed)
            {
                spare.Dispose();
                try
                {
                    spare.Input=new GraphicsBuffer(GraphicsBuffer.Target.Structured,needed,32);
                    spare.Visible=new GraphicsBuffer(GraphicsBuffer.Target.Append,needed,4);
                    spare.Args=new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments,1,GraphicsBuffer.IndirectDrawArgs.size);
                    spare.Capacity=needed;
                }
                catch{spare.Dispose();throw;}
            }
            if(data.Length>0)spare.Input.SetData(data);
            spare.Count=data.Length;spare.Anchor=anchor;
            commands[0]=new GraphicsBuffer.IndirectDrawArgs{vertexCountPerInstance=6,instanceCount=0,startVertex=0,startInstance=0};
            spare.Args.SetData(commands);
            Buffers previous=active;active=spare;spare=previous;
        }
        public int Draw(Camera camera,WorldPoint eye,WorldPoint renderOrigin,double near,double far,int layer=0)
        {
            if(active.Count==0)return 0;
            Vector4 offset=new Vector4((float)(active.Anchor.X-renderOrigin.X),(float)(active.Anchor.Y-renderOrigin.Y),(float)(active.Anchor.Z-renderOrigin.Z),0);
            Vector4 observer=new Vector4((float)(eye.X-renderOrigin.X),(float)(eye.Y-renderOrigin.Y),(float)(eye.Z-renderOrigin.Z),0);
            GeometryUtility.CalculateFrustumPlanes(camera,planes);
            for(int i=0;i<6;i++)equations[i]=new Vector4(planes[i].normal.x,planes[i].normal.y,planes[i].normal.z,planes[i].distance);
            active.Visible.SetCounterValue(0);
            compute.SetInt("_InstanceCount",active.Count);compute.SetVector("_SnapshotOffset",offset);compute.SetVector("_Observer",observer);
            compute.SetVector("_Range",new Vector4((float)near,(float)far,0,0));compute.SetVectorArray("_Planes",equations);
            compute.SetBuffer(kernel,"_ForestInstances",active.Input);compute.SetBuffer(kernel,"_VisibleForest",active.Visible);
            compute.Dispatch(kernel,(active.Count+63)/64,1,1);
            // Pinned Unity 6000.3 UnityIndirect.cginc defines the non-indexed
            // command as four uints, with instanceCount at byte4. The C# members
            // are properties, so Marshal.OffsetOf(nameof(instanceCount)) is invalid.
            if(GraphicsBuffer.IndirectDrawArgs.size!=16)throw new InvalidOperationException("Revalidate Unity indirect command layout before using GPU forests.");
            GraphicsBuffer.CopyCount(active.Visible,active.Args,sizeof(uint));
            properties.SetBuffer("_ForestInstances",active.Input);properties.SetBuffer("_VisibleForest",active.Visible);
            properties.SetVector("_SnapshotOffset",offset);
            material.SetFloat("_HorizonNear",(float)near);material.SetFloat("_HorizonFar",(float)far);
            var bounds=new Bounds(new Vector3(observer.x,observer.y,observer.z),Vector3.one*(float)(far*2+8192));
            var render=new RenderParams(material){camera=camera,worldBounds=bounds,matProps=properties,layer=layer,shadowCastingMode=ShadowCastingMode.Off,receiveShadows=false,lightProbeUsage=LightProbeUsage.Off};
            Graphics.RenderPrimitivesIndirect(render,MeshTopology.Triangles,active.Args,1);return 1;
        }
        /// <summary>Explicit validation-only synchronization. Never called by Tick.</summary>
        public uint ReadVisibleCountForValidation(){if(active.Args==null)return 0;active.Args.GetData(commands);return commands[0].instanceCount;}
        public void Dispose(){active.Dispose();spare.Dispose();if(Application.isPlaying){UnityEngine.Object.Destroy(material);UnityEngine.Object.Destroy(compute);}else{UnityEngine.Object.DestroyImmediate(material);UnityEngine.Object.DestroyImmediate(compute);}}
    }
}
#endif
