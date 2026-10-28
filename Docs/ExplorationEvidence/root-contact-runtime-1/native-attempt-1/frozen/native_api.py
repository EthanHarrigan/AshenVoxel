from pathlib import Path
import datetime,hashlib,json,shutil,subprocess
HERE=Path(__file__).resolve().parent
DEV=HERE.parents[1];MAIN=DEV.parent/'AshenVoxel'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
stamp=datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%S%fZ')
out=HERE/'native-api'/stamp;out.mkdir(parents=True)
sources=[HERE/n for n in ['RootContactNativePreview.cs','SharedUpperSplit.cs','RootBaseChunkMesher.cs']]
inputs=sources+[DEV/'.terrain-cache/grounding-root-visual/sites.json',MAIN/'Assets/AshenVoxel/Exploration/Prototypes/ExplorationPrototypes.bytes']
for i in (10,11):
    inputs.extend([DEV/f'.terrain-cache/root-contact-runtime-prototype/v2-expected-{i}.bin',DEV/f'.terrain-cache/grounding-root-visual/terrain-{i}.bin'])
inputs.extend(MAIN/'Assets/AshenVoxel/Rendering'/name for name in ['WorldSurface.shader','MaterialOptics.hlsl','VoxelWind.hlsl','MaterialOpticsProfile.cs'])
frozen={str(p):sha(p) for p in inputs}
(HERE/'native-inputs.json').write_text(json.dumps({'files':[{'path':p,'sha256':h} for p,h in frozen.items()]},indent=2),encoding='utf8')
dotnet=Path(shutil.which('dotnet'));sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=lambda p:tuple(map(int,p.name.split('.'))))[-1]
lines=(DEV/'Tools/VoxelTerrainChecks/bin/unity-api/AshenVoxel.Exploration.rsp').read_text(encoding='utf8').splitlines()
options=[x for x in lines if x.startswith('-') and not x.startswith('-out:') and 'AshenVoxel-' not in x]
refs=[]
for name in ['AshenVoxel.Core','AshenVoxel.WorldScaleData','AshenVoxel.Rendering']:
    p=MAIN/f'Library/ScriptAssemblies/{name}.dll';refs.append(p);options.append('-r:"'+str(p)+'"')
options+=['-out:"'+str(out/'RootContactNativeFixture.dll')+'"']+['"'+str(p)+'"' for p in sources]
rsp=out/'compile.rsp';rsp.write_text('\n'.join(options),encoding='utf8')
identities=dict(frozen,**{str(p):sha(p) for p in refs})
result=subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],capture_output=True,text=True,encoding='utf8')
(out/'compile.log').write_text(result.stdout+result.stderr,encoding='utf8')
assert identities=={p:sha(Path(p)) for p in identities},'Concurrent change during API compile'
report={'status':'PASS' if result.returncode==0 else 'FAIL','utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'scope':'Isolated Editor fixture compiled against installed Unity6000.3.27f1 APIs and main currently compiled Core/WorldScaleData/Rendering assemblies. No import, native execution, Play, collision integration or FPS test.','source_input_assembly_sha256':identities,'compiler_returncode':result.returncode,'dll_sha256':sha(out/'RootContactNativeFixture.dll') if result.returncode==0 else None}
(out/'report.json').write_text(json.dumps(report,indent=2),encoding='utf8')
print(result.stdout+result.stderr);print(json.dumps({'status':report['status'],'report':str(out/'report.json')},indent=2));raise SystemExit(result.returncode)
