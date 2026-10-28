#!/usr/bin/env python3
"""Contract tests use a declared fake pipeline; they do not execute neural weights."""
from pathlib import Path
import concurrent.futures,json,sys,tempfile,threading,time,urllib.request,urllib.error
import numpy as np
import bridge as b

CHECKS=0
def check(condition,message):
    global CHECKS
    if not condition:raise AssertionError(message)
    CHECKS+=1;print('PASS '+message,flush=True)
def rejects(action,message,exception=ValueError):
    try:action()
    except exception:check(True,message);return
    raise AssertionError('Expected rejection: '+message)

class FakeBackend:
    kind='FAKE-CONTRACT-TEST-NOT-NEURAL'
    def __init__(self,gate=None,fail=False):self.calls=0;self.gate=gate;self.fail=fail;self.entered=threading.Event()
    def query(self,window):
        self.calls+=1;self.entered.set()
        if self.gate is not None:self.gate.wait(timeout=5)
        if self.fail:raise RuntimeError('deliberate inference failure')
        r,c,h,w=window
        y=np.arange(r,r+h,dtype=np.float32)[:,None];x=np.arange(c,c+w,dtype=np.float32)[None,:]
        height=(y*100+x+.125).astype(np.float32)
        climate=np.stack([-8+height*.0001,8+height*.000001,1100+height*.001,
            35+height*.00001,np.full_like(height,-.0065)])
        return height,climate,np.full((h,w),9,dtype=np.uint8),(height>=0).astype(np.uint8)

class FakeConditioning:
    def __init__(self):self.imports=[];self.rebuilds=0
    def set_custom_conditioning_import(self,*args,**kwargs):self.imports.append((args,kwargs))
    def rebuild(self):self.rebuilds+=1

def req(job,r=0,c=0,h=4,w=5):return {'world_id':job['world_id'],'row0':r,'col0':c,'rows':h,'cols':w}

