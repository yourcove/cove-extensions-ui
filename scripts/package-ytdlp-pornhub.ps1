param(
    [string]$Version = "0.0.1"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "extensions\YtDlpPornhub\YtDlpPornhub.csproj"
$publishDir = Join-Path $repoRoot "artifacts\ytdlp-pornhub"
$packageName = "cove.official.ytdlp.pornhub-$Version.zip"
$packagePath = Join-Path $repoRoot $packageName
$releasedAt = (Get-Date).ToUniversalTime().ToString("s") + "Z"

if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}
if (Test-Path $packagePath) {
    Remove-Item $packagePath -Force
}

Write-Host "Publishing YtDlpPornhub extension..."
dotnet publish $projectPath -c Release -o $publishDir

Write-Host "Creating package $packageName..."
Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $packagePath -Force

$hash = (Get-FileHash $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()

Write-Host ""
Write-Host "Package: $packagePath"
Write-Host "SHA256:  $hash"
Write-Host ""
Write-Host "Registry metadata snippet:"
Write-Host ""
@"
{
  "id": "cove.official.ytdlp.pornhub",
  "sourceManifestUrl": "https://raw.githubusercontent.com/yourcove/cove-extensions-ui/main/extensions/YtDlpPornhub/extension.json",
  "repositoryUrl": "https://github.com/yourcove/cove-extensions-ui",
  "versions": [
    {
      "version": "$Version",
      "releasedAt": "$releasedAt",
      "checksum": "sha256:$hash",
      "downloadUrl": "https://github.com/yourcove/cove-extensions-ui/releases/download/v$Version/$packageName"
    }
  ]
}
"@
