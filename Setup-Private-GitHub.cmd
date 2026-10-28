@echo off
rem Double-click: puts this folder under git and pushes it to a PRIVATE GitHub repo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Tools\setup_private_github.ps1"
pause
