"""Frozen-source .NET lifecycle checks; production Assets/evidence are untouched."""
from pathlib import Path
import argparse, datetime, hashlib, json, shutil, subprocess

ROOT = Path(__file__).resolve().parents[2]
STAGE = ROOT / '.terrain-cache/progressive-lod'
def version(p): return tuple(map(int, p.name.split('.')))
def digest(p): return hashlib.sha256(p.read_bytes()).hexdigest()

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--baseline', action='store_true')
    parser.add_argument('--production', action='store_true', help='Read current Assets directly instead of the staged WorldExplorer candidate.')
    parser.add_argument('--overlay-dir', type=Path, help='Override sources using canonical Assets/AshenVoxel/<assembly>/<file>.cs paths under this directory.')
    parser.add_argument('--program', type=Path, help='Use a specific C# check entrypoint instead of the standard lifecycle suite.')
    parser.add_argument('--program-arg', action='append', default=[], help='Append an explicit fixture argument after the output path.')
    parser.add_argument('--existing', action='store_true')
    parser.add_argument('--api', action='store_true')
    args = parser.parse_args()
    if args.baseline and args.production: parser.error('--baseline and --production are mutually exclusive')
    if args.program and (args.api or args.existing): parser.error('--program cannot be combined with --api or --existing')
    if args.program and not args.program.is_file(): parser.error('--program must name an existing C# source file')
    overlay = args.overlay_dir.resolve() if args.overlay_dir else None
    if overlay and not (overlay / 'Assets/AshenVoxel').is_dir():
        parser.error('--overlay-dir must contain canonical Assets/AshenVoxel source paths')
    stamp = datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%d-%H%M%S-%f')
    source_mode = 'production' if args.production else 'baseline' if args.baseline else 'candidate'
    label = source_mode + ('-overlay' if overlay else '') + ('-api' if args.api else '-existing' if args.existing else '-'+args.program.stem if args.program else '-progressive')
    out = STAGE / 'runs' / (stamp + '-' + label)
    out.mkdir(parents=True)
    dotnet = Path(shutil.which('dotnet')).resolve()
    sdk = sorted((dotnet.parent / 'sdk').glob('8.*'), key=version)[-1]
    refpack = 'NETStandard.Library.Ref' if args.api else 'Microsoft.NETCore.App.Ref'
    refver, reftarget = ('2.1.*', 'netstandard2.1') if args.api else ('8.*', 'net8.0')
    refs = sorted((dotnet.parent / 'packs' / refpack).glob(refver), key=version)[-1] / 'ref' / reftarget
    folders = ['Core', 'WorldScaleData', 'Rendering', 'WorldScaleRuntime', 'Exploration']
    hashes = {}
    source_paths = {}
    overlay_files = []
    groups = {}
    for folder in folders:
        d = ROOT / 'Assets/AshenVoxel' / folder
        originals = sorted(d.rglob('*.cs')) if folder == 'Core' else sorted(d.glob('*.cs'))
        relative_sources = {p.relative_to(ROOT) for p in originals}
        if overlay:
            overlay_folder = overlay / 'Assets/AshenVoxel' / folder
            additions = overlay_folder.rglob('*.cs') if folder == 'Core' else overlay_folder.glob('*.cs')
            relative_sources.update(p.relative_to(overlay) for p in additions)
        groups[folder] = []
        for rel in sorted(relative_sources):
            original = ROOT / rel
            staged = STAGE / ('baseline' if args.baseline else 'candidate') / original.name
            chosen = staged if not args.production and folder == 'WorldScaleRuntime' and staged.exists() else original
            if overlay and (overlay / rel).is_file():
                chosen = overlay / rel
                overlay_files.append(rel.as_posix())
            frozen = out / 'source' / rel
            frozen.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(chosen, frozen)
            hashes[rel.as_posix()] = digest(frozen)
            source_paths[rel.as_posix()] = str(chosen)
            groups[folder].append(frozen)
    if overlay and not overlay_files: parser.error('--overlay-dir contains no supported C# source overrides')
    q = lambda p: '"' + str(p) + '"'
    if args.api:
        external = sorted(Path('C:/Program Files/Unity/Hub/Editor/6000.3.27f1/Editor/Data/Managed/UnityEngine').glob('*.dll'))
        external += [ROOT / 'Library/ScriptAssemblies' / n for n in ['Unity.InputSystem.dll', 'Unity.RenderPipelines.Core.Runtime.dll', 'Unity.RenderPipelines.Universal.Runtime.dll']]
        built = []
        for folder in folders:
            assembly = out / ('AshenVoxel.' + folder + '.dll')
            options = ['-nologo', '-nostdlib+', '-define:UNITY_5_3_OR_NEWER,UNITY_STANDALONE_WIN,ENABLE_UPSCALER_FRAMEWORK,ENABLE_NVIDIA,ASHEN_NVIDIA_MODULE', '-langversion:9.0', '-target:library', '-optimize+', '-out:' + q(assembly)]
            rsp = assembly.with_suffix('.rsp')
            rsp.write_text('\n'.join(options + ['-r:' + q(p) for p in sorted(refs.glob('*.dll')) + external + built] + [q(p) for p in groups[folder]]), encoding='utf-8')
            subprocess.run([str(dotnet), str(sdk / 'Roslyn/bincore/csc.dll'), '@' + str(rsp)], check=True)
            built.append(assembly)
            print('PASS official Unity API:', folder, flush=True)
        data = {'status': 'PASS', 'assemblies': len(built), 'scope': 'Installed Unity6000.3.27f1 references/C#9/.NETStandard2.1; no native import, shaders, Editor or FPS.'}
    else:
        selected = ['WorldExplorer.cs', 'WorldExplorerIntegration.cs', 'WorldExplorerVoxels.cs', 'WorldExplorerMemory.cs', 'WorldExplorerTerrainSeams.cs', 'RemoteTerrainField.cs', 'VisualWater.cs']
        sources = groups['Core'] + groups['WorldScaleData'] + [p for p in groups['WorldScaleRuntime'] if p.name in selected]
        program = args.program.resolve() if args.program else ROOT / ('Tools/VoxelTerrainChecks/LodRuntimeProgram.cs' if args.existing else 'Tools/ProgressiveLod/Program.cs')
        for original in [ROOT / 'Tools/ExplorationSmokeChecks/UnityShim.cs', program]:
            rel = original.relative_to(ROOT)
            frozen = out / 'source' / rel
            frozen.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(original, frozen)
            sources.append(frozen)
            hashes[rel.as_posix()] = digest(frozen)
            source_paths[rel.as_posix()] = str(original)
        assembly = out / 'ProgressiveLodChecks.dll'
        options = ['-nologo', '-warnaserror+', '-langversion:9.0', '-optimize+', '-target:exe', '-out:' + q(assembly)]
        if args.baseline: options += ['-define:BASELINE']
        rsp = out / 'compile.rsp'
        rsp.write_text('\n'.join(options + ['-r:' + q(p) for p in sorted(refs.glob('*.dll'))] + [q(p) for p in sources]), encoding='utf-8')
        subprocess.run([str(dotnet), str(sdk / 'Roslyn/bincore/csc.dll'), '@' + str(rsp)], check=True)
        assembly.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions': {'tfm': 'net8.0', 'framework': {'name': 'Microsoft.NETCore.App', 'version': '8.0.0'}}}))
        report = out / 'result.json'
        completed = subprocess.run([str(dotnet), str(assembly), str(report), *args.program_arg], text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        (out / 'console.txt').write_text(completed.stdout, encoding='utf-8')
        print(completed.stdout)
        if completed.returncode:
            data = json.loads(report.read_text()) if report.is_file() else {}
            data.update(status='FAIL', returncode=completed.returncode)
        else: data = json.loads(report.read_text())
    data.update(source_sha256=hashes, source_paths=source_paths, source_mode=source_mode, program_args=args.program_arg,
                overlay_dir=str(overlay) if overlay else None, overlay_files=overlay_files,
                generated_utc=datetime.datetime.now(datetime.timezone.utc).isoformat(), source_snapshot=str(out / 'source'),
                runner_sha256=digest(Path(__file__)))
    (out / 'result.json').write_text(json.dumps(data, indent=2) + '\n')
    print(out / 'result.json', flush=True)
    if data['status'] != 'PASS': raise SystemExit(1)

if __name__ == '__main__': main()
