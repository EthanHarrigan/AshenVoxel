"""Focused engine-free far-appearance cache lifecycle checks. Never starts Unity."""
from pathlib import Path
import datetime, hashlib, json, shutil, subprocess

ROOT = Path(__file__).resolve().parents[2]
out = ROOT/'.terrain-cache/far-appearance-checks'/datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%d-%H%M%S-%f')
out.mkdir(parents=True)
dotnet = Path(shutil.which('dotnet'))
version = lambda p: tuple(map(int, p.name.split('.')))
sdk = sorted((dotnet.parent/'sdk').glob('8.*'), key=version)[-1]
refs = sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'), key=version)[-1]/'ref/net8.0'
sources = sorted((ROOT/'Assets/AshenVoxel/Core').rglob('*.cs'))
sources += sorted((ROOT/'Assets/AshenVoxel/WorldScaleData').glob('*.cs'))
sources += [Path(__file__).with_name('Program.cs')]
def hashes(): return {p.relative_to(ROOT).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest() for p in sources}
before = hashes()
q = lambda p: '"'+str(p)+'"'
assembly = out/'FarAppearanceChecks.dll'
rsp = out/'compile.rsp'
rsp.write_text('\n'.join(['-nologo','-warnaserror+','-langversion:9.0','-optimize+','-target:exe','-out:'+q(assembly)]+['-r:'+q(p) for p in sorted(refs.glob('*.dll'))]+[q(p) for p in sources]), encoding='utf-8')
def run(command, name):
    result = subprocess.run(command, text=True, capture_output=True, timeout=90)
    (out/name).write_text(result.stdout+result.stderr, encoding='utf-8')
    print(result.stdout+result.stderr, end='')
    return result
compiled = run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)], 'compile.log')
compiled.check_returncode()
assembly.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}))
result = run([str(dotnet),str(assembly),str(out/'result.json')], 'console.log')
report = json.loads((out/'result.json').read_text())
report.update(source_sha256=before,source_unchanged=before==hashes(),generated_utc=datetime.datetime.now(datetime.timezone.utc).isoformat(),native_gates='UNEXECUTED: GPU publication, shader sampling, appearance and frame timing')
(out/'result.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
print(out/'result.json')
result.check_returncode()
if not report['source_unchanged']: raise RuntimeError('Source changed during checks; preserve report and rerun affected checks')
