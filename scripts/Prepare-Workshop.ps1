param([string]$GameDir = $env:STS2_GAME_DIR, [switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$repoDir = Split-Path -Parent $PSScriptRoot
& "$PSScriptRoot/Build.ps1" -GameDir $GameDir -SkipBuild:$SkipBuild
$workspace = Join-Path $repoDir 'workshop/FriendView'
$contentDir = Join-Path $workspace 'content'
New-Item -ItemType Directory -Force -Path $contentDir | Out-Null
$packageDir = Join-Path $repoDir 'dist/Sts2Demo'
# Explicit allowlist: no game files, saves, local configs, lab or logs.
Copy-Item -LiteralPath "$packageDir/Sts2Demo.dll", "$packageDir/Sts2Demo.json", "$repoDir/LICENSE" -Destination $contentDir
Copy-Item -LiteralPath "$repoDir/workshop/CONTENT_README.md" -Destination "$contentDir/README.md"
Write-Output "Workshop content prepared: $contentDir"
Write-Output 'An approved image.png under 1 MB is required before upload.'
