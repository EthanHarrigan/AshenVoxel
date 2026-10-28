#!/usr/bin/env python3
"""Compile/run independent exhaustive AABB-versus-DDA checks using installed .NET 8, with no restore or network.

Usage: python3 Tools/run_raycast_checks.py [--dotnet /path/to/dotnet]
This uses the SDK's Roslyn compiler and installed framework reference assemblies.
It is useful in restricted environments where NuGet named mutexes are unavailable.
"""
import argparse
import datetime
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

def version(path):
    return tuple(int(p) if p.isdigit() else 0 for p in path.name.split('.'))

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet',type=Path)
    args=parser.parse_args()
    project=Path(__file__).resolve().parent.parent
    candidates=[args.dotnet,Path(os.environ['DOTNET_ROOT'])/'dotnet' if os.environ.get('DOTNET_ROOT') else None,
                Path(shutil.which('dotnet')) if shutil.which('dotnet') else None,project.parent/'.dotnet/dotnet']
    dotnet=next((p.resolve() for p in candidates if p and p.is_file()),None)
    if not dotnet:
        parser.error('Install .NET 8 SDK or pass --dotnet /path/to/dotnet.')
    root=dotnet.parent
    sdks=sorted((root/'sdk').glob('8.*'),key=version)
    refpacks=sorted((root/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=version)
    if not sdks or not refpacks:
        parser.error('The selected dotnet installation needs .NET 8 SDK and its reference pack.')
    compiler=sdks[-1]/'Roslyn/bincore/csc.dll'
    refs=refpacks[-1]/'ref/net8.0'
    output=project/'Tools/RaycastChecks/bin/Direct'
    output.mkdir(parents=True,exist_ok=True)
    assembly=output/'RaycastChecks.dll'
    quoted=lambda p:'"'+str(p)+'"'
    options=['-nologo','-warnaserror+','-target:exe','-out:'+quoted(assembly)]
    options+=['-r:'+quoted(p) for p in sorted(refs.glob('*.dll'))]
    sources=sorted((project/'Assets/AshenVoxel/Core').rglob('*.cs'))+[project/'Tools/RaycastChecks/Program.cs']
    hashes={str(p.relative_to(project)):hashlib.sha256(p.read_bytes()).hexdigest() for p in sources}
    options += [quoted(p) for p in sources]
    response=output/'compile.rsp'
    response.write_text('\n'.join(options),encoding='utf-8')
    (output/'RaycastChecks.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}),encoding='utf-8')
    print('Compiling with .NET SDK '+sdks[-1].name,flush=True)
    subprocess.run([str(dotnet),str(compiler),'@'+str(response)],check=True)
    result=project/'Docs/raycast-validation.json'
    temporary=result.with_suffix('.json.tmp')
    subprocess.run([str(dotnet),str(assembly),str(temporary)],check=True)
    if hashes != {str(p.relative_to(project)):hashlib.sha256(p.read_bytes()).hexdigest() for p in sources}:
        raise RuntimeError('Source changed during ray checks; rerun after edits settle.')
    data=json.loads(temporary.read_text())
    data['generated_utc']=datetime.datetime.now(datetime.timezone.utc).isoformat()
    data['sdk']=sdks[-1].name
    data['source_sha256']=hashes
    temporary.write_text(json.dumps(data,indent=2)+'\n')
    temporary.replace(result)
if __name__=='__main__':
    try: main()
    except subprocess.CalledProcessError as error: sys.exit(error.returncode)
