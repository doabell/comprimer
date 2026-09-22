$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path publish,symbols -Force | Out-Null
Copy-Item -LiteralPath target/release/Comprimer.exe,README.md,LICENSE -Destination publish
Copy-Item -LiteralPath target/release/Comprimer.pdb -Destination symbols
$commit = (git rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot determine package commit.' }
$files = [ordered]@{}
foreach ($path in @('publish/Comprimer.exe','publish/README.md','publish/LICENSE','symbols/Comprimer.pdb')) {
    $files[$path] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
}
[ordered]@{ commit = $commit; files = $files } | ConvertTo-Json | Set-Content -LiteralPath publish/build-info.json -Encoding utf8
