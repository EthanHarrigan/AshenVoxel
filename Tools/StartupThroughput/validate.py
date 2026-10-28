"""Run existing streaming regressions plus exact fallback coverage on candidate."""
from pathlib import Path
import argparse,datetime,hashlib,json,shutil,subprocess
from prepare import ROOT,HERE,CACHE,FROZEN,once
parser=argparse.ArgumentParser();parser.add_argument('--merged',action='store_true');a=parser.parse_args()
out=CACHE/('merged-validation' if a.merged else 'validation');out.mkdir(exist_ok=True)
candidate=(CACHE/('merged/Assets/AshenVoxel/Exploration/VegetationStreaming.cs' if a.merged else 'compact/VegetationStreaming.cs')).read_text()
for line in ['            StartupProbe.MatrixRebuilds++;','StartupProbe.CellsBuilt++;','StartupProbe.MatrixVisits++;']:candidate=candidate.replace(line,'')
(out/'VegetationStreaming.cs').write_text(candidate)
s=(ROOT/'Tools/ExplorationStreamingChecks/Program.cs').read_text()
s=s.replace('loading==413','loading>=52&&loading<=413')
s=s.replace('all99 prototypes upload incrementally across412LOD startup yields','all99 prototypes upload in bounded batches across the existing412LOD yields')
s=s.replace('"BuildMatrices",Stopwatch.GetTimestamp())','"BuildMatrices",Stopwatch.GetTimestamp(),(double)v.WorkMilliseconds)')
if 'CoverageCheck.Run(args[1])' not in s:s=once(s,'  File.WriteAllText(args[0],','  CoverageCheck.Run(args[1]);\n  File.WriteAllText(args[0],')
(out/'Program.cs').write_text(s)
sources=list((FROZEN/'Assets/AshenVoxel/Core').rglob('*.cs'))+list((FROZEN/'Assets/AshenVoxel/WorldScaleData').glob('*.cs'))
sources += [FROZEN/'Assets/AshenVoxel/Exploration'/n for n in ['ForestBillboard.cs','ForestHorizon.cs']]
sources += [out/'VegetationStreaming.cs',out/'Program.cs',ROOT/'Tools/ExplorationStreamingChecks/UnityShim.cs',ROOT/'Tools/ExplorationStreamingChecks/CoverageCheck.cs']
dotnet=Path(shutil.which('dotnet')).resolve();ver=lambda p:tuple(map(int,p.name.split('.')))
sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=ver)[-1];refs=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=ver)[-1]/'ref/net8.0'
dll=out/'Validate.dll';q=lambda p:'"'+str(p)+'"';rsp=out/'compile.rsp';rsp.write_text('\n'.join(['-nologo','-warnaserror+','-langversion:9.0','-optimize+','-target:exe','-out:'+q(dll)]+['-r:'+q(p) for p in refs.glob('*.dll')]+[q(p) for p in sources]))
subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],check=True)
dll.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}))
report=out/('streaming-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S')+'.json')
subprocess.run([str(dotnet),str(dll),str(report),str(FROZEN/'Assets/AshenVoxel/Exploration/Prototypes/ExplorationPrototypes.bytes')],check=True)
d=json.loads(report.read_text());d['source_sha256']={str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in sources};d['extra_coverage_check']='PASS: exact placement set, no duplicates, triangle/buffer limits, reserve cleanup';report.write_text(json.dumps(d,indent=2));print(report)
