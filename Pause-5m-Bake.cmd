@echo off
rem Pause the 5 m detail bake. Finished tiles are kept; only the tile in progress is redone on resume.
cd /d "%~dp0"
echo Stopping the 5 m detail bake...
powershell -NoProfile -Command "Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like '*run_fine_bake_loop.cmd*' -or $_.CommandLine -like '*prebake_fine_owners.py*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }"
if exist ".terrain-cache\prebake-fine\bake.lock" del ".terrain-cache\prebake-fine\bake.lock"
echo Paused. Run Resume-5m-Bake.cmd to continue where it left off.
pause
