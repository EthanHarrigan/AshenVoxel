@echo off
rem Downloads what is already in the GitHub repo (changes nothing in your files).
cd /d "%~dp0"
git fetch origin > ".terrain-cache\git-fetch.log" 2>&1
type ".terrain-cache\git-fetch.log"
echo.
echo Done - tell Claude "fetched".
pause
