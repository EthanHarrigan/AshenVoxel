#!/usr/bin/env python3
"""Run actual mesher/regressions against a minimal geometry shim, NOT the Unity engine."""
from pathlib import Path
import argparse,json,os,shutil,subprocess
ROOT=Path(__file__).resolve().parents[1]
def version(p):return tuple(int(v) if v.isdigit() else 0 for v in p.name.split('.'))
def main():
 parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--dotnet',type=Path);args=parser.parse_args()
 candidates=[args.dotnet,Path(os.environ['DOTNET_ROOT'])/'dotnet' if os.environ.get('DOTNET_ROOT') else None,Path(shutil.which('dotnet')) if shutil.which('dotnet') else None,ROOT.parent/'.dotnet/dotnet']
 dotnet=next((p.resolve() for p in candidates if p and p.is_file()),None)
 if dotnet is None:parser.error('Install .NET8 SDK or pass --dotnet /path/to/dotnet')
 sdks=sorted((dotnet.parent/'sdk').glob('8.*'),key=version)
 packs=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=version)
 if not sdks or not packs:parser.error('Selected installation requires .NET8 SDK and reference assemblies')
 out=ROOT/'Tools/MeshChecks/bin';out.mkdir(parents=True,exist_ok=True)
 assembly=out/'MeshChecks.dll';quote=lambda p:'"'+str(p)+'"'
 files=sorted((ROOT/'Assets/AshenVoxel/Core').rglob('*.cs'))
 files += [ROOT/'Assets/AshenVoxel/Rendering/GreedyChunkMesher.cs']
 files += sorted((ROOT/'Assets/AshenVoxel/Rendering/Editor').glob('*.cs'))
 files += [ROOT/'Tools/MeshChecks/UnityShim.cs']
 options=['-nologo','-warnaserror+','-langversion:9.0','-define:UNITY_EDITOR','-target:exe','-out:'+quote(assembly)]
 options += ['-r:'+quote(p) for p in sorted((packs[-1]/'ref/net8.0').glob('*.dll'))]
 options += [quote(p) for p in files]
 response=out/'compile.rsp';response.write_text('\n'.join(options))
 (out/'MeshChecks.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}))
 print('Compiling actual mesher against minimal geometry shim; Unity engine and shaders are NOT being compiled.',flush=True)
 subprocess.run([str(dotnet),str(sdks[-1]/'Roslyn/bincore/csc.dll'),'@'+str(response)],check=True)
 subprocess.run([str(dotnet),str(assembly)],check=True)
if __name__=='__main__':main()
