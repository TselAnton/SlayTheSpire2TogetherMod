param([int]$Runs = 8, [string]$GameDir = $env:STS2_GAME_DIR, [string]$AuditDir)
$ErrorActionPreference = 'Stop'
$repoDir = Split-Path -Parent $PSScriptRoot
$labDir = Join-Path $repoDir 'artifacts/lab'
$labExe = (Resolve-Path -LiteralPath "$labDir/SlayTheSpire2.exe").Path
if ($Runs -lt 1 -or $Runs -gt 40) { throw 'Choose 1 to 40 startup runs.' }
if (Get-Process SlayTheSpire2 -ErrorAction SilentlyContinue | Where-Object Path -eq $labExe) { throw 'Finish the current isolated lab first.' }
if (-not $GameDir) { throw 'Pass the original GameDir to provision the native crash backend in the lab.' }
if ($labExe -eq (Join-Path (Resolve-Path $GameDir).Path 'SlayTheSpire2.exe')) { throw 'The lab must be a separate copy.' }
$backendDir = Join-Path $labDir 'addons/sentry/bin/windows/x86_64'
New-Item -ItemType Directory -Path $backendDir -Force | Out-Null
Copy-Item -LiteralPath "$GameDir/crashpad_handler.exe", "$GameDir/crashpad_wer.dll" -Destination $backendDir
if (-not $AuditDir) { $AuditDir = Join-Path $repoDir ('artifacts/native-startup-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')) }
New-Item -ItemType Directory -Path $AuditDir -Force | Out-Null
$AuditDir = (Resolve-Path -LiteralPath $AuditDir).Path
$isolate = Join-Path $repoDir 'tests/Sts2Demo.Lab/startup-isolate.gd'
$hash = (Get-FileHash -LiteralPath "$labDir/mods/Sts2Demo/Sts2Demo.dll" -Algorithm SHA256).Hash
$results = @()
for ($i = 1; $i -le $Runs; $i++) {
    $renderer = if ($i % 2 -eq 0) { 'forward_plus' } else { 'gl_compatibility' }
    $log = Join-Path $AuditDir "native-startup-$i.log"
    $arguments = @('--script', "`"$isolate`"", '--force-steam', 'off', '--demo-startup', 'on', '--demo-startup-audit', 'on', '--windowed', '--resolution', '1280x720', '--rendering-method', $renderer, '--log-file', "`"$log`"")
    if ($renderer -eq 'forward_plus') { $arguments += @('--rendering-driver', 'd3d12') }
    $process = Start-Process -FilePath $labExe -WorkingDirectory $labDir -ArgumentList $arguments -PassThru -WindowStyle Hidden
    if (-not $process.WaitForExit(45000)) {
        if ($process.Path -eq $labExe) { $process.Kill(); $process.WaitForExit() }
        throw "Isolated startup timed out; retained $log"
    }
    $content = [IO.File]::ReadAllText($log)
    $passed = $process.ExitCode -eq 0 -and $content.Contains('[DemoLab] STARTUP PASS:') -and
        $content.Contains('[StartupAudit] PASS: managed reporting disabled; native logger retained') -and
        $content.Contains('[StartupAudit] PASS: native SDK shut down after signal dispatch.') -and
        -not $content.Contains('[StartupAudit] FAIL:') -and -not $content.Contains('slot >= slot_max')
    $result = [ordered]@{run=$i; renderer=$renderer; passed=$passed; exit_code=$process.ExitCode; log=[IO.Path]::GetFileName($log)}
    $results += $result
    @{dll_sha256=$hash; isolated=$true; steam=$false; native_sentry_required=$true; results=$results} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$AuditDir/results.json" -Encoding utf8
    $result | ConvertTo-Json -Compress
    if (-not $passed) { throw "Native startup audit failed; retained $log" }
}
