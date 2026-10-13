param(
    [Parameter(Mandatory)][string]$UploaderDir,
    [Parameter(Mandatory)][string]$GameDir,
    [string]$UmPath = 'um',
    [switch]$ConfirmRelease
)
$ErrorActionPreference = 'Stop'
if (-not $ConfirmRelease) { throw 'Release is not approved. Complete docs/PRE_RELEASE_TESTS.md and obtain explicit author approval before using -ConfirmRelease.' }
$repoDir = Split-Path -Parent $PSScriptRoot
$workspace = Join-Path $repoDir 'workshop/FriendView'
$uploader = Join-Path (Resolve-Path -LiteralPath $UploaderDir).Path 'ModUploader.exe'
if (-not (Test-Path -LiteralPath $uploader)) { throw 'Official ModUploader.exe not found.' }
if ((Get-Content -LiteralPath (Join-Path $UploaderDir 'steam_appid.txt') -Raw).Trim() -ne '2868840') { throw 'Uploader is not configured for Slay the Spire 2.' }
$image = Get-Item -LiteralPath (Join-Path $workspace 'image.png')
if ($image.Length -ge 1MB) { throw 'Steam requires image.png to be less than 1 MB.' }
$config = Get-Content -LiteralPath (Join-Path $workspace 'workshop.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ($config.title -ne 'Friend View') { throw 'Unexpected Workshop title.' }
& $UmPath publish check (Join-Path $workspace 'content') --game $GameDir
if ($LASTEXITCODE -ne 0) { throw 'Publication lint failed; upload cancelled.' }
# Preserve mod_id.txt: the official uploader uses it to update, not create duplicates.
Push-Location -LiteralPath $UploaderDir
try {
    & $uploader upload -w $workspace
    if ($LASTEXITCODE -ne 0) { throw 'Steam upload failed. Check mod-uploader.log before retrying.' }
} finally { Pop-Location }
