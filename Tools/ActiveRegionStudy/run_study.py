"""Offline source-matched scheduling experiment. Does not modify Assets or control Unity."""
from pathlib import Path
import argparse, datetime, hashlib, json, os, platform, shutil, subprocess

ROOT=Path(__file__).resolve().parents[2]
HERE=Path(__file__).resolve().parent
def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()
def version(path): return tuple(int(p) if p.isdigit() else 0 for p in path.name.split('.'))

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output',type=Path,required=True,help='New evidence directory; never overwrite a previous run')
    args=parser.parse_args()
    evidence=args.output.resolve()
    evidence.mkdir(parents=True,exist_ok=False)
    build=HERE/'bin'/evidence.name
    build.mkdir(parents=True,exist_ok=True)
    dotnet=Path(shutil.which('dotnet') or ROOT.parent/'.dotnet/dotnet')
    sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=version)[-1]
    refs=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=version)[-1]/'ref/net8.0'
    originals=sorted((ROOT/'Assets/AshenVoxel/Core').rglob('*.cs'))+[
        ROOT/'Assets/AshenVoxel/Exploration/NaturalTreePhysics.cs',
        ROOT/'Assets/_Validation/FiniteMatterSlice/Runtime/LocalizedMatter.cs',
        HERE/'PagedCellSet.cs',HERE/'DormantRegionIndex.cs',HERE/'Program.cs']
    hashes={p.relative_to(ROOT).as_posix():sha(p) for p in originals+[Path(__file__).resolve()]}
    copied=[]
    # Compile frozen copies so a concurrent edit cannot produce a mixed compilation.
    for source in originals:
        target=build/'frozen'/source.relative_to(ROOT)
        target.parent.mkdir(parents=True,exist_ok=True)
        target.write_bytes(source.read_bytes())
        assert sha(target)==hashes[source.relative_to(ROOT).as_posix()]
        copied.append(target)
    simulation=build/'frozen/Assets/AshenVoxel/Core/Simulation.cs'
    candidate=simulation.read_text(encoding='utf-8').replace('sealed class Simulation','sealed class PagedSimulation').replace('public Simulation(','public PagedSimulation(')
    assert candidate.count('readonly HashSet<Int3>')==2
    candidate=candidate.replace('readonly HashSet<Int3> queued=new HashSet<Int3>();','readonly PagedCellSet queued=new PagedCellSet();')
    candidate=candidate.replace('readonly HashSet<Int3> movedThisStep=new HashSet<Int3>();','readonly PagedCellSet movedThisStep=new PagedCellSet();')
    candidate=candidate.replace('public int ActiveCount => frontier.Count;','public int ActiveCount => frontier.Count;\n        public int RetainedMembershipPages => queued.PageCount+movedThisStep.PageCount;')
    transformed=build/'PagedSimulation.cs';transformed.write_text(candidate,encoding='utf-8');copied.append(transformed)
    cpu=subprocess.run(['powershell','-NoProfile','-Command','(Get-CimInstance Win32_Processor | Select-Object -First 1).Name'],text=True,capture_output=True).stdout.strip() if platform.system()=='Windows' else platform.processor()
    manifest={'captured_utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'source_sha256':hashes,
              'generated_candidate_sha256':sha(transformed),'candidate_transform':'Rename Simulation; replace two membership HashSets with PagedCellSet; expose page count. FIFO and all movement/wake rules unchanged.',
              'environment':{'os':platform.platform(),'cpu':cpu,'logical_cpu_count':os.cpu_count(),'sdk':sdk.name,'optimized':True,'tiered_compilation':False,'note':'Windows standalone .NET CPU experiment; other host workload uncontrolled. No Unity, GPU, renderer or native physics measurements.'}}
    (evidence/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
    quote=lambda p:'"'+str(p)+'"'
    dll=build/'Study.dll';rsp=build/'compile.rsp'
    rsp.write_text('\n'.join(['-nologo','-warnaserror+','-langversion:9.0','-optimize+','-target:exe','-out:'+quote(dll)]+['-r:'+quote(p) for p in sorted(refs.glob('*.dll'))]+[quote(p) for p in copied]),encoding='utf-8')
    dll.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}))
    compile_run=subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],capture_output=True,text=True)
    (evidence/'compile.log').write_text(compile_run.stdout+compile_run.stderr,encoding='utf-8')
    print(compile_run.stdout+compile_run.stderr,end='');compile_run.check_returncode()
    env=os.environ.copy();env['DOTNET_TieredCompilation']='0'
    run=subprocess.run([str(dotnet),str(dll),str(evidence/'results.json')],env=env,capture_output=True,text=True)
    (evidence/'run.log').write_text(run.stdout+run.stderr,encoding='utf-8');print(run.stdout+run.stderr,end='')
    manifest['source_changes_during_run']={p:sha(ROOT/p) for p,h in hashes.items() if sha(ROOT/p)!=h}
    manifest['exit_code']=run.returncode
    (evidence/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
    run.check_returncode()
    results=json.loads((evidence/'results.json').read_text())
    for fixture in ['sparse','dense','dispersed']:
        rows=[row for row in results['records'] if row.get('fixture')==fixture]
        assert len({(row['final_snapshot_sha256'],row['processed'],row['moves'],row['calls']) for row in rows})==1, 'Final state/work mismatch: '+fixture
    (evidence/'equivalence.json').write_text(json.dumps({'all_three_fixtures_all_ten_runs':'identical final serialized SHA-256, candidates, moves and call count'},indent=2)+'\n',encoding='utf-8')
    print('Evidence:',evidence)

if __name__=='__main__': main()
