"""Scalar contract checks for clipmap cross-grid face lighting, not rendered pixels.

Checks the analytic radiance blend independently of URP's BRDF implementation.
Source guards tie its branch/fetch assumptions to the current shader. The separate
HLSL compiler check covers syntax and selected variants; neither is a Unity visual
or GPU timing test.
"""
from pathlib import Path
import argparse
import datetime
import hashlib
import json
import math
import re

ROOT = Path(__file__).resolve().parents[2]
SHADER = ROOT / 'Assets/AshenVoxel/WorldScaleRuntime/Resources/TerrainClipmap.shader'
checks = 0


def check(condition, message):
    global checks
    checks += 1
    if not condition:
        raise AssertionError(message)


def dot(a, b):
    return sum(x*y for x, y in zip(a, b))


def norm(v):
    length = math.sqrt(dot(v, v))
    return tuple(x/length for x in v) if length else None


def mix(a, b, w):
    return tuple(x+(y-x)*w for x, y in zip(a, b))


def diffuse(n, light):
    return max(0, dot(n, light))


def evaluate(smooth, face_a, face_b, bevel_a, bevel_b, detail, strength, w, light, valid_b=True):
    """Scalar model of the normal choice and one reused-light diffuse term."""
    a = norm(mix(face_a, bevel_a, detail))
    b = norm(mix(face_b, bevel_b, detail))
    split = valid_b and 0 < w < 1 and dot(face_a, face_b) < .5
    if split:
        na, nb = norm(mix(smooth, a, strength)), norm(mix(smooth, b, strength))
        return diffuse(na, light)*(1-w)+diffuse(nb, light)*w, split
    normal = norm(mix(smooth, norm(mix(a, b, w)), strength))
    return diffuse(normal, light), split


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--report', default='Docs/ClipmapEvidence/face-lighting-20261029/scalar.json')
    args = parser.parse_args()
    up, left, right, forward = (0., 1., 0.), (-1., 0., 0.), (1., 0., 0.), (0., 0., 1.)
    smooth = norm((-.3, 1., .2))
    lights = [up, left, right, forward, norm((.7, .4, -.5)), norm((-.6, .8, .1))]

    # An upward light illuminates half top/half side coverage by .5. A normalized
    # normal blend yields sqrt(.5), inventing 41.4% extra diffuse illumination.
    old = diffuse(norm(mix(up, left, .5)), up)
    new, split = evaluate(up, up, left, up, left, 0, 1, .5, up)
    check(split and abs(new-.5) < 1e-12, 'Half top/side radiance must be .5')
    check(abs(old-math.sqrt(.5)) < 1e-12 and old > new, 'Old diagonal-light bias fixture')

    # Exact endpoints and same-face blends retain the original result, including
    # differing bevels and the fade back to the underlying smooth terrain normal.
    for face_a, face_b in [(up, left), (left, right), (left, forward), (up, up)]:
        bevel_a, bevel_b = norm(mix(face_a, up, .2)), norm(mix(face_b, up, .35))
        for detail in [0., .4, 1.]:
            a, b = norm(mix(face_a, bevel_a, detail)), norm(mix(face_b, bevel_b, detail))
            for strength in [0., .35, 1.]:
                for w in [0., .2, .5, .8, 1.]:
                    for light in lights:
                        result, split = evaluate(smooth, face_a, face_b, bevel_a, bevel_b, detail, strength, w, light)
                        check(math.isfinite(result), 'Finite radiance including opposing faces')
                        if face_a == face_b or w in (0., 1.):
                            expected = diffuse(norm(mix(smooth, norm(mix(a, b, w)), strength)), light)
                            check(not split and abs(result-expected) < 1e-12, 'Original fast-path parity')
                        else:
                            endpoint_a = diffuse(norm(mix(smooth, a, strength)), light)
                            endpoint_b = diffuse(norm(mix(smooth, b, strength)), light)
                            check(split and abs(result-((1-w)*endpoint_a+w*endpoint_b)) < 1e-12, 'Endpoint radiance interpolation')
                            check(min(endpoint_a, endpoint_b)-1e-12 <= result <= max(endpoint_a, endpoint_b)+1e-12, 'No invented illumination beyond endpoints')
                            swapped, _ = evaluate(smooth, face_b, face_a, bevel_b, bevel_a, detail, strength, 1-w, light)
                            check(abs(result-swapped) < 1e-12, 'Grid-order parity')

    check(norm(mix(left, right, .5)) is None, 'Old opposing-face normal is undefined')
    opposite, split = evaluate(up, left, right, left, right, 0, 1, .5, left)
    check(split and abs(opposite-.5) < 1e-12, 'Opposing faces keep finite endpoint energy')
    invalid, split = evaluate(smooth, up, up, up, up, 0, .7, 0, up, valid_b=False)
    check(not split and math.isfinite(invalid), 'Invalid second hit uses existing single-hit fallback')

    source = SHADER.read_text(encoding='utf-8')
    compact = re.sub(r'\s+', '', source)
    check('if(rb>0&&w1>0&&w1<1&&dot(ha.normal,hb.normal)<.5h)' in compact, 'Shader cross-face eligibility contract')
    check('secondFaceNormal=normalize(lerp(normal,nb,strength));normal=normalize(lerp(normal,na,strength));faceLightBlend=w1;' in compact, 'Both endpoints fade from the original smooth normal')
    check('elsenormal=normalize(lerp(normal,normalize(lerp(na,nb,w1)),strength));' in compact, 'Original fast-path formula retained')
    check(source.count('GetMainLight(') == 1 and source.count('GetAdditionalLight(') == 1, 'Fetch each light/shadow once')
    check(source.count('GetScreenSpaceAmbientOcclusion(') == 1, 'Fetch SSAO once')
    check(source.count('ra=VirtualStepAt(') == 1 and source.count('rb=VirtualStepAt(') == 1, 'No extra virtual-grid tracing')
    check(source.count('CmMainLighting(brdf,sun,') == 2, 'Both main-light endpoints evaluated')
    check('LightingPhysicallyBased(brdf,light,secondFaceNormal,view)' in compact, 'Additional-light second endpoint shares fetched light')
    helper = source.split('half3 CmMainLighting(', 1)[1].split('half4 Frag(', 1)[0]
    check(all(token not in helper for token in ['GetMainLight(', 'GetAdditionalLight(', '.Sample', 'VirtualStepAt(']), 'Lighting helper performs no extra fetch or trace')

    report = ROOT / args.report
    report.parent.mkdir(parents=True, exist_ok=True)
    report.write_text(json.dumps(dict(status='PASS', assertions=checks, scope=__doc__.strip(),
        generated_utc=datetime.datetime.now(datetime.timezone.utc).isoformat(),
        shader_sha256=hashlib.sha256(SHADER.read_bytes()).hexdigest(),
        scalar_fixture=dict(old_normal_blend=old, corrected_radiance_blend=new,
                            old_relative_brightness_bias=old/new-1),
        limits='No native visual acceptance, Unity import, depth/SSAO parity, or GPU timing measurement.'), indent=2)+'\n', encoding='utf-8')
    print(f'PASS {checks} scalar/structural assertions; {report.relative_to(ROOT)}')


if __name__ == '__main__':
    main()
