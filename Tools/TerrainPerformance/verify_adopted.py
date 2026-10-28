"""Compare the retained production builder with the frozen pre-change builder.

Uses the actual float-valued Unity settings (promoted to double). The earlier
profiling fixture used double literals for two settings; keep that evidence intact.
Only constructor visibility changes in the test copy so both builders can return
the same data type. Production method bodies are compiled unchanged.
"""
from pathlib import Path
import datetime,hashlib,json,shutil,subprocess
from prepare import ROOT,HERE,CACHE,replace_once

def main():
    out=CACHE/'adopted';out.mkdir(exist_ok=True)
    live=ROOT/'Assets/AshenVoxel/WorldScaleData/VoxelLodSurface.cs'
    source=replace_once(live.read_text(),'        VoxelLodSurface(int n,','        public VoxelLodSurface(int n,')
    (out/'VoxelLodSurface.cs').write_text(source)
    probe=(HERE/'Probe.cs').read_text().replace('maxSnowDepthMeters:2.4,','maxSnowDepthMeters:2.4f,').replace('winterFraction:.2,','winterFraction:.2f,')
    probe=replace_once(probe,'return VoxelLodSurface.Build(sampler,c.tile,c.n,floor);','return VoxelLodSurface.Build(sampler,c.tile,c.n,floor,parallelSampling:Workers==2);')
    probe=replace_once(probe,'if(Workers==0)return UnboxedSurfaceBuilder.Build(sampler,c.tile,c.n,floor);','')
    (out/'Probe.cs').write_text(probe)
    (out/'Verify.cs').write_text('''using System;using System.IO;using System.Diagnostics;using System.Collections.Generic;using System.Text.Json;
class Verify{static void Main(string[] args){
var sampler=TerrainProbe.Load(args[0]);var rows=new List<object>();var hashes=new Dictionary<string,string>();
foreach(var c in TerrainProbe.Cases()){TerrainProbe.Workers=1;hashes[c.label]=TerrainProbe.Hash(TerrainProbe.Build(sampler,c));}
for(int repeat=0;repeat<5;repeat++)foreach(var c in TerrainProbe.Cases())foreach(int mode in repeat%2==0?new[]{1,0,2}:new[]{2,0,1}){
TerrainProbe.Workers=mode;long start=GC.GetTotalAllocatedBytes(true);var sw=Stopwatch.StartNew();var data=TerrainProbe.Build(sampler,c);sw.Stop();long bytes=GC.GetTotalAllocatedBytes(true)-start;
string hash=TerrainProbe.Hash(data);if(hash!=hashes[c.label])throw new Exception("Geometry mismatch "+c.label);
rows.Add(new{label=c.label,mode,repeat,ms=sw.Elapsed.TotalMilliseconds,allocated_bytes=bytes,faces=data.Faces.Count,sha256=hash});}
File.WriteAllText(args[1],JsonSerializer.Serialize(new{status="PASS",scope="Exact retained production method vs frozen legacy; .NET8, float-valued settings; not Unity FPS",source=sampler.SourceIdentity,rows},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine("PASS: 120 builds, exact ordered-face/height hashes across original, retained serial and retained two-worker methods.");}}
''')
    dotnet=Path(shutil.which('dotnet')).resolve();version=lambda p:tuple(map(int,p.name.split('.')))
    sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=version)[-1];refs=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=version)[-1]/'ref/net8.0'
    sources=sorted((ROOT/'Assets/AshenVoxel/Core').rglob('*.cs'))+[p for p in sorted((ROOT/'Assets/AshenVoxel/WorldScaleData').glob('*.cs')) if p.name!='VoxelLodSurface.cs']
    hashes={str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest() for p in sources+[live,HERE/'verify_adopted.py']}
    sources+=[out/'VoxelLodSurface.cs',out/'Probe.cs',out/'Verify.cs',CACHE/'parallel-unboxed/BaselineSurfaceBuilder.cs']
    assembly=out/'Verify.dll';q=lambda p:'"'+str(p)+'"';rsp=out/'compile.rsp'
    rsp.write_text('\n'.join(['-nologo','-optimize+','-langversion:9.0','-target:exe','-out:'+q(assembly)]+['-r:'+q(p) for p in refs.glob('*.dll')]+[q(p) for p in sources]))
    subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],check=True)
    assembly.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}))
    report=out/('verify-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S')+'.json')
    subprocess.run([str(dotnet),str(assembly),str(CACHE/'frozen/Assets/AshenVoxel/Exploration/Samples'),str(report)],check=True)
    assert hashes=={p:hashlib.sha256((ROOT/p).read_bytes()).hexdigest() for p in hashes},'Source changed during checks'
    d=json.loads(report.read_text());d['source_sha256']=hashes;report.write_text(json.dumps(d,indent=2));print(report)
if __name__=='__main__':main()
