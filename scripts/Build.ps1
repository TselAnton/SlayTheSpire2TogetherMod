param([string]$GameDir = $env:STS2_GAME_DIR, [switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$repoDir = Split-Path -Parent $PSScriptRoot
if (-not $GameDir -and -not $SkipBuild) { throw 'Set STS2_GAME_DIR or pass -GameDir with your game installation path.' }
if (-not $SkipBuild) {
    & dotnet build "$repoDir/src/Sts2Demo/Sts2Demo.csproj" -c Release "-p:GameDir=$GameDir"
    if ($LASTEXITCODE -ne 0) { throw 'Mod build failed.' }
}
$packageDir = Join-Path $repoDir 'dist/Sts2Demo'
New-Item -ItemType Directory -Force -Path $packageDir | Out-Null
Copy-Item -LiteralPath "$repoDir/src/Sts2Demo/.godot/mono/temp/bin/Release/Sts2Demo.dll" -Destination $packageDir
Copy-Item -LiteralPath "$repoDir/src/Sts2Demo/Sts2Demo.json" -Destination $packageDir
Copy-Item -LiteralPath "$repoDir/README.md" -Destination $packageDir
Copy-Item -LiteralPath "$repoDir/CHANGELOG.md" -Destination $packageDir
Copy-Item -LiteralPath "$repoDir/LICENSE" -Destination $packageDir
$docsDir = Join-Path $packageDir 'docs'
New-Item -ItemType Directory -Force -Path $docsDir | Out-Null
Copy-Item -LiteralPath "$repoDir/docs/COOP_TEST.md", "$repoDir/docs/VERIFICATION.md", "$repoDir/docs/WORKSHOP.md", "$repoDir/docs/PRE_RELEASE_TESTS.md", "$repoDir/docs/RELEASE_CANDIDATE.md" -Destination $docsDir
$manifest = Get-Content -Raw "$repoDir/src/Sts2Demo/Sts2Demo.json" | ConvertFrom-Json
Compress-Archive -LiteralPath $packageDir -DestinationPath "$repoDir/dist/Sts2Demo-$($manifest.version).zip" -Force
Compress-Archive -LiteralPath $packageDir -DestinationPath "$repoDir/dist/FriendView-$($manifest.version).zip" -Force
Write-Output "Package: $packageDir"
