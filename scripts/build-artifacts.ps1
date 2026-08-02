param(
    [string]$Configuration = "Release",
    [string]$OutputDirectory = "$PSScriptRoot\..\artifacts\dist",
    [string]$RepositoryUrl = ""
)

$ErrorActionPreference = "Stop"
$repository = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$output = [System.IO.Path]::GetFullPath($OutputDirectory)
$packages = Join-Path $output "packages"
$agent = Join-Path $output "agent-win-x64"

foreach ($directory in @($packages, $agent))
{
    if (Test-Path -LiteralPath $directory)
    {
        $resolvedDirectory = [System.IO.Path]::GetFullPath($directory)
        if (!$resolvedDirectory.StartsWith(
            $output + [System.IO.Path]::DirectorySeparatorChar,
            [System.StringComparison]::OrdinalIgnoreCase))
        {
            throw "Refusing to clean artifact directory outside output root: $resolvedDirectory"
        }

        Remove-Item -LiteralPath $resolvedDirectory -Recurse -Force
    }
}

foreach ($obsoletePath in @(
    (Join-Path $output "sdk-net462"),
    (Join-Path $output "sdk-netstandard2.0"),
    (Join-Path $output "rewind-sdk-net462.zip"),
    (Join-Path $output "rewind-sdk-netstandard2.0.zip")))
{
    if (Test-Path -LiteralPath $obsoletePath)
    {
        Remove-Item -LiteralPath $obsoletePath -Recurse -Force
    }
}

New-Item -ItemType Directory -Force -Path $packages, $agent | Out-Null
$packProperties = @()
if (![string]::IsNullOrWhiteSpace($RepositoryUrl))
{
    $packProperties += "-p:RepositoryUrl=$RepositoryUrl"
}
dotnet pack (Join-Path $repository "src\Rewind.Abstractions\Rewind.Abstractions.csproj") -c $Configuration -o $packages @packProperties
dotnet pack (Join-Path $repository "src\Rewind.Protocol\Rewind.Protocol.csproj") -c $Configuration -o $packages @packProperties
dotnet pack (Join-Path $repository "src\Rewind.Sdk\Rewind.Sdk.csproj") -c $Configuration -o $packages @packProperties
dotnet publish (Join-Path $repository "src\Rewind.Agent.Host\Rewind.Agent.Host.csproj") `
    -c $Configuration `
    -r win-x64 `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $agent
Copy-Item -LiteralPath (Join-Path $repository "rewind-agent.example.json") -Destination $agent
Copy-Item -LiteralPath (Join-Path $repository "README.md") -Destination $agent
Copy-Item -LiteralPath (Join-Path $repository "schemas\rewind-agent.schema.json") -Destination $agent
Copy-Item -LiteralPath (Join-Path $repository "scripts\install-service.ps1") -Destination $agent
Copy-Item -LiteralPath (Join-Path $repository "scripts\uninstall-service.ps1") -Destination $agent
Copy-Item -LiteralPath (Join-Path $repository "docs\known-limitations.md") -Destination $agent
Copy-Item -LiteralPath (Join-Path $repository "docs\user-guide.md") -Destination $agent
Copy-Item -LiteralPath (Join-Path $repository "docs\sdk-integration.md") -Destination $agent
Copy-Item -LiteralPath (Join-Path $repository "docs\configuration.md") -Destination $agent
Copy-Item -LiteralPath (Join-Path $repository "docs\windows-service.md") -Destination $agent
Copy-Item -LiteralPath (Join-Path $repository "docs\troubleshooting.md") -Destination $agent
Copy-Item -LiteralPath (Join-Path $repository "docs\build-from-source.md") -Destination $agent

$zip = Join-Path $output "rewind-agent-win-x64.zip"
if (Test-Path -LiteralPath $zip)
{
    Remove-Item -LiteralPath $zip
}

Compress-Archive -Path (Join-Path $agent "*") -DestinationPath $zip
