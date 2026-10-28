"""Python wire encoder -> actual C# overview decoder and injected HTTP transport."""
from pathlib import Path
import datetime,hashlib,json,shutil,struct,subprocess,sys,zlib
import numpy as np
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'Tools/ExplorationBake'))
import overview_provider as provider
stamp=datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%d-%H%M%S-%f');out=ROOT/'.terrain-cache/overview-runtime'/stamp;fixtures=out/'fixtures';fixtures.mkdir(parents=True)
native='a'*64;source='b'*64
def fixture(name,spacing=160,changes=None,values=None):
    window={'spacing_m':spacing,'row0':(4*2560-320)//spacing,'col0':(-3*2560-320)//spacing,'rows':3200//spacing+1,'cols':3200//spacing+1}
    if values is None:values=(100+np.arange(window['rows']*window['cols'],dtype=np.float32).reshape(window['rows'],window['cols'])*.125).astype('<f4')
    meta={'schema':provider.SCHEMA,'source_id':source,'native_model_world_id':native,'window':window,**window,'origin_x_m':window['col0']*spacing,'origin_z_m':window['row0']*spacing,'height_units':'meters','height_sha256':hashlib.sha256(values.astype('<f4').tobytes()).hexdigest()}
    if changes:meta.update(changes)
    raw=provider.encode(meta,values);(fixtures/(name+'.bin')).write_bytes(raw);return raw,meta,values
raw,meta,values=fixture('valid160');fixture('valid80',80)
for name,changes in {'wrong-source':{'source_id':'d'*64},'wrong-native':{'native_model_world_id':'d'*64},'wrong-origin':{'origin_x_m':-7999.5},'wrong-schema':{'schema':'unrelated-v2'},'wrong-units':{'height_units':'feet'},'wrong-dimensions':{'rows':20},'wrong-window':{'row0':63}}.items():fixture(name,changes=changes)
for name,value in [('nan',float('nan')),('infinity',float('inf')),('too-high',48001.)]:
    invalid=values.copy();invalid[0,0]=value;fixture(name,values=invalid)
changed=values.copy();changed[0,0]+=1;fixture('wrong-sha',changes={'height_sha256':meta['height_sha256']},values=changed)
bad=bytearray(raw);bad[-8]^=1;(fixtures/'corrupt-crc.bin').write_bytes(bad)
(fixtures/'truncated.bin').write_bytes(raw[:-1]);short=raw[:-8];(fixtures/'truncated-valid-crc.bin').write_bytes(short+struct.pack('<I',zlib.crc32(short)&0xffffffff))
bad=bytearray(raw);bad[0]=ord('X');bad[-4:]=struct.pack('<I',zlib.crc32(bad[:-4])&0xffffffff);(fixtures/'wrong-magic.bin').write_bytes(bad)
bad=bytearray(raw);bad[8:12]=struct.pack('<I',20000);bad[-4:]=struct.pack('<I',zlib.crc32(bad[:-4])&0xffffffff);(fixtures/'bad-metadata-length.bin').write_bytes(bad)
provider.decode(raw) # Independent Python decoder accepts the golden source fixture.
dotnet=Path(shutil.which('dotnet'));version=lambda p:tuple(map(int,p.name.split('.')))
sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=version)[-1];refs=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=version)[-1]/'ref/net8.0'
sources=sorted((ROOT/'Assets/AshenVoxel/Core').rglob('*.cs'))+sorted((ROOT/'Assets/AshenVoxel/WorldScaleData').glob('*.cs'))+[ROOT/'Assets/AshenVoxel/Exploration/ExplorationOverviewRefinement.cs',Path(__file__).with_name('UnityShim.cs'),Path(__file__).with_name('Program.cs')]
hashes={str(p.relative_to(ROOT)).replace('\\','/'):hashlib.sha256(p.read_bytes()).hexdigest() for p in sources};q=lambda p:'"'+str(p)+'"'
assembly=out/'OverviewRuntimeChecks.dll';rsp=out/'compile.rsp';rsp.write_text('\n'.join(['-nologo','-warnaserror+','-langversion:9.0','-optimize+','-target:exe','-out:'+q(assembly)]+['-r:'+q(p) for p in sorted(refs.glob('*.dll'))]+[q(p) for p in sources]),encoding='utf-8')
result=subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],capture_output=True,text=True);(out/'compile.log').write_text(result.stdout+result.stderr,encoding='utf-8');print(result.stdout+result.stderr,end='');result.check_returncode()
assembly.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}))
result=subprocess.run([str(dotnet),str(assembly),str(fixtures),str(out/'result.json')],capture_output=True,text=True);(out/'console.log').write_text(result.stdout+result.stderr,encoding='utf-8');print(result.stdout+result.stderr,end='');result.check_returncode()
if hashes!={str(p.relative_to(ROOT)).replace('\\','/'):hashlib.sha256(p.read_bytes()).hexdigest() for p in sources}:raise RuntimeError('Production source changed during overview runtime checks; rerun source-matched validation')
report=json.loads((out/'result.json').read_text());report.update(source_sha256=hashes,fixture_sha256={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in fixtures.glob('*.bin')},python_encoder_sha256=hashlib.sha256((ROOT/'Tools/ExplorationBake/overview_provider.py').read_bytes()).hexdigest(),generated_utc=datetime.datetime.now(datetime.timezone.utc).isoformat());(out/'result.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8');print(out/'result.json')
