"""Measure bounded real-model overview work before scheduling a whole-world bake."""
from pathlib import Path
import argparse
import json
import time
import numpy as np
from coarse_overview import (atomic_json, available_commit, job_for_batch,
                             load_height_pipeline, model_query, sample_overview, release_temporary_gpu_memory)


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--job', type=Path, required=True)
    p.add_argument('--output', type=Path, required=True)
    p.add_argument('--batch', type=int, default=8)
    a = p.parse_args()
    a.output.mkdir(parents=True, exist_ok=True)
    job = job_for_batch(a.job, a.batch)
    atomic_json(a.output/'job.json', job)
    report = {'scope': 'Actual learned 320m overview probe; no full decoder or renderer',
              'job_world_id': job['world_id'], 'batch': a.batch, 'queries': [],
              'commit_before_load': available_commit()}
    if report['commit_before_load'] is not None and report['commit_before_load'] < 4*1024**3:
        raise RuntimeError('Need 4 GiB commit headroom for a bounded model-load probe')
    start = time.perf_counter()
    pipeline, _ = load_height_pipeline(job)
    report['load_seconds'] = time.perf_counter()-start
    report['commit_after_load'] = available_commit()
    print(json.dumps({'loaded': report['load_seconds'], 'commit': report['commit_after_load']}), flush=True)
    try:
        query = model_query(pipeline)
        # One typical 20km-wide output block, then an adjacent block with warm context.
        for name, x0 in [('valley', -51200.), ('adjacent', -30720.)]:
            headroom = available_commit()
            if headroom is not None and headroom < 2*1024**3:
                raise RuntimeError('Insufficient commit headroom; probe stopped safely')
            t = time.perf_counter()
            xs, zs = x0 + np.arange(64)*320, -25600 + np.arange(64)*320
            h = sample_overview(query, xs, zs)
            np.savez_compressed(a.output/(name+'.npz'), height=h, x=xs, z=zs)
            row = {'name': name, 'seconds': time.perf_counter()-t,
                   'height_range': [float(h.min()),float(h.max())],
                   'memory_release': release_temporary_gpu_memory(), 'commit_after': available_commit()}
            report['queries'].append(row)
            print(json.dumps(row), flush=True)
        report['status'] = 'PASS'
    except Exception as error:
        report['status'] = 'STOPPED'
        report['error'] = str(error)
        raise
    finally:
        report['elapsed_seconds'] = time.perf_counter()-start
        atomic_json(a.output/'probe.json', report)
        pipeline.close()


if __name__ == '__main__':
    main()
