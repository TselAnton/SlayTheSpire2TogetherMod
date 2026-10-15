param([int]$Runs = 8, [string]$UmPath = "$env:USERPROFILE/.local/bin/um.exe")
$ErrorActionPreference = 'Stop'
$repoDir = Split-Path -Parent $PSScriptRoot
$labDir = Join-Path $repoDir 'artifacts/lab'
$labExe = (Resolve-Path -LiteralPath (Join-Path $labDir 'SlayTheSpire2.exe')).Path
$auditDir = Join-Path $repoDir 'artifacts/validation-1.4.0'
if ($Runs -lt 1 -or $Runs -gt 20) { throw 'Choose 1 to 20 isolated startup runs.' }
if (Get-Process SlayTheSpire2 -ErrorAction SilentlyContinue | Where-Object Path -eq $labExe) { throw 'Finish and stop the current isolated lab first.' }
$hash = (Get-FileHash -LiteralPath "$labDir/mods/Sts2Demo/Sts2Demo.dll" -Algorithm SHA256).Hash
$results = @()
$runStamp = [DateTime]::UtcNow.ToString("yyyyMMdd-HHmmss")
for ($index = 1; $index -le $Runs; $index++) {
    $render = if ($index % 2 -eq 0) { 'forward_plus' } else { 'gl_compatibility' }
    $logPath = Join-Path $auditDir "startup-$runStamp-$index.log"
    $arguments = @('--script', "$labDir/isolate.gd", '--force-steam', 'off', '--demo-startup', 'on', '--windowed', '--resolution', '1280x720', '--rendering-method', $render, '--log-file', $logPath)
    if ($render -eq 'forward_plus') { $arguments += @('--rendering-driver', 'd3d12') }
    $watch = [Diagnostics.Stopwatch]::StartNew()
    & $UmPath win launch $labExe -- @arguments | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Isolated startup launch failed: $index" }
    $passed = $false
    $trackedProcess = $null
    do {
        Start-Sleep -Milliseconds 500
        if (Test-Path -LiteralPath $logPath) {
            $passed = [IO.File]::ReadAllText($logPath).Contains('[DemoLab] STARTUP PASS: reached native main menu.')
        }
        $running = @(Get-Process SlayTheSpire2 -ErrorAction SilentlyContinue | Where-Object Path -eq $labExe)
        if (-not $trackedProcess -and $running.Count -gt 0) { $trackedProcess = $running[0]; $null = $trackedProcess.Handle }
    } while ($watch.Elapsed.TotalSeconds -lt 120 -and (-not $passed -or $running.Count -gt 0))
    # Every PID is matched against the resolved isolated executable, never the user's game.
    foreach ($labProcess in $running) { Stop-Process -Id $labProcess.Id; $labProcess.WaitForExit(5000) | Out-Null }
    $exitCode = if ($trackedProcess -and $trackedProcess.HasExited) { $trackedProcess.ExitCode } else { $null }
    $results += @{ exit_code = $exitCode; run = $index; renderer = $render; passed = $passed; clean_exit = ($running.Count -eq 0); seconds = [Math]::Round($watch.Elapsed.TotalSeconds, 2); log = [IO.Path]::GetFileName($logPath) }
    @{ dll_sha256 = $hash; isolated = $true; steam = $false; results = $results } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $auditDir 'startup-runs.json') -Encoding utf8
    Write-Output "Startup $index/$Runs ($render): reached menu=$passed, clean exit=$($running.Count -eq 0)"
    if (-not $passed -or $running.Count -gt 0 -or ($null -ne $exitCode -and $exitCode -ne 0)) { throw 'Startup audit failed; preserve logs before retrying.' }
}
