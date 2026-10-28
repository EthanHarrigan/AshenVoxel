# Puts the AshenVoxel project under git and pushes it to a PRIVATE GitHub repository.
# Safe to re-run: it skips steps that are already done. It never deletes files.
# Library/Temp/Logs/UserSettings/.terrain-cache stay out of git (see .gitignore).
$ErrorActionPreference = 'Continue'   # native git/gh write progress to stderr
function Check($what){ if($LASTEXITCODE -ne 0){ Write-Host "FAILED: $what (exit $LASTEXITCODE) - see git-setup.log" -ForegroundColor Red; Stop-Transcript | Out-Null; exit 1 } }
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$repoName = 'AshenVoxel'
Start-Transcript -Path (Join-Path $root 'git-setup.log') -Force | Out-Null
Write-Host "Project: $root"

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    Write-Host "git is not installed. Install it with:  winget install --id Git.Git -e   then run this again." -ForegroundColor Yellow
    exit 1
}
if (-not (git config --global user.name)) {
    $n = Read-Host 'Your name for commits'; git config --global user.name $n
}
if (-not (git config --global user.email)) {
    $e = Read-Host 'Your email for commits (GitHub email or the noreply address)'; git config --global user.email $e
}

if (-not (Test-Path '.git')) { git init -b main | Out-Null; Check 'git init'; Write-Host 'Initialized git repository.' }
git config core.longpaths true      # Windows 260-char path limit
git config core.autocrlf false      # .gitattributes controls line endings
git --version
# Embedded repositories inside the project would be added as broken submodules.
Get-ChildItem -Path . -Recurse -Force -Directory -Filter .git -ErrorAction SilentlyContinue |
  Where-Object { $_.FullName -ne (Join-Path $root '.git') -and $_.FullName -notmatch '\\(Library|Temp|Logs|\.terrain-cache)\\' } |
  ForEach-Object { Write-Host "Embedded repo found: $($_.FullName)" -ForegroundColor Yellow }

# Large binary data must never be text-normalized.
if (-not (Select-String -Path .gitattributes -Pattern '^\*\.bytes' -Quiet)) {
    Add-Content .gitattributes "*.bytes binary`n*.jpg binary`n*.tif binary`n*.tiff binary`n*.zip binary`n*.dll binary"
}

Write-Host 'Adding files (can take a minute)...'
git add -A 2>&1 | Where-Object { $_ -notmatch 'LF will be replaced|CRLF will be replaced' } | ForEach-Object { "$_" }
Check 'git add'
Write-Host "Files staged: $((git ls-files | Measure-Object).Count)"
$pending = git status --porcelain
if ($pending) {
    git commit -q -m "AshenVoxel baseline: consolidated docs (START_HERE/MEMORY/TODO), project as of 2026-10-28"; Check 'git commit'
    Write-Host 'Committed.'
} else { Write-Host 'Nothing new to commit.' }

# Refuse to push anything over GitHub's 100 MB per-file limit.
$big = git ls-files -z | ForEach-Object { $_ -split "`0" } | Where-Object { $_ -and (Test-Path $_) -and ((Get-Item $_).Length -gt 95MB) }
if ($big) { Write-Host "Files too large for GitHub (need Git LFS):`n$($big -join "`n")" -ForegroundColor Red; exit 1 }

$hasOrigin = (git remote) -contains 'origin'
if (-not $hasOrigin) {
    if (Get-Command gh -ErrorAction SilentlyContinue) {
        gh auth status *> $null; if ($LASTEXITCODE -ne 0) { gh auth login --web --git-protocol https }
        gh repo create $repoName --private --source . --remote origin --push; Check 'gh repo create'
    } else {
        Write-Host ''
        Write-Host 'GitHub CLI (gh) not found. Either install it:  winget install --id GitHub.cli -e  and run this again,' -ForegroundColor Yellow
        Write-Host "or create an EMPTY repo named $repoName at https://github.com/new with visibility = PRIVATE," -ForegroundColor Yellow
        $url = Read-Host 'then paste its HTTPS URL here (or press Enter to stop)'
        if (-not $url) { exit 0 }
        git remote add origin $url
        git push -u origin main; Check 'git push'
    }
} else {
    git push -u origin main; Check 'git push'
}

if (Get-Command gh -ErrorAction SilentlyContinue) {
    $vis = gh repo view --json visibility -q .visibility 2>$null
    if ($vis -and $vis -ne 'PRIVATE') {
        Write-Host "WARNING: repo visibility is $vis - making it private." -ForegroundColor Red
        gh repo edit --visibility private --accept-visibility-change-consequences
    }
    Write-Host "Repo visibility: $(gh repo view --json visibility -q .visibility)" -ForegroundColor Green
}
Write-Host 'Done.' -ForegroundColor Green
Stop-Transcript | Out-Null
