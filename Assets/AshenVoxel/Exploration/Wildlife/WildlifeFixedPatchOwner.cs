using System;
using System.Collections.Generic;
using UnityEngine;
namespace AshenVoxel.WildlifeDraft
{
    public interface IWildlifeFrameAuthority : IHabitatAuthority
    {
        // Called after camera/vegetation origin update. False means do no work.
        bool TryFrame(out double time,out double eyeX,out double eyeY,out double eyeZ,
            out double originX,out double originY,out double originZ);
    }
    [DisallowMultipleComponent]
    public sealed class WildlifeFixedPatchOwner : MonoBehaviour
    {
        static readonly HashSet<Camera> Owners=new HashSet<Camera>();
        WildlifePatchRenderer rendererOwner;
        IWildlifeFrameAuthority authority;
        Camera ownedCamera;
        Mesh beeBody,beeWing,birdBody,birdWing;
        bool configured,claimed;
        public int LastDrawCalls=>rendererOwner!=null?rendererOwner.LastDrawCalls:0;
        public int LastDrawnActors=>rendererOwner!=null?rendererOwner.LastDrawnActors:0;
        public bool Configure(Camera camera,Material instancedVertexColorMaterial,IWildlifeFrameAuthority source,
            double x,double y,double z,uint seed,int bees=8,int birds=4,float beeRadiusScale=1)
        {
            if(configured)throw new InvalidOperationException("Fixed patch is already configured; dispose before replacing habitat.");
            if(camera==null||source==null||instancedVertexColorMaterial==null||!instancedVertexColorMaterial.enableInstancing)return false;
            if(bees<0||birds<0||bees+birds<1||bees+birds>12||float.IsNaN(beeRadiusScale)||beeRadiusScale<.1f||beeRadiusScale>1)return false;
            if(!Owners.Add(camera))return false;
            ownedCamera=camera;claimed=true;
            try
            {
                rendererOwner=gameObject.AddComponent<WildlifePatchRenderer>();
                rendererOwner.BeeCount=bees;rendererOwner.BirdCount=birds;rendererOwner.BeeRadiusScale=beeRadiusScale;
                rendererOwner.TargetCamera=camera;rendererOwner.SharedMaterial=instancedVertexColorMaterial;
                beeBody=Body(false);birdBody=Body(true);beeWing=Wing(false);birdWing=Wing(true);
                rendererOwner.Bee=new WildlifePatchRenderer.Shape{Body=beeBody,Wing=beeWing,LeftPivot=new Vector3(-.04f,.025f,0),RightPivot=new Vector3(.04f,.025f,0),FlapDegrees=38};
                rendererOwner.Bird=new WildlifePatchRenderer.Shape{Body=birdBody,Wing=birdWing,LeftPivot=new Vector3(-.07f,.02f,0),RightPivot=new Vector3(.07f,.02f,0),FlapDegrees=28};
                if(!rendererOwner.TrySetHabitat(x,y,z,seed,source)){Release();return false;}
                authority=source;configured=true;return true;
            }
            catch{Release();throw;}
        }
        // Explicit ordering contract: integration owner invokes this AFTER its
        // camera pose + floating-origin update. No competing automatic Update.
        public void RenderAfterCamera()
        {
            if(!configured||!isActiveAndEnabled||authority==null)return;
            if(!authority.TryFrame(out double t,out double ex,out double ey,out double ez,out double ox,out double oy,out double oz))return;
            rendererOwner.SetView(ex,ey,ez,ox,oy,oz);rendererOwner.RenderAtTime(t,12);
        }
        void OnDisable(){if(rendererOwner!=null)rendererOwner.enabled=false;}
        void OnEnable(){if(rendererOwner!=null)rendererOwner.enabled=true;}
        void OnDestroy(){Release();}
        void Release()
        {
            if(rendererOwner!=null){rendererOwner.UnloadHabitat();Destroy(rendererOwner);rendererOwner=null;}
            foreach(var mesh in new[]{beeBody,beeWing,birdBody,birdWing})if(mesh!=null)Destroy(mesh);
            beeBody=beeWing=birdBody=birdWing=null;
            if(claimed)Owners.Remove(ownedCamera);
            claimed=configured=false;authority=null;ownedCamera=null;
        }
        static Mesh Body(bool bird)
        {
            var v=new List<Vector3>();var c=new List<Color>();var t=new List<int>();
            if(bird)
            {
                Box(v,c,t,new Vector3(0,0,0),new Vector3(.13f,.13f,.33f),new Color(.19f,.15f,.11f));
                Box(v,c,t,new Vector3(0,.045f,.18f),new Vector3(.10f,.10f,.10f),new Color(.25f,.22f,.17f));
                Box(v,c,t,new Vector3(0,0,.25f),new Vector3(.035f,.035f,.06f),new Color(.65f,.49f,.17f));
            }
            else
            {
                for(int i=0;i<4;i++)Box(v,c,t,new Vector3(0,0,(i-1.5f)*.045f),new Vector3(.075f,.075f,.045f),i%2==0?new Color(.95f,.67f,.04f):new Color(.07f,.06f,.025f));
                Box(v,c,t,new Vector3(0,.01f,.11f),new Vector3(.065f,.065f,.065f),new Color(.06f,.045f,.025f));
            }
            return Finish(v,c,t,bird?"Original small bird body":"Original striped bee body");
        }
        static Mesh Wing(bool bird)
        {
            var v=new List<Vector3>();var c=new List<Color>();var t=new List<int>();
            // Mesh origin is the hinge. Extends left; right draw rotates180Y.
            Box(v,c,t,new Vector3(bird?-.18f:-.07f,0,0),new Vector3(bird?.36f:.14f,.012f,bird?.16f:.085f),bird?new Color(.30f,.27f,.22f):new Color(.94f,.96f,1));
            return Finish(v,c,t,bird?"Original bird wing":"Original white bee wing");
        }
        static Mesh Finish(List<Vector3> v,List<Color> c,List<int> t,string name)
        {var m=new Mesh{name=name};try{m.SetVertices(v);m.SetColors(c);m.SetTriangles(t,0);m.RecalculateNormals();m.RecalculateBounds();return m;}catch{Destroy(m);throw;}}
        static void Box(List<Vector3> vertices,List<Color> colors,List<int> triangles,Vector3 center,Vector3 size,Color color)
        {
            Vector3 h=size*.5f;
            Vector3[] corners={new Vector3(-h.x,-h.y,-h.z),new Vector3(h.x,-h.y,-h.z),new Vector3(h.x,h.y,-h.z),new Vector3(-h.x,h.y,-h.z),new Vector3(-h.x,-h.y,h.z),new Vector3(h.x,-h.y,h.z),new Vector3(h.x,h.y,h.z),new Vector3(-h.x,h.y,h.z)};
            int[] faces={0,3,2,1,4,5,6,7,0,4,7,3,1,2,6,5,3,7,6,2,0,1,5,4};
            for(int f=0;f<6;f++)
            {int start=vertices.Count;for(int j=0;j<4;j++){vertices.Add(center+corners[faces[f*4+j]]);colors.Add(color);}triangles.Add(start);triangles.Add(start+1);triangles.Add(start+2);triangles.Add(start);triangles.Add(start+2);triangles.Add(start+3);}
        }
    }
}
