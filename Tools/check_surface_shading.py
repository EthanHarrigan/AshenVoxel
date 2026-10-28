#!/usr/bin/env python3
"""Check opaque shader/asset contracts; this does not compile HLSL or run Unity."""
from pathlib import Path
import datetime, hashlib, json, re

ROOT = Path(__file__).resolve().parents[1]


def main():
    source = ROOT/'Assets/AshenVoxel/Rendering/WorldSurface.shader'
    shader = source.read_text()
    passed = []

    def check(condition, name):
        if not condition:
            raise AssertionError(name)
        passed.append(name)

    blocks = re.findall(r'Pass\s*\{(.*?)ENDHLSL\s*\}', shader, re.S)
    check(len(blocks) == 5, 'forward, shadow, depth, camera-normal and motion passes exist')
    common = re.search(r'HLSLINCLUDE(.*?)ENDHLSL', shader, re.S)[1]
    by_name = {re.search(r'Name\s+"([^"]+)"', block)[1]: block for block in blocks}
    for name, block in by_name.items():
        check('#pragma multi_compile_instancing' in block and '#pragma instancing_options assumeuniformscaling' in block,
              name+' retains uniform-scale instancing variants')
    check('UNITY_VERTEX_INPUT_INSTANCE_ID' in common and 'UNITY_SETUP_INSTANCE_ID(input)' in common,
          'shared vertex reads per-instance transforms')
    check(shader.count('CBUFFER_START(UnityPerMaterial)') == 1 and 'CBUFFER_START(UnityPerMaterial)' in common,
          'all five passes inherit one identical material layout')
    check('ClipView(input)' in by_name['DepthOnly'] and 'ClipView(input)' in by_name['DepthNormals'] and
          'ClipMaterialStable(MaterialViewOpacity(' in by_name['ForwardLit'] and
          'ClipMaterialStable(MaterialViewOpacity(' in by_name['MotionVectors'] and
          'ClipMaterialStable(MaterialShadowCoverage(' in by_name['ShadowCaster'],
          'all object passes apply anchored material coverage with shared camera and shadow policies')
    forward = by_name['ForwardLit']
    check('UNITY_TRANSFER_INSTANCE_ID(input,output)' in common and 'UNITY_SETUP_INSTANCE_ID(input)' in forward,
          'fragment spherical harmonics receive the instance identity')
    check('SRGBToLinear(output.color.rgb)' in common, 'authored opaque sRGB colors are decoded')
    check('input.patternPositionOS' in forward and 'Hash(floor(input.positionWS' not in forward,
          'optional surface grain follows object coordinates across origin rebasing')
    check('min(lerp(.45h,1.0h,saturate(input.color.a)),lerp(.35h,1.0h,ao.indirectAmbientOcclusion))' in forward,
          'vertex and screen occlusion combine without double multiplication')
    check('InitializeBRDFData' in forward and 'LightingPhysicallyBased' in forward,
          'direct lighting uses the URP dielectric BRDF API')
    check('PackNormalOctQuadEncode' in by_name['DepthNormals'] and 'PackFloat2To888' in by_name['DepthNormals'],
          'camera normals support URP octahedral encoding')
    check('VoxelWindVertex' in common and 'Varyings output=Vert(input)' in by_name['ShadowCaster'],
          'forward, shadow, depth and normal passes share wind positions and analytic normals')
    motion = by_name['MotionVectors']
    check('CalcNdcMotionVectorFromCsPositions' in motion and '_NonJitteredViewProjMatrix' in motion and
          '_PrevViewProjMatrix' in motion and '_VoxelWindPreviousTime' in motion and '_VoxelWindOriginDelta' in motion,
          'motion vectors use current and previous nonjittered wind with explicit static rebase history')
    material = ROOT/'Assets/AshenVoxel/Rendering/Resources/VoxelSurface.mat'
    material_text = material.read_text()
    check('m_EnableInstancingVariants: 1' in material_text, 'Resources material retains instancing in builds')
    check('- _GrainStrength: 0\n' in material_text, 'default material adds no independent cell noise')
    ground = ROOT/'Assets/AshenVoxel/WorldScaleRuntime/GroundSurface.shader'
    ground_text = ground.read_text()
    ground_passes = re.findall(r'Pass\s*\{(.*?)ENDHLSL\s*\}', ground_text, re.S)
    check(len(ground_passes) == 4 and 'Name "DepthNormals"' in ground_text,
          'terrain supplies camera normals as well as color, depth and shadows')
    common = re.search(r'HLSLINCLUDE(.*?)ENDHLSL', ground_text, re.S)
    if common:
        check(common[1].count('CBUFFER_START(UnityPerMaterial)') == 1 and
              ground_text.count('CBUFFER_START(UnityPerMaterial)') == 1,
              'all terrain passes inherit one shared material layout')
        check('_VoxelTerrainMask[64]' in common[1] and 'all(cell < width)' in common[1],
              'voxel cutout indexing has an explicit bounded readiness grid')
        check(all('ClipVoxelTerrain(input.positionWS)' in p for p in ground_passes),
              'color, shadow, depth and normal fragments use identical completed-page cutouts')
    else:
        ground_layouts = [re.search(r'CBUFFER_START\(UnityPerMaterial\)(.*?)CBUFFER_END', p, re.S)[1].split()
                          for p in ground_passes]
        check(all(layout == ground_layouts[0] for layout in ground_layouts), 'terrain material layout agrees across passes')
    check('_SCREEN_SPACE_OCCLUSION' in ground_passes[0] and 'GetScreenSpaceAmbientOcclusion' in ground_passes[0],
          'terrain consumes screen AO instead of only writing its source normals')
    check('PackNormalOctQuadEncode' in ground_passes[-1] and 'PackFloat2To888' in ground_passes[-1],
          'terrain normal packing matches the object normal pass')
    check('GroundCoveragePosition(float3 positionWS)' in ground_text and
          'frac((positionWS+_SurfaceOriginPhase.xyz)/4096.0)*4096.0' in ground_text,
          'terrain coverage wraps its world anchor across floating-origin phase rollover')
    check(ground_text.count('MaterialViewOpacity(') == 3 and ground_text.count('MaterialShadowCoverage(') == 1 and
          all('ClipMaterialStable(' in p and 'GroundCoveragePosition(input.positionWS)' in p for p in ground_passes),
          'terrain shares anchored view coverage across camera passes and separate shadow coverage')
    report = {
        'status': 'PASS', 'checks': len(passed),
        'generated_utc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
        'scope': 'Static shader/material contracts only; separate native material optics evidence is in QualityPatches/patch-5',
        'passed': passed,
        'source_sha256': {str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest()
                          for p in (source, material, ground)},
    }
    (ROOT/'Docs/shading-validation.json').write_text(json.dumps(report, indent=2)+'\n')
    print('PASS', len(passed), 'static shader/instancing/material contracts; native compilation remains pending')


if __name__ == '__main__':
    main()
