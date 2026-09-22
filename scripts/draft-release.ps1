$ErrorActionPreference = 'Stop'
$tag = $env:GITHUB_REF_NAME
if ($tag -notmatch '^v[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$') {
    throw 'Expected a version tag.'
}
$packageZip = "Comprimer-$tag-win-x64.zip"
$symbolsZip = "Comprimer-$tag-symbols-win-x64.zip"
Compress-Archive -Path publish/* -DestinationPath $packageZip -Force
Compress-Archive -Path symbols/* -DestinationPath $symbolsZip -Force
$draft = gh release view $tag --repo $env:GITHUB_REPOSITORY --json isDraft --jq .isDraft 2>$null
if ($LASTEXITCODE -eq 0) {
    if ($draft -ne 'true') { throw 'Refusing to modify a published release.' }
} else {
    gh release create $tag --repo $env:GITHUB_REPOSITORY --verify-tag --draft --title "Comprimer $tag" --generate-notes --target $env:GITHUB_SHA
    if ($LASTEXITCODE -ne 0) { throw 'Draft release creation failed.' }
}
gh release upload $tag $packageZip $symbolsZip --repo $env:GITHUB_REPOSITORY --clobber
if ($LASTEXITCODE -ne 0) { throw 'Draft asset upload failed.' }
