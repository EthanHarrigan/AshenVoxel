from pathlib import Path
import hashlib,json,subprocess,shutil,datetime
HERE=Path(__file__).resolve().parent;ROOT=HERE.parents[1]
GEOMETRY=ROOT/'.terrain-cache/root-contact-runtime-prototype';INTERACTION=ROOT/'.terrain-cache/root-contact-interaction-prototype';VISUAL=ROOT/'.terrain-cache/grounding-root-visual'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
frozen={GEOMETRY/'RootContact.cs':'0154d84bb0f3ff2f7638ffe55c389f813237a4d438545a3f7a4d53902e314b8a',INTERACTION/'Interaction.cs':'8133a5181e77df83a94f66b6b4ae668c51e408ffce78df406a59bd01aa066f8e'}
for path,digest in frozen.items():assert sha(path)==digest,'Frozen source changed'
sources=sorted((ROOT/'Assets/AshenVoxel/Core').rglob('*.cs'))+list(frozen)+[HERE/'Controller.cs',HERE/'FrozenJob.cs',HERE/'Checks.cs']
inputs=sources+[Path(__file__),GEOMETRY/'report.json',INTERACTION/'report.json',VISUAL/'sites.json']
for id in [10,11]:inputs.extend([GEOMETRY/f'v2-template-{id}.bin',VISUAL/f'terrain-{id}.bin',VISUAL/f'wood-{id}-baseline.bin',INTERACTION/f'actual-{id}-oracle.bin'])
for report in [GEOMETRY/'report.json',INTERACTION/'report.json']:
    known=json.loads(report.read_text())['source_sha256']
    for path in inputs:
        if str(path) in known:assert sha(path)==known[str(path)],'Frozen fixture mismatch'
hashes={str(p):sha(p) for p in inputs}
dotnet=Path(shutil.which('dotnet'));version=lambda p:tuple(int(n) for n in p.name.split('.'))
sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=version)[-1];refs=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=version)[-1]/'ref/net8.0'
q=lambda p:'"'+str(p)+'"';assembly=HERE/'CacheChecks.dll'
def compile(output,references,files,kind):
    options=['-nologo','-warnaserror+','-langversion:9.0','-optimize+','-target:'+kind,'-out:'+q(output)]+['-r:'+q(p) for p in references.glob('*.dll')]+[q(p) for p in files]
    rsp=output.with_suffix('.rsp');rsp.write_text('\n'.join(options),encoding='utf-8')
    subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],check=True)
compile(assembly,refs,sources,'exe')
compile(HERE/'Cache.netstandard21.dll',dotnet.parent/'packs/NETStandard.Library.Ref/2.1.0/ref/netstandard2.1',sources[:-1],'library')
assembly.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}))
subprocess.run([str(dotnet),str(assembly),str(HERE),str(GEOMETRY),str(INTERACTION),str(VISUAL)],check=True)
assert hashes=={str(p):sha(p) for p in inputs},'Inputs changed during validation'
report=json.loads((HERE/'checks.json').read_text());report.update(utc=datetime.datetime.now(datetime.timezone.utc).isoformat(),source_sha256=hashes,netstandard21_csharp9_compile=True,
    scope='Isolated single-owner cooperative CPU admission/cache prototype around unchanged frozen root+interaction builders. No threads/native/Assets integration.',
    defaults=dict(max_pending_including_active=4,max_ready_entries_including_retired_pinned=2,max_leases=4,max_ready_accounted_bytes=2*1024*1024,max_worker_declared_reservation_bytes=24*1024*1024,max_resident_accounted_bytes=26*1024*1024,workers=1),
    configuration_ceiling=dict(pending=64,ready_entries=64,leases=128,ready_bytes=256*1024*1024,worker_bytes=256*1024*1024,resident_bytes=512*1024*1024),
    accounting=['Owned artifact exact array element payload is17bytes per stored cell: Int3+uint tint+byte role. Row30 context is retained with None role; owned rendering/collision remains below30.','Accounted artifact bytes add512bytes plus twice identity-string character counts as an explicit envelope allowance; this is not exact CLR heap measurement.','A single active job reserves24MiB throughout root construction, interaction classification and incremental packaging, including waiting publication. Shared immutable templates/context and terrain-provider storage are caller-owned and excluded.','The resident ledger includes pinned/retired entries plus the worker reservation. Artifact publication transfers accounting from worker to cache.','Work units count supplied job candidates plus a unit for bounded controller start/publication/failure handling. Those metadata units may scan/evict up to configured64 ready entries; they are not millisecond guarantees.','Third-party IJob producers are trusted cooperative callbacks: reservations and Step work reports are checked but cannot sandbox arbitrary allocations, exceptions or CPU time. The included adapter uses the independently bounded frozen builders.'],
    production_wiring=['Create immutable shared template/context after pack load; snapshot terrain revision and original pose into Identity.For.','Keep this controller on one coordinator-owned execution lane. Request returns QueueFull explicitly; retain the placement request outside and retry when capacity changes. Do not silently discard it.','Pump bounded candidate budgets from the chosen worker/coordinator. Cancel tickets and invalidate source/context revisions on travel, regeneration or unload. Frozen builders independently reject changed terrain identity.','Acquire a lease only after publication. Build/upload render meshes and collision/resistance data from the same complete cell+role payload, with row30 context excluded from ownership. That downstream mesh/collider transaction is not implemented here.','Release leases after consumers finish. A completed worker waiting for pinned capacity remains pending; caller must release/cancel/backpressure deliberately.','Provider identities must never be reused after mutation. Recheck before downstream upload/publication; a cached CPU snapshot does not certify current GPU or gameplay state.'],
    limitations=['No asynchronous worker, Unity scheduler, native meshes, colliders, GPU buffers, actual memory-residency measurements or frame-time guarantees.','No disk persistence, cross-session cache, global cache or production generator/version change.','This controller bounds its own records and declared accounting, not caller-retained terminal tickets, provider data, shared templates/context, CLR garbage collection backlog or arbitrary producer allocations.','Identity/current-provider callbacks must be side-effect-free. Pump explicitly rejects producer reentry; trusted callbacks and cooperative byte/work reporting remain API contracts. Lease reads reject stale/current mismatches; lifecycle still requires caller release.','Only the two recorded actual redwood fixtures and focused controller cases were exercised; no live travel/Play validation.'])
(HERE/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps({k:v for k,v in report.items() if k!='source_sha256'},indent=2))
