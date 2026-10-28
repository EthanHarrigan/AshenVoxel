#!/usr/bin/env python3
"""Static Unity project integrity checks; this is not a Unity import/compiler."""
from pathlib import Path
import json,re,sys
root=Path(__file__).resolve().parents[1]
errors=[]
def check(condition,message):
 if not condition: errors.append(message)
guid_paths={}
for p in (root/'Assets').rglob('*'):
 if p.name.endswith('.meta'):continue
 meta=p.with_name(p.name+'.meta')
 check(meta.exists(),f'Missing meta: {p.relative_to(root)}')
 if not meta.exists():continue
 matches=re.findall(r'^guid: ([0-9a-f]{32})$',meta.read_text(),re.M)
 check(len(matches)==1,f'Invalid GUID: {meta}')
 if matches:
  g=matches[0];check(g not in guid_paths,f'Duplicate GUID: {p} and {guid_paths.get(g)}');guid_paths[g]=p
manifest=json.loads((root/'Packages/manifest.json').read_text())
check(manifest['dependencies'].get('com.unity.render-pipelines.universal')=='17.3.0','URP version changed; review baseline')
check(re.search(r'^m_EditorVersion: 6000\.3\.27f1$',(root/'ProjectSettings/ProjectVersion.txt').read_text(),re.M) is not None,'Editor baseline changed; review compatibility')
check('activeInputHandler: 1' in (root/'ProjectSettings/ProjectSettings.asset').read_text(),'New Input System not selected')
scene=(root/'Assets/Scenes/ForestArena.unity').read_text()
script=re.search(r'm_Script:.*guid: ([0-9a-f]{32})',scene)
check(script and guid_paths.get(script[1])==root/'Assets/AshenVoxel/Runtime/DemoBootstrap.cs','Scene bootstrap reference broken')
workshop=(root/'Assets/Scenes/TreeWorkshop.unity').read_text()
workshop_script=re.search(r'm_Script:.*guid: ([0-9a-f]{32})',workshop)
check(workshop_script and guid_paths.get(workshop_script[1])==root/'Assets/AshenVoxel/Authoring/TreeShowcase.cs','Workshop script reference broken')
explorer_path=root/'Assets/Scenes/WorldExplorer.unity'
if explorer_path.exists():
 explorer=explorer_path.read_text()
 explorer_script=re.search(r'm_Script:.*guid: ([0-9a-f]{32})',explorer)
 check(explorer_script and guid_paths.get(explorer_script[1])==root/'Assets/AshenVoxel/WorldScaleRuntime/WorldExplorer.cs','World explorer script reference broken')
groundcover=(root/'Assets/Scenes/GroundCoverWorkshop.unity').read_text()
groundcover_script=re.search(r'm_Script:.*guid: ([0-9a-f]{32})',groundcover)
check(groundcover_script and guid_paths.get(groundcover_script[1])==root/'Assets/AshenVoxel/WorldScaleRuntime/GroundCoverShowcase.cs','Ground-cover workshop script reference broken')
for species in ['Birch','Oak','Redwood','Pine']:
 asset=root/f'Assets/AshenVoxel/Settings/Trees/{species}.asset'
 check(asset.exists(),f'Missing bundled species: {species}')
 if not asset.exists():continue
 profile_script=re.search(r'm_Script:.*guid: ([0-9a-f]{32})',asset.read_text())
 check(profile_script and guid_paths.get(profile_script[1])==root/'Assets/AshenVoxel/Authoring/TreeProfile.cs',f'Profile script reference broken: {species}')
 meta=asset.with_name(asset.name+'.meta')
 if meta.exists():
  profile_guid=re.search(r'^guid: ([0-9a-f]{32})$',meta.read_text(),re.M)
  check(profile_guid and profile_guid[1] in workshop,f'Workshop does not reference bundled species: {species}')
for material_path,shader_path in [
 ('Assets/AshenVoxel/Rendering/Resources/VoxelSurface.mat','Assets/AshenVoxel/Rendering/WorldSurface.shader'),
 ('Assets/AshenVoxel/WorldScaleRuntime/Resources/GroundSurface.mat','Assets/AshenVoxel/WorldScaleRuntime/GroundSurface.shader'),
 ('Assets/AshenVoxel/WorldScaleRuntime/Resources/VisualWater.mat','Assets/AshenVoxel/WorldScaleRuntime/VisualWater.shader')]:
 material=(root/material_path).read_text()
 shader=re.search(r'm_Shader:.*guid: ([0-9a-f]{32})',material)
 check(shader and guid_paths.get(shader[1])==root/shader_path,f'Shader reference broken: {material_path}')
assemblies={}
for p in (root/'Assets').rglob('*.asmdef'):
 data=json.loads(p.read_text());check(data['name'] not in assemblies,'Duplicate assembly name');assemblies[data['name']]=data
for name,data in assemblies.items():
 for ref in data.get('references',[]):
  check(ref in assemblies or ref.startswith('Unity.'),f'Unresolved project assembly: {name}->{ref}')
check(assemblies['AshenVoxel.Core'].get('noEngineReferences'), 'Core has engine dependency')
if 'AshenVoxel.WorldScaleData' in assemblies:
 check(assemblies['AshenVoxel.WorldScaleData'].get('noEngineReferences'), 'World-scale data has engine dependency')
bootstrap=(root/'Assets/AshenVoxel/Runtime/DemoBootstrap.cs').read_text()
check('public const float VoxelSize = .1f;' in bootstrap,'10cm runtime contract changed')
for md in [root/'README.md',*(root/'Docs').glob('*.md')]:
 for target in re.findall(r'\]\(([^)#]+)(?:#[^)]*)?\)',md.read_text()):
  if '://' in target or target.startswith('#'):continue
  check((md.parent/target).exists(),f'Broken documentation link {md.relative_to(root)}: {target}')
if errors:
 print('\n'.join('FAIL '+e for e in errors));sys.exit(1)
print(f'PASS project integrity: {len(guid_paths)} unique assets, {len(assemblies)} assemblies, scene/material references, 10cm contract and local documentation links')
