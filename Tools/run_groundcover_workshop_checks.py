#!/usr/bin/env python3
"""Execute actual ground-cover workshop generation using Unity API substitutes.

This is not a Unity Editor import, serialized scene, renderer or shader test.
"""
from pathlib import Path
import argparse, datetime, hashlib, json, os, re, shutil, subprocess

ROOT = Path(__file__).resolve().parents[1]


def version(path):
    return tuple(int(part) if part.isdigit() else 0 for part in path.name.split('.'))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet', type=Path)
    args = parser.parse_args()
    candidates = [args.dotnet,
                  Path(os.environ['DOTNET_ROOT']) / 'dotnet' if os.environ.get('DOTNET_ROOT') else None,
                  Path(shutil.which('dotnet')) if shutil.which('dotnet') else None,
                  ROOT.parent / '.dotnet/dotnet']
    dotnet = next((p.resolve() for p in candidates if p and p.is_file()), None)
    if dotnet is None:
        parser.error('Install .NET 8 SDK or pass --dotnet /path/to/dotnet')
    sdk = sorted((dotnet.parent / 'sdk').glob('8.*'), key=version)[-1]
    refs = sorted((dotnet.parent / 'packs/Microsoft.NETCore.App.Ref').glob('8.*'), key=version)[-1] / 'ref/net8.0'
    directory = ROOT / 'Tools/GroundCoverWorkshopChecks'
    output = directory / 'bin'
    output.mkdir(parents=True, exist_ok=True)
    runtime = ROOT / 'Assets/AshenVoxel/WorldScaleRuntime'
    sources = sorted((ROOT / 'Assets/AshenVoxel/Core').rglob('*.cs')) + [
        runtime / 'GroundCoverShowcase.cs', runtime / 'WorkshopCamera.cs',
        runtime / 'Editor/GroundCoverWorkshopSetup.cs'] + sorted(directory.glob('*.cs'))
    tracked = sources + [Path(__file__).resolve(), ROOT / 'Assets/Scenes/GroundCoverWorkshop.unity']
    tracked += [p.with_name(p.name + '.meta') for p in sources if ROOT / 'Assets' in p.parents]
    tracked += [ROOT / 'Assets/Scenes/GroundCoverWorkshop.unity.meta']

    def hashes():
        return {str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest() for p in tracked}

    before = hashes()
    script_guid = re.search(r'^guid: ([a-f0-9]{32})$',
                            (runtime / 'GroundCoverShowcase.cs.meta').read_text(), re.M).group(1)
    assert 'guid: ' + script_guid in (ROOT / 'Assets/Scenes/GroundCoverWorkshop.unity').read_text(), 'Scene script reference is broken'
    quote = lambda p: '"' + str(p) + '"'
    assembly = output / 'GroundCoverWorkshopChecks.dll'
    options = ['-nologo', '-warnaserror+', '-langversion:9.0', '-optimize+',
               '-target:exe', '-out:' + quote(assembly)]
    options += ['-r:' + quote(p) for p in sorted(refs.glob('*.dll'))] + [quote(p) for p in sources]
    response = output / 'compile.rsp'
    response.write_text('\n'.join(options))
    (output / 'GroundCoverWorkshopChecks.runtimeconfig.json').write_text(json.dumps({
        'runtimeOptions': {'tfm': 'net8.0', 'framework': {'name': 'Microsoft.NETCore.App', 'version': '8.0.0'}}}))
    print('Compiling actual workshop/runtime/editor C# with Unity API substitutes; not native Unity.', flush=True)
    subprocess.run([str(dotnet), str(sdk / 'Roslyn/bincore/csc.dll'), '@' + str(response)], check=True)
    report = ROOT / 'Docs/groundcover-workshop-validation.json'
    subprocess.run([str(dotnet), str(assembly), str(report)], check=True)
    if before != hashes():
        raise RuntimeError('Tracked source changed during workshop checks')
    result = json.loads(report.read_text())
    result.update({'generated_utc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
                   'source_sha256': before, 'sdk': sdk.name, 'scene_reference_check': 'PASS'})
    report.write_text(json.dumps(result, indent=2) + '\n')


if __name__ == '__main__':
    main()
