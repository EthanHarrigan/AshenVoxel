from pathlib import Path
import json,hashlib,struct,subprocess,shutil,datetime
HERE=Path(__file__).resolve().parent;DEV=HERE.parents[1];FORMAT=HERE.parent/'root-contact-template-format';RUN=FORMAT/'runs/20261027T133818719190Z'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
prior=json.loads((RUN/'report.json').read_text());library=RUN/'TemplateBundle.dll';assert sha(library)==prior['library_sha256']
manifest=json.loads((FORMAT/'export-manifest.json').read_text());valid=bytearray((FORMAT/'model-10.avrc').read_bytes())
assert hashlib.sha256(valid).hexdigest()==manifest['models'][0]['file_sha256']
def integer(b,at):return struct.unpack_from('<i',b,at)[0]
def seal(b):
 struct.pack_into('<i',b,12,len(b)-48);b[16:48]=hashlib.sha256(b[48:]).digest();return b
(HERE/'valid.bin').write_bytes(valid)
oversized=valid[:16];struct.pack_into('<i',oversized,12,2*1024*1024+1);(HERE/'oversized.bin').write_bytes(oversized)
at=188
for _ in range(integer(valid,184)):
 count=integer(valid,at+4);at+=8+6*count
context_at=at;n=integer(valid,at);at+=4
remove=next(at+10*i for i in range(n) if struct.unpack_from('<h',valid,at+10*i+2)[0]==31)
missing=bytearray(valid);del missing[remove:remove+10];struct.pack_into('<i',missing,context_at,n-1);(HERE/'missing-context31.bin').write_bytes(seal(missing))
at+=10*n;rank_count=integer(valid,at);at+=4+6*rank_count;assert integer(valid,at)==5
fixed_count_at=at+12;assert integer(valid,fixed_count_at)>0
record=fixed_count_at+4;old_rank=integer(valid,record+11)
mismatch=bytearray(valid);struct.pack_into('<i',mismatch,record+11,(old_rank+1)%rank_count);(HERE/'mismatched-fixed-rank.bin').write_bytes(seal(mismatch))
dotnet=Path(shutil.which('dotnet'));version=lambda p:tuple(map(int,p.name.split('.')));sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=version)[-1];refs=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=version)[-1]/'ref/net8.0'
local=HERE/'TemplateBundle.dll';shutil.copyfile(library,local);exe=HERE/'ReviewChecks.dll';q=lambda p:'"'+str(p)+'"'
rsp=HERE/'checks.rsp';rsp.write_text('\n'.join(['-nologo','-warnaserror+','-langversion:9.0','-target:exe','-out:'+q(exe)]+['-r:'+q(p) for p in refs.glob('*.dll')]+['-r:'+q(local),q(HERE/'ReviewChecks.cs')]))
subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],check=True)
exe.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}))
subprocess.run([str(dotnet),str(exe),str(HERE),manifest['pack_sha256'],str(HERE/'adverse-results.json')],check=True)
report=json.loads((HERE/'adverse-results.json').read_text());report.update(utc=datetime.datetime.now(datetime.timezone.utc).isoformat(),reviewed_library_sha256=sha(library),original_source_sha256=prior['source_input_sha256'],review_sources={str(p):sha(p) for p in [HERE/'ReviewChecks.cs',Path(__file__)]})
(HERE/'report.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report['results'],indent=2))
