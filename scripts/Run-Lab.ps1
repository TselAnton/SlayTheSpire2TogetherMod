param([string]$GameDir = $env:STS2_GAME_DIR, [switch]$StopOnly, [string]$CandidateDll, [string]$UmPath, [switch]$Fast, [switch]$UiOnly, [switch]$StressOnly)
$ErrorActionPreference = 'Stop'
if (-not $UmPath) { $UmPath = (Get-Command um -ErrorAction SilentlyContinue).Source }
if (-not $UmPath) { $UmPath = Join-Path $env:USERPROFILE '.local/bin/um.exe' }
if (-not (Test-Path -LiteralPath $UmPath)) { throw 'Pass -UmPath with universal-modder executable.' }
$repoDir = Split-Path -Parent $PSScriptRoot
if (-not $GameDir) { throw 'Set STS2_GAME_DIR or pass -GameDir with your game installation path.' }
$labDir = Join-Path $repoDir 'artifacts/lab'
$labExe = Join-Path $labDir 'SlayTheSpire2.exe'
if (-not (Test-Path -LiteralPath $labExe)) { throw 'Create an isolated game copy under artifacts/lab first.' }
$labResolved = (Resolve-Path -LiteralPath $labExe).Path
if ($labResolved -eq (Join-Path (Resolve-Path -LiteralPath $GameDir).Path 'SlayTheSpire2.exe')) { throw 'Lab cannot be the original game.' }
# Stop only the previous laboratory process, never another running game.
foreach ($labProcess in (Get-Process SlayTheSpire2 -ErrorAction SilentlyContinue)) {
    if ($labProcess.Path -eq $labResolved) {
        Stop-Process -Id $labProcess.Id
        $labProcess.WaitForExit(5000) | Out-Null
    }
}
if ($StopOnly) { Write-Output 'Only the isolated lab process was stopped.'; exit 0 }
& dotnet build "$repoDir/tests/Sts2Demo.Lab/Sts2Demo.Lab.csproj" -c Release "-p:GameDir=$GameDir" --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Lab build failed.' }
$modDll = "$repoDir/src/Sts2Demo/.godot/mono/temp/bin/Release/Sts2Demo.dll"
if ($CandidateDll) {
    $candidateResolved = (Resolve-Path -LiteralPath $CandidateDll).Path
    $repoResolved = (Resolve-Path -LiteralPath $repoDir).Path + [IO.Path]::DirectorySeparatorChar
    if (-not $candidateResolved.StartsWith($repoResolved, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($candidateResolved) -ne 'Sts2Demo.dll') { throw 'Candidate must be an own Sts2Demo.dll inside this repository.' }
    $manifest = Get-Content -LiteralPath "$repoDir/src/Sts2Demo/Sts2Demo.json" -Raw | ConvertFrom-Json
    $version = [Reflection.AssemblyName]::GetAssemblyName($candidateResolved).Version
    if ($version.ToString(3) -ne $manifest.version) { throw 'Candidate assembly/manifest version mismatch.' }
    $modDll = $candidateResolved
}
Copy-Item -LiteralPath $modDll, "$repoDir/src/Sts2Demo/Sts2Demo.json" -Destination "$labDir/mods/Sts2Demo"
Get-FileHash -LiteralPath "$labDir/mods/Sts2Demo/Sts2Demo.dll" -Algorithm SHA256
Copy-Item -LiteralPath "$repoDir/tests/Sts2Demo.Lab/.godot/mono/temp/bin/Release/Sts2DemoLab.dll", "$repoDir/tests/Sts2Demo.Lab/Sts2DemoLab.json" -Destination "$labDir/mods/Sts2DemoLab"
$auditDir = Join-Path $repoDir 'artifacts/validation-1.3.0'
New-Item -ItemType Directory -Path $auditDir -Force | Out-Null
$logName = if ($StressOnly) { 'lab-stress.log' } else { 'lab-current.log' }
$manifestName = if ($StressOnly) { 'stress-run.json' } else { 'run.json' }
$previousLog = Join-Path $auditDir $logName
if (Test-Path -LiteralPath $previousLog) {
    $stamp = (Get-Item -LiteralPath $previousLog).LastWriteTimeUtc.ToString('yyyyMMdd-HHmmss')
    Copy-Item -LiteralPath $previousLog -Destination (Join-Path $auditDir "lab-run-$stamp.log")
}
@{ started_unix = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds();
   dll_sha256 = (Get-FileHash -LiteralPath "$labDir/mods/Sts2Demo/Sts2Demo.dll" -Algorithm SHA256).Hash;
   fast = [bool]$Fast; ui_only = [bool]$UiOnly; stress = [bool]$StressOnly } | ConvertTo-Json | Set-Content (Join-Path $auditDir $manifestName) -Encoding utf8
$fastMode = if ($Fast) { 'on' } else { 'off' }
$uiMode = if ($UiOnly) { 'on' } else { 'off' }
$stressMode = if ($StressOnly) { 'on' } else { 'off' }
& $UmPath win launch $labExe -- --script "$labDir/isolate.gd" --force-steam off --demo-fast $fastMode --demo-ui-only $uiMode --demo-stress $stressMode --rendering-method gl_compatibility --windowed --resolution 1280x720 --log-file "$auditDir/$logName"
if ($LASTEXITCODE -ne 0) { throw 'Lab launch failed.' }
