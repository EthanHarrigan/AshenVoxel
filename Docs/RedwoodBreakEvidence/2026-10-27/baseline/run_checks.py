"""Bounded matter visual-cache checks; numerical substitutes or real Unity APIs."""
from pathlib import Path
import argparse,datetime,hashlib,json,shutil,subprocess
ROOT=Path(__file__).resolve().parents[2]
def version(p):return tuple(map(int,p.name.split('.')))
def main():
    parser=argparse.ArgumentParser();parser.add_argument('--api',action='store_true');args=parser.parse_args()
    stamp=datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%d-%H%M%S-%f')
    out=ROOT/'.terrain-cache/natural-matter-renderer'/stamp;out.mkdir(parents=True)
    dotnet=Path(shutil.which('dotnet'));sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=version)[-1]
    pack='NETStandard.Library.Ref' if args.api else 'Microsoft.NETCore.App.Ref'
    refs=sorted((dotnet.parent/'packs'/pack).glob('2.1.*' if args.api else '8.*'),key=version)[-1]/'ref'/('netstandard2.1' if args.api else 'net8.0')
    sources=sorted((ROOT/'Assets/AshenVoxel/Core').rglob('*.cs'))
    sources += [ROOT/'Assets/_Validation/FiniteMatterSlice/Runtime'/name for name in ['LocalizedMatter.cs','FallingAggregate.cs']]
    sources += [ROOT/'Assets/AshenVoxel/Rendering/GreedyChunkMesher.cs']
    sources += [ROOT/'Assets/AshenVoxel/Exploration'/name for name in ['NaturalTreePhysics.cs','NaturalTreeTemplate.cs','NaturalMatterRenderer.cs']]
    external=sorted(Path('C:/Program Files/Unity/Hub/Editor/6000.3.27f1/Editor/Data/Managed/UnityEngine').glob('*.dll')) if args.api else []
    if not args.api:sources += [Path(__file__).with_name('UnityShim.cs'),Path(__file__).with_name('Program.cs')]
    hashes={str(p.relative_to(ROOT)).replace('\\','/'):hashlib.sha256(p.read_bytes()).hexdigest() for p in sources}
    q=lambda p:'"'+str(p)+'"'
    assembly=out/'NaturalMatterRendererChecks.dll';rsp=out/'compile.rsp'
    options=['-nologo','-warnaserror+','-langversion:9.0','-optimize+','-target:'+('library' if args.api else 'exe'),'-out:'+q(assembly)]
    options+=['-r:'+q(p) for p in sorted(refs.glob('*.dll'))+external]+[q(p) for p in sources]
    rsp.write_text('\n'.join(options),encoding='utf-8')
    result=subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],text=True,capture_output=True)
    (out/'compile.log').write_text(result.stdout+result.stderr,encoding='utf-8');print(result.stdout+result.stderr,end='');result.check_returncode()
    if args.api:report={'status':'PASS','scope':'Real installed Unity6000.3.27f1 APIs; no native import, shader, rendering or frame measurement.'}
    else:
        assembly.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}))
        result=subprocess.run([str(dotnet),str(assembly),str(out/'result.json')],text=True,capture_output=True)
        (out/'console.log').write_text(result.stdout+result.stderr,encoding='utf-8');print(result.stdout+result.stderr,end='');result.check_returncode();report=json.loads((out/'result.json').read_text())
    if hashes!={str(p.relative_to(ROOT)).replace('\\','/'):hashlib.sha256(p.read_bytes()).hexdigest() for p in sources}:raise RuntimeError('Source changed during renderer checks')
    report.update(source_sha256=hashes,generated_utc=datetime.datetime.now(datetime.timezone.utc).isoformat())
    (out/'result.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8');print(out/'result.json')
if __name__=='__main__':main()
