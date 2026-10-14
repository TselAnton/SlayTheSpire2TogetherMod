param([string]$OutputDirectory = 'artifacts/visual-fixes-1.2.2', [datetime]$NotBefore = [datetime]::MinValue)
$ErrorActionPreference = 'Stop'
$directory = (Resolve-Path -LiteralPath $OutputDirectory).Path
$files = @(Get-ChildItem -LiteralPath $directory -Filter '*.png' -File | Where-Object { $_.LastWriteTime -ge $NotBefore } | Sort-Object Name)
$manifest = @($files | ForEach-Object { [ordered]@{ file = $_.Name; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash; capturedUtc = $_.LastWriteTimeUtc.ToString('o') } })
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $directory 'manifest.json') -Encoding UTF8
$cards = foreach ($file in $files) {
    $name = [System.Net.WebUtility]::HtmlEncode($file.Name)
    "<figure><figcaption>$name</figcaption><a href='$name'><img loading='lazy' src='$name' alt='$name'></a></figure>"
}
$html = "<!doctype html><meta charset='utf-8'><title>Friend View 1.2.2 - visual fixes</title><style>body{background:#141b20;color:#eee;font:16px sans-serif;margin:24px}figure{margin:20px 0}img{max-width:100%;width:1280px}figcaption{padding:8px}a{color:#8de}</style><h1>Friend View 1.2.2 - visual fixes</h1><p>Isolated native-game laboratory. Source/demo pairs are not two independent Steam clients. Blocked screens are shown outside demo. Filenames identify cases; manifest.json contains capture timestamps and hashes.</p>$($cards -join "`n")"
$html | Set-Content -LiteralPath (Join-Path $directory 'index.html') -Encoding UTF8
Write-Output "Gallery: $($files.Count) screenshots in $directory"
