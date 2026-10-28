@echo off
rem Continue the 5 m detail bake: already-baked tiles are skipped (cache hits).
cd /d "%~dp0"
if exist ".terrain-cache\prebake-fine\bake.lock" del ".terrain-cache\prebake-fine\bake.lock"
start "Ashen 5m bake" "%~dp0Tools\ExplorationBake\run_fine_bake_loop.cmd"
echo Resumed in its own window.
