"""Create a fresh frozen native-check project; never overwrite another run."""
from pathlib import Path
import hashlib,json,shutil
ROOT=Path(__file__).resolve().parents[2]
def main():
    target=ROOT/'.terrain-cache/voxel-native-project'
    target.mkdir(exist_ok=False)
    hashes={}
    for folder in ['Assets','Packages','ProjectSettings']:
        shutil.copytree(ROOT/folder,target/folder)
        for p in (target/folder).rglob('*'):
            if p.is_file():hashes[p.relative_to(target).as_posix()]=hashlib.sha256(p.read_bytes()).hexdigest()
    shutil.copy2(Path(__file__).with_name('NativeCheck.cs'),target/'Assets/AshenVoxel/Exploration/Editor/VoxelNativeCheck.cs')
    (target/'source-hashes.json').write_text(json.dumps(hashes,indent=2)+'\n',encoding='utf-8')
    print(target)
if __name__=='__main__':main()
