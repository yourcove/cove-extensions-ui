param(
    [string]$Version = "0.0.1"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "extensions\OfficialDownloaders\OfficialDownloaders.csproj"
$publishDir = Join-Path $repoRoot "artifacts\official-downloaders"
$packageName = "cove.official.downloaders-$Version.zip"
$packagePath = Join-Path $repoRoot $packageName
$manifestPath = Join-Path $repoRoot "extensions\OfficialDownloaders\extension.json"
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json

if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}
if (Test-Path $packagePath) {
    Remove-Item $packagePath -Force
}

Write-Host "Publishing OfficialDownloaders extension..."
dotnet publish $projectPath -c Release -o $publishDir

Write-Host "Creating package $packageName..."
Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $packagePath -Force

$hash = (Get-FileHash $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()

Write-Host ""
Write-Host "Package: $packagePath"
Write-Host "SHA256:  $hash"
Write-Host "Registry CI will compute checksum from downloadUrl and stamp releasedAt on merge."
Write-Host ""
Write-Host "Registry metadata snippet:"
Write-Host ""
@"
{
  "id": "cove.official.downloaders",
  "sourceManifestUrl": "https://raw.githubusercontent.com/yourcove/cove-extensions-ui/main/extensions/OfficialDownloaders/extension.json",
  "repositoryUrl": "https://github.com/yourcove/cove-extensions-ui",
  "versions": [
    {
      "version": "$Version",
      "minCoveVersion": "$($manifest.minCoveVersion)",
      "downloadUrl": "https://github.com/yourcove/cove-extensions-ui/releases/download/downloaders/v$Version/$packageName"
    }
  ]
}
"@
