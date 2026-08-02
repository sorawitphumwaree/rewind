param([string]$OutputPath = "$PSScriptRoot\..\artifacts\dist\sbom.spdx.json")

$ErrorActionPreference = "Stop"
$repository = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$output = [System.IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $output) | Out-Null

[xml]$buildProperties = Get-Content -LiteralPath (Join-Path $repository "Directory.Build.props")
$version = [string]$buildProperties.Project.PropertyGroup.Version
$dependencies = @{}
$projectPaths = Get-ChildItem -LiteralPath $repository -Filter *.csproj -Recurse |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
    Sort-Object FullName

foreach ($projectPath in $projectPaths)
{
    $result = dotnet list $projectPath.FullName package --include-transitive --format json |
        ConvertFrom-Json
    foreach ($project in $result.projects)
    {
        foreach ($framework in $project.frameworks)
        {
            foreach ($package in @($framework.topLevelPackages) + @($framework.transitivePackages))
            {
                if ($null -ne $package -and ![string]::IsNullOrWhiteSpace($package.id))
                {
                    $resolvedVersion = if ($package.resolvedVersion)
                    {
                        [string]$package.resolvedVersion
                    }
                    else
                    {
                        [string]$package.requestedVersion
                    }
                    $dependencies["$($package.id)|$resolvedVersion"] = [ordered]@{
                        name = [string]$package.id
                        version = $resolvedVersion
                    }
                }
            }
        }
    }
}

$packages = @(
    [ordered]@{
        name = "Rewind"
        SPDXID = "SPDXRef-Package-Rewind"
        versionInfo = $version
        downloadLocation = "NOASSERTION"
        filesAnalyzed = $false
        licenseConcluded = "NOASSERTION"
        licenseDeclared = "Apache-2.0"
        copyrightText = "NOASSERTION"
    }
)

$index = 0
foreach ($dependency in $dependencies.Values | Sort-Object name, version)
{
    $index++
    $safeName = $dependency.name -replace '[^A-Za-z0-9.-]', '-'
    $packages += [ordered]@{
        name = $dependency.name
        SPDXID = "SPDXRef-Package-$safeName-$index"
        versionInfo = $dependency.version
        downloadLocation = "https://www.nuget.org/packages/$($dependency.name)/$($dependency.version)"
        filesAnalyzed = $false
        licenseConcluded = "NOASSERTION"
        licenseDeclared = "NOASSERTION"
        copyrightText = "NOASSERTION"
    }
}

$document = [ordered]@{
    spdxVersion = "SPDX-2.3"
    dataLicense = "CC0-1.0"
    SPDXID = "SPDXRef-DOCUMENT"
    name = "Rewind-$version"
    documentNamespace = "urn:uuid:$([guid]::NewGuid())"
    creationInfo = [ordered]@{
        created = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
        creators = @("Tool: Rewind scripts/generate-sbom.ps1")
    }
    packages = $packages
}

$document | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $output -Encoding utf8
