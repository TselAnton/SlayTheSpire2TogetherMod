param()
$ErrorActionPreference = 'Stop'
$repoDir = Split-Path -Parent $PSScriptRoot
$files = @(& git -C $repoDir ls-files --cached --others --exclude-standard)
if ($LASTEXITCODE -ne 0) { throw 'Could not enumerate Git candidate files.' }
$patterns = @(
    '-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----',
    '\b(?:ghp_|github_pat_)[A-Za-z0-9_]{20,}',
    '\bsk-[A-Za-z0-9_-]{20,}',
    '\bAKIA[0-9A-Z]{16}\b',
    '(?i)[A-Z]:[\\/]+Users[\\/]+(?!<)[^\\/\s]+',
    '(?i)[A-Z]:[\\/]+SteamLibrary[\\/]',
    '\b7656119[0-9]{10}\b'
)
$issues = @()
foreach ($file in $files) {
    if ($file -match '(?i)(^|/)(?:\.env(?:\..*)?|\.aws|\.codex|\.agents|artifacts|\.tools|\.venv|logs)(/|$)|\.(?:dll|pck|pdb|save|log|zip)$') {
        $issues += "Forbidden/private candidate: $file"
        continue
    }
    $extension = [IO.Path]::GetExtension($file)
    if ($extension -in @('.png', '.jpg', '.webp')) { continue }
    $contents = Get-Content -LiteralPath (Join-Path $repoDir $file) -Raw -Encoding UTF8
    foreach ($pattern in $patterns) {
        if ($contents -match $pattern) { $issues += "Potential secret or machine-specific data in: $file"; break }
    }
}
if ($issues.Count) { $issues | Write-Output; throw 'Git content audit failed. No matched values were printed.' }
Write-Output "PASS: $($files.Count) Git candidate files; no forbidden artifacts, detected credential patterns or private machine paths."
Write-Output 'This is a heuristic audit; review the staged file list before pushing.'
