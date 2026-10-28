from pathlib import Path
import datetime,hashlib,json
HERE=Path(__file__).resolve().parent
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
run=HERE/'runs/20261027T134408961318Z';m=json.loads((run/'manifest.json').read_text(encoding='utf8'))
for p,value in m['source_and_input_sha256'].items():assert sha(Path(p))==value,'Input changed '+p
for p,value in m['sha256'].items():assert sha(run/p)==value,'Evidence changed '+p
assert sha(HERE/'ManagedComposite.cs')=='32d7948cb2232263d809a5c3d266b6ae264255e525009bb72450c4d358c514b5'
names=['ManagedComposite.cs','Checks.cs','run.py','README.md','freeze.py']
out=HERE/'checkpoint.json';assert not out.exists()
out.write_text(json.dumps({'utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'status':'FROZEN pure managed compute adapter; no runtime/native adoption','source_sha256':sha(HERE/'ManagedComposite.cs'),'report':str(run/'report.json'),'report_sha256':sha(run/'report.json'),'manifest_sha256':sha(run/'manifest.json'),'preserved_compile_failure':'runs/20261027T134352164276Z','sha256':{n:sha(HERE/n) for n in names},'scope':'All10 exact lower meshes;164394 assertions; immutable outputs, sequential private coarse world, explicit declared array accounting.'},indent=2),encoding='utf8')
print('FROZEN checkpoint SHA256 '+sha(out))
