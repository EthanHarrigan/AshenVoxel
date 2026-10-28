#!/usr/bin/env python3
"""Meaningful importer/AVWF checks on a declared synthetic Azgaar schema fixture."""
from pathlib import Path
import copy,json,struct,tempfile,zlib
import numpy as np
from convert_world import convert_azgaar,parse_azgaar,write_avwf,read_avwf,height_meters,convert_tiff

HERE=Path(__file__).resolve().parent
checks=0
def expect(value,message):
    global checks
    if not value:raise AssertionError(message)
    checks+=1;print('PASS '+message)
def reject(fn,message):
    try:fn()
    except (ValueError,UnicodeError):expect(True,message);return
    raise AssertionError('Accepted invalid data: '+message)

def main():
    fixture=HERE/'fixtures/azgaar-full.synthetic.json'
    data=json.loads(fixture.read_text())
    with tempfile.TemporaryDirectory() as tmp:
        folder=Path(tmp);atlas=folder/'full.avwf'
        report=convert_azgaar(fixture,atlas,cols=5,width_km=20)
        parsed=read_avwf(atlas)
        expect((parsed['columns'],parsed['rows'])==(5,3),'derived raster preserves2:1 canvas aspect')
        expect((parsed['extent_x'],parsed['extent_z'],parsed['origin_x'],parsed['origin_z'])==(20000,10000,-10000,-5000),'physical width and centered origin are explicit meters')
        convert_azgaar(fixture,atlas,cols=5,rows=5,width_km=20)
        parsed=read_avwf(atlas)
        expect(parsed['heights'][1,3]==4 and parsed['heights'][3,1]==1764 and parsed['heights'][3,3]==6724,'native Azgaar height equation gives4/1764/6724 meter land samples')
        expect(parsed['heights'][1,1]==-50 and not parsed['land'][1,1],'water conversion preserves negative depth and water classification')
        expect(parsed['biomes'][1,3]==6 and parsed['biomes'][3,1]==9 and parsed['biomes'][3,3]==11,'categorical source biomes remain independent of elevation')
        expect(parsed['biomes'][2,2]==0 and not parsed['land'][2,2],'shared polygon boundaries deterministically belong to lowest cell ID')
        first=atlas.read_bytes();convert_azgaar(fixture,atlas,cols=5,rows=5,width_km=20)
        expect(atlas.read_bytes()==first,'same source and settings produce identical atlas bytes')
        identity=json.loads(parsed['source_identity'])
        expect(len(identity['source_sha256'])==64 and identity['mapping']=='azgaar-control-atlas-v2','source identity fingerprints source bytes and mapping version')
        convert_azgaar(fixture,atlas,cols=5,rows=5,width_km=40,depth_km=10,peak_meters=5000)
        changed=read_avwf(atlas)
        expect(changed['extent_x']==40000 and changed['extent_z']==10000 and changed['heights'][3,3]==5000,'explicit depth and peak controls alter separate horizontal and vertical scales')
        expect(changed['heights'][1,3]==0 and changed['land'][1,3],'peak remap explicitly places raw20 at sea level without changing land identity')
        expect(changed['source_identity']!=parsed['source_identity'],'mapping changes invalidate atlas source identity')
        convert_azgaar(fixture,atlas,cols=9,rows=9,width_km=20,landmass_scale=.5)
        small=read_avwf(atlas)
        expect(not small['land'][0].any() and not small['land'][:,-1].any() and np.all(small['heights'][0]==-990),'shrinking landmass introduces ocean border within unchanged physical extent')
        pack=copy.deepcopy(data);pack.pop('pack');pack['cells']=data['pack'];pack.pop('settings')
        source=folder/'pack.json';source.write_text(json.dumps(pack))
        convert_azgaar(source,atlas,cols=5,rows=5,width_km=20)
        packed=read_avwf(atlas)
        expect(np.array_equal(packed['heights'],parsed['heights']) and np.array_equal(packed['biomes'],parsed['biomes']),'Pack Cells wrapper yields same data as Full wrapper')
        expect(json.loads(packed['source_identity'])['height_exponent_defaulted'],'Pack Cells missing height settings records default exponent explicitly')
        convert_azgaar(fixture,atlas,cols=5,rows=5,width_km=20,sampling='nearest')
        nearest=read_avwf(atlas)
        expect(np.array_equal(nearest['heights'],parsed['heights']),'declared nearest-center sampling agrees on this exact rectangular fixture')
        reject(lambda:convert_azgaar(fixture,atlas,cols=1),'rejects invalid atlas resolution')
        reject(lambda:convert_azgaar(fixture,atlas,cols=5000,rows=5000),'rejects sample budget overflow before allocation')
        reject(lambda:convert_azgaar(fixture,atlas,width_km=float('nan')),'rejects nonfinite physical scale')
        reject(lambda:convert_azgaar(fixture,atlas,landmass_scale=0),'rejects zero landmass scale')
        def malformed(change):
            content=copy.deepcopy(data);change(content);source.write_text(json.dumps(content));return parse_azgaar(source)
        reject(lambda:malformed(lambda d:d['pack']['cells'][0].update(h=float('nan'))),'rejects nonfinite JSON heights')
        reject(lambda:malformed(lambda d:d['pack']['cells'][0].update(h=101)),'rejects raw height outside0..100')
        reject(lambda:malformed(lambda d:d['pack']['cells'][0].update(biome=256)),'rejects biome overflow')
        reject(lambda:malformed(lambda d:d['pack']['cells'][0].update(biome=5)),'rejects missing biome table reference')
        reject(lambda:malformed(lambda d:d['pack']['cells'][1].update(i=0)),'rejects duplicate source cell IDs')
        reject(lambda:malformed(lambda d:d['pack']['cells'][0].update(v=[0,1,999])),'rejects missing polygon vertex references')
        reject(lambda:malformed(lambda d:d['pack']['cells'][0].update(v=[0,0,1])),'rejects repeated polygon vertex IDs')
        broken=copy.deepcopy(data);broken['pack']['cells'].pop();source.write_text(json.dumps(broken))
        reject(lambda:convert_azgaar(source,atlas,cols=5,rows=5),'rejects uncovered polygon atlas samples')
        full_grid=copy.deepcopy(data);full_grid['grid']={'cells':copy.deepcopy(data['pack']['cells']),'vertices':copy.deepcopy(data['pack']['vertices'])}
        full_grid['pack']['cells']=[c for c in full_grid['pack']['cells'] if c['i']!=0];source.write_text(json.dumps(full_grid))
        restored=convert_azgaar(source,atlas,cols=5,rows=5)
        restored_atlas=read_avwf(atlas)
        expect(restored['coverage']['original_grid_water_samples']>0 and restored_atlas['heights'][1,1]==-50 and not restored_atlas['land'][1,1],'original-grid water restores deep ocean intentionally absent from packed polygons')
        reject(lambda:convert_azgaar(source,atlas,cols=5,rows=5,uncovered='error'),'explicit strict coverage still rejects missing packed polygons')
        missing_land=copy.deepcopy(full_grid);missing_land['pack']['cells']=[c for c in missing_land['pack']['cells'] if c['i']!=3];source.write_text(json.dumps(missing_land))
        reject(lambda:convert_azgaar(source,atlas,cols=5,rows=5),'grid-water policy never fills absent original-grid land with a fabricated biome')
        grid_edge=copy.deepcopy(full_grid)
        # Shorten only the water polygon; its original center still classifies
        # the missing source-water samples by Grid.findCell semantics.
        grid_edge['grid']['vertices'][1]['p']=[1,0];grid_edge['grid']['vertices'][4]['p']=[1,1];grid_edge['grid']['vertices'][3]['p']=[0,1]
        source.write_text(json.dumps(grid_edge));edge_report=convert_azgaar(source,atlas,cols=5,rows=5)
        expect(edge_report['coverage']['original_grid_nearest_water_samples']>0 and not read_avwf(atlas)['land'][1,1],'omitted grid boundary facets use explicit original-grid water classification')
        annotated=copy.deepcopy(data);annotated['terrainDiffusion']={'formatVersion':2,'summitAnchors':[{'u':.75,'v':.75,'boostMetres':3000,'radiusNativeAt750':100}]};source.write_text(json.dumps(annotated))
        metadata_only=convert_azgaar(source,atlas,cols=5,rows=5,width_km=20)
        expect(not metadata_only['summits']['applied'] and metadata_only['source_terrain_diffusion']==annotated['terrainDiffusion'] and read_avwf(atlas)['heights'][3,3]==6724,'authored summit metadata is preserved without silently changing default terrain')
        uplifted=convert_azgaar(source,atlas,cols=5,rows=5,width_km=20,apply_summits=True)
        expect(read_avwf(atlas)['heights'][3,3]==9724 and uplifted['summits']['applied'],'explicit Unity summit profile applies physical-meter authored boost at its center')
        expect(abs(uplifted['summits']['anchors'][0]['radius_m']-80)<1e-8,'reference summit radius converts native30-meter pixels and physical map width into meters')
        expect(metadata_only['identity']!=uplifted['identity'],'summit application/profile is part of generation identity')
        annotated['terrainDiffusion']['summitAnchors'].append(copy.deepcopy(annotated['terrainDiffusion']['summitAnchors'][0]));source.write_text(json.dumps(annotated))
        convert_azgaar(source,atlas,cols=5,rows=5,width_km=20,apply_summits=True)
        expect(read_avwf(atlas)['heights'][3,3]==9724,'overlapping summit envelopes use maximum uplift rather than additive double boosts')
        annotated['terrainDiffusion']['summitAnchors']=[{'u':.25,'v':.25,'boostMetres':10000,'radiusNativeAt750':100}];source.write_text(json.dumps(annotated))
        convert_azgaar(source,atlas,cols=5,rows=5,width_km=20,apply_summits=True)
        expect(read_avwf(atlas)['heights'][1,1]==-50 and not read_avwf(atlas)['land'][1,1],'summit anchors cannot convert ocean into a mountain or erase land masks')
        annotated['terrainDiffusion']['formatVersion']=99;source.write_text(json.dumps(annotated))
        reject(lambda:convert_azgaar(source,atlas,cols=5,rows=5,apply_summits=True),'rejects applying unknown summit extension versions')
        source=folder/'native.map';source.write_text('1.154.0|native save\r\n{}')
        reject(lambda:parse_azgaar(source),'native .map produces conversion guidance instead of mismatching grid and packed biomes')
        h=np.array([[1,2],[3,4]],dtype=np.float32);b=np.array([[0,6],[9,11]],dtype=np.uint8);l=np.array([[0,1],[1,1]],dtype=bool)
        write_avwf(atlas,h,b,l,-1,2,30,40,'UTF-8 identity: forêt')
        result=read_avwf(atlas)
        expect(np.array_equal(result['heights'],h) and np.array_equal(result['biomes'],b) and np.array_equal(result['land'],l),'AVWF row-major channels round trip without conflation')
        expect(result['source_identity']=='UTF-8 identity: forêt','UTF-8 source identity round trips')
        reject(lambda:write_avwf(atlas,h,b,l,0,0,1,1,''),'rejects blank identity')
        reject(lambda:write_avwf(atlas,h,b,l,0,0,1,1,'é'*3000),'rejects identity UTF-8 byte overflow')
        reject(lambda:write_avwf(atlas,np.array([[np.inf,0],[0,0]]),b,l,0,0,1,1,'x'),'rejects infinite output heights')
        reject(lambda:write_avwf(atlas,h,b.astype(float),l,0,0,1,1,'x'),'rejects interpolated/floating biome IDs')
        raw=atlas.read_bytes();corrupt=bytearray(raw);corrupt[-5]^=1;atlas.write_bytes(corrupt)
        reject(lambda:read_avwf(atlas),'CRC detects corrupted payload')
        atlas.write_bytes(raw[:-1]);reject(lambda:read_avwf(atlas),'rejects truncated payload')
        altered=bytearray(raw);altered[50+len('UTF-8 identity: forêt'.encode())+5]=3;struct.pack_into('<I',altered,len(altered)-4,zlib.crc32(altered[:-4])&0xffffffff);atlas.write_bytes(altered)
        reject(lambda:read_avwf(atlas),'rejects invalid land flag even with repaired checksum')
        reject(lambda:convert_tiff(fixture,atlas,elevation_units=None,origin_x=0,origin_z=0,extent_x=1,extent_z=1),'TIFF requires explicit meter-unit declaration before optional dependency use')
    output=HERE/'fixtures/azgaar-synthetic.avwf'
    convert_azgaar(fixture,output,cols=5,rows=5,width_km=20,peak_meters=5000)
    print(f'PASS {checks} importer checks; wrote C# interoperability fixture: {output}')

if __name__=='__main__':main()
