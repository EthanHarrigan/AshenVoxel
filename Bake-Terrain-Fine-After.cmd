@echo off
rem Waits for the 160/80 m and 40 m far-terrain bakes (Bake-Terrain-80m.cmd) to finish,
rem then pre-bakes full-detail 5 m terrain around spawn (CPU), then runs the 5 m service for Play.
rem Safe to close and rerun: everything resumes. Leave Bake-Terrain-80m.cmd running too.
cd /d "%~dp0"
title Ashen Voxel - 5m detail bake (waits for 80m/40m)
set PYTHONUTF8=1
set PY=.terrain-cache\python\Scripts\python.exe
if not exist "%PY%" ( echo CPU Python environment not found at %PY% & pause & exit /b 1 )
:wait
powershell -NoProfile -Command "try{$a=(Get-Content '.terrain-cache\prebake-overview\progress.json' -Raw|ConvertFrom-Json).status;$b=(Get-Content '.terrain-cache\prebake-overview\progress40.json' -Raw|ConvertFrom-Json).status;if($a -eq 'PASS' -and $b -eq 'PASS'){exit 0}else{Write-Host ((Get-Date -Format T)+'  waiting: 80m='+$a+'  40m='+$b);exit 1}}catch{Write-Host ((Get-Date -Format T)+'  waiting for the 80m/40m bake to report');exit 1}"
if errorlevel 1 ( timeout /t 120 /nobreak >nul & goto wait )
echo 80 m and 40 m are done. Starting the 5 m detail bake.
:bake
"%PY%" -X utf8 Tools\ExplorationBake\prebake_fine_owners.py
if errorlevel 1 (
  echo 5 m bake stopped ^(see .terrain-cache\prebake-fine\prebake-fine.log^). Retrying in 2 minutes...
  timeout /t 120 /nobreak >nul
  goto bake
)
echo.
echo 5 m bake complete. Starting the 5 m detail service on port 8787 for Play. Leave this window open while playing.
:serve
"%PY%" -X utf8 Tools\ExplorationBake\serve_exploration.py --job .terrain-cache\world-job.json --owner-cache-dir .terrain-cache\exploration-bake\owners --port 8787 --world-spacing-m 5 --world-width-m 299520 --world-depth-m 149760
echo Service stopped. Restarting in 30 seconds...
timeout /t 30 /nobreak >nul
goto serve
