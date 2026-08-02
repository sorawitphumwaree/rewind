param([string]$ArtifactDirectory = "$PSScriptRoot\..\artifacts\dist")

$ErrorActionPreference = "Stop"
$root = [System.IO.Path]::GetFullPath($ArtifactDirectory)
$repository = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
[xml]$buildProperties = Get-Content -LiteralPath (Join-Path $repository "Directory.Build.props")
$version = [string]$buildProperties.Project.PropertyGroup.Version
$zip = Join-Path $root "rewind-agent-win-x64.zip"
if (!(Test-Path -LiteralPath $zip))
{
    throw "Agent ZIP not found: $zip"
}
$checksumPath = Join-Path $root "SHA256SUMS.txt"
if (!(Test-Path -LiteralPath $checksumPath -PathType Leaf))
{
    throw "Release checksum manifest not found: $checksumPath"
}
$expectedHashes = @{}
foreach ($line in Get-Content -LiteralPath $checksumPath)
{
    if ($line -match '^([0-9a-f]{64})  (.+)$')
    {
        $expectedHashes[$Matches[2]] = $Matches[1]
    }
}
foreach ($assetName in @("rewind-agent-win-x64.zip", "sbom.spdx.json"))
{
    $actual = (Get-FileHash -LiteralPath (Join-Path $root $assetName) -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($expectedHashes[$assetName] -ne $actual)
    {
        throw "Checksum mismatch for release asset: $assetName"
    }
}
$packageDirectory = Join-Path $root "packages"
$packagePaths = @{
    "Rewind.Abstractions" = Join-Path $packageDirectory "Rewind.Abstractions.$version.nupkg"
    "Rewind.Protocol" = Join-Path $packageDirectory "Rewind.Protocol.$version.nupkg"
    "Rewind.Sdk" = Join-Path $packageDirectory "Rewind.Sdk.$version.nupkg"
}
foreach ($packagePath in $packagePaths.Values)
{
    if (!(Test-Path -LiteralPath $packagePath))
    {
        throw "NuGet package not found: $packagePath"
    }
    $symbolPath = [System.IO.Path]::ChangeExtension($packagePath, ".snupkg")
    if (!(Test-Path -LiteralPath $symbolPath))
    {
        throw "NuGet symbol package not found: $symbolPath"
    }
}

$temporary = Join-Path ([System.IO.Path]::GetTempPath()) "rewind-artifact-$([guid]::NewGuid().ToString('N'))"
try
{
    Expand-Archive -LiteralPath $zip -DestinationPath $temporary
    $agent = Join-Path $temporary "Rewind.Agent.Host.exe"
    if (!(Test-Path -LiteralPath $agent))
    {
        throw "Published Agent executable is missing."
    }

    $process = Start-Process -FilePath $agent -ArgumentList @("--pipe", "Rewind.Artifact.Verify", "--data", (Join-Path $temporary "data")) -PassThru -WindowStyle Hidden
    Start-Sleep -Seconds 1
    if ($process.HasExited)
    {
        throw "Published Agent exited unexpectedly with code $($process.ExitCode)."
    }

    Stop-Process -Id $process.Id
    Wait-Process -Id $process.Id -Timeout 10 -ErrorAction SilentlyContinue

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    foreach ($package in $packagePaths.GetEnumerator())
    {
        $archive = [System.IO.Compression.ZipFile]::OpenRead($package.Value)
        try
        {
            foreach ($framework in @("net462", "netstandard2.0"))
            {
                $assembly = "lib/$framework/$($package.Key).dll"
                if (!($archive.Entries | Where-Object FullName -eq $assembly))
                {
                    throw "NuGet package is missing $assembly`: $($package.Value)"
                }
            }

            $nuspec = $archive.Entries | Where-Object FullName -Like "*.nuspec" | Select-Object -First 1
            $reader = [System.IO.StreamReader]::new($nuspec.Open())
            try
            {
                [xml]$metadata = $reader.ReadToEnd()
            }
            finally
            {
                $reader.Dispose()
            }

            if ($metadata.package.metadata.version -ne $version)
            {
                throw "Unexpected package version in $($package.Value)."
            }

            if ($package.Key -eq "Rewind.Sdk")
            {
                $dependencies = @($metadata.package.metadata.dependencies.group.dependency.id)
                if ($dependencies -notcontains "Rewind.Abstractions" -or
                    $dependencies -notcontains "Rewind.Protocol")
                {
                    throw "Rewind.Sdk does not declare both transitive dependencies."
                }
            }
        }
        finally
        {
            $archive.Dispose()
        }
    }
}
finally
{
    if (Test-Path -LiteralPath $temporary)
    {
        $resolvedTemporary = [System.IO.Path]::GetFullPath($temporary)
        $temporaryRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
        if (!$resolvedTemporary.StartsWith($temporaryRoot, [System.StringComparison]::OrdinalIgnoreCase) -or
            !([System.IO.Path]::GetFileName($resolvedTemporary).StartsWith("rewind-artifact-", [System.StringComparison]::Ordinal)))
        {
            throw "Refusing to remove unexpected verification path: $resolvedTemporary"
        }

        $removed = $false
        for ($attempt = 0; $attempt -lt 10 -and !$removed; $attempt++)
        {
            try
            {
                Remove-Item -LiteralPath $temporary -Recurse -Force
                $removed = $true
            }
            catch [System.UnauthorizedAccessException]
            {
                Start-Sleep -Milliseconds 100
            }
        }

        if (!$removed)
        {
            throw "Unable to remove verification directory after the Agent stopped: $resolvedTemporary"
        }
    }
}
