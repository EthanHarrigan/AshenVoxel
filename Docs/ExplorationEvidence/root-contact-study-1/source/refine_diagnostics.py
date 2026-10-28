"""Preserve the first evidence; fix camera placement and isolate JIT effects."""
from pathlib import Path
import hashlib,json,os,shutil,subprocess
HERE=Path(__file__).resolve().parent
source=(HERE/'render_final.py').read_text(encoding='utf-8')
source=source.replace("OUT=HERE/'continuous-shaft'","OUT=HERE/'grounded-camera'")
source=source.replace("target=HERE/'compare_camera.py'","target=HERE/'compare_grounded_camera.py'")
source=source.replace("target.write_text(source.replace(old,'horizontal=np.asarray(view,dtype=float)'),encoding='utf-8')", """camera_line='camera=target+horizontal*12+np.array([0,1.2,0])'
assert source.count(camera_line)==1
camera_fix=camera_line+'\\n    ix=int(math.floor((camera[0]-to[0])/.1));iz=int(math.floor((camera[2]-to[2])/.1))\\n    assert 0<=ix<a.shape[1] and 0<=iz<a.shape[0]\\n    camera[1]=max(camera[1],(int(a[\"top\"][iz,ix])+1)*.1+1.7)'
target.write_text(source.replace(old,'horizontal=np.asarray(view,dtype=float)').replace(camera_line,camera_fix),encoding='utf-8')""")
source=source.replace('import compare_camera as c','import compare_grounded_camera as c')
source=source.replace("HERE/'compare_camera.py'","HERE/'compare_grounded_camera.py'")
(HERE/'render_grounded_camera.py').write_text(source,encoding='utf-8')

original=HERE/'continuous-shaft';out=original/'jit-disabled';out.mkdir(exist_ok=True)
for prefix in ['cost','expected']:
    for id in [10,11]:shutil.copy2(original/f'{prefix}-{id}.bin',out/f'{prefix}-{id}.bin')
env=os.environ.copy();env['DOTNET_TieredCompilation']='0';env['DOTNET_TieredPGO']='0'
subprocess.run([shutil.which('dotnet'),str(original/'BaseCost.dll'),str(out)],check=True,env=env)
(out/'provenance.json').write_text(json.dumps({'scope':'same frozen BaseCost assembly and data, repeat with process-local tiered JIT disabled to remove first-site tiering disparity; not a system setting change',
    'process_environment':{k:env[k] for k in ['DOTNET_TieredCompilation','DOTNET_TieredPGO']},
    'assembly_sha256':hashlib.sha256((original/'BaseCost.dll').read_bytes()).hexdigest(),
    'source_provenance_sha256':hashlib.sha256((original/'cost-provenance.json').read_bytes()).hexdigest(),
    'input_sha256':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in out.glob('*.bin')}},indent=2),encoding='utf-8')
