#!/usr/bin/env python3
"""Compile actual tree authoring using API substitutes and test profile mappings, not Unity."""
from pathlib import Path
import argparse,json,os,shutil,subprocess
ROOT=Path(__file__).resolve().parents[1]
def version(p):return tuple(int(v) if v.isdigit() else 0 for v in p.name.split('.'))
def main():
 parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--dotnet',type=Path);parser.add_argument('--refresh-presets',action='store_true',help='Overwrite the four bundled profile assets from current Core.TreePresets values.');args=parser.parse_args()
 candidates=[args.dotnet,Path(os.environ['DOTNET_ROOT'])/'dotnet' if os.environ.get('DOTNET_ROOT') else None,Path(shutil.which('dotnet')) if shutil.which('dotnet') else None,ROOT.parent/'.dotnet/dotnet']
 dotnet=next((p.resolve() for p in candidates if p and p.is_file()),None)
 if dotnet is None:parser.error('Install .NET8 SDK or pass --dotnet /path/to/dotnet')
 sdks=sorted((dotnet.parent/'sdk').glob('8.*'),key=version);packs=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=version)
 if not sdks or not packs:parser.error('Selected installation requires .NET8 SDK and reference assemblies')
 out=ROOT/'Tools/AuthoringChecks/bin';out.mkdir(parents=True,exist_ok=True)
 assembly=out/'AuthoringChecks.dll';quote=lambda p:'"'+str(p)+'"'
 files=sorted((ROOT/'Assets/AshenVoxel/Core').rglob('*.cs'))
 files += sorted((ROOT/'Assets/AshenVoxel/Authoring').glob('*.cs'))
 files += sorted((ROOT/'Tools/AuthoringChecks').glob('*.cs'))
 options=['-nologo','-warnaserror+','-langversion:9.0','-target:exe','-out:'+quote(assembly)]
 options += ['-r:'+quote(p) for p in sorted((packs[-1]/'ref/net8.0').glob('*.dll'))]+[quote(p) for p in files]
 response=out/'compile.rsp';response.write_text('\n'.join(options))
 (out/'AuthoringChecks.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}))
 print('Compiling actual authoring sources with compile-only Unity/input/renderer substitutes; Unity engine and serialization are NOT being compiled.',flush=True)
 subprocess.run([str(dotnet),str(sdks[-1]/'Roslyn/bincore/csc.dll'),'@'+str(response)],check=True)
 subprocess.run([str(dotnet),str(assembly)],check=True)
 if args.refresh_presets:
  profiles=json.loads(subprocess.check_output([str(dotnet),str(assembly),'--export-profiles'],text=True))
  for species,fields in profiles.items():
   asset=ROOT/'Assets/AshenVoxel/Settings/Trees'/(species+'.asset')
   text=asset.read_text();prefix=text.split('  Species:',1)[0]
   def scalar(value):
    if isinstance(value,dict):return '{'+', '.join(key+': '+format(channel,'.9g') for key,channel in value.items())+'}'
    return format(value,'.9g') if isinstance(value,float) else str(value)
   asset.write_text(prefix+''.join('  '+key+': '+scalar(value)+'\n' for key,value in fields.items()))
   print('Refreshed bundled profile from actual Core.TreePresets: '+species)

if __name__=='__main__':main()
