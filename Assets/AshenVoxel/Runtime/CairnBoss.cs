using System.Collections;
using AshenVoxel.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AshenVoxel.Runtime
{
    /// <summary>Telegraphed terrain-attack harness; no claim of final boss AI or rigid voxel physics.</summary>
    public sealed class CairnBoss : MonoBehaviour
    {
        public DemoBootstrap Demo;
        public bool AutoAttack;
        float nextAttack = 10;
        bool attacking;
        LineRenderer warning;
        Material warningMaterial;
        Transform figure;
        public void Build(Material stone, Material eye)
        {
            ResetPosition();
            figure = new GameObject("Cairn silhouette").transform; figure.SetParent(transform,false);
            Part("Torso",new Vector3(0,1.6f,0),new Vector3(1.2f,1.6f,.7f),stone);
            Part("Head",new Vector3(0,2.8f,0),new Vector3(.75f,.65f,.65f),stone);
            Part("Left arm",new Vector3(-.95f,1.5f,0),new Vector3(.5f,1.7f,.6f),stone);
            Part("Right arm",new Vector3(.95f,1.5f,0),new Vector3(.5f,1.7f,.6f),stone);
            Part("Left leg",new Vector3(-.36f,.5f,0),new Vector3(.5f,1,.6f),stone);
            Part("Right leg",new Vector3(.36f,.5f,0),new Vector3(.5f,1,.6f),stone);
            Part("Ember eye",new Vector3(0,2.85f,-.34f),new Vector3(.47f,.075f,.035f),eye);
            var light = new GameObject("Ember spill").AddComponent<Light>();
            light.transform.SetParent(figure,false); light.transform.localPosition = new Vector3(0,2.8f,-.5f);
            light.type = LightType.Point; light.color = new Color(1,.25f,.04f); light.range = 4; light.intensity = 3;
            warning = new GameObject("Slam telegraph").AddComponent<LineRenderer>();
            warning.transform.SetParent(transform,false); warning.loop = true; warning.positionCount = 65;
            warning.widthMultiplier = .055f;
            warningMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            warningMaterial.color = new Color(1,.21f,.04f); warning.sharedMaterial = warningMaterial; warning.enabled = false;
        }
        public void ResetPosition()
        {
            var w=Demo.World;
            int x=w.SizeX/2, z=w.SizeZ*3/5;
            int y=w.SizeY-1; while(y>0 && w.Get(x,y,z)==MaterialId.Air)y--;
            transform.position=new Vector3(x*w.VoxelSize,(y+1)*w.VoxelSize,z*w.VoxelSize);
        }
        public void CancelAttack()
        {
            StopAllCoroutines(); attacking=false; nextAttack=10;
            if(figure!=null)figure.localRotation=Quaternion.identity;
            if(warning!=null)warning.enabled=false;
        }
        void Part(string label,Vector3 position,Vector3 scale,Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = label;
            Destroy(go.GetComponent<Collider>()); go.transform.SetParent(figure,false);
            go.transform.localPosition = position; go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
        }
        void Update()
        {
            if (Demo.Player == null || attacking) return;
            nextAttack -= Time.deltaTime;
            if ((Keyboard.current != null && Keyboard.current.bKey.wasPressedThisFrame) || (AutoAttack && nextAttack <= 0)) StartCoroutine(Slam());
        }
        IEnumerator Slam()
        {
            attacking = true;
            Vector3 target = Demo.Player.transform.position;
            // Ground ray traverses current data; it never relies on stale MeshColliders.
            Vector3 start = new Vector3(target.x,Demo.World.SizeY*Demo.World.VoxelSize+1,target.z);
            if (VoxelRaycast.TryCast(Demo.World,start.x,start.y,start.z,0,-1,0,100,out VoxelHit hit)) target.y = (hit.Cell.y+1)*Demo.World.VoxelSize;
            warning.enabled = true;
            for (int i=0;i<65;i++)
            {
                float angle=i*2*Mathf.PI/64;
                warning.SetPosition(i,target+new Vector3(Mathf.Cos(angle)*1.8f,.07f,Mathf.Sin(angle)*1.8f));
            }
            float elapsed=0;
            while(elapsed<1.5f)
            {
                elapsed+=Time.deltaTime;
                figure.localRotation = Quaternion.Euler(-Mathf.Sin(elapsed/1.5f*Mathf.PI/2)*18,0,0);
                warning.widthMultiplier=.04f+Mathf.Sin(elapsed*18)*.015f;
                yield return null;
            }
            // A bounded sequence of small edits spreads impact and remeshing over several frames.
            for(int i=0;i<9;i++)
            {
                float a=i*Mathf.PI*2/8;
                Vector3 p=i==8 ? target : target+new Vector3(Mathf.Cos(a)*1.05f,-.25f,Mathf.Sin(a)*1.05f);
                WorldEdits.CarveSphere(Demo.World,p.x,p.y-.3f,p.z,.85f,10);
                yield return null;
            }
            figure.localRotation=Quaternion.identity; warning.enabled=false;
            nextAttack=10; attacking=false;
        }
        void OnDestroy() { if(warningMaterial!=null) Destroy(warningMaterial); }
    }
}
