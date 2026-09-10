# Export SQLite -> wwwroot/data JSON, then push so GitHub Pages republishes.
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
Set-Location (Join-Path $root "NkpLocal")
dotnet run --no-launch-profile -- export

Set-Location $root
git add -- "NkpLocal/wwwroot/data"
$status = git status --porcelain -- "NkpLocal/wwwroot/data"
if (-not $status) {
    Write-Host "JSON is already up to date. Nothing to push."
    exit 0
}

git commit -m "Update published judgment JSON."
git push
Write-Host "Pushed. GitHub Pages will republish at https://avaysubedi.github.io/nkp/"
