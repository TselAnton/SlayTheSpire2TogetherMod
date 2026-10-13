param([Parameter(Mandatory)][string]$GameDir)
$ErrorActionPreference = 'Stop'
$repoDir = Split-Path -Parent $PSScriptRoot
$resolvedGame = (Resolve-Path -LiteralPath $GameDir).Path
if (-not (Test-Path -LiteralPath "$resolvedGame/SlayTheSpire2.exe")) { throw 'Not a Slay the Spire 2 installation.' }
if (Get-Process SlayTheSpire2 -ErrorAction SilentlyContinue) { throw 'Close the game before installing.' }
$packageDir = Join-Path $repoDir 'dist/Sts2Demo'
if (-not (Test-Path -LiteralPath "$packageDir/Sts2Demo.dll")) { throw 'Run scripts/Build.ps1 first.' }
$target = Join-Path $resolvedGame 'mods/Sts2Demo'
if (Test-Path -LiteralPath $target) {
    # Back up only this mod when updating; game assets, profiles and saves are never changed.
    $backupDir = Join-Path $repoDir ('artifacts/backups/Sts2Demo-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
    New-Item -ItemType Directory -Force -Path $backupDir | Out-Null
    Copy-Item -LiteralPath $target -Destination $backupDir -Recurse
    Write-Output "Previous mod backed up to: $backupDir"
}
New-Item -ItemType Directory -Force -Path $target | Out-Null
Copy-Item -LiteralPath "$packageDir/Sts2Demo.dll", "$packageDir/Sts2Demo.json" -Destination $target
Write-Output "Installed: $target"
Write-Output 'Enable mods in the game itself. This script never edits settings or saves.'
