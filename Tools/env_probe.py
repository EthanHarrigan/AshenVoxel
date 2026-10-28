"""Report the local toolchain available for project checks (no changes made)."""
import sys,shutil,subprocess,pathlib,platform
print('python',sys.version);print('platform',platform.platform())
d=shutil.which('dotnet');print('dotnet',d)
if d:print(subprocess.run([d,'--list-sdks'],capture_output=True,text=True).stdout)
e=pathlib.Path('C:/Program Files/Unity/Hub/Editor/6000.3.27f1/Editor');print('unity editor',e.exists())
print('ScriptAssemblies',(pathlib.Path(__file__).resolve().parents[1]/'Library/ScriptAssemblies').exists())
try:
    import PIL;print('Pillow',PIL.__version__)
except Exception as x:print('Pillow missing',x)
try:
    import numpy;print('numpy',numpy.__version__)
except Exception as x:print('numpy missing',x)
