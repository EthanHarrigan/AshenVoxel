@echo off
rem Serves pre-baked 5 m detail (port 8787) for Play. Baked tiles load instantly; new places are generated on demand.
rem The far-terrain service (8788) comes from Bake-Terrain-80m.cmd; leave that window open too.
cd /d "%~dp0"
title Ashen Voxel - 5m detail service (8787)
set PYTHONUTF8=1
set PY=.terrain-cache\python\Scripts\python.exe
:serve
"%PY%" -X utf8 Tools\ExplorationBake\serve_exploration.py --job .terrain-cache\world-job.json --owner-cache-dir .terrain-cache\exploration-bake\owners --port 8787 --world-spacing-m 5 --world-width-m 299520 --world-depth-m 149760
echo Service stopped. Restarting in 30 seconds...
timeout /t 30 /nobreak >nul
goto serve
