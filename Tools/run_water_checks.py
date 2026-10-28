#!/usr/bin/env python3
"""Compile actual visual-water component under data-only Unity substitutes.

Checks material ownership, geometry bounds, global phase and underwater state.
Shader checks are static; this is not a native Unity shader/import test.
"""
from pathlib import Path
import datetime,hashlib,json,os,re,shutil,subprocess
ROOT=Path(__file__).resolve().parents[1]
def version(p):return tuple(int(x) if x.isdigit() else 0 for x in p.name.split('.'))
def main():
    candidates=[Path(os.environ['DOTNET_ROOT'])/'dotnet' if os.environ.get('DOTNET_ROOT') else None,Path(shutil.which('dotnet')) if shutil.which('dotnet') else None,ROOT.parent/'.dotnet/dotnet']
    dotnet=next((p.resolve() for p in candidates if p and p.is_file()),None)
    if dotnet is None:raise RuntimeError('Install .NET8 SDK or set DOTNET_ROOT')
    sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=version)[-1]
    refs=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=version)[-1]/'ref/net8.0'
    source=ROOT/'Assets/AshenVoxel/WorldScaleRuntime'
    files=[ROOT/'Assets/AshenVoxel/WorldScaleData'/n for n in ['WorldCoordinates.cs','WorldFields.cs','HeightAtlas.cs']]
    files+=[source/'VisualWater.cs',ROOT/'Tools/WaterChecks/UnityWaterShim.cs',ROOT/'Tools/WaterChecks/Program.cs']
    audited=files+[source/'VisualWater.shader',source/'Resources/VisualWater.mat',Path(__file__)]
    hashes={str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest() for p in audited}
    build=ROOT/'Tools/WaterChecks/bin';build.mkdir(parents=True,exist_ok=True);assembly=build/'WaterChecks.dll';quote=lambda p:'"'+str(p)+'"'
    options=['-nologo','-warnaserror+','-langversion:9.0','-optimize+','-target:exe','-out:'+quote(assembly)]
    options+=['-r:'+quote(p) for p in sorted(refs.glob('*.dll'))]+[quote(p) for p in files]
    rsp=build/'compile.rsp';rsp.write_text('\n'.join(options))
    (build/'WaterChecks.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}))
    subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],check=True)
    result=subprocess.run([str(dotnet),str(assembly)],check=True,text=True,capture_output=True);print(result.stdout,end='')
    count=int(re.search(r'All (\d+) visual-water checks passed',result.stdout)[1])
    shader=(source/'VisualWater.shader').read_text();material=(source/'Resources/VisualWater.mat').read_text()
    for text in ['"RenderPipeline"="UniversalPipeline"','"Queue"="Transparent"','Blend SrcAlpha OneMinusSrcAlpha','ZWrite Off ZTest LEqual Cull Off','IS_FRONT_VFACE','fwidth' if 'fwidth' in shader else 'ddx','_MaximumDepthHint']:
        assert text in shader,'Missing expected static shader feature: '+text
    guid=re.search(r'^guid: ([0-9a-f]{32})$',(source/'VisualWater.shader.meta').read_text(),re.M)[1]
    assert guid in material and 'Shader "AshenVoxel/VisualWater"' in shader
    assert '_CameraDepthTexture' not in shader and 'SampleSceneDepth' not in shader
    assert hashes=={str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest() for p in audited},'Water sources changed during checks'
    report={'status':'PASS','checks':count,'generated_utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),
        'scope':'Actual C# visual-water logic under Unity substitutes; static shader/material checks only. No native Unity, HLSL compilation or water simulation.',
        'sdk':sdk.name,'source_sha256':hashes,'static_shader_checks':'PASS; retained shader reference, transparent/depth/cull contract, filtered analytic waves, no depth-texture dependency'}
    (ROOT/'Docs/water-validation.json').write_text(json.dumps(report,indent=2)+'\n')
    print('PASS retained shader/material references and static URP contracts; native HLSL/import unverified')
if __name__=='__main__':main()
