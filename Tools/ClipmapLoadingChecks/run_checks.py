"""Small production runtime/loader lifecycle test; graphics boundary is modeled.
No Unity control, full world load, native rendering or performance claim.
"""
from pathlib import Path
import argparse, datetime, hashlib, json, shutil, subprocess
parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--report',default='Docs/ClipmapEvidence/loading-20261029/lifecycle.json');options=parser.parse_args()
ROOT=Path(__file__).resolve().parents[2]
folder=Path(__file__).parent
out=ROOT/'.terrain-cache/clipmap-loading-checks'
out.mkdir(parents=True,exist_ok=True)
dotnet=Path(shutil.which('dotnet'))
version=lambda p:tuple(map(int,p.name.split('.')))
sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=version)[-1]
refs=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=version)[-1]/'ref/net8.0'
bootstrap=ROOT/'Assets/AshenVoxel/Exploration/ExplorationBootstrapClipmap.cs'
text=bootstrap.read_text(encoding='utf-8')
start=text.index('        IEnumerator WarmClipmapBeforeReady(WorldPoint spawn)')
opening=text.index('{',start);depth=1;end=opening+1
while depth:
    depth+=(text[end]=='{')-(text[end]=='}');end+=1
handoff=out/'Handoff.cs'
handoff.write_text('using System.Collections;using UnityEngine;using AshenVoxel.WorldScale;using AshenVoxel.WorldScaleRuntime;namespace AshenVoxel.Exploration{public sealed class ExplorationBootstrap{public TerrainClipmap Clipmap;public WorldExplorer Vista;public string Status;public IEnumerator Handoff()=>WarmClipmapBeforeReady(new WorldPoint(0,0,0));\n'+text[start:end]+'}}',encoding='utf-8')
sources=sorted((ROOT/'Assets/AshenVoxel/Core').rglob('*.cs'))+sorted((ROOT/'Assets/AshenVoxel/WorldScaleData').glob('*.cs'))
sources += [ROOT/'Assets/AshenVoxel/WorldScaleRuntime/TerrainClipmap.cs',ROOT/'Assets/AshenVoxel/WorldScaleRuntime/TerrainClipmapFarAppearance.cs',folder/'UnityShim.cs',folder/'Program.cs']
hash_files=sources+[bootstrap,Path(__file__)]
hashes={p.relative_to(ROOT).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in hash_files}
assembly=out/'Checks.dll';q=lambda p:'"'+str(p)+'"'
args=['-nologo','-langversion:9.0','-optimize+','-target:exe','-out:'+q(assembly)]
args+=['-r:'+q(p) for p in sorted(refs.glob('*.dll'))]+[q(p) for p in sources+[handoff]]
rsp=out/'compile.rsp';rsp.write_text('\n'.join(args),encoding='utf-8')
subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],check=True)
assembly.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}),encoding='utf-8')
result=subprocess.run([str(dotnet),str(assembly)],capture_output=True,text=True,timeout=20)
print(result.stdout+result.stderr)
assert hashes=={p.relative_to(ROOT).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in hash_files},'Source changed during check'
evidence=ROOT/options.report;evidence.parent.mkdir(parents=True,exist_ok=True)
evidence.write_text(json.dumps(dict(status='PASS' if result.returncode==0 else 'FAIL',scope=__doc__,generated_utc=datetime.datetime.now(datetime.timezone.utc).isoformat(),source_sha256=hashes,exit_code=result.returncode,stdout=result.stdout,stderr=result.stderr),indent=2)+'\n',encoding='utf-8')
result.check_returncode()
