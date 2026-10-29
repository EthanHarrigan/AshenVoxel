"""Compile clipmap HLSL with Windows D3DCompiler, without running Unity.

This checks actual URP includes and selected D3D11/SM5 variants. Unity's
include_with_pragmas directive becomes include, and zero-argument function macros
receive an unused formal parameter for FXC's legacy preprocessor. Half uses FXC's
backward-compatible float mapping. Files on disk are not changed by this bridge.
This is not Unity ShaderLab import, D3D12 rendering, full variant coverage, or a
visual gate.
"""
from pathlib import Path
import argparse
import ctypes as c
import datetime
import hashlib
import json
import re

ROOT = Path(__file__).resolve().parents[2]
SHADER = ROOT / 'Assets/AshenVoxel/WorldScaleRuntime/Resources/TerrainClipmap.shader'
COMPUTE = SHADER.with_name('TerrainClipmapScatter.compute')
INCLUDES = {}
BUFFERS = {}
OPEN_ERRORS = []


def resolve(name, parent):
    if name.startswith('Packages/'):
        package, rel = name[9:].split('/', 1)
        direct = ROOT / 'Packages' / package / rel
        if direct.exists(): return direct
        candidates = list((ROOT / 'Library/PackageCache').glob(package + '@*'))
        if len(candidates) == 1: return candidates[0] / rel
    return parent.parent / name


OPEN = c.WINFUNCTYPE(c.c_long, c.c_void_p, c.c_uint, c.c_char_p, c.c_void_p, c.POINTER(c.c_void_p), c.POINTER(c.c_uint))
CLOSE = c.WINFUNCTYPE(c.c_long, c.c_void_p, c.c_void_p)
current_source = SHADER


@OPEN
def include_open(this, kind, name, parent, out_data, out_bytes):
    try:
        path = resolve(name.decode(), BUFFERS[parent][1] if parent in BUFFERS else current_source).resolve()
        data = path.read_bytes()
        # Unity preprocesses this directive; the platform compiler receives an include.
        data = data.replace(b'#include_with_pragmas', b'#include')
        data = re.sub(rb'(#define\s+\w+)\(\)', rb'\1(_unity_empty)', data)
        INCLUDES[str(path.relative_to(ROOT)).replace('\\', '/')] = hashlib.sha256(path.read_bytes()).hexdigest()
        buffer = c.create_string_buffer(data)
        pointer = c.addressof(buffer)
        BUFFERS[pointer] = (buffer, path)
        out_data[0] = pointer
        out_bytes[0] = len(data)
        return 0
    except Exception as error:
        OPEN_ERRORS.append(str(error))
        return -2147467259


@CLOSE
def include_close(this, pointer):
    BUFFERS.pop(pointer, None)
    return 0


vtable = (c.c_void_p * 2)(c.cast(include_open, c.c_void_p), c.cast(include_close, c.c_void_p))
include = (c.c_void_p * 1)(c.cast(vtable, c.c_void_p))


class Macro(c.Structure):
    _fields_ = [('Name', c.c_char_p), ('Definition', c.c_char_p)]


compiler = c.WinDLL('d3dcompiler_47.dll')
compiler.D3DCompile.argtypes = [c.c_void_p, c.c_size_t, c.c_char_p, c.POINTER(Macro), c.c_void_p,
                              c.c_char_p, c.c_char_p, c.c_uint, c.c_uint, c.POINTER(c.c_void_p), c.POINTER(c.c_void_p)]
compiler.D3DCompile.restype = c.c_long


def blob_bytes(blob):
    if not blob: return b''
    table = c.cast(blob, c.POINTER(c.POINTER(c.c_void_p))).contents
    pointer = c.WINFUNCTYPE(c.c_void_p, c.c_void_p)(table[3])(blob)
    size = c.WINFUNCTYPE(c.c_size_t, c.c_void_p)(table[4])(blob)
    result = c.string_at(pointer, size)
    c.WINFUNCTYPE(c.c_uint, c.c_void_p)(table[2])(blob)
    return result


