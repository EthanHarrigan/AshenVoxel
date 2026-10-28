from pathlib import Path
import datetime,hashlib,json,shutil,subprocess
HERE=Path(__file__).resolve().parent;DEV=HERE.parents[1];ADAPTER=HERE.parent/'root-contact-terrain-adapter'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
known=json.loads((ADAPTER/'report.json').read_text())['source_sha256']
for path,digest in known.items():assert sha(Path(path))==digest,path
assert sha(ADAPTER/'RootTerrainSnapshot.cs')=='d132d7b36405e94624db437f161a85d47a3e5f409f423ebee9b1c1a4d9336462'
out=HERE/'runs'/datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%S%fZ');out.mkdir(parents=True)
dotnet=Path(shutil.which('dotnet'));version=lambda p:tuple(map(int,p.name.split('.')));sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=version)[-1];refs=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=version)[-1]/'ref/net8.0'
# Compile the exact recorded sources afresh; do not trust an unrecorded old binary.
sources=[Path(s) for s in known if s.endswith('.cs') and Path(s).name!='Checks.cs']+[HERE/'Repro.cs']
identities={str(p):sha(p) for p in sources+[HERE/'run.py']}
dll=out/'Repro.dll';q=lambda p:'"'+str(p)+'"';rsp=out/'compile.rsp';rsp.write_text('\n'.join(['-nologo','-warnaserror+','-langversion:9.0','-target:exe','-out:'+q(dll)]+['-r:'+q(p) for p in refs.glob('*.dll')]+[q(p) for p in sources]))
c=subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],capture_output=True,text=True);(out/'compile.log').write_text(c.stdout+c.stderr);assert c.returncode==0,c.stdout+c.stderr
dll.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}))
r=subprocess.run([str(dotnet),str(dll),str(out/'result.json')],capture_output=True,text=True);(out/'run.log').write_text(r.stdout+r.stderr)
assert identities=={str(p):sha(Path(p)) for p in identities},'Source changed'
(out/'provenance.json').write_text(json.dumps({'source_sha256':identities,'adapter_original_report_sha256':sha(ADAPTER/'report.json'),'returncode':r.returncode,'scope':'Read-only frozen-source audit, new separate CPU repro artifacts only'},indent=2));print(r.stdout+r.stderr);print(out);raise SystemExit(r.returncode)
