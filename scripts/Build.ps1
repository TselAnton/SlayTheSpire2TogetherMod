param([string]$GameDir = $env:STS2_GAME_DIR, [switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$repoDir = Split-Path -Parent $PSScriptRoot
if (-not $GameDir -and -not $SkipBuild) { throw 'Set STS2_GAME_DIR or pass -GameDir with your game installation path.' }
if (-not $SkipBuild) {
    & dotnet build "$repoDir/src/Sts2Demo/Sts2Demo.csproj" -c Release "-p:GameDir=$GameDir"
    if ($LASTEXITCODE -ne 0) { throw 'Mod build failed.' }
}
$packageDir = Join-Path $repoDir 'dist/Sts2Demo'
if (Test-Path -LiteralPath $packageDir) {
    $packageResolved = (Resolve-Path -LiteralPath $packageDir).Path
    $expected = [IO.Path]::GetFullPath((Join-Path $repoDir 'dist/Sts2Demo'))
    if ($packageResolved -ne $expected -or -not $packageResolved.StartsWith((Resolve-Path -LiteralPath $repoDir).Path + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected package directory.' }
    Remove-Item -LiteralPath $packageResolved -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $packageDir | Out-Null
Copy-Item -LiteralPath "$repoDir/src/Sts2Demo/.godot/mono/temp/bin/Release/Sts2Demo.dll" -Destination $packageDir
Copy-Item -LiteralPath "$repoDir/src/Sts2Demo/Sts2Demo.json" -Destination $packageDir
Copy-Item -LiteralPath "$repoDir/README.md" -Destination $packageDir
Copy-Item -LiteralPath "$repoDir/CHANGELOG.md" -Destination $packageDir
Copy-Item -LiteralPath "$repoDir/LICENSE" -Destination $packageDir
$docsDir = Join-Path $packageDir 'docs'
New-Item -ItemType Directory -Force -Path $docsDir | Out-Null
Copy-Item -LiteralPath "$repoDir/docs/COOP_TEST.md", "$repoDir/docs/WORKSHOP.md", "$repoDir/docs/PRE_RELEASE_TESTS.md", "$repoDir/docs/SUPPORTED_MODES.md", "$repoDir/docs/PLAN_1.4.0.md", "$repoDir/docs/RELEASE_1.4.0.md" -Destination $docsDir
Copy-Item -LiteralPath "$repoDir/docs/STARTUP_CRASH_2026-10-16.md" -Destination $docsDir
$manifest = Get-Content -Raw "$repoDir/src/Sts2Demo/Sts2Demo.json" | ConvertFrom-Json
Add-Type -AssemblyName System.IO.Compression
function Write-PortableModArchive([string]$Destination) {
    # Windows PowerShell Compress-Archive can write backslash entry names.
    # ZIP paths must use '/' so Deck/Linux gets real directories, not filenames.
    $zipStream = [IO.File]::Open($Destination, [IO.FileMode]::Create)
    $archive = New-Object IO.Compression.ZipArchive($zipStream, [IO.Compression.ZipArchiveMode]::Create, $false)
    try {
        foreach ($packageFile in (Get-ChildItem -LiteralPath $packageDir -File -Recurse)) {
            $entryName = 'Sts2Demo/' + $packageFile.FullName.Substring($packageDir.Length + 1).Replace('\', '/')
            $entry = $archive.CreateEntry($entryName, [IO.Compression.CompressionLevel]::Optimal)
            $inputStream = [IO.File]::OpenRead($packageFile.FullName)
            $outputStream = $entry.Open()
            try { $inputStream.CopyTo($outputStream) }
            finally { $outputStream.Dispose(); $inputStream.Dispose() }
        }
    }
    finally { $archive.Dispose(); $zipStream.Dispose() }
}
Write-PortableModArchive "$repoDir/dist/Sts2Demo-$($manifest.version).zip"
Write-PortableModArchive "$repoDir/dist/FriendView-$($manifest.version).zip"
Write-Output "Package: $packageDir"
