param([Parameter(Mandatory)][string]$GameDir, [string]$UmPath)
$ErrorActionPreference = 'Stop'
$repoDir = Split-Path -Parent $PSScriptRoot
& dotnet run --project "$repoDir/tests/Sts2Demo.Tests/Sts2Demo.Tests.csproj" -c Release "-p:GameDir=$GameDir"
if ($LASTEXITCODE -ne 0) { throw 'Observer/network tests failed.' }
& dotnet run --project "$repoDir/tests/Sts2Demo.Bootstrap.Tests/Sts2Demo.Bootstrap.Tests.csproj" -c Release
if ($LASTEXITCODE -ne 0) { throw 'Bootstrap/profile tests failed.' }
& "$PSScriptRoot/Prepare-Workshop.ps1" -GameDir $GameDir
$contentDir = (Resolve-Path -LiteralPath (Join-Path $repoDir 'workshop/FriendView/content')).Path
$manifest = Get-Content -LiteralPath "$contentDir/Sts2Demo.json" -Raw -Encoding UTF8 | ConvertFrom-Json
$assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName("$contentDir/Sts2Demo.dll").Version
$declaredVersion = [version]$manifest.version
$entry = Get-Content -LiteralPath "$repoDir/src/Sts2Demo/ModEntry.cs" -Raw -Encoding UTF8
if ($manifest.name -ne 'Friend View' -or $manifest.id -ne 'Sts2Demo' -or $manifest.affects_gameplay -ne $false) { throw 'Invalid manifest identity/gameplay classification.' }
if ($assemblyVersion.Major -ne $declaredVersion.Major -or $assemblyVersion.Minor -ne $declaredVersion.Minor -or $assemblyVersion.Build -ne $declaredVersion.Build -or $entry -notmatch ('public const string Version = "' + [regex]::Escape($manifest.version) + '"')) { throw 'Release versions do not match.' }
$allowed = @('Sts2Demo.dll', 'Sts2Demo.json', 'README.md', 'LICENSE')
$files = Get-ChildItem -LiteralPath $contentDir -File -Recurse
if ($files.Count -ne 4 -or ($files | Where-Object { $_.DirectoryName -ne $contentDir -or $_.Name -notin $allowed })) { throw 'Workshop content has unexpected/missing files.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$releaseZip = [IO.Compression.ZipFile]::OpenRead("$repoDir/dist/FriendView-$($manifest.version).zip")
try {
    if ($releaseZip.Entries | Where-Object { $_.FullName.Contains('\') -or $_.FullName.StartsWith('/') -or $_.FullName.Contains('../') }) { throw 'ZIP has non-portable or unsafe entry paths.' }
    $dllEntry = $releaseZip.GetEntry('Sts2Demo/Sts2Demo.dll')
    if (-not $dllEntry) { throw 'ZIP DLL entry missing.' }
    $zipDll = $dllEntry.Open()
    try {
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $zipHash = [BitConverter]::ToString($sha.ComputeHash($zipDll)).Replace('-', '') }
        finally { $sha.Dispose() }
    }
    finally { $zipDll.Dispose() }
    if ($zipHash -ne (Get-FileHash -LiteralPath "$contentDir/Sts2Demo.dll" -Algorithm SHA256).Hash) { throw 'ZIP DLL differs from Workshop candidate.' }
}
finally { $releaseZip.Dispose() }
$image = Get-Item -LiteralPath "$repoDir/workshop/FriendView/image.png"
if ($image.Length -ge 1MB) { throw 'Preview exceeds Steam size limit.' }
if ((Get-Content -LiteralPath "$repoDir/workshop/FriendView/mod_id.txt" -Raw).Trim() -ne '3819332749') { throw 'Unexpected Workshop item ID.' }
if ($UmPath) { & $UmPath publish check $contentDir --game $GameDir }
else { & uvx --from universal-modder um publish check $contentDir --game $GameDir }
if ($LASTEXITCODE -ne 0) { throw 'Publication lint failed.' }
& git -C $repoDir diff --check
if ($LASTEXITCODE -ne 0) { throw 'Git diff whitespace check failed.' }
Get-FileHash -LiteralPath "$contentDir/Sts2Demo.dll" -Algorithm SHA256
Write-Output 'Automated checks passed. Manual Linux/SteamDeck, co-op and profile checks remain required: docs/PRE_RELEASE_TESTS.md.'
Write-Output 'No game launch, original-game install, Steam upload or Git push was performed by this script.'
