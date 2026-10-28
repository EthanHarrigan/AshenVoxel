@echo off
rem 5 m detail pre-bake on all CPU cores (several model processes), restarted on failure.
cd /d "%~dp0..\.."
title Ashen Voxel - 5m detail bake (CPU)
set PYTHONUTF8=1
:bake
.terrain-cache\python\Scripts\python.exe -X utf8 Tools\ExplorationBake\prebake_fine_owners.py
if errorlevel 1 (
  echo 5 m bake stopped ^(see .terrain-cache\prebake-fine\prebake-fine.log^). Retrying in 2 minutes...
  timeout /t 120 /nobreak >nul
  goto bake
)
echo 5 m bake finished. This window can be closed.
