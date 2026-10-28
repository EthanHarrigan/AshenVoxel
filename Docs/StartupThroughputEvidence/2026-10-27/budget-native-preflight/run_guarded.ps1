param([ValidateSet(32000000,40000000)][int]$Budget=32000000)
$ErrorActionPreference='Stop'
$trialRoot=$PSScriptRoot
$unityExe='C:\Program Files\Unity\Hub\Editor\6000.3.27f1\Editor\Unity.exe'
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class TrialMemory {
 [StructLayout(LayoutKind.Sequential)] public struct Info { public uint cb; public UIntPtr CommitTotal,CommitLimit,CommitPeak,PhysicalTotal,PhysicalAvailable,SystemCache,KernelTotal,KernelPaged,KernelNonpaged,PageSize; public uint HandleCount,ProcessCount,ThreadCount; }
 [DllImport("psapi.dll",SetLastError=true)] static extern bool GetPerformanceInfo(out Info p,uint size);
 public static ulong Headroom(){ Info p; if(!GetPerformanceInfo(out p,(uint)Marshal.SizeOf(typeof(Info))))throw new InvalidOperationException("GetPerformanceInfo failed");return(p.CommitLimit.ToUInt64()-p.CommitTotal.ToUInt64())*p.PageSize.ToUInt64(); }
}
"@
$stamp=[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff')
$logDir=Join-Path $trialRoot ('watchdog-'+$Budget+'-'+$stamp)
New-Item -ItemType Directory -Path $logDir | Out-Null
$headroom=[TrialMemory]::Headroom()
$preflight=@{utc=[DateTime]::UtcNow.ToString('o');budget=$Budget;headroomBytes=$headroom;requiredBytes=5GB;launched=$false}
$preflight | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $logDir 'preflight.json')
if($headroom -lt 5GB){throw 'Native preflight below 5 GiB commit headroom; no process launched.'}
$manifest=Get-Content -LiteralPath (Join-Path $trialRoot 'snapshot.json') -Raw | ConvertFrom-Json
foreach($row in $manifest.files){$actual=(Get-FileHash -LiteralPath (Join-Path $trialRoot $row.path) -Algorithm SHA256).Hash.ToLower();if($actual -ne $row.sha256){throw ('Frozen source mismatch: '+$row.path)}}
$fixture=Join-Path $trialRoot 'Assets/AshenVoxel/Exploration/Editor/VegetationBudgetNativeTrial.cs'
if((Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash.ToLower() -ne $manifest.fixture_sha256){throw 'Fixture hash mismatch'}
$method=if($Budget -eq 32000000){'Run32'}else{'Run40'}
$argsList=@('-batchmode','-force-d3d11','-refreshImportMode','InProcess','-desiredWorkerCount','0','-standbyWorkerCount','0','-job-worker-count','2','-projectPath',('"'+$trialRoot+'"'),'-executeMethod',('AshenVoxel.Exploration.Editor.VegetationBudgetNativeTrial.'+$method),'-logFile',('"'+(Join-Path $logDir 'Editor.log')+'"'))
$headroom=[TrialMemory]::Headroom()
if($headroom -lt 5GB){throw 'Immediate prelaunch headroom below 5 GiB after frozen-source validation; no launch.'}
$preflight.immediatePrelaunchHeadroomBytes=$headroom
$beforeReports=@(Get-ChildItem -LiteralPath (Join-Path $trialRoot 'Logs/VegetationBudget') -Filter results.json -Recurse -ErrorAction SilentlyContinue | ForEach-Object FullName)
$trialProcess=Start-Process -FilePath $unityExe -ArgumentList $argsList -PassThru -WindowStyle Hidden
$owned=@{};$owned[$trialProcess.Id]=$trialProcess.StartTime.ToUniversalTime().Ticks
$watch=[Diagnostics.Stopwatch]::StartNew();$lastDiscovery=-2.;$failure=$null;$rootExitCode=$null;$trialReport=$null;$samples=New-Object Collections.Generic.List[object]
$preflight.launched=$true;$preflight.rootPid=$trialProcess.Id
$preflight | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $logDir 'preflight.json')
try {
 while($true){
  $trialProcess.Refresh()
  if($trialProcess.HasExited){$rootExitCode=$trialProcess.ExitCode;break}
  if($watch.Elapsed.TotalSeconds-$lastDiscovery -ge 1){
   $processes=Get-CimInstance Win32_Process | Select-Object ProcessId,ParentProcessId,CreationDate
   foreach($row in $processes){
    if(!$owned.ContainsKey([int]$row.ParentProcessId) -or $owned.ContainsKey([int]$row.ProcessId)){continue}
    try{
     $parent=Get-Process -Id $row.ParentProcessId -ErrorAction Stop
     if($parent.StartTime.ToUniversalTime().Ticks -ne $owned[[int]$row.ParentProcessId]){continue}
     $fresh=Get-CimInstance Win32_Process -Filter ('ProcessId='+$row.ProcessId)
     if(!$fresh -or $fresh.ParentProcessId -ne $row.ParentProcessId){continue}
     $child=Get-Process -Id $row.ProcessId -ErrorAction Stop
     $childStart=$child.StartTime.ToUniversalTime()
     if($childStart.ToString('yyyyMMddHHmmssffffff') -ne $fresh.CreationDate.ToUniversalTime().ToString('yyyyMMddHHmmssffffff')){continue}
     $parent.Refresh()
     if($parent.HasExited -or $parent.StartTime.ToUniversalTime().Ticks -ne $owned[[int]$row.ParentProcessId]){continue}
     if($childStart.Ticks -lt $owned[[int]$row.ParentProcessId]){continue}
     $owned[[int]$row.ProcessId]=$childStart.Ticks
    }catch{}
   }
   $lastDiscovery=$watch.Elapsed.TotalSeconds
  }
  $bytes=0L;$alive=0
  foreach($key in @($owned.Keys)){try{$child=Get-Process -Id $key -ErrorAction Stop;if($child.StartTime.ToUniversalTime().Ticks -eq $owned[$key]){$bytes+=$child.PrivateMemorySize64;$alive++}}catch{}}
  $free=[TrialMemory]::Headroom();$samples.Add(@{seconds=$watch.Elapsed.TotalSeconds;familyPrivateBytes=$bytes;headroomBytes=$free;alive=$alive})
  $trialProcess.Refresh()
  if($bytes -gt 3GB){$failure='Owned family private memory exceeded 3 GiB';break}
  if($free -lt 2GB){$failure='Global commit headroom below 2 GiB';break}
  if($watch.Elapsed.TotalSeconds -gt 300){$failure='300 second own-process watchdog';break}
  Start-Sleep -Milliseconds 200
 }
 if($null -eq $rootExitCode){$trialProcess.Refresh();if($trialProcess.HasExited){$rootExitCode=$trialProcess.ExitCode}}
 if(!$failure -and $rootExitCode -ne 0){$failure='Native root exit code '+$rootExitCode}
 if(!$failure){
  $newReports=@(Get-ChildItem -LiteralPath (Join-Path $trialRoot 'Logs/VegetationBudget') -Filter results.json -Recurse -ErrorAction SilentlyContinue | Where-Object {$beforeReports -notcontains $_.FullName})
  if($newReports.Count -ne 1){$failure='Expected exactly one new native report'}
  else{$trialReport=$newReports[0].FullName;$native=Get-Content -LiteralPath $trialReport -Raw | ConvertFrom-Json;if($native.status -ne 'PASS' -or $native.triangleBudget -ne $Budget){$failure='Native fixture did not PASS the requested budget'}}
 }
} catch {$failure=$_.Exception.ToString()}
finally {
 # Only exact PID/start-time identities from this spawned family can be stopped.
 foreach($key in @($owned.Keys | Sort-Object -Descending)){
  try{$child=Get-Process -Id $key -ErrorAction Stop;if($child.StartTime.ToUniversalTime().Ticks -eq $owned[$key]){Stop-Process -Id $key -Force -ErrorAction Stop}}catch{}
 }
 $remaining=@()
 foreach($key in @($owned.Keys)){try{$child=Get-Process -Id $key -ErrorAction Stop;if($child.StartTime.ToUniversalTime().Ticks -eq $owned[$key]){$remaining+=$key}}catch{}}
 if($remaining.Count){$failure=($failure+' Owned process family not fully gone: '+($remaining -join ','))}
 $samples | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $logDir 'samples.json')
 $result=@{utc=[DateTime]::UtcNow.ToString('o');budget=$Budget;failure=$failure;rootExitCode=$rootExitCode;nativeReport=$trialReport;remainingOwnedPids=$remaining;finalHeadroomBytes=[TrialMemory]::Headroom();elapsedSeconds=$watch.Elapsed.TotalSeconds;rootPid=$trialProcess.Id;owned=$owned;scope='Own-family private/global commit guard; 200ms target sample plus process enumeration overhead. Thresholds are cooperative, not hard allocation limits.'}
 $result | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $logDir 'watchdog.json')
}
Write-Output $logDir
if($failure){throw $failure}
