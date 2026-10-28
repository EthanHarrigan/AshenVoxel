#!/usr/bin/env python3
"""Compile actual WorldExplorer with Unity substitutes and check mesh/cache behavior."""
from pathlib import Path
import argparse,datetime,hashlib,importlib.util,json,os,re,shutil,subprocess
ROOT=Path(__file__).resolve().parents[1]
def version(p):return tuple(int(v) if v.isdigit() else 0 for v in p.name.split('.'))
def main():
 parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--live-url',help='Optional real provider URL for the already-cached canonical model integration window.');parser.add_argument('--baked-height',type=Path);parser.add_argument('--baked-climate',type=Path);args=parser.parse_args()
 if bool(args.baked_height)!=bool(args.baked_climate):parser.error('Supply both --baked-height and --baked-climate')
 candidates=[Path(os.environ['DOTNET_ROOT'])/'dotnet' if os.environ.get('DOTNET_ROOT') else None,Path(shutil.which('dotnet')) if shutil.which('dotnet') else None,ROOT.parent/'.dotnet/dotnet']
 dotnet=next((p.resolve() for p in candidates if p and p.is_file()),None)
 if dotnet is None:raise RuntimeError('Install .NET8 SDK or set DOTNET_ROOT')
 sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=version)[-1]
 refs=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=version)[-1]/'ref/net8.0'
 out=ROOT/'Tools/WorldExplorerChecks/bin';out.mkdir(parents=True,exist_ok=True);assembly=out/'WorldExplorerChecks.dll';quote=lambda p:'"'+str(p)+'"'
 files=sorted((ROOT/'Assets/AshenVoxel/Core').rglob('*.cs'))+sorted((ROOT/'Assets/AshenVoxel/WorldScaleData').rglob('*.cs'))
 # The independent workshop runner compiles its camera/scene with its own shim.
 # Keep this harness focused on the explorer, remote transport and visual water.
 files+=[ROOT/'Assets/AshenVoxel/WorldScaleRuntime'/name for name in ['WorldExplorer.cs','WorldExplorerIntegration.cs','WorldExplorerVoxels.cs','RemoteTerrainField.cs','VisualWater.cs']]+sorted((ROOT/'Tools/WorldExplorerChecks').glob('*.cs'))+[ROOT/'Tools/AuthoringChecks/ProfileShim.cs']
 hashes={str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest() for p in files}
 options=['-nologo','-warnaserror+','-langversion:9.0','-optimize+','-target:exe','-out:'+quote(assembly)]
 options+=['-r:'+quote(p) for p in sorted(refs.glob('*.dll'))]+[quote(p) for p in files]
 rsp=out/'compile.rsp';rsp.write_text('\n'.join(options));(out/'WorldExplorerChecks.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}))
 subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],check=True)
 # Exercise the actual Python producer -> C# consumer format, with explicit test
 # data/backend identity. This is protocol evidence, not neural inference.
 import numpy as np
 spec=importlib.util.spec_from_file_location('ashen_bridge',ROOT/'Tools/DiffusionBridge/bridge.py');bridge=importlib.util.module_from_spec(spec);spec.loader.exec_module(bridge)
 coords=np.arange(4,dtype=np.float32)-1
 heights=6500+coords[:,None]*3+coords[None,:]
 climate=np.empty((5,4,4),dtype=np.float32)
 for channel,value in enumerate([-5,8,1200,35,-.0065]):climate[channel]=value
 raw=bridge.encode_region({'world_id':'a'*64,'spec':{'spacing_m':30,'climate_encoding':'physical-si-v2'}},(-1,-1,4,4),
  (heights,climate,np.full((4,4),12,dtype=np.uint8),np.ones((4,4),dtype=np.uint8)),
  'FAKE-CONTRACT-TEST-NOT-NEURAL')
 fixture=out/'region.atdr';fixture.write_bytes(raw)
 command=[str(dotnet),str(assembly),str(fixture)]
 if args.live_url or args.baked_height:command.append(args.live_url or '')
 if args.baked_height:command.extend([str(args.baked_height.resolve()),str(args.baked_climate.resolve())])
 result=subprocess.run(command,text=True,capture_output=True)
 print(result.stdout,end='')
 if result.stderr:print(result.stderr,end='')
 result.check_returncode()
 if hashes!={str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest() for p in files}:raise RuntimeError('Source changed during WorldExplorer checks')
 count=int(re.search(r'All (\d+) WorldExplorer checks passed',result.stdout)[1])
 report={'status':'PASS','checks':count,'generated_utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),
  'scope':'Actual runtime C# with Unity API substitutes; Python-producer/C#-consumer fake transport fixture. Not native Unity, Editor, URP or real-model execution.',
  'sdk':sdk.name,'source_sha256':hashes,'bridge_producer_sha256':hashlib.sha256((ROOT/'Tools/DiffusionBridge/bridge.py').read_bytes()).hexdigest(),
  'fixture_sha256':hashlib.sha256(raw).hexdigest()}
 if args.live_url:
  report['live_probe']=json.loads(next(line.split('LIVE_MODEL_PROBE ',1)[1] for line in result.stdout.splitlines() if line.startswith('LIVE_MODEL_PROBE ')))
 if args.baked_height:
  report['baked_climate_probe']=json.loads(next(line.split('BAKED_CLIMATE_PROBE ',1)[1] for line in result.stdout.splitlines() if line.startswith('BAKED_CLIMATE_PROBE ')))
 (ROOT/'Docs/world-explorer-validation.json').write_text(json.dumps(report,indent=2)+'\n')
if __name__=='__main__':main()
