#!/usr/bin/env python3
"""Pair an executed neural bake's climate with its exact AVWF height atlas.

Legacy raw seasonality is centi-C despite its historical channel name. This
converter explicitly decodes that version once and emits physical-only AVCL0001.
"""
from pathlib import Path
import argparse,hashlib,json,os,struct,sys,zlib
import numpy as np
import bridge
sys.path.insert(0,str(bridge.ROOT/'Tools/WorldScaleImport'))
from convert_world import read_avwf
MAGIC=b'AVCL0001'
def sha(path):return hashlib.sha256(Path(path).read_bytes()).hexdigest()
def convert(height_path,climate_path,report_path,output):
    height_path=Path(height_path);climate_path=Path(climate_path);report_path=Path(report_path);output=Path(output)
    atlas=read_avwf(height_path);report=json.loads(report_path.read_text())
    if report.get('avwf_sha256')!=sha(height_path) or report.get('climate_sha256')!=sha(climate_path):raise ValueError('Bake report does not match exact input file hashes')
    meta=report['region_metadata'];spec=report['job']['spec'];source=json.loads(atlas['source_identity'])
    if not meta.get('backend','').startswith('upstream-pretrained') or source.get('provider')!='InfiniteDiffusion':raise ValueError('Expected actual pretrained bake provenance')
    if meta['world_id']!=report['job']['world_id'] or source.get('world_id')!=meta['world_id']:raise ValueError('Height and climate belong to different worlds')
    if meta['window']['cols']!=atlas['columns'] or meta['window']['rows']!=atlas['rows']:raise ValueError('Height and climate dimensions differ')
    if meta['origin_x_m']!=atlas['origin_x'] or meta['origin_z_m']!=atlas['origin_z']:raise ValueError('Height and climate origin differs')
    spacing=float(meta['spacing_m'])
    if abs(atlas['extent_x']-(atlas['columns']-1)*spacing)>1e-7 or abs(atlas['extent_z']-(atlas['rows']-1)*spacing)>1e-7:raise ValueError('Height and climate spacing differs')
    encoding=bridge.climate_encoding(meta)
    if encoding!=bridge.climate_encoding(spec):raise ValueError('Job and payload climate encoding differ')
    with np.load(climate_path,allow_pickle=False) as archive:
        if archive.files!=['climate']:raise ValueError('Expected a single explicit climate array')
        physical=bridge.physical_climate(archive['climate'],encoding)
    if physical.shape!=(5,atlas['rows'],atlas['columns']):raise ValueError('Expected five matching physical climate planes')
    bounds=[(-150,100),(0,150),(0,100000),(0,10000),(-.1,.1)]
    for channel,(lo,hi) in enumerate(bounds):
        if np.any(physical[channel]<lo) or np.any(physical[channel]>hi):raise ValueError('Physical climate channel out of range: '+str(channel))
    identity={'schema':'ashen-baked-physical-climate-v1','world_id':meta['world_id'],
        'model_revision':spec['model_revision'],'input_climate_encoding':encoding,
        'climate_value_units':bridge.climate_units(bridge.CLIMATE_PHYSICAL),
        'source_climate_sha256':sha(climate_path),'temperature_is_surface_adjusted':True}
    encoded=bridge.canonical(identity)
    if len(encoded)>4096:raise ValueError('Climate identity exceeds bounded size')
    raw=MAGIC+struct.pack('<ii4d',atlas['columns'],atlas['rows'],atlas['origin_x'],atlas['origin_z'],atlas['extent_x'],atlas['extent_z'])
    raw+=bytes.fromhex(sha(height_path))+struct.pack('<H',len(encoded))+encoded+np.asarray(physical,dtype='<f4').tobytes()
    raw+=struct.pack('<I',zlib.crc32(raw)&0xffffffff)
    output.parent.mkdir(parents=True,exist_ok=True);temporary=output.with_suffix(output.suffix+'.tmp');temporary.write_bytes(raw);os.replace(temporary,output)
    result={'output_sha256':sha(output),'height_sha256':sha(height_path),'identity':identity,
        'channel_ranges':[{'min':float(v.min()),'max':float(v.max())} for v in physical],
        'note':'Exact paired baked climate. Legacy centi-C seasonality is normalized once; no new inference or terrain geometry.'}
    output.with_suffix(output.suffix+'.json').write_text(json.dumps(result,indent=2)+'\n')
    return result
def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('height',type=Path);p.add_argument('climate',type=Path);p.add_argument('report',type=Path);p.add_argument('output',type=Path);a=p.parse_args()
    r=convert(a.height,a.climate,a.report,a.output);print('Paired physical climate archive:',a.output,'seasonality C:',r['channel_ranges'][1])
if __name__=='__main__':main()
