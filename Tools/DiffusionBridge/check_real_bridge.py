#!/usr/bin/env python3
"""Optional actual-weight bridge validation. Requires the prepared inference environment."""
from pathlib import Path
import argparse,hashlib,json,resource,time
from datetime import datetime,timezone
import numpy as np
import bridge as b

def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--job',type=Path,required=True)
    p.add_argument('--output-dir',type=Path,required=True);args=p.parse_args()
    args.output_dir.mkdir(parents=True,exist_ok=True)
    report={'status':'RUNNING','created_utc':datetime.now(timezone.utc).isoformat(),
        'scope':'Actual pinned pretrained model through the canonical bridge; not Unity or gameplay FPS.',
        'checks':{},'timings_seconds':{}}
    executed_sources={name:b.digest(Path(__file__).parent/name) for name in ['bridge.py','check_real_bridge.py','model-manifest.json']}
    dest=args.output_dir/'validation.json'
    def save():
        temporary=dest.with_suffix('.json.tmp');temporary.write_text(json.dumps(report,indent=2)+'\n');temporary.replace(dest)
    job=b.read_job(args.job);report['job']=job;save()
    start=time.monotonic();backend=b.RealBackend(job);report['timings_seconds']['prepare']=time.monotonic()-start;save()
    canonical=b.CanonicalBackend(job,backend,args.output_dir/'owners');provider=b.RegionProvider(job,canonical)
    report['owner_cache_preexisting']=bool(list(canonical.directory.glob('*.atdr')))
    report['cold_neural_region_execution_expected']=not report['owner_cache_preexisting']
    try:
        first_request={'world_id':job['world_id'],'row0':608,'col0':1120,'rows':33,'cols':33}
        start=time.monotonic();raw=provider.request(first_request).result();report['timings_seconds']['first_canonical_region']=time.monotonic()-start;save()
        meta,height,climate,biomes,land=b.decode_region(raw,job['world_id'])
        report['checks']['actual_pretrained_backend']=meta['backend']=='upstream-pretrained/canonical-owner-v1'
        report['checks']['all_float_channels_finite']=bool(np.isfinite(height).all() and np.isfinite(climate).all())
        start=time.monotonic();repeat=provider.request(first_request).result();report['timings_seconds']['response_cache_repeat']=time.monotonic()-start
        report['checks']['cached_response_bit_exact']=repeat==raw
        start=time.monotonic();small=provider.request({**first_request,'row0':616,'col0':1128,'rows':17,'cols':17}).result()
        report['timings_seconds']['overlap_from_owner_disk']=time.monotonic()-start
        _,small_height,small_climate,small_biomes,small_land=b.decode_region(small,job['world_id'])
        report['checks']['canonical_overlap_height_bit_exact']=bool(np.array_equal(height[8:25,8:25],small_height))
        report['checks']['canonical_overlap_climate_bit_exact']=bool(np.array_equal(climate[:,8:25,8:25],small_climate))
        class NoInference:
            kind='upstream-pretrained'
            def query(self,window):raise AssertionError('Verified owner disk replay must not invoke a model')
        restored=b.CanonicalBackend(job,NoInference(),args.output_dir/'owners')
        replay=restored.query((608,1120,33,33))
        report['checks']['owner_disk_recreation_bit_exact']=all(np.array_equal(a,v) for a,v in zip((height,climate,biomes,land),replay))
        baked=b.bake(provider,(608,1120,33,33),args.output_dir/'neural-sample.avwf')
        report['checks']['avwf_baked']=True
        report['avwf_sha256']=baked['avwf_sha256'];report['response_sha256']=hashlib.sha256(raw).hexdigest()
        report['height_min_m']=float(height.min());report['height_max_m']=float(height.max())
        report['climate_shape']=list(climate.shape);report['native_spacing_m']=30
        report['source_sha256']=executed_sources
        if executed_sources!={name:b.digest(Path(__file__).parent/name) for name in executed_sources}:
            raise RuntimeError('Bridge sources changed during actual validation; retry after edits settle')
        report['peak_rss_kib']=resource.getrusage(resource.RUSAGE_SELF).ru_maxrss
        report['limits']='Actual test covers one canonical owner, overlapping requests and verified disk replay. If owner_cache_preexisting is true, this run verifies prior actual neural bytes without generating a new model region. Adjacent owner geometry, GPU/runtime changes, far LOD, Unity and model-cache eviction/recompute remain separate tests.'
        if not all(report['checks'].values()):raise AssertionError('Actual bridge check failed')
        report['status']='PASS';save();print(json.dumps(report,indent=2),flush=True)
    except Exception as error:
        report['status']='FAIL';report['error']=type(error).__name__+': '+str(error);save();raise
    finally:provider.close()

if __name__=='__main__':main()
