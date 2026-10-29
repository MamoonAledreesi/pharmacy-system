<#
.SYNOPSIS
    Installs the published pharmacy application as a Windows service and opens its HTTPS port.
.DESCRIPTION
    Run from an elevated PowerShell. Publish first:
        dotnet publish src\Pharmacy.Web -c Release -o C:\Pharmacy\app
    then copy deploy\appsettings.Production.template.json to C:\Pharmacy\app\appsettings.Production.json and edit it.
.EXAMPLE
    .\install-service.ps1 -AppFolder C:\Pharmacy\app
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$AppFolder,
    [string]$ServiceName = "PharmacySystem",
    [int]$Port = 443
)

$ErrorActionPreference = "Stop"

$exe = Join-Path $AppFolder "Pharmacy.Web.exe"
if (-not (Test-Path $exe)) {
    throw "Pharmacy.Web.exe not found in $AppFolder. Publish the application there first."
}

if (-not (Test-Path (Join-Path $AppFolder "appsettings.Production.json"))) {
    throw "appsettings.Production.json is missing in $AppFolder (copy deploy\appsettings.Production.template.json and edit it)."
}

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Stop-Service -Name $ServiceName -Force
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

New-Service -Name $ServiceName -BinaryPathName "`"$exe`" --environment=Production" `
    -DisplayName "Pharmacy System" -Description "Pharmacy management system" -StartupType Automatic | Out-Null

# Restart automatically if the service ever stops unexpectedly.
sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/30000/restart/60000 | Out-Null

if (-not (Get-NetFirewallRule -DisplayName "Pharmacy System HTTPS" -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName "Pharmacy System HTTPS" -Direction Inbound -Protocol TCP -LocalPort $Port -Action Allow -Profile Private,Domain | Out-Null
}

Start-Service -Name $ServiceName
Write-Host "Service '$ServiceName' installed and started. Open https://<server-name>:$Port from a pharmacy computer."
