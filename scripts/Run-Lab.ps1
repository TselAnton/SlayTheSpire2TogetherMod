param([string]$GameDir = $env:STS2_GAME_DIR, [switch]$StopOnly)
$ErrorActionPreference = 'Stop'
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
Copy-Item -LiteralPath "$repoDir/src/Sts2Demo/.godot/mono/temp/bin/Release/Sts2Demo.dll", "$repoDir/src/Sts2Demo/Sts2Demo.json" -Destination "$labDir/mods/Sts2Demo"
Copy-Item -LiteralPath "$repoDir/tests/Sts2Demo.Lab/.godot/mono/temp/bin/Release/Sts2DemoLab.dll", "$repoDir/tests/Sts2Demo.Lab/Sts2DemoLab.json" -Destination "$labDir/mods/Sts2DemoLab"
& uvx --from universal-modder um win launch $labExe -- --script "$labDir/isolate.gd" --force-steam off --rendering-method gl_compatibility --windowed --resolution 1280x720 --log-file "$repoDir/artifacts/lab-current.log"
if ($LASTEXITCODE -ne 0) { throw 'Lab launch failed.' }
