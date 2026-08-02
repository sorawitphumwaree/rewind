param([string]$ArtifactDirectory = "$PSScriptRoot\..\artifacts\dist")

$ErrorActionPreference = "Stop"
$root = [System.IO.Path]::GetFullPath($ArtifactDirectory)
$assets = @(
    (Join-Path $root "rewind-agent-win-x64.zip"),
    (Join-Path $root "sbom.spdx.json")
)
foreach ($asset in $assets)
{
    if (!(Test-Path -LiteralPath $asset -PathType Leaf))
    {
        throw "Release asset not found: $asset"
    }
}

$assets |
    ForEach-Object { Get-FileHash -LiteralPath $_ -Algorithm SHA256 } |
    ForEach-Object { "$($_.Hash.ToLowerInvariant())  $([System.IO.Path]::GetFileName($_.Path))" } |
    Set-Content -LiteralPath (Join-Path $root "SHA256SUMS.txt") -Encoding utf8
