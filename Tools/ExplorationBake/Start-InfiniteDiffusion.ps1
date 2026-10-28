param(
    [string]$AzgaarJson = "",
    [double]$WorldWidthMeters = 299520,
    [double]$WorldDepthMeters = 149760,
    [ValidateRange(1,30)][double]$ModelMetersPerSample = 5,
    [ValidateSet('cpu','cuda','mps')][string]$Device = 'cpu',
    [ValidateRange(1,16)][int]$Threads = 4,
    [ValidateSet(512,1024)][int]$FineSide = 1024,
    [UInt64]$Seed = 714103,
    [int]$Port = 8787,
    [switch]$SkipBake
)
$ErrorActionPreference = 'Stop'
# Azgaar exports can contain Unicode names; Windows Python otherwise uses cp1252.
$env:PYTHONUTF8 = '1'
$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$CacheRoot = Join-Path $ProjectRoot '.terrain-cache'
$VenvRoot = Join-Path $CacheRoot 'python'
$PythonExe = Join-Path $VenvRoot 'Scripts/python.exe'
$Upstream = Join-Path $CacheRoot 'terrain-diffusion'
$Job = Join-Path $CacheRoot 'world-job.json'
$BakeOut = Join-Path $CacheRoot 'exploration-bake'
$Samples = Join-Path $ProjectRoot 'Assets/AshenVoxel/Exploration/Samples'
function Checked([string]$Program, [string[]]$Arguments) {
    & $Program @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Command failed ($LASTEXITCODE): $Program $($Arguments -join ' ')" }
}
try {
    if (!(Get-Command git -ErrorAction SilentlyContinue)) { throw 'Optional live generation requires Git for Windows. Install it, then rerun. Bundled exploration needs neither Git nor Python.' }
    if ([string]::IsNullOrWhiteSpace($AzgaarJson)) { $AzgaarJson = Join-Path $Samples 'Ceteviles_Source.json' }
    if (!(Test-Path $AzgaarJson)) { throw "Azgaar Full JSON not found: $AzgaarJson" }
    if ($WorldWidthMeters -le 0 -or $WorldDepthMeters -le 0) { throw 'World dimensions must be positive meters.' }
    New-Item -ItemType Directory -Force -Path $CacheRoot | Out-Null
    if (!(Test-Path $PythonExe)) {
        if (Get-Command py -ErrorAction SilentlyContinue) { Checked 'py' @('-3.12','-m','venv',$VenvRoot) }
        elseif (Get-Command python -ErrorAction SilentlyContinue) { Checked 'python' @('-m','venv',$VenvRoot) }
        else { throw 'Install Python 3.12 for optional model generation. The bundled Unity world can already be explored without Python.' }
    }
    Checked $PythonExe @('-m','pip','install','--upgrade','pip')
    Checked $PythonExe @('-m','pip','install','-r',(Join-Path $ProjectRoot 'Tools/DiffusionBridge/requirements-inference.txt'))
    if (!(Test-Path (Join-Path $Upstream '.git'))) { Checked 'git' @('clone','https://github.com/xandergos/terrain-diffusion',$Upstream) }
    Checked 'git' @('-C',$Upstream,'checkout','e8dcb4b1a834ab2f6b1a6f5256ed7c9f2f3e8230')
    $Stats = Join-Path $CacheRoot 'pipeline_data.json'
    if (!(Test-Path $Stats)) { Invoke-WebRequest -UseBasicParsing -Uri 'https://huggingface.co/xandergos/terrain-diffusion-30m-onnx/resolve/ad2df557eca5645f588766101cf3bc3682455c3e/pipeline_data.json' -OutFile $Stats }
    # Unity's inherited PowerShell module path can omit Get-FileHash. Use the
    # framework directly so verification does not depend on module discovery.
    $StatsStream = [System.IO.File]::OpenRead($Stats)
    $StatsHasher = [System.Security.Cryptography.SHA256]::Create()
    try { $StatsHash = [System.BitConverter]::ToString($StatsHasher.ComputeHash($StatsStream)).Replace('-', '').ToLowerInvariant() }
    finally { $StatsHasher.Dispose(); $StatsStream.Dispose() }
    if ($StatsHash -ne 'e3132c3ef0c65d8613615f9278ffe23bbd9363ddcd87f1cc6f18456bcc9efe5c') { throw 'Statistics checksum differs from the pinned official data.' }
    $Conditioning = Join-Path $CacheRoot 'conditioning'
    $NativeWidthKm = $WorldWidthMeters * 30 / $ModelMetersPerSample / 1000
    $NativeDepthKm = $WorldDepthMeters * 30 / $ModelMetersPerSample / 1000
    Checked $PythonExe @((Join-Path $ProjectRoot 'Tools/DiffusionBridge/prepare_azgaar.py'),$AzgaarJson,$Conditioning,'--upstream-source',$Upstream,'--width-km',$NativeWidthKm.ToString([cultureinfo]::InvariantCulture),'--depth-km',$NativeDepthKm.ToString([cultureinfo]::InvariantCulture))
    Checked $PythonExe @((Join-Path $ProjectRoot 'Tools/DiffusionBridge/bridge.py'),'plan','--upstream-source',$Upstream,'--stats-file',$Stats,'--conditioning',(Join-Path $Conditioning 'conditioning.json'),'--seed',$Seed.ToString(),'--device',$Device,'--torch-threads',$Threads.ToString(),'--output',$Job)
    if (!$SkipBake) {
        # Stop Play before rebaking. Existing downloaded samples are backed up once.
        $Backup = Join-Path $CacheRoot 'original-bundled-samples'
        if (!(Test-Path $Backup)) { Copy-Item -Recurse $Samples $Backup }
        Checked $PythonExe @((Join-Path $PSScriptRoot 'bake_exploration.py'),'--job',$Job,'--output',$BakeOut,'--fine-side',$FineSide.ToString(),'--world-width-m',$WorldWidthMeters.ToString([cultureinfo]::InvariantCulture),'--world-depth-m',$WorldDepthMeters.ToString([cultureinfo]::InvariantCulture),'--world-spacing-m',$ModelMetersPerSample.ToString([cultureinfo]::InvariantCulture),'--allow-download')
        Checked $PythonExe @((Join-Path $PSScriptRoot 'finalize_samples.py'),'--input',$BakeOut,'--output',$Samples,'--source-map',$AzgaarJson)
    }
    Write-Host 'Refresh Unity after the bake, stop Play if needed, and Play again to load the matching local view identity.'
    Write-Host 'The model service is optional for bundled exploration. Keep this window open while live generation is enabled.'
    Checked $PythonExe @((Join-Path $PSScriptRoot 'serve_exploration.py'),'--job',$Job,'--owner-cache-dir',(Join-Path $BakeOut 'owners'),'--port',$Port.ToString(),'--world-spacing-m',$ModelMetersPerSample.ToString([cultureinfo]::InvariantCulture),'--world-width-m',$WorldWidthMeters.ToString([cultureinfo]::InvariantCulture),'--world-depth-m',$WorldDepthMeters.ToString([cultureinfo]::InvariantCulture),'--allow-download')
} catch {
    Write-Host $_ -ForegroundColor Red
    Write-Host 'No local world should be activated from a different model identity. Bundled Unity exploration remains available.'
    exit 1
}
