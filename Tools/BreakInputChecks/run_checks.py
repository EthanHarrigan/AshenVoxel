"""Deterministic tap/hold/publication-admission checks; no Unity execution."""
from pathlib import Path
import datetime, hashlib, json, shutil, subprocess

ROOT = Path(__file__).resolve().parents[2]

def version(path):
    return tuple(map(int, path.name.split('.')))

def main():
    stamp = datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%d-%H%M%S-%f')
    out = ROOT / '.terrain-cache/redwood-break-input' / stamp
    out.mkdir(parents=True)
    dotnet = Path(shutil.which('dotnet'))
    sdk = sorted((dotnet.parent / 'sdk').glob('8.*'), key=version)[-1]
    refs = sorted((dotnet.parent / 'packs/Microsoft.NETCore.App.Ref').glob('8.*'), key=version)[-1] / 'ref/net8.0'
    sources = [ROOT / 'Assets/AshenVoxel/Exploration/NaturalMatterBreakInput.cs', Path(__file__).with_name('Program.cs')]
    hashes = {str(p.relative_to(ROOT)).replace('\\', '/'): hashlib.sha256(p.read_bytes()).hexdigest() for p in sources}
    quote = lambda p: '"' + str(p) + '"'
    assembly = out / 'BreakInputChecks.dll'
    options = ['-nologo', '-warnaserror+', '-langversion:9.0', '-optimize+', '-target:exe', '-out:' + quote(assembly)]
    options += ['-r:' + quote(p) for p in sorted(refs.glob('*.dll'))] + [quote(p) for p in sources]
    response = out / 'compile.rsp'
    response.write_text('\n'.join(options), encoding='utf-8')
    result = subprocess.run([str(dotnet), str(sdk / 'Roslyn/bincore/csc.dll'), '@' + str(response)], text=True, capture_output=True)
    (out / 'compile.log').write_text(result.stdout + result.stderr, encoding='utf-8')
    print(result.stdout + result.stderr, end='')
    result.check_returncode()
    assembly.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions': {'tfm': 'net8.0', 'framework': {'name': 'Microsoft.NETCore.App', 'version': '8.0.0'}}}))
    result = subprocess.run([str(dotnet), str(assembly), str(out / 'result.json')], text=True, capture_output=True)
    (out / 'console.log').write_text(result.stdout + result.stderr, encoding='utf-8')
    print(result.stdout + result.stderr, end='')
    result.check_returncode()
    if hashes != {str(p.relative_to(ROOT)).replace('\\', '/'): hashlib.sha256(p.read_bytes()).hexdigest() for p in sources}:
        raise RuntimeError('Input sources changed during checks')
    report = json.loads((out / 'result.json').read_text())
    report.update(source_sha256=hashes, generated_utc=datetime.datetime.now(datetime.timezone.utc).isoformat())
    (out / 'result.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(out / 'result.json')

if __name__ == '__main__':
    main()
