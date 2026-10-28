#!/usr/bin/env python3
"""Compile and execute actual bounded rock generation without Unity or restore."""
from pathlib import Path
import datetime, hashlib, json, os, shutil, subprocess
ROOT = Path(__file__).resolve().parents[1]
def version(p): return tuple(int(v) if v.isdigit() else 0 for v in p.name.split('.'))
def main():
    candidates = [Path(os.environ['DOTNET_ROOT'])/'dotnet' if os.environ.get('DOTNET_ROOT') else None,
                  Path(shutil.which('dotnet')) if shutil.which('dotnet') else None, ROOT.parent/'.dotnet/dotnet']
    dotnet = next((p.resolve() for p in candidates if p and p.is_file()), None)
    if dotnet is None: raise RuntimeError('Install .NET8 SDK or set DOTNET_ROOT')
    sdk = sorted((dotnet.parent/'sdk').glob('8.*'), key=version)[-1]
    refs = sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'), key=version)[-1]/'ref/net8.0'
    out = ROOT/'Tools/RockChecks/bin'; out.mkdir(parents=True, exist_ok=True)
    core = sorted((ROOT/'Assets/AshenVoxel/Core').rglob('*.cs'))
    files = core+[ROOT/'Tools/RockChecks/Program.cs']
    hashes = {str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest() for p in files}
    q = lambda p: '"'+str(p)+'"'
    def compile(assembly, refdir, sources, target):
        opts = ['-nologo', '-warnaserror+', '-langversion:9.0', '-optimize+', '-target:'+target, '-out:'+q(assembly)]
        opts += ['-r:'+q(p) for p in sorted(refdir.glob('*.dll'))]+[q(p) for p in sources]
        rsp = out/(assembly.stem+'.rsp'); rsp.write_text('\n'.join(opts))
        subprocess.run([str(dotnet), str(sdk/'Roslyn/bincore/csc.dll'), '@'+str(rsp)], check=True)
    assembly = out/'RockChecks.dll'; compile(assembly, refs, files, 'exe')
    (out/'RockChecks.runtimeconfig.json').write_text(json.dumps({'runtimeOptions': {'tfm': 'net8.0', 'framework': {'name': 'Microsoft.NETCore.App', 'version': '8.0.0'}}}))
    report = ROOT/'Docs/rock-validation.json'
    subprocess.run([str(dotnet), str(assembly), str(report)], check=True)
    standard = sorted((dotnet.parent/'packs/NETStandard.Library.Ref').glob('2.1.*'), key=version)
    if standard:
        compile(out/'RockNetStandard.dll', standard[-1]/'ref/netstandard2.1', core, 'library')
        print('PASS C#9/.NET Standard2.1 rock API compile')
    if hashes != {str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest() for p in files}:
        raise RuntimeError('Core changed during rock checks; rerun once source owners freeze')
    data = json.loads(report.read_text()); data.update({'source_sha256': hashes, 'generated_utc': datetime.datetime.now(datetime.timezone.utc).isoformat(), 'sdk': sdk.name})
    report.write_text(json.dumps(data, indent=2)+'\n')
if __name__ == '__main__': main()
