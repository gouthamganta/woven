$ErrorActionPreference = 'Stop'
$wovenRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$wovenScript = Join-Path $PSScriptRoot 'sync-github-project.mjs'
$wovenLocal = Join-Path $wovenRoot 'qa/.local'
New-Item -ItemType Directory -Force -Path $wovenLocal | Out-Null
$wovenPidFile = Join-Path $wovenLocal 'github-sync.pid'
if (Test-Path -LiteralPath $wovenPidFile) {
    $wovenPreviousPid = [int](Get-Content -LiteralPath $wovenPidFile)
    $wovenPrevious = Get-CimInstance Win32_Process -Filter "ProcessId = $wovenPreviousPid"
    if ($wovenPrevious -and $wovenPrevious.CommandLine.Contains($wovenScript)) {
        Write-Output "GitHub sync already running (PID $wovenPreviousPid)."
        exit 0
    }
}
$wovenNode = (Get-Command node.exe).Source
$wovenProcess = Start-Process -FilePath $wovenNode -ArgumentList @(('"' + $wovenScript + '"'), '--watch') -WorkingDirectory $wovenRoot -WindowStyle Hidden -RedirectStandardOutput (Join-Path $wovenLocal 'github-sync.stdout.log') -RedirectStandardError (Join-Path $wovenLocal 'github-sync.stderr.log') -PassThru
$wovenProcess.Id | Set-Content -LiteralPath $wovenPidFile
Write-Output "GitHub board sync started (PID $($wovenProcess.Id)). This updates board items only; AI dispatch is not enabled."
