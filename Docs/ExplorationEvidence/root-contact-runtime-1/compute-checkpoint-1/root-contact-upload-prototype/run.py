from pathlib import Path
import hashlib,json,subprocess,shutil,datetime
HERE=Path(__file__).resolve().parent;DEV=HERE.parents[1];MAIN=DEV.parent/'AshenVoxel'
SPLIT=DEV/'.terrain-cache/root-contact-mesh-prototype/SharedUpperSplit.cs';ARRAY=DEV/'.terrain-cache/root-contact-array-mesher'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
frozen=json.loads((ARRAY/'runs/20261027T132524372509Z/report.json').read_text())['source_input_sha256']
for p in [ARRAY/'MeshData.cs',ARRAY/'ArrayRootMesher.cs']:assert sha(p)==frozen[str(p)]
native_frozen=DEV/'.terrain-cache/root-contact-mesh-prototype/native-install-4/frozen/SharedUpperSplit.cs'
assert sha(SPLIT)==sha(native_frozen)
core=sorted((MAIN/'Assets/AshenVoxel/Core').rglob('*.cs'));world=sorted((MAIN/'Assets/AshenVoxel/WorldScaleData').glob('*.cs'))
candidate=[HERE/'UploadPreparation.cs',HERE/'UnityMeshUpload.cs'];inputs=core+world+[SPLIT,ARRAY/'MeshData.cs',ARRAY/'ArrayRootMesher.cs',HERE/'Checks.cs',Path(__file__),MAIN/'Assets/AshenVoxel/Exploration/VegetationStreaming.cs']+candidate
hashes={str(p):sha(p) for p in inputs}
dotnet=Path(shutil.which('dotnet'));version=lambda p:tuple(map(int,p.name.split('.')));sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=version)[-1]
std=list((dotnet.parent/'packs/NETStandard.Library.Ref/2.1.0/ref/netstandard2.1').glob('*.dll'));net=list((sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=version)[-1]/'ref/net8.0').glob('*.dll'))
def compile(name,sources,refs,kind):
 out=HERE/(name+'.dll');q=lambda p:'"'+str(p)+'"';rsp=out.with_suffix('.rsp')
 rsp.write_text('\n'.join(['-nologo','-warnaserror+','-langversion:9.0','-optimize+','-target:'+kind,'-out:'+q(out)]+['-r:'+q(p) for p in refs]+[q(p) for p in sources]))
 subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],check=True);return out
c=compile('Core',core,std,'library');w=compile('WorldScale',world,std+[c],'library')
a=compile('ArrayMesher',[ARRAY/'MeshData.cs',ARRAY/'ArrayRootMesher.cs'],std+[c],'library')
p=compile('Preparation',[SPLIT,HERE/'UploadPreparation.cs'],std+[c,w,a],'library')
unity=Path('C:/Program Files/Unity/Hub/Editor/6000.3.27f1/Editor/Data/Managed/UnityEngine')
u=compile('UnityUpload',[HERE/'UnityMeshUpload.cs'],std+[c,w,a,p,unity/'UnityEngine.CoreModule.dll'],'library')
t=compile('Checks',[HERE/'Checks.cs'],net+[c,w,a,p],'exe')
t.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}))
subprocess.run([str(dotnet),str(t),str(HERE)],check=True)
assert hashes=={str(p):sha(p) for p in inputs},'Inputs changed during validation'
report=json.loads((HERE/'checks.json').read_text());report.update(utc=datetime.datetime.now(datetime.timezone.utc).isoformat(),source_sha256=hashes,unity_reference_sha256={str(unity/'UnityEngine.CoreModule.dll'):sha(unity/'UnityEngine.CoreModule.dll')},csharp9_netstandard21=True,separate_assembly_api_compile=True,installed_unity_api_compile='6000.3.27f1 CoreModule; no Unity run or Mesh creation executed',limitations=['Canonical preparation is whole-prototype synchronous, not an incremental Step; counts/byte reservations do not prove frame time.','Prepared element payload and new Unity conversion arrays coexist. Source, splitter scratch and CLR/native overhead must be accounted separately.','This only creates complete unpublished render resources; cache admission, GPU lifetime, batches, source/owner epochs, collision switch and readiness remain external.','Default production treeLOD3/4 is billboard; these mesh paths must be explicitly skipped there. No atlas/wind/shader/native validation here.'])
(HERE/'report.json').write_text(json.dumps(report,indent=2));print(json.dumps({k:v for k,v in report.items() if k not in ('source_sha256','unity_reference_sha256')},indent=2))
