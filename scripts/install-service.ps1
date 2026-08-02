param(
    [Parameter(Mandatory = $true)]
    [string]$AgentExecutable,
    [Parameter(Mandatory = $true)]
    [string]$ConfigurationFile,
    [Parameter(Mandatory = $true)]
    [System.Management.Automation.PSCredential]$Credential,
    [string]$ServiceName = "RewindAgent"
)

$ErrorActionPreference = "Stop"
$executable = [System.IO.Path]::GetFullPath($AgentExecutable)
$configuration = [System.IO.Path]::GetFullPath($ConfigurationFile)
if (!(Test-Path -LiteralPath $executable -PathType Leaf))
{
    throw "Agent executable not found: $executable"
}
if (!(Test-Path -LiteralPath $configuration -PathType Leaf))
{
    throw "Configuration file not found: $configuration"
}

$binaryPath = "`"$executable`" --config `"$configuration`""
New-Service `
    -Name $ServiceName `
    -BinaryPathName $binaryPath `
    -Credential $Credential `
    -DisplayName "Rewind Agent" `
    -Description "Local incident flight recorder for machine software." `
    -StartupType Automatic

Write-Output "Installed $ServiceName. Start it with: Start-Service -Name '$ServiceName'"