def main():
    job=b.make_job('/unused-upstream')
    check(job['spec']['climate_encoding']==b.CLIMATE_PHYSICAL,'new immutable jobs declare physical climate units')
    legacy=json.loads(json.dumps(job));del legacy['spec']['climate_encoding']
    legacy['world_id']=b.hashlib.sha256(b.canonical(b.identity_spec(legacy['spec']))).hexdigest()
    check(b.validate_job(legacy)==legacy and legacy['world_id']!=job['world_id'],'legacy jobs remain readable and cannot share normalized owner cache identity')
    native=np.stack([np.full((2,2),v,dtype=np.float32) for v in [-55,1078,190,13,-.0049]])
    physical=b.normalize_upstream_climate(native,b.CLIMATE_PHYSICAL)
    check(np.allclose(physical[1],10.78) and np.array_equal(native[1],np.full((2,2),1078)),'real upstream centi-C seasonality normalizes once without mutating tensor storage')
    check(np.array_equal(physical[0],native[0]) and np.array_equal(physical[4],native[4]),'surface-adjusted temperature and lapse rate are not adjusted a second time')
    check(np.array_equal(b.normalize_upstream_climate(native,b.CLIMATE_NATIVE),native),'legacy execution preserves historical native owner bytes')
    check(np.array_equal(b.physical_climate(physical,b.CLIMATE_PHYSICAL),physical),'physical wire data is not divided twice')
    check(np.allclose(b.physical_climate(native,b.CLIMATE_NATIVE),physical),'legacy archived climate decodes to physical units')
    rejects(lambda:b.physical_climate(native,'future'),'unknown climate encoding rejected')
    channel_data=(np.full((2,2),5800,dtype=np.float32),native,np.full((2,2),11,dtype=np.uint8),np.ones((2,2),dtype=np.uint8))
    legacy_raw=b.encode_region(legacy,(0,0,2,2),channel_data,FakeBackend.kind)
    legacy_meta,*_=b.decode_region(legacy_raw)
    check(legacy_meta['climate_value_units'][1]=='centi_degC','legacy transport declares native seasonality scale')
    n=b.struct.unpack_from('<I',legacy_raw,8)[0]
    del legacy_meta['climate_encoding'];del legacy_meta['climate_value_units']
    header=b.canonical(legacy_meta);older=b.MAGIC+b.struct.pack('<I',len(header))+header+legacy_raw[12+n:-4]
    older+=b.struct.pack('<I',b.zlib.crc32(older)&0xffffffff)
    decoded=b.decode_region(older)
    check(b.climate_encoding(decoded[0])==b.CLIMATE_NATIVE and np.all(decoded[2][1]==1078),'historical metadata absence remains explicitly native without changing stored values')
    rejects(lambda:b.encode_region(job,(0,0,2,2),channel_data,FakeBackend.kind),'centi-C cannot be encoded while declaring physical seasonality')
    check(b.validate_job(job)==job,'prepared manifest has a reproducible immutable identity')
    check(b.make_job('/other-location')['world_id']==job['world_id'],'moving source locator does not relabel the world')
    check(b.make_job('/unused-upstream',seed=3)['world_id']!=job['world_id'],'seed changes world/cache identity')
    check(b.make_job('/unused-upstream',T=1)['world_id']!=job['world_id'],'pipeline blending phases change world identity')
    b.validate_runtime(job);check(True,'prepared package versions match the current execution environment')
    wrong_runtime=json.loads(json.dumps(job));wrong_runtime['spec']['runtime_versions']['torch']='other'
    rejects(lambda:b.validate_runtime(wrong_runtime),'execution version drift requires a new generation identity',RuntimeError)
    rejects(lambda:b.make_job('/unused',max_samples=999999999),'reject unbounded sample requests at plan time')
    rejects(lambda:b.make_job('/unused',snr=[0,1,1,1,1]),'reject zero SNR before upstream logarithm')
    altered=json.loads(json.dumps(job));altered['spec']['seed']=42
    rejects(lambda:b.validate_job(altered),'reject modified manifest identity')
    backend=FakeBackend();provider=b.RegionProvider(job,backend)
    try:
        raw=provider.request(req(job,-3,-2)).result(timeout=5)
        meta,h,climate,biome,land=b.decode_region(raw,job['world_id'])
        check(meta['backend']==FakeBackend.kind,'fake evidence is explicitly marked and cannot imply pretrained inference')
        check(meta['origin_x_m']==-60 and meta['origin_z_m']==-90,'negative native columns map to X and rows map to Z in meters')
        check(abs(float(h[0,0])-(-301.875))<1e-5,'fractional float heights survive without integer-meter compression')
        check(np.all(climate[4]==np.float32(-.0065)) and np.allclose(climate[2],1100+h*.001),'all five physically valid climate channels preserve planar order')
        check(meta['climate_encoding']==b.CLIMATE_PHYSICAL and meta['climate_value_units']==['degC','degC','mm/year','percent','degC/m'],'wire metadata declares physical per-plane units')
        check(np.all(biome==9) and np.array_equal(land,h>=0),'source biome and generated land channels survive')
        check(provider.request(req(job,-3,-2)).result()==raw and backend.calls==1,'repeat region queries use resident cache')
        overlap=b.decode_region(provider.request(req(job,-2,-1)).result())[1]
        check(np.array_equal(h[1:,1:],overlap[:-1,:-1]),'overlapping signed requests share identical field samples')
        corrupted=bytearray(raw);corrupted[-8]^=1
        rejects(lambda:b.decode_region(corrupted),'reject corrupted binary region')
        rejects(lambda:b.decode_region(raw,expected_world_id='other'),'reject stale world response')
        rejects(lambda:provider.request({**req(job),'world_id':'stale'}),'reject request from another generation',b.IdentityMismatch)
        rejects(lambda:provider.request(req(job,h=1024,w=1024)),'enforce region sample budget before allocation')
        rejects(lambda:provider.request(req(job,h=True)),'reject boolean dimensions')
        # Caller-side dict mutation cannot mutate an already-created process provider.
        mutable=json.loads(json.dumps(job));copy_provider=b.RegionProvider(mutable,FakeBackend())
        mutable['spec']['seed']=900
        check(copy_provider.job['spec']['seed']==714103,'provider owns its configuration snapshot')
        copy_provider.close()
        with tempfile.TemporaryDirectory() as temp:
            target=Path(temp)/'fake.avwf';report=b.bake(provider,(-3,-2,4,5),target)
            sys.path.insert(0,str(b.ROOT/'Tools/WorldScaleImport'));from convert_world import read_avwf
            atlas=read_avwf(target)
            check(np.array_equal(atlas['heights'],h),'AVWF baking carries exact float32 height samples')
            check(atlas['extent_x']==120 and atlas['extent_z']==90,'AVWF extent uses intervals rather than sample count')
            check(report['region_metadata']['backend']==FakeBackend.kind,'fake bake report remains explicitly non-neural')
            check(np.array_equal(np.load(target.with_suffix('.climate.npz'))['climate'],climate),'bake retains all climate data in sidecar')
    finally:provider.close()
    gate=threading.Event();backend=FakeBackend(gate);small=b.make_job('/unused',queue_capacity=1);provider=b.RegionProvider(small,backend)
    try:
        first=provider.request(req(small));check(backend.entered.wait(2),'single inference owner starts the request')
        duplicate=provider.request(req(small));check(first is duplicate,'concurrent identical callers share one inference future')
        second=provider.request(req(small,c=9))
        rejects(lambda:provider.request(req(small,c=18)),'bounded queue rejects further distinct work',b.QueueFull)
        gate.set();first.result(timeout=5);second.result(timeout=5)
        check(backend.calls==2,'deduplicated callers do not duplicate backend execution')
    finally:gate.set();provider.close()
    failed=FakeBackend(fail=True);provider=b.RegionProvider(job,failed)
    try:
        rejects(lambda:provider.request(req(job)).result(timeout=5),'failed inference propagates its error',RuntimeError)
        check(not provider.status()['ready'] and provider.status()['failure'] is not None,'failure latches readiness instead of claiming success')
        rejects(lambda:provider.request(req(job,c=7)),'later callers cannot start an error retry loop',RuntimeError)
        check(failed.calls==1,'latched failure prevents repeated native inference')
    finally:provider.close()
    with tempfile.TemporaryDirectory() as temp:
        path=Path(temp);values=np.arange(6,dtype=np.float32).reshape(2,3);np.save(path/'season.npy',values)
        doc={'schema':'ashen-conditioning-v1','coarse_spacing_m':7680,'origin_i':-2,'origin_j':7,
            'channels':{'2':{'path':'season.npy','units':'degC'}}}
        manifest=path/'conditioning.json';manifest.write_text(json.dumps(doc))
        cond=b.load_conditioning_manifest(manifest);pipeline=FakeConditioning();b.install_conditioning(pipeline,cond)
        args,kwargs=pipeline.imports[0]
        check(args[0]==2 and np.array_equal(args[1][64:-64,64:-64],values*100),'conditioning seasonality converts Celsius to upstream Celsius×100')
        check(args[2:]==(-66,-57),'edge padding keeps the original signed world lattice anchored')
        check(pipeline.rebuilds==1 and kwargs['rebuild'] is False,'conditioning batches stage invalidation into one rebuild')
        doc['periodic']=True;manifest.write_text(json.dumps(doc));cond=b.load_conditioning_manifest(manifest)
        pipeline=FakeConditioning();b.install_conditioning(pipeline,cond)
        check(pipeline.imports[0][0][1].shape==(2,3) and pipeline.imports[0][1]['periodic'],'periodic imports keep original period without edge padding')
        installed=pipeline.imports[0][0][1]
        check(installed[cond['origin_i']%2,cond['origin_j']%3]==values[0,0]*100,'nonzero periodic origins preserve phase despite upstream global-modulo lookup')
        cond_job=b.make_job('/unused',conditioning=manifest);np.save(path/'season.npy',values+1)
        rejects(lambda:b.validate_job(cond_job),'changed conditioning bytes invalidate immutable jobs')
        doc['coarse_spacing_m']=7000;manifest.write_text(json.dumps(doc))
        rejects(lambda:b.make_job('/unused',conditioning=manifest),'reject incorrectly scaled conditioning rasters')
        doc['coarse_spacing_m']=7680;doc['channels']['2']['units']='meters';manifest.write_text(json.dumps(doc))
        rejects(lambda:b.load_conditioning_manifest(manifest),'reject mismatched climate units')
    with tempfile.TemporaryDirectory() as temp:
        class PartitionSensitiveFake(FakeBackend):
            def query(self,window):
                height,climate,biome,land=super().query(window)
                # Deliberately mimic a reconstruction whose floating results depend
                # on requested rectangle shape/origin, unlike a true field query.
                height=height+(window[2]+window[3]+window[0]%17)*.0001
                return height,climate,biome,land
        raw_backend=PartitionSensitiveFake();canonical=b.CanonicalBackend(job,raw_backend,temp)
        large=canonical.query((-2,254,6,7));small=canonical.query((-1,255,3,4))
        check(np.array_equal(large[0][1:4,1:5],small[0]),'canonical owners eliminate request-partition drift across negative and tile-boundary windows')
        calls=raw_backend.calls;fresh=b.CanonicalBackend(job,raw_backend,temp)
        repeat=fresh.query((-1,255,3,4))
        check(np.array_equal(repeat[0],small[0]) and raw_backend.calls==calls,'checksum-verified canonical owner bytes survive provider recreation')
        check('FAKE' in canonical.kind,'canonical wrapping does not disguise fake backend evidence')
        owner=next((Path(temp)/job['world_id']).glob('*.atdr'));damaged=bytearray(owner.read_bytes());damaged[-4]^=1;owner.write_bytes(damaged)
        # The selected query overlaps every owner produced above.
        rejects(lambda:fresh.query((-2,254,6,7)),'corrupt canonical owner cache is rejected before reuse')
    # Actual HTTP wire behavior is exercised with the explicitly fake backend.
    provider=b.RegionProvider(job,FakeBackend());server=b.http.server.ThreadingHTTPServer(('127.0.0.1',0),b.handler_for(provider))
    server_thread=threading.Thread(target=server.serve_forever,daemon=True);server_thread.start()
    base='http://127.0.0.1:'+str(server.server_port)
    try:
        with urllib.request.urlopen(base+'/status') as response:status=json.load(response)
        check(status['world_id']==job['world_id'] and status['backend']==FakeBackend.kind,'HTTP status publishes identity, readiness and honest backend')
        request=urllib.request.Request(base+'/region',data=json.dumps(req(job)).encode(),headers={'Content-Type':'application/json'})
        with urllib.request.urlopen(request) as response:body=response.read();mime=response.headers['Content-Type']
        check(b.decode_region(body,job['world_id'])[1].shape==(4,5) and 'terrain-region' in mime,'HTTP region serves versioned float data')
        request=urllib.request.Request(base+'/region',data=json.dumps({**req(job),'world_id':'stale'}).encode())
        try:urllib.request.urlopen(request)
        except urllib.error.HTTPError as e:check(e.code==409,'HTTP rejects stale world IDs with409')
        else:raise AssertionError('stale HTTP request accepted')
    finally:server.shutdown();server.server_close();provider.close();server_thread.join(timeout=2)
    print(f'PASS {CHECKS} bridge contract checks. Fake pipeline only; real pretrained inference is separate evidence.',flush=True)

if __name__=='__main__':main()
