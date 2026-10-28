using System;
using AshenVoxel.Exploration;
using AshenVoxel.WorldScale;
using UnityEngine;
namespace AshenVoxel.WildlifeDraft
{
    // One original, world-fixed small bee patch, explicitly ticked by Bootstrap
    // after camera/population origin updates. No habitat streaming or bird AI.
    public sealed class ExplorationBeePatch : MonoBehaviour,IWildlifeFrameAuthority
    {
        ExplorationBootstrap boot;
        WildlifeFixedPatchOwner owner;
        Material material;
        HabitatGridCertificate certificate;
        HabitatEnvelope envelope;
        Func<double,double,double> terrainTop;
        double spawnX,spawnZ,anchorX,anchorY,anchorZ;
        int attempt;
        string identity;
        bool initialized,stopped;
        long revision;
        public long Revision=>revision;
        public string Status {get;private set;}="Waiting for ready terrain and props";
        public int CertificateSamples=>certificate?.Samples??0;
        public int DrawCalls=>owner!=null?owner.LastDrawCalls:0;
        public int DrawnActors=>owner!=null?owner.LastDrawnActors:0;
        public void Initialize(ExplorationBootstrap source){boot=source;terrainTop=Top;}
        double Top(double x,double z)=>VoxelTerrainPage.SurfaceTop(boot.Vista.TerrainField.Sample(x,z).HeightMeters);
        public void TickAfterCamera()
        {
            try{Tick();}
            catch(Exception exception){stopped=true;Status="Bee patch stopped: "+exception.Message;Release();Debug.LogWarning(Status,this);}
        }
        void Tick()
        {
            if(stopped||!isActiveAndEnabled||boot==null||!boot.Ready||boot.Failed||boot.Player==null)return;
            if(!initialized){spawnX=boot.Player.Feet.X;spawnZ=boot.Player.Feet.Z;initialized=true;}
            if(owner!=null){owner.RenderAfterCamera();return;}
            if(attempt>=8){Status="No certified local bee clearing";stopped=true;return;}
            string current=boot.WorldField.SourceIdentity;
            if(certificate==null||identity!=current)
            {
                // Fixed deterministic candidates around initial spawn, not the camera.
                double angle=attempt*Math.PI*.25;anchorX=spawnX+Math.Cos(angle)*5;anchorZ=spawnZ+Math.Sin(angle)*5;
                anchorY=Top(anchorX,anchorZ)+.15;
                envelope=new HabitatEnvelope(anchorX,anchorY,anchorZ,false,.25);
                identity=current;certificate=new HabitatGridCertificate(envelope);
            }
            if(!boot.Population.IsReady(anchorX,anchorZ,2))return;
            certificate.Step(terrainTop,32);
            Status="Certifying bee clearing: "+certificate.Samples+" terrain columns";
            if(!certificate.Complete)return;
            if(!certificate.Clear||Blocked()){attempt++;certificate=null;return;}
            Shader shader=Resources.Load<Shader>("WildlifeVertexColor");
            if(shader==null||!shader.isSupported){Status="Bee shader unavailable";stopped=true;return;}
            material=new Material(shader){name="Owned local bee material",enableInstancing=true};
            owner=gameObject.AddComponent<WildlifeFixedPatchOwner>();
            if(!owner.Configure(boot.Vista.MainCamera,material,this,anchorX,anchorY,anchorZ,unchecked((uint)boot.ActiveSettings.Seed),6,0,.25f))
            {Status="Bee ownership/certificate rejected";stopped=true;Release();return;}
            Status="Six bees in one certified fixed clearing";
            owner.RenderAfterCamera();
        }
        bool Blocked()=>boot.Population.BlocksBody(envelope.MinX,envelope.MinY,envelope.MinZ,envelope.MaxX,envelope.MaxY,envelope.MaxZ);
        public bool IsWholeVolumeClear(HabitatEnvelope volume)=>certificate!=null&&certificate.Complete&&certificate.Clear&&
            volume.MinX==envelope.MinX&&volume.MinY==envelope.MinY&&volume.MinZ==envelope.MinZ&&
            volume.MaxX==envelope.MaxX&&volume.MaxY==envelope.MaxY&&volume.MaxZ==envelope.MaxZ&&
            boot.WorldField.SourceIdentity==identity&&boot.Population.IsReady(anchorX,anchorZ,2)&&!Blocked();
        public bool TryFrame(out double time,out double ex,out double ey,out double ez,out double ox,out double oy,out double oz)
        {
            time=Time.timeAsDouble;ex=ey=ez=ox=oy=oz=0;
            if(boot==null||!boot.Ready||boot.Failed)return false;
            if(boot.WorldField.SourceIdentity!=identity||Blocked())
            {revision++;stopped=true;Status="Bee habitat invalidated; patch unloaded";return false;}
            if(!boot.Population.IsReady(anchorX,anchorZ,2))return false;
            var eye=boot.Player.Eye;var origin=boot.Vista.RenderOrigin;
            ex=eye.X;ey=eye.Y;ez=eye.Z;ox=origin.X;oy=origin.Y;oz=origin.Z;return true;
        }
        void Release(){if(owner!=null)Destroy(owner);if(material!=null)Destroy(material);owner=null;material=null;}
        void OnDisable(){if(owner!=null)owner.enabled=false;}
        void OnEnable(){if(owner!=null)owner.enabled=true;}
        void OnDestroy(){Release();}
    }
}
