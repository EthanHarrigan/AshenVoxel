"""Stage pitch visibility against installed startup-lod-1; never writes Assets."""
from pathlib import Path
import datetime, difflib, hashlib, json

ROOT=Path(__file__).resolve().parents[2]
DEST=ROOT/'.terrain-cache/startup-throughput/pitch-candidate'
BASE=ROOT/'.terrain-cache/startup-throughput/pitch-baseline'
REL=Path('Assets/AshenVoxel/WorldScaleRuntime/WorldExplorerVoxels.cs')
before='''            Vector3 forward=view==null?Vector3.forward:view.transform.forward;
            double vertical=(view==null?75:view.fieldOfView)*Math.PI/360;
            double horizontal=Math.Atan(Math.Tan(vertical)*Math.Max(1,view==null?Screen.width/(double)Math.Max(1,Screen.height):view.aspect));
            return VoxelLodQuality.InView(tile,GlobalCamera,forward.x,forward.z,Math.Min(Math.PI, horizontal+20*Math.PI/180));'''
after='''            Quaternion rotation=view==null?Quaternion.Euler(0,0,0):view.transform.rotation;
            Vector3 forward=rotation*Vector3.forward,right=rotation*Vector3.right,up=rotation*Vector3.up;
            double length=Math.Sqrt((double)forward.x*forward.x+(double)forward.z*forward.z);
            if(length<.2)return true;
            double vertical=Math.Tan((view==null?75:view.fieldOfView)*Math.PI/360);
            double horizontal=vertical*Math.Max(1,view==null?Screen.width/(double)Math.Max(1,Screen.height):view.aspect);
            // Project the complete frustum ray rectangle onto horizontal heading
            // axes. Pitch/roll can widen it far beyond the level-camera FOV.
            double along=length-(Math.Abs(right.x*forward.x+right.z*forward.z)*horizontal+
                Math.Abs(up.x*forward.x+up.z*forward.z)*vertical)/length;
            double across=(Math.Abs(right.x*forward.z-right.z*forward.x)*horizontal+
                Math.Abs(up.x*forward.z-up.z*forward.x)*vertical)/length;
            // If the rectangle crosses the eye's heading plane, retaining every
            // heading is conservative. Otherwise bound its largest azimuth.
            double angle=along<=0?Math.PI:Math.Atan2(across,along);
            return VoxelLodQuality.InView(tile,GlobalCamera,forward.x,forward.z,Math.Min(Math.PI,angle+20*Math.PI/180));'''

raw=(ROOT/REL).read_bytes()
source=raw.decode('utf-8').replace('\r\n','\n')
if source.count(before)!=1: raise SystemExit('Installed visibility baseline does not match; inspect before restaging.')
candidate=source.replace(before,after)
(BASE/REL).parent.mkdir(parents=True,exist_ok=True)
(DEST/REL).parent.mkdir(parents=True,exist_ok=True)
(BASE/REL).write_bytes(raw)
(DEST/REL).write_text(candidate,encoding='utf-8')
digest=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
baseline_paths=[REL,Path('Assets/AshenVoxel/WorldScaleRuntime/WorldExplorer.cs'),Path('Assets/AshenVoxel/WorldScaleRuntime/WorldExplorerIntegration.cs'),Path('Assets/AshenVoxel/WorldScaleData/VoxelLodSurface.cs')]
manifest=dict(generated_utc=datetime.datetime.now(datetime.timezone.utc).isoformat(),
    baseline='installed startup-lod-1',baseline_sha256={p.as_posix():digest(ROOT/p) for p in baseline_paths},
    candidate_sha256={REL.as_posix():digest(DEST/REL)},changed_files=[REL.as_posix()])
(DEST/'source-manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
(DEST/'pitch-visibility.patch').write_text(''.join(difflib.unified_diff(source.splitlines(True),candidate.splitlines(True),fromfile='a/'+REL.as_posix(),tofile='b/'+REL.as_posix())),encoding='utf-8')
print(DEST)
