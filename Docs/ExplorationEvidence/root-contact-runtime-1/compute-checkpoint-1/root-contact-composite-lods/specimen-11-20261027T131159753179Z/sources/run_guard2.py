from pathlib import Path
import ctypes,datetime,hashlib,json,os,shutil,subprocess,sys,time

HERE=Path(__file__).resolve().parent;ROOT=HERE.parents[1]
id=int(sys.argv[1]);assert id in (10,11)
timestamp=datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%S%fZ')
OUT=HERE/f'specimen-{id}-{timestamp}';OUT.mkdir()
def sha(p):
    h=hashlib.sha256()
    with p.open('rb') as f:
        for b in iter(lambda:f.read(1024*1024),b''):h.update(b)
    return h.hexdigest()
pack=ROOT.parent/'AshenVoxel/Assets/AshenVoxel/Exploration/Prototypes/ExplorationPrototypes.bytes'
assert sha(pack)=='41556b474ce7a26342e4dd7bcff2e4fe658ab0333e8360058262ee21c0f011aa'
fixture=ROOT/f'.terrain-cache/root-contact-runtime-prototype/v2-expected-{id}.bin'
bake=ROOT/'Docs/exploration-prototype-bake.json';report=json.loads(bake.read_text(encoding='utf8'));known={k.replace('\\','/'):v for k,v in report['bakes'][0]['source_sha256'].items()}
core=sorted((ROOT/'Assets/AshenVoxel/Core').rglob('*.cs'))
actual=[ROOT/'Tools/ExplorationPrototypes/ExportPrototypes.cs',ROOT/'Assets/AshenVoxel/Rendering/GreedyChunkMesher.cs',ROOT/'Tools/ExplorationPrototypes/UnityShim.cs']
for p in core+actual:assert sha(p)==known[str(p.relative_to(ROOT)).replace('\\','/')],str(p)+' historical bake mismatch'
masked=ROOT/'.terrain-cache/root-contact-mesh-prototype/RootBaseChunkMesher.cs'
sources=core+actual+[ROOT/'Assets/AshenVoxel/WorldScaleData/ExplorationPrototypePack.cs',masked,HERE/'CompositeLowerLod.cs',HERE/'CompositeChecks.cs']
inputs=sources+[pack,fixture,bake,Path(__file__),ROOT/'.terrain-cache/root-contact-alllod-checks/dependency-manifest.json']
hashes={str(p):sha(p) for p in inputs}
snapshots=OUT/'sources';snapshots.mkdir()
for p in sources+[Path(__file__)]:
    dest=snapshots/p.name;assert not dest.exists();shutil.copyfile(p,dest);assert sha(dest)==hashes[str(p)]
(OUT/'preimages.json').write_text(json.dumps(hashes,indent=2),encoding='utf8')
dotnet=Path(shutil.which('dotnet'));version=lambda p:tuple(map(int,p.name.split('.')));sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=version)[-1];refs=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=version)[-1]/'ref/net8.0'
assembly=OUT/'CompositeChecks.dll';q=lambda p:'"'+str(p)+'"';options=['-nologo','-warnaserror+','-langversion:9.0','-optimize+','-target:exe','-main:CompositeChecks','-out:'+q(assembly)]+['-r:'+q(p) for p in refs.glob('*.dll')]+[q(p) for p in sources]
rsp=OUT/'compile.rsp';rsp.write_text('\n'.join(options),encoding='utf8');compiled=subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],capture_output=True,text=True,encoding='utf8',timeout=30);(OUT/'compile.log').write_text(compiled.stdout+compiled.stderr,encoding='utf8');print(compiled.stdout+compiled.stderr);compiled.check_returncode()
assembly.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}),encoding='utf8')
if '--compile-only' in sys.argv:print('COMPILED '+str(OUT));sys.exit(0)
class Performance(ctypes.Structure):
    _fields_=[('cb',ctypes.c_ulong)]+[(n,ctypes.c_size_t) for n in ['CommitTotal','CommitLimit','CommitPeak','PhysicalTotal','PhysicalAvailable','SystemCache','KernelTotal','KernelPaged','KernelNonpaged','PageSize']]+[(n,ctypes.c_ulong) for n in ['HandleCount','ProcessCount','ThreadCount']]
