param([string]$ServiceName = "RewindAgent")

$ErrorActionPreference = "Stop"
$service = Get-Service -Name $ServiceName -ErrorAction Stop
if ($service.Status -ne "Stopped")
{
    Stop-Service -Name $ServiceName
    $service.WaitForStatus("Stopped", [TimeSpan]::FromSeconds(30))
}

& sc.exe delete $ServiceName
if ($LASTEXITCODE -ne 0)
{
    throw "Windows Service removal failed with exit code $LASTEXITCODE."
}

Write-Output "Removed $ServiceName."
