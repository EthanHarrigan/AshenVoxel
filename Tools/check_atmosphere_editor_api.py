"""Compile the bounded atmosphere fixture against installed Unity Editor APIs.

Run check_voxel_unity_api.py first. Does not start Unity or execute the fixture.
"""
from pathlib import Path
import argparse
import datetime
import hashlib
import json
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--editor', type=Path, default=Path('C:/Program Files/Unity/Hub/Editor/6000.3.27f1/Editor'))
    parser.add_argument('--report', type=Path, default=ROOT/'Docs/AtmosphereEvidence/editor-api.json')
    args = parser.parse_args()
    out = ROOT/'Tools/VoxelTerrainChecks/bin/unity-api'
    source = ROOT/'Assets/AshenVoxel/Exploration/Editor/AtmosphereGraphicsCheck.cs'
    options = (out/'AshenVoxel.Exploration.rsp').read_text(encoding='utf-8').splitlines()
    options = [line for line in options if not line.endswith('.cs"') and not line.startswith('-out:')]
    options = [line+',UNITY_EDITOR' if line.startswith('-define:') else line for line in options]
    options += ['-out:"'+str(out/'AshenVoxel.AtmosphereFixture.dll')+'"', '-r:"'+str(out/'AshenVoxel.Exploration.dll')+'"']
    editor_refs = sorted((args.editor/'Data/Managed').glob('UnityEditor*.dll'))
    editor_refs += sorted((args.editor/'Data/Managed/UnityEngine').glob('UnityEditor*.dll'))
    if not editor_refs:
        raise SystemExit('Installed Unity Editor API assemblies not found.')
    options += ['-r:"'+str(p)+'"' for p in editor_refs]
    options += ['"'+str(source)+'"']
    rsp = out/'atmosphere-fixture.rsp'
    rsp.write_text('\n'.join(options), encoding='utf-8')
    dotnet = Path(shutil.which('dotnet')).resolve()
    sdk = sorted((dotnet.parent/'sdk').glob('8.*'), key=lambda p: tuple(map(int,p.name.split('.'))))[-1]
    subprocess.run([str(dotnet), str(sdk/'Roslyn/bincore/csc.dll'), '@'+str(rsp)], check=True)
    report = dict(status='PASS', scope='Installed Unity 6000.3.27f1 Editor API compilation only; fixture not executed.',
                  generated_utc=datetime.datetime.now(datetime.timezone.utc).isoformat(),
                  source_sha256={source.relative_to(ROOT).as_posix(): hashlib.sha256(source.read_bytes()).hexdigest()})
    args.report.parent.mkdir(parents=True,exist_ok=True)
    args.report.write_text(json.dumps(report, indent=2)+'\n', encoding='utf-8')
    print('PASS installed Unity Editor API compile: AtmosphereGraphicsCheck (not executed)')


if __name__ == '__main__':
    main()
