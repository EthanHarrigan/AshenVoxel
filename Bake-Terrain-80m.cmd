@echo off
rem Pre-bakes the 160 m / 80 m learned far terrain (land from spawn outward, ocean kept flat),
rem then starts the overview service the game uses while playing. Safe to close and rerun: it resumes.
cd /d "%~dp0"
title Ashen Voxel - terrain 80m pre-bake
set PYTHONUTF8=1
set PY=.terrain-cache\python-gpu\Scripts\python.exe
if not exist "%PY%" ( echo GPU Python environment not found at %PY% & pause & exit /b 1 )
:bake
"%PY%" -X utf8 Tools\ExplorationBake\prebake_overview_cache.py
if errorlevel 1 (
  echo Pre-bake stopped ^(see .terrain-cache\prebake-overview\prebake.log^). Retrying in 2 minutes...
  timeout /t 120 /nobreak >nul
  goto bake
)
echo.
echo Pre-bake complete. Starting the far-terrain service on port 8788 for Play. Leave this window open while playing.
:serve
"%PY%" -X utf8 Tools\ExplorationBake\serve_overview.py --job .terrain-cache\coarse320-study\bake320-v1\job.json --cache-dir .terrain-cache\regional-overview-cache
echo Service stopped. Restarting in 30 seconds...
timeout /t 30 /nobreak >nul
goto serve