def compile_one(source, entry, target, defines):
    flags = {'SHADER_API_D3D11': '1', 'SHADER_TARGET': '45', 'UNITY_VERSION': '600030', 'UNITY_COMPILER_HLSL': '1'}
    flags.update(defines)
    pairs = [(name.encode(), value.encode()) for name, value in flags.items()]
    macros = (Macro * (len(pairs) + 1))(*(Macro(k, v) for k, v in pairs), Macro(None, None))
    code, errors = c.c_void_p(), c.c_void_p()
    encoded = source.encode('utf-8')
    result = compiler.D3DCompile(encoded, len(encoded), str(current_source).encode(), macros, include,
                                 entry.encode(), target.encode(), (1 << 12) | (1 << 2), 0, c.byref(code), c.byref(errors))
    bytecode = blob_bytes(code)
    diagnostics = blob_bytes(errors).decode('utf-8', errors='replace')
    return dict(entry=entry, target=target, defines=defines, passed=result >= 0, bytecode_bytes=len(bytecode), diagnostics=diagnostics)


def main():
    global current_source
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--report', default='Docs/ClipmapEvidence/gpu-hlsl-background.json')
    args = parser.parse_args()
    text = SHADER.read_text(encoding='utf-8')
    common = re.search(r'HLSLINCLUDE(.*?)ENDHLSL', text, re.S).group(1)
    passes = re.findall(r'HLSLPROGRAM(.*?)ENDHLSL', text, re.S)
    results = []
    for index, program in enumerate(passes):
        entries = re.findall(r'#pragma (vertex|fragment) (\w+)', program)
        source = common + re.sub(r'^\s*#pragma[^\n]*', '', program, flags=re.M)
        for stage, entry in entries:
            variants = [dict()]
            if index == 0:
                variants.append({'_MAIN_LIGHT_SHADOWS_CASCADE': '1', '_ADDITIONAL_LIGHTS': '1', '_ADDITIONAL_LIGHT_SHADOWS': '1', '_SHADOWS_SOFT': '1', '_SCREEN_SPACE_OCCLUSION': '1', 'FOG_EXP2': '1'})
            if index == 1: variants.append({'_CASTING_PUNCTUAL_LIGHT_SHADOW': '1'})
            if index == 3: variants.append({'_GBUFFER_NORMALS_OCT': '1'})
            for variant in variants:
                variant['SHADER_STAGE_' + stage.upper()] = '1'
                outcome = compile_one(source, entry, 'vs_5_0' if stage == 'vertex' else 'ps_5_0', variant)
                results.append(outcome)
                print(('PASS' if outcome['passed'] else 'FAIL'), entry, variant)
                if not outcome['passed']: print(outcome['diagnostics'])
    current_source = COMPUTE
    compute_source = re.sub(r'^\s*#pragma[^\n]*', '', COMPUTE.read_text(), flags=re.M)
    for entry in re.findall(r'#pragma kernel (\w+)', COMPUTE.read_text()):
        result = compile_one(compute_source, entry, 'cs_5_0', {'SHADER_STAGE_COMPUTE': '1'})
        results.append(result)
        print(('PASS' if result['passed'] else 'FAIL'), entry)
        if not result['passed']: print(result['diagnostics'])
    report = ROOT / args.report
    report.parent.mkdir(parents=True, exist_ok=True)
    report.write_text(json.dumps(dict(status='PASS' if all(r['passed'] for r in results) else 'FAIL',
        scope=__doc__.strip(), generated_utc=datetime.datetime.now(datetime.timezone.utc).isoformat(),
        source_sha256={str(p.relative_to(ROOT)).replace('\\', '/'): hashlib.sha256(p.read_bytes()).hexdigest() for p in [SHADER, COMPUTE]},
        includes_sha256=INCLUDES, include_errors=OPEN_ERRORS, variants=results), indent=2) + '\n')
    raise SystemExit(0 if all(r['passed'] for r in results) else 1)


if __name__ == '__main__': main()
