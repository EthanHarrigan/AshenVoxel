#!/usr/bin/env python3
"""Run actual pure-C# core workloads offline. Requires installed .NET8 SDK.

No Unity runtime, graphics or target-frame-rate claims. Results include CPU model,
SDK/framework, physical fixture, raw samples, percentiles and correctness gates.
"""
from pathlib import Path
import argparse, json, os, platform, shutil, subprocess, hashlib

ROOT=Path(__file__).resolve().parents[2]

def version(p):return tuple(int(s) if s.isdigit() else 0 for s in p.name.split('.'))
def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--dotnet',type=Path)
    args=p.parse_args()
    candidates=[args.dotnet,Path(os.environ['DOTNET_ROOT'])/'dotnet' if os.environ.get('DOTNET_ROOT') else None,Path(shutil.which('dotnet')) if shutil.which('dotnet') else None,ROOT.parent/'.dotnet/dotnet']
    dotnet=next((v.resolve() for v in candidates if v and v.is_file()),None)
    if not dotnet:p.error('Install .NET8 SDK or pass --dotnet /path/to/dotnet')
    sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=version)[-1]
    refs=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=version)[-1]/'ref/net8.0'
    build=ROOT/'Tools/Benchmarks/bin';build.mkdir(parents=True,exist_ok=True)
    output=ROOT/'Docs/Benchmark';output.mkdir(parents=True,exist_ok=True)
    assembly=build/'Benchmark.dll';quote=lambda v:'"'+str(v)+'"'
    core=sorted((ROOT/'Assets/AshenVoxel/Core').rglob('*.cs'))
    source_hashes={v.relative_to(ROOT / "Assets/AshenVoxel/Core").as_posix():hashlib.sha256(v.read_bytes()).hexdigest() for v in core}
    options=['-nologo','-warnaserror+','-optimize+','-target:exe','-out:'+quote(assembly)]
    options+=['-r:'+quote(v) for v in sorted(refs.glob('*.dll'))]
    options+=[quote(v) for v in core]+[quote(ROOT/'Tools/Benchmarks/Benchmark.cs')]
    rsp=build/'compile.rsp';rsp.write_text('\n'.join(options))
    (build/'Benchmark.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}))
    subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],check=True)
    result=output/'core-baseline.json'
    subprocess.run([str(dotnet),str(assembly),str(result)],check=True)
    data=json.loads(result.read_text());cpu='unknown'
    if source_hashes != {v.relative_to(ROOT / "Assets/AshenVoxel/Core").as_posix():hashlib.sha256(v.read_bytes()).hexdigest() for v in core}:
        raise RuntimeError('Core source changed during benchmark; rerun after edits settle.')
    if Path('/proc/cpuinfo').exists():
        for line in Path('/proc/cpuinfo').read_text().splitlines():
            if line.startswith('model name'):
                cpu=line.split(':',1)[1].strip();break
    data['environment']={'cpu_model':cpu,'architecture':platform.machine(),'os':platform.system(),'logical_cpu_count':os.cpu_count(),'sdk':sdk.name,'runtime_compiler_optimization':True,'source_sha256':source_hashes,'note':'Container observation; host load, quotas, thermal state and available RAM not controlled.'}
    result.write_text(json.dumps(data,indent=2)+'\n')
    table=['# Executed pure-core baseline','',f"Generated UTC: `{data['generated_utc']}`. Generator version **{data['generator_version']}**.",'',f"{cpu}; {platform.machine()}; {data['framework']}; SDK {sdk.name}; optimized Roslyn compilation.",'','**Container CPU observations only. Excludes Unity, meshes, graphics, colliders and GPU. These are not frame rates.**','','| Workload | Samples | p50 ms | p95 ms | Maximum ms | Mean thread-allocated bytes |','| --- | ---: | ---: | ---: | ---: | ---: |']
    for key in ['generation','brush_calls','active_simulation_calls','idle_simulation_calls','ray_batches_1000','snapshot_save','snapshot_load']:
        metric=data[key]
        table.append(f"| {key} | {metric['count']} | {metric['p50_ms']:.4f} | {metric['p95_ms']:.4f} | {metric['max_ms']:.4f} | {metric['allocated_bytes_mean']:.1f} |")
    table += ['',f"Processed {data['workload']['total_candidates_processed']:,} candidates; {data['workload']['total_moves']:,} moves; peak frontier {data['workload']['peak_active']:,}; final frontier {data['workload']['final_active']:,}.",'',f"All correctness gates passed: {all(data['assertions'].values())}. Full raw samples, fixture counts and exact source hashes are in [core-baseline.json](core-baseline.json).",'','Read [README.md](README.md) for methodology, sample-count limits and reproduction.']
    (output/'RESULTS.md').write_text('\n'.join(table)+'\n')
    for key in ['generation','brush_calls','active_simulation_calls','idle_simulation_calls','ray_batches_1000','snapshot_save','snapshot_load']:
        s=data[key];print(f'{key}: n={s["count"]}, p50={s["p50_ms"]:.3f}ms, p95={s["p95_ms"]:.3f}ms')
    print('Correctness gates:',data['assertions'])
if __name__=='__main__':main()
