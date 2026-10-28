"""Compile exact project atlas readers and validate the separately baked map."""
from pathlib import Path
import argparse
import json
import shutil
import subprocess
from coarse_overview import ROOT,atomic_json,sha


def version(path):return tuple(int(s) for s in path.name.split('.'))


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--input',type=Path,required=True)
    p.add_argument('--output',type=Path,required=True)
    p.add_argument('--compile-only',action='store_true')
    a=p.parse_args()
    dotnet=Path(shutil.which('dotnet'))
    sdk=max((dotnet.parent/'sdk').glob('8.*'),key=version)
    refs=max((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=version)/'ref/net8.0'
    out=ROOT/'.terrain-cache/coarse320-study/consumer';out.mkdir(parents=True,exist_ok=True)
    sources=list((ROOT/'Assets/AshenVoxel/Core').rglob('*.cs'))
    sources += [ROOT/'Assets/AshenVoxel/WorldScaleData'/name for name in
                ('WorldCoordinates.cs','WorldFields.cs','HeightAtlas.cs','ClimateArchive.cs','SurfaceProfile.cs')]
    sources.append(Path(__file__).with_name('OverviewConsumerCheck.cs'))
    hashes={str(p.relative_to(ROOT)):sha(p) for p in sources}
    assembly=out/'OverviewConsumer.dll';quote=lambda p:'"'+str(p)+'"'
    args=['-nologo','-warnaserror+','-langversion:9.0','-optimize+','-target:exe','-out:'+quote(assembly)]
    args += ['-r:'+quote(p) for p in refs.glob('*.dll')]+[quote(p) for p in sources]
    rsp=out/'compile.rsp';rsp.write_text('\n'.join(args),encoding='utf-8')
    subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],check=True)
    atomic_json(out/'OverviewConsumer.runtimeconfig.json',{'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}})
    if a.compile_only:
        print('PASS actual C# consumer source compilation');return
    a.output.parent.mkdir(parents=True,exist_ok=True)
    subprocess.run([str(dotnet),str(assembly),str(a.input.resolve()),str(a.output.resolve())],check=True)
    if hashes!={str(p.relative_to(ROOT)):sha(p) for p in sources}:raise RuntimeError('Consumer source changed during validation')
    data=json.loads(a.output.read_text());data['source_sha256']=hashes
    atomic_json(a.output,data)


if __name__=='__main__':main()