class Memory(ctypes.Structure):
    _fields_=[('cb',ctypes.c_ulong),('PageFaultCount',ctypes.c_ulong)]+[(n,ctypes.c_size_t) for n in ['PeakWorkingSetSize','WorkingSetSize','QuotaPeakPagedPoolUsage','QuotaPagedPoolUsage','QuotaPeakNonPagedPoolUsage','QuotaNonPagedPoolUsage','PagefileUsage','PeakPagefileUsage','PrivateUsage']]
psapi=ctypes.WinDLL('psapi',use_last_error=True);kernel=ctypes.WinDLL('kernel32',use_last_error=True)
psapi.GetPerformanceInfo.argtypes=[ctypes.POINTER(Performance),ctypes.c_ulong];psapi.GetPerformanceInfo.restype=ctypes.c_int
psapi.GetProcessMemoryInfo.argtypes=[ctypes.c_void_p,ctypes.POINTER(Memory),ctypes.c_ulong];psapi.GetProcessMemoryInfo.restype=ctypes.c_int
kernel.OpenProcess.argtypes=[ctypes.c_ulong,ctypes.c_int,ctypes.c_ulong];kernel.OpenProcess.restype=ctypes.c_void_p;kernel.CloseHandle.argtypes=[ctypes.c_void_p]
def headroom():
    p=Performance();p.cb=ctypes.sizeof(p);assert psapi.GetPerformanceInfo(ctypes.byref(p),p.cb);return (p.CommitLimit-p.CommitTotal)*p.PageSize
preflight=headroom();assert preflight>=3*2**30,'Below3GiB capture launch guard; no process started'
print('LAUNCH '+str(OUT)+' preflight GiB='+str(round(preflight/2**30,3)),flush=True)
started=time.monotonic();samples=[];reason=None
environment=os.environ.copy();environment['DOTNET_GCHeapHardLimit']='14000000' #320MiB managed hard limit, local process only
with (OUT/'run.log').open('w',encoding='utf8') as log:
    process=subprocess.Popen([str(dotnet),str(assembly),str(OUT),str(fixture),str(pack),str(id)],stdout=log,stderr=subprocess.STDOUT,env=environment)
    handle=kernel.OpenProcess(0x0400|0x0010,False,process.pid);assert handle
    try:
        while process.poll() is None:
            m=Memory();m.cb=ctypes.sizeof(m);ok=psapi.GetProcessMemoryInfo(handle,ctypes.byref(m),m.cb);available=headroom();elapsed=time.monotonic()-started
            if ok:samples.append({'seconds':elapsed,'private_bytes':m.PrivateUsage,'commit_headroom_bytes':available})
            if not ok:reason='process memory observation failed'
            elif m.PrivateUsage>512*2**20:reason='sampled private memory exceeded512MiB'
            elif available<2*2**30:reason='system commit headroom below2GiB'
            elif elapsed>120:reason='120second process timeout'
            if reason:process.terminate();break
            time.sleep(.25)
        code=process.wait(timeout=10)
    finally:kernel.CloseHandle(handle)
resources={'status':'PASS' if code==0 and not reason else 'FAIL','exit_code':code,'stop_reason':reason,'preflight_commit_headroom_bytes':preflight,'elapsed_seconds':time.monotonic()-started,'sample_interval_seconds':.25,'max_observed_private_bytes':max((s['private_bytes'] for s in samples),default=0),'min_observed_commit_headroom_bytes':min((s['commit_headroom_bytes'] for s in samples),default=preflight),'samples':samples,'scope':'Owned one-specimen CPU child only. Sampled memory can miss/overshoot peaks. No automatic retry.'}
(OUT/'resources.json').write_text(json.dumps(resources,indent=2),encoding='utf8')
after={str(p):sha(p) for p in inputs};unchanged=after==hashes
(OUT/'postimages.json').write_text(json.dumps({'unchanged':unchanged,'sha256':after},indent=2),encoding='utf8')
print(json.dumps({k:v for k,v in resources.items() if k!='samples'},indent=2));print((OUT/'run.log').read_text(encoding='utf8')[-6000:]);assert unchanged,'Input changed; stop and reconcile'
if code!=0 or reason:sys.exit(1)
(OUT/'manifest.json').write_text(json.dumps({'status':'PASS','inputs_unchanged':unchanged,'sha256':{str(p.relative_to(OUT)):sha(p) for p in OUT.rglob('*') if p.is_file()},'scope':'Isolated one-specimen CPU candidate; not runtime/native adoption.'},indent=2),encoding='utf8')
