from pathlib import Path
import shutil,json
from prepare import ROOT,HERE,CACHE,FROZEN,freeze
freeze();p=CACHE/'native';p.mkdir(exist_ok=False)
for name in ['Core','WorldScaleData']:
    dest=p/'Assets'/name;dest.mkdir(parents=True)
    for source in (FROZEN/'Assets/AshenVoxel'/name).rglob('*.cs'):
        target=dest/source.relative_to(FROZEN/'Assets/AshenVoxel'/name);target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(source,target)
(p/'Assets/Editor').mkdir();shutil.copy2(HERE/'NativeLodProbe.cs',p/'Assets/Editor/NativeLodProbe.cs');shutil.copy2(HERE/'LodMeasurements.cs',p/'Assets/LodMeasurements.cs')
(p/'Packages').mkdir();(p/'Packages/manifest.json').write_text(json.dumps({'dependencies':{'com.unity.modules.jsonserialize':'1.0.0'}}))
(p/'ProjectSettings').mkdir();shutil.copy2(ROOT/'ProjectSettings/ProjectVersion.txt',p/'ProjectSettings/ProjectVersion.txt')
print(p)
