using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace AshenVoxel.WildlifeDraft
{
    // Explicit caller-driven adapter; no automatic scene/bootstrap integration.
    // Shared meshes/material are borrowed, never modified or destroyed here.
    public sealed class WildlifePatchRenderer : MonoBehaviour
    {
        [Serializable] public struct Shape
        {
            public Mesh Body, Wing;
            public Vector3 LeftPivot, RightPivot;
            [Range(0,90)] public float FlapDegrees;
        }
        public Shape Bee, Bird;
        public Material SharedMaterial;
        public Camera TargetCamera;
        [Range(0,128)] public int BeeCount=16,BirdCount=8;
        [Range(.1f,1)] public float BeeRadiusScale=1;
        [Min(0)] public float BeeDistance=25,BirdDistance=150;
        [Range(0,2000000)] public int TriangleBudget=50000;
        public int LastDrawCalls {get;private set;}
        public int LastDrawnActors {get;private set;}
        public long LastSubmittedTriangles {get;private set;}
        public int LastTriangleBudgetOmitted {get;private set;}
        public int LastOriginRejected {get;private set;}
        public int LastBudgetOmitted {get;private set;}
        public bool HasHabitat=>patch!=null&&habitat;
        FlightPatch patch;
        FlightPose[] poses;
        Matrix4x4[][] bodies,wings;
        readonly int[] bodyCounts=new int[2],wingCounts=new int[2];
        double originX,originY,originZ,eyeX,eyeY,eyeZ;
        bool habitat,observer;
        IHabitatAuthority authority;
        long habitatRevision;

        void OnEnable()
        {
            poses=new FlightPose[FlightPatch.MaximumActors];
            bodies=new[]{new Matrix4x4[128],new Matrix4x4[128]};
            wings=new[]{new Matrix4x4[256],new Matrix4x4[256]};
        }
        void OnDisable()
        {
            // Configuration survives temporary disable. Owner rechecks the
            // immutable habitat revision before drawing after enable.
            poses=null;bodies=null;wings=null;
            observer=false;LastDrawCalls=LastDrawnActors=LastOriginRejected=LastBudgetOmitted=0;
            LastSubmittedTriangles=0;LastTriangleBudgetOmitted=0;
        }
        static void Check(double value)
        {if(double.IsNaN(value)||double.IsInfinity(value))throw new ArgumentOutOfRangeException(nameof(value));}
        public void SetView(double globalEyeX,double globalEyeY,double globalEyeZ,
            double renderOriginX,double renderOriginY,double renderOriginZ)
        {
            Check(globalEyeX);Check(globalEyeY);Check(globalEyeZ);Check(renderOriginX);Check(renderOriginY);Check(renderOriginZ);
            eyeX=globalEyeX;eyeY=globalEyeY;eyeZ=globalEyeZ;
            originX=renderOriginX;originY=renderOriginY;originZ=renderOriginZ;observer=true;
        }
        public bool TrySetHabitat(double globalX,double groundY,double globalZ,uint seed,IHabitatAuthority source)
        {
            if(habitat)throw new InvalidOperationException("Unload fixed habitat before replacing its anchor.");
            if(source==null)throw new ArgumentNullException(nameof(source));
            long revision=source.Revision;
            if((BeeCount>0&&!source.IsWholeVolumeClear(new HabitatEnvelope(globalX,groundY,globalZ,false,BeeRadiusScale)))||
                (BirdCount>0&&!source.IsWholeVolumeClear(new HabitatEnvelope(globalX,groundY,globalZ,true)))||source.Revision!=revision)return false;
            // Explicit replacement invalidates old patch and its observer-independent
            // state. At most128 poses; oversized inspector values reject visibly.
            var next=new FlightPatch(BeeCount,BirdCount,seed,BeeRadiusScale);
            next.SetHabitat(globalX,groundY,globalZ);
            patch?.Unload();patch=next;habitat=true;authority=source;habitatRevision=revision;return true;
        }
        public void UnloadHabitat()
        {patch?.Unload();habitat=false;authority=null;LastDrawCalls=LastDrawnActors=0;LastSubmittedTriangles=0;LastTriangleBudgetOmitted=0;}
        public void RenderAtTime(double simulationSeconds,int actorBudget=24)
        {
            LastDrawCalls=LastDrawnActors=LastOriginRejected=LastBudgetOmitted=0;
            LastSubmittedTriangles=0;LastTriangleBudgetOmitted=0;
            if(actorBudget<0||actorBudget>128)throw new ArgumentOutOfRangeException(nameof(actorBudget));
            if(!isActiveAndEnabled||!habitat||!observer||patch==null||TargetCamera==null||SharedMaterial==null)return;
            if(authority==null||authority.Revision!=habitatRevision){UnloadHabitat();return;}
            if(!SharedMaterial.enableInstancing)throw new InvalidOperationException("Wildlife shared material must already support instancing.");
            if(poses==null)return;
            int count=patch.Fill(simulationSeconds,eyeX,eyeY,eyeZ,BeeDistance,BirdDistance,poses.AsSpan(0,actorBudget));
            LastBudgetOmitted=patch.LastOmitted;
            bodyCounts[0]=bodyCounts[1]=wingCounts[0]=wingCounts[1]=0;
            for(int i=0;i<count;i++)
            {
                FlightPose p=poses[i];int kind=p.Kind==FlightKind.Bee?0:1;
                Shape shape=kind==0?Bee:Bird;
                if(shape.Body==null)continue;
                if(shape.Body.subMeshCount<1||(shape.Wing!=null&&shape.Wing.subMeshCount<1))continue;
                long triangles=(long)shape.Body.GetIndexCount(0)/3;
                if(shape.Wing!=null)triangles+=2*((long)shape.Wing.GetIndexCount(0)/3);
                if(triangles>Math.Max(0,TriangleBudget)-LastSubmittedTriangles)
                {LastTriangleBudgetOmitted++;continue;}
                double rx=p.X-originX,ry=p.Y-originY,rz=p.Z-originZ;
                if(Math.Abs(rx)>100000||Math.Abs(ry)>100000||Math.Abs(rz)>100000)
                {LastOriginRejected++;continue;}
                Vector3 velocity=new Vector3((float)p.VX,(float)p.VY,(float)p.VZ);
                Quaternion rotation=velocity.sqrMagnitude>1e-8f?Quaternion.LookRotation(velocity,Vector3.up):Quaternion.identity;
                Matrix4x4 basis=Matrix4x4.TRS(new Vector3((float)rx,(float)ry,(float)rz),rotation,Vector3.one);
                bodies[kind][bodyCounts[kind]++]=basis;
                if(shape.Wing!=null)
                {
                    float angle=(float)p.Wing*shape.FlapDegrees;
                    wings[kind][wingCounts[kind]++]=basis*Matrix4x4.TRS(shape.LeftPivot,Quaternion.Euler(0,0,angle),Vector3.one);
                    // Mirror the left-authored wing FIRST, then flap about the
                    // body's forward axis. Euler yaw-after-flap makes the two
                    // wing tips move in opposite vertical directions.
                    Quaternion right=Quaternion.AngleAxis(-angle,Vector3.forward)*Quaternion.AngleAxis(180,Vector3.up);
                    wings[kind][wingCounts[kind]++]=basis*Matrix4x4.TRS(shape.RightPivot,right,Vector3.one);
                }
                LastDrawnActors++;
                LastSubmittedTriangles+=triangles;
            }
            Draw(Bee.Body,bodies[0],bodyCounts[0]);Draw(Bird.Body,bodies[1],bodyCounts[1]);
            Draw(Bee.Wing,wings[0],wingCounts[0]);Draw(Bird.Wing,wings[1],wingCounts[1]);
        }
        void Draw(Mesh mesh,Matrix4x4[] matrices,int count)
        {
            if(count==0||mesh==null)return;
            Graphics.DrawMeshInstanced(mesh,0,SharedMaterial,matrices,count,null,
                ShadowCastingMode.Off,false,gameObject.layer,TargetCamera,LightProbeUsage.Off);
            LastDrawCalls++;
        }
    }
}
