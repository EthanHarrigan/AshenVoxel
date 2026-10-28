from pathlib import Path
import hashlib,json,subprocess,shutil,datetime
HERE=Path(__file__).resolve().parent
DEV=HERE.parents[1];MAIN=DEV.parent/'AshenVoxel'
FROZEN=DEV/'.terrain-cache/root-contact-runtime-prototype/RootContact.cs'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
assert sha(FROZEN)=='0154d84bb0f3ff2f7638ffe55c389f813237a4d438545a3f7a4d53902e314b8a'
sources=sorted((MAIN/'Assets/AshenVoxel/Core').rglob('*.cs'))+sorted((MAIN/'Assets/AshenVoxel/WorldScaleData').glob('*.cs'))+[FROZEN,HERE/'RootTerrainSnapshot.cs']
inputs=sources+[HERE/'Checks.cs',Path(__file__)]
hashes={str(p):sha(p) for p in inputs}
dotnet=Path(shutil.which('dotnet'));version=lambda p:tuple(int(n) for n in p.name.split('.'))
sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=version)[-1]
refs=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=version)[-1]/'ref/net8.0'
q=lambda p:'"'+str(p)+'"'
def compile(output,references,files,kind):
    options=['-nologo','-warnaserror+','-langversion:9.0','-optimize+','-target:'+kind,'-out:'+q(output)]+['-r:'+q(p) for p in references.glob('*.dll')]+[q(p) for p in files]
    rsp=output.with_suffix('.rsp');rsp.write_text('\n'.join(options),encoding='utf-8')
    subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],check=True)
assembly=HERE/'TerrainChecks.dll'
compile(assembly,refs,sources+[HERE/'Checks.cs'],'exe')
compile(HERE/'Terrain.netstandard21.dll',dotnet.parent/'packs/NETStandard.Library.Ref/2.1.0/ref/netstandard2.1',sources,'library')
assembly.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}))
subprocess.run([str(dotnet),str(assembly),str(HERE)],check=True)
assert hashes=={str(p):sha(p) for p in inputs},'Inputs changed during validation'
report=json.loads((HERE/'checks.json').read_text())
report.update(utc=datetime.datetime.now(datetime.timezone.utc).isoformat(),source_sha256=hashes,netstandard21_csharp9_compile=True,
 scope='Small synthetic CPU checks against actual CURRENT MAIN source. No assets/world/pack loading, native/Unity work, production changes or root regeneration.',
 limits=dict(max_identity_characters=4096,max_surface_wrapper_depth=8,max_rectangle_side_cells=1024,max_rectangle_area_cells=262144,max_query_attempts=1048576,max_abs_global_cell=10000001000),
 contract=['Only a snapshot-provider bare field is accepted; SurfaceWorldField wrappers are removed before capture. TerrainDetailField is preserved and snapshots its own basis. Unknown provider internals must honor immutable snapshot and unique revision contracts.',
 'Identity reads and snapshot capture are trusted bounded callbacks, not sandboxed operations. Providers that allocate huge strings before returning cannot be bounded by this adapter; overlength returned identities are rejected before hashing or retention.',
 'Every successful request uses exactly one snapshot.Sample at WorldCell.Center and existing VoxelTerrainPage.SurfaceTop. Same coordinates and quantization as SurfaceMaterialSampler.GroundHeightMeters; no terrain downresolution or material/snow sampling.',
 'Live and snapshot identity checks bracket every sample; any error or stale state is sticky and returns false with output0. IsCurrent must also run after CPU construction; cache Current must additionally cover template/context/owner/edit freshness.',
 'Single owner, no concurrent mutation or identity reuse. Snapshot consistency does not prove currentness; a source change after the last check still requires the coordinator final transaction gate.',
 'Rectangle limits total allowed coordinates; query budget separately bounds repeated queries. No height memoization or resident world allocation; caller supplies outer per-Step candidate budget.',
 'No surface-material 32m external halo is needed for this adapter. Any internal footprint/dependency of the preserved bare field still belongs to its provider revision contract.'],
 limitations=['No real-world provider performance, Unity profile/native compile, threaded race guarantee or production integration claim.','Exactly11 full-column backing queries here use explicit climate override; other climate providers may add work. Actual source declares up to15 height queries. Counts are not timing guarantees.','Supports standard SurfaceWorldField(TerrainDetailField(bare)) layering. Does not introspect arbitrary opaque wrapper internals or prove a provider is immutable.'])
(HERE/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps({k:v for k,v in report.items() if k!='source_sha256'},indent=2))
