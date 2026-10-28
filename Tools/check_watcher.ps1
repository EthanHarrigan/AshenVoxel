# Lets Claude run this project's own check scripts (Tools\*.py) on this PC.
# Claude drops a request file into .terrain-cache\check-requests; this window runs it
# with your Python/.NET and writes the log to .terrain-cache\check-results.
# Only scripts inside this project's Tools folder can run. Close this window to stop.
$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent $PSScriptRoot
$req = Join-Path $root '.terrain-cache\check-requests'
$res = Join-Path $root '.terrain-cache\check-results'
New-Item -ItemType Directory -Force -Path $req,$res | Out-Null
$py = $null
foreach ($c in @('python','py',"$env:USERPROFILE\miniforge3\python.exe","$env:USERPROFILE\AppData\Local\Programs\Python\Python311\python.exe","$env:USERPROFILE\AppData\Local\Programs\Python\Python312\python.exe")) {
  $cmd = Get-Command $c -ErrorAction SilentlyContinue
  if ($cmd) { $py = $cmd.Source; break }
}
$dot = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
"python=$py`r`ndotnet=$dot`r`nstarted=$(Get-Date -Format o)" | Set-Content (Join-Path $res 'watcher-status.txt')
Write-Host "Claude check runner is active. Python: $py  .NET: $dot" -ForegroundColor Green
Write-Host "Leave this window open while Claude works; close it to stop." -ForegroundColor Green
while ($true) {
  "alive=$(Get-Date -Format o)" | Set-Content (Join-Path $res 'heartbeat.txt')
  foreach ($f in Get-ChildItem $req -Filter *.req -ErrorAction SilentlyContinue | Sort-Object Name) {
    $id = $f.BaseName; $log = Join-Path $res "$id.log"
    $line = (Get-Content $f.FullName -Raw).Trim(); Remove-Item $f.FullName -Force
    $parts = $line -split '\s+'; $script = $parts[0]; $args2 = @(); if ($parts.Count -gt 1) { $args2 = $parts[1..($parts.Count-1)] }
    $full = [IO.Path]::GetFullPath((Join-Path $root $script))
    $tools = [IO.Path]::GetFullPath((Join-Path $root 'Tools')) + '\'
    if (-not $full.StartsWith($tools, [StringComparison]::OrdinalIgnoreCase) -or -not $full.EndsWith('.py') -or -not (Test-Path $full)) {
      "REFUSED: only existing Tools\*.py scripts may run ($script)" | Set-Content $log; "exit=refused" | Add-Content $log; continue
    }
    Write-Host "$(Get-Date -Format T) running $script $args2"
    $sw = [Diagnostics.Stopwatch]::StartNew()
    Push-Location $root
    $env:PYTHONUTF8 = '1'
    & $py -X utf8 $full @args2 *> $log
    $code = $LASTEXITCODE
    Pop-Location
    "`r`nexit=$code seconds=$([int]$sw.Elapsed.TotalSeconds)" | Add-Content $log
    Write-Host "  -> exit $code"
  }
  Start-Sleep -Seconds 3
}
