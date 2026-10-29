"""Clipmap CPU lifecycle/geometry checks. Does not launch or control Unity."""
from pathlib import Path
import datetime, hashlib, json, shutil, subprocess
ROOT = Path(__file__).resolve().parents[2]
stamp = datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%d-%H%M%S-%f')
out = ROOT / '.terrain-cache/clipmap-checks' / stamp
out.mkdir(parents=True)
dotnet_path = shutil.which('dotnet')
if not dotnet_path:
    raise RuntimeError('Install .NET 8 SDK before running clipmap checks')
dotnet = Path(dotnet_path)
version = lambda p: tuple(map(int, p.name.split('.')))
sdk = sorted((dotnet.parent / 'sdk').glob('8.*'), key=version)[-1]
refs = sorted((dotnet.parent / 'packs/Microsoft.NETCore.App.Ref').glob('8.*'), key=version)[-1] / 'ref/net8.0'
sources = sorted((ROOT / 'Assets/AshenVoxel/Core').rglob('*.cs'))
sources += sorted((ROOT / 'Assets/AshenVoxel/WorldScaleData').glob('*.cs'))
sources += [ROOT / 'Assets/AshenVoxel/Exploration/Editor/ClipmapChecks.cs', ROOT / 'Assets/AshenVoxel/Exploration/Editor/ClipmapSurfaceOracle.cs', Path(__file__).with_name('Program.cs')]
hashes = {p.relative_to(ROOT).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest() for p in sources}
assembly = out / 'ClipmapChecks.dll'
quote = lambda p: '"' + str(p) + '"'
arguments = ['-nologo', '-warnaserror+', '-langversion:9.0', '-optimize+', '-target:exe', '-out:' + quote(assembly)]
arguments += ['-r:' + quote(p) for p in sorted(refs.glob('*.dll'))] + [quote(p) for p in sources]
rsp = out / 'compile.rsp'
rsp.write_text('\n'.join(arguments), encoding='utf-8')
def run(command, name):
    result = subprocess.run(command, text=True, capture_output=True)
    (out / name).write_text(result.stdout + result.stderr, encoding='utf-8')
    print(result.stdout + result.stderr, end='')
    result.check_returncode()
run([str(dotnet), str(sdk / 'Roslyn/bincore/csc.dll'), '@' + str(rsp)], 'compile.log')
assembly.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions': {'tfm': 'net8.0', 'framework': {'name': 'Microsoft.NETCore.App', 'version': '8.0.0'}}}))
run([str(dotnet), str(assembly), str(out / 'result.json')], 'console.log')
if hashes != {p.relative_to(ROOT).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest() for p in sources}:
    raise RuntimeError('Source changed during clipmap checks; result is not source-matched')
report = json.loads((out / 'result.json').read_text(encoding='utf-8'))
report.update(source_sha256=hashes, generated_utc=datetime.datetime.now(datetime.timezone.utc).isoformat(), native_gates='UNEXECUTED: shader import, rendering, holes, tree alignment and GPU/main-thread timing')
(out / 'result.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
print(out / 'result.json')
