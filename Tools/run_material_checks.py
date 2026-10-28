#!/usr/bin/env python3
"""Compile/run actual engine-free material/persistence checks without NuGet."""
from pathlib import Path
import datetime,hashlib,json,os,shutil,subprocess
ROOT=Path(__file__).resolve().parents[1]
def version(p):return tuple(int(v) if v.isdigit() else 0 for v in p.name.split('.'))
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
    candidates=[Path(os.environ['DOTNET_ROOT'])/'dotnet' if os.environ.get('DOTNET_ROOT') else None,
        Path(shutil.which('dotnet')) if shutil.which('dotnet') else None,ROOT.parent/'.dotnet/dotnet']
    dotnet=next((p.resolve() for p in candidates if p and p.is_file()),None)
    if dotnet is None:raise RuntimeError('Install .NET8 SDK or set DOTNET_ROOT')
    sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=version)[-1]
    refs=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=version)[-1]/'ref/net8.0'
    output=ROOT/'Tools/MaterialChecks/bin';output.mkdir(exist_ok=True,parents=True)
    files=sorted((ROOT/'Assets/AshenVoxel/Core').rglob('*.cs'))+[ROOT/'Tools/MaterialChecks/Program.cs']
    hashes={str(p.relative_to(ROOT)):sha(p) for p in files};q=lambda p:'"'+str(p)+'"'
    assembly=output/'MaterialChecks.dll'
    options=['-nologo','-warnaserror+','-langversion:9.0','-optimize+','-target:exe','-out:'+q(assembly)]
    options+=['-r:'+q(p) for p in sorted(refs.glob('*.dll'))]+[q(p) for p in files]
    response=output/'compile.rsp';response.write_text('\n'.join(options))
    (output/'MaterialChecks.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}))
    subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(response)],check=True)
    result=ROOT/'Docs/material-validation.json';temporary=result.with_suffix('.json.tmp')
    fixture=ROOT/'Tools/MaterialChecks/fixtures/legacy-format2.bin'
    subprocess.run([str(dotnet),str(assembly),str(fixture),str(temporary)],check=True)
    if hashes!={str(p.relative_to(ROOT)):sha(p) for p in files}:raise RuntimeError('Core material sources changed during checks; rerun')
    data=json.loads(temporary.read_text());data.update({'generated_utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),
        'sdk':sdk.name,'source_sha256':hashes,'fixture_sha256':sha(fixture)})
    temporary.write_text(json.dumps(data,indent=2)+'\n');temporary.replace(result)
if __name__=='__main__':main()
