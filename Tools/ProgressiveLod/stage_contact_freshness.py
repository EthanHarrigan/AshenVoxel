"""Stage contact source-revision retention against installed capacity-lod-1."""
from pathlib import Path
import datetime,difflib,hashlib,json
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'.terrain-cache/startup-throughput/contact-freshness-candidate'
OUT.mkdir(parents=True,exist_ok=False)
EXPECTED={'WorldExplorer.cs':'0d7d76771373134623fa9c13cc2e00f2830763c7658cfb3cf05701d71a8e0834',
          'WorldExplorerVoxels.cs':'b9e7964405642f84fd276644c2ecf726dac536ab756d2ffc1d92a2e31be6e20d'}
CHANGES={
 'WorldExplorer.cs': [('            public int TriangleCount;',
                      '            public int TriangleCount;\n            public int SourceRevision;')],
 'WorldExplorerVoxels.cs': [
  ('            bool sourceChanged=contactRevision!=revision;\n',''),
  ('                if(sourceChanged||!contactSurfaces.ContainsKey(key))',
   '''                // A deferred replacement must survive overlap/radius replans.
                // Keeping a fallback mesh does not make its source current.
                if(!contactSurfaces.TryGetValue(key,out Surface existing)||existing.SourceRevision!=revision)'''),
  ('            ReleaseContact(work.Tile.Key);contactSurfaces.Add(work.Tile.Key,built);',
   '            built.SourceRevision=work.Revision;\n            ReleaseContact(work.Tile.Key);contactSurfaces.Add(work.Tile.Key,built);')]
}
manifest={'id':'contact-freshness-1','utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'status':'ISOLATED CANDIDATE; NOT INSTALLED','files':[]};patch=[]
for name,expected in EXPECTED.items():
 rel=Path('Assets/AshenVoxel/WorldScaleRuntime')/name;raw=(ROOT/rel).read_bytes();assert hashlib.sha256(raw).hexdigest()==expected,'Concurrent source change: '+name
 before=raw.decode().replace('\r\n','\n');after=before
 for old,new in CHANGES[name]:
  assert after.count(old)==1,name+': expected unique preimage';after=after.replace(old,new)
 target=OUT/rel;target.parent.mkdir(parents=True,exist_ok=True);target.write_text(after)
 backup=OUT/'baseline'/rel;backup.parent.mkdir(parents=True,exist_ok=True);backup.write_bytes(raw);assert backup.read_bytes()==raw;backup.read_text()
 manifest['files'].append({'path':rel.as_posix(),'before':expected,'sha256':hashlib.sha256(target.read_bytes()).hexdigest()})
 patch.extend(difflib.unified_diff(before.splitlines(True),after.splitlines(True),fromfile='a/'+rel.as_posix(),tofile='b/'+rel.as_posix()))
(OUT/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n');(OUT/'candidate.diff').write_text(''.join(patch));print(OUT)
