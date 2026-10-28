@echo off
rem Double-click once and leave the window open: lets Claude compile and test this project on your PC.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Tools\check_watcher.ps1"
pause
