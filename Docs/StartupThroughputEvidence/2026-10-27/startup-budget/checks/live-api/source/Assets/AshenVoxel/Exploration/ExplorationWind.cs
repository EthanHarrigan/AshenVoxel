#if UNITY_5_3_OR_NEWER
using System;
using AshenVoxel.WorldScale;
using UnityEngine;

namespace AshenVoxel.Exploration
{
    /// <summary>Owned render-only forest wind. Bootstrap calls SetOrigin once per
    /// LateUpdate, after the floating origin changes and before vegetation draws.</summary>
    public sealed class ExplorationWind:MonoBehaviour
    {
        public const float MaximumDisplacementMeters=2f;
        static readonly int TimeId=Shader.PropertyToID("_VoxelWindTime");
        static readonly int PreviousTimeId=Shader.PropertyToID("_VoxelWindPreviousTime");
        static readonly int StateId=Shader.PropertyToID("_VoxelWindState");
        static readonly int PreviousStateId=Shader.PropertyToID("_VoxelWindPreviousState");
        static readonly int DeltaId=Shader.PropertyToID("_VoxelWindOriginDelta");
        WorldPoint origin,previousFrameOrigin;
        Vector4 angles,state;
        float strength=1;
        int publishedFrame=-1;
        bool initialized;
        public float Strength=>strength;
        public void Initialize(float amount,WorldPoint renderOrigin)
        {
            Apply(amount);origin=renderOrigin;initialized=false;SetOrigin(renderOrigin);
        }
        public void Apply(float amount){strength=float.IsNaN(amount)||float.IsInfinity(amount)?1:Mathf.Clamp(amount,0,2);}
        static float Angle(double value)
        {
            const double turn=Math.PI*2;
            return (float)(value-Math.Floor(value/turn)*turn);
        }
        public void SetOrigin(WorldPoint renderOrigin)
        {
            // Previous globals remain the preceding rendered frame even if a
            // fixture/owner calls this twice in one frame.
            if(publishedFrame!=Time.frameCount)
            {
                Shader.SetGlobalVector(PreviousTimeId,angles);
                Shader.SetGlobalVector(PreviousStateId,state);
                previousFrameOrigin=origin;
                publishedFrame=Time.frameCount;
            }
            Shader.SetGlobalVector(DeltaId,new Vector4((float)(renderOrigin.X-previousFrameOrigin.X),(float)(renderOrigin.Y-previousFrameOrigin.Y),(float)(renderOrigin.Z-previousFrameOrigin.Z),0));
            double t=Time.timeAsDouble;
            angles=new Vector4(Angle(t*.8),Angle(t*1.17),Angle(t*3.7),0);
            state=new Vector4(strength,Angle(renderOrigin.X*.037+renderOrigin.Z*.023),0,0);
            Shader.SetGlobalVector(TimeId,angles);Shader.SetGlobalVector(StateId,state);
            if(!initialized)
            {
                Shader.SetGlobalVector(PreviousTimeId,angles);Shader.SetGlobalVector(PreviousStateId,state);
                Shader.SetGlobalVector(DeltaId,Vector4.zero);initialized=true;
            }
            origin=renderOrigin;
        }
        void OnDisable()
        {
            // Globals must not animate the separate arena or atlas bake later.
            Shader.SetGlobalVector(StateId,Vector4.zero);Shader.SetGlobalVector(PreviousStateId,Vector4.zero);
            Shader.SetGlobalVector(DeltaId,Vector4.zero);initialized=false;publishedFrame=-1;
        }
    }
}
#endif
