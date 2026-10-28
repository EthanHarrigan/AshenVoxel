from pathlib import Path
import os,shutil,subprocess,json,hashlib,datetime,sys
root=Path(__file__).resolve().parents[1];baseline='--baseline' in sys.argv
version=lambda p:tuple(int(x) if x.isdigit() else 0 for x in p.name.split('.'))
dotnet=Path(shutil.which('dotnet') or root.parent/'.dotnet/dotnet');sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=version)[-1];refs=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=version)[-1]/'ref/net8.0'
physics=root/('.terrain-cache/cheap-matter-2026-10-27/preimages/NaturalTreePhysics.cs' if baseline else 'Assets/AshenVoxel/Exploration/NaturalTreePhysics.cs')
files=sorted((root/'Assets/AshenVoxel/Core').rglob('*.cs'))+[root/'Assets/AshenVoxel/Exploration/NaturalTreeTemplate.cs',physics,root/'Assets/_Validation/FiniteMatterSlice/Runtime/LocalizedMatter.cs',root/'Tools/CheapMatterChecks/Program.cs']
hashes={str(p.relative_to(root)):hashlib.sha256(p.read_bytes()).hexdigest() for p in files};out=root/'Tools/CheapMatterChecks/bin'/('baseline' if baseline else 'current');out.mkdir(parents=True,exist_ok=True)
q=lambda p:'"'+str(p)+'"';dll=out/'Checks.dll';rsp=out/'compile.rsp';rsp.write_text('\n'.join(['-nologo','-warnaserror+','-langversion:9.0','-optimize+','-target:exe','-out:'+q(dll)]+(['-define:BASELINE'] if baseline else [])+['-r:'+q(p) for p in refs.glob('*.dll')]+[q(p) for p in files]))
dll.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}))
subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],check=True)
evidence=Path(sys.argv[sys.argv.index('--output-dir')+1]) if '--output-dir' in sys.argv else root/'Docs/NaturalMatterEvidence'
evidence.mkdir(parents=True,exist_ok=True);report=evidence/('cheap-physics-baseline.json' if baseline else 'cheap-physics.json')
run=subprocess.run([str(dotnet),str(dll),str(report),'baseline' if baseline else 'awake' if '--awake' in sys.argv else 'perf' if '--perf' in sys.argv else 'current'],text=True,capture_output=True)
log=evidence/('cheap-physics-baseline-final.log' if baseline else 'cheap-physics-adaptive-final.log');log.write_text(run.stdout+run.stderr,encoding='utf-8');print(run.stdout,end='');print(run.stderr,end='');run.check_returncode()
assert hashes=={str(p.relative_to(root)):hashlib.sha256(p.read_bytes()).hexdigest() for p in files},'Source changed; report not source-matched, rerun'
d=json.loads(report.read_text());d.update(source_sha256=hashes,generated_utc=datetime.datetime.now(datetime.timezone.utc).isoformat());report.write_text(json.dumps(d,indent=2)+'\n');log.write_text(run.stdout+run.stderr+'\n'+json.dumps(d,indent=2)+'\n',encoding='utf-8');print(json.dumps(d['records'],indent=2))
