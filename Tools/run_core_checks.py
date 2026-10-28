#!/usr/bin/env python3
"""Compile/run engine-independent checks using installed .NET 8, with no restore or network.

Usage: python3 Tools/run_core_checks.py [--dotnet /path/to/dotnet]
This uses the SDK's Roslyn compiler and installed framework reference assemblies.
It is useful in restricted environments where NuGet named mutexes are unavailable.
"""
import argparse
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
    output=project/'Tools/CoreChecks/bin/Direct'
    output.mkdir(parents=True,exist_ok=True)
    assembly=output/'CoreChecks.dll'
    quoted=lambda p:'"'+str(p)+'"'
    options=['-nologo','-warnaserror+','-target:exe','-out:'+quoted(assembly)]
    options+=['-r:'+quoted(p) for p in sorted(refs.glob('*.dll'))]
    options += [quoted(p) for p in sorted((project/'Assets/AshenVoxel/Core').rglob('*.cs'))]
    options += [quoted(project/'Tools/CoreChecks/Program.cs')]
    response=output/'compile.rsp'
    response.write_text('\n'.join(options),encoding='utf-8')
    (output/'CoreChecks.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}),encoding='utf-8')
    print('Compiling with .NET SDK '+sdks[-1].name,flush=True)
    subprocess.run([str(dotnet),str(compiler),'@'+str(response)],check=True)
    standardpacks=sorted((root/'packs/NETStandard.Library.Ref').glob('2.1.*'),key=version)
    if standardpacks:
        standardrefs=standardpacks[-1]/'ref/netstandard2.1'
        standardoptions=['-nologo','-warnaserror+','-langversion:9.0','-target:library','-out:'+quoted(output/'AshenVoxel.Core.NetStandard21.dll')]
        standardoptions+=['-r:'+quoted(p) for p in sorted(standardrefs.glob('*.dll'))]
        standardoptions+=[quoted(p) for p in sorted((project/'Assets/AshenVoxel/Core').rglob('*.cs'))]
        standardresponse=output/'compile-netstandard21.rsp'
        standardresponse.write_text('\n'.join(standardoptions),encoding='utf-8')
        subprocess.run([str(dotnet),str(compiler),'@'+str(standardresponse)],check=True)
        print('PASS core compiles with C# 9 against .NET Standard 2.1 reference APIs',flush=True)
        probe=output/'MutationBoundaryProbe.cs'
        probe.write_text('using AshenVoxel.Core; public static class MutationBoundaryProbe { public static void ViaView(Chunk c) { c.Cells[0]=MaterialId.Wood; } public static void ViaBacking(Chunk c) { c.Data[0]=MaterialId.Wood; } }',encoding='utf-8')
        probeoptions=['-nologo','-langversion:9.0','-target:library','-out:'+quoted(output/'MutationBoundaryProbe.dll')]
        probeoptions+=['-r:'+quoted(p) for p in sorted(standardrefs.glob('*.dll'))]
        probeoptions+=['-r:'+quoted(output/'AshenVoxel.Core.NetStandard21.dll'),quoted(probe)]
        proberesponse=output/'compile-mutation-probe.rsp'
        proberesponse.write_text('\n'.join(probeoptions),encoding='utf-8')
        result=subprocess.run([str(dotnet),str(compiler),'@'+str(proberesponse)],capture_output=True,text=True)
        diagnostics=result.stdout+result.stderr
        if result.returncode==0 or 'CS8331' not in diagnostics or not any(code in diagnostics for code in ('CS1061','CS0122')):
            print(diagnostics,file=sys.stderr)
            raise SystemExit('FAIL external assembly mutation boundary probe')
        print('PASS external assembly cannot write read-only cells or access backing storage',flush=True)
    else:
        print('SKIP .NET Standard 2.1 API check: reference pack not installed',flush=True)
    subprocess.run([str(dotnet),str(assembly)],check=True)
if __name__=='__main__':
    try: main()
    except subprocess.CalledProcessError as error: sys.exit(error.returncode)
