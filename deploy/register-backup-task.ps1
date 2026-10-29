<#
.SYNOPSIS
    Schedules backup.ps1 to run every day (default 02:00) as SYSTEM.
.EXAMPLE
    .\register-backup-task.ps1 -BackupFolder D:\PharmacyBackups
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$BackupFolder,
    [string]$At = "02:00",
    [int]$KeepDays = 30,
    [string]$Database = "PharmacyDb",
    [string]$TaskName = "Pharmacy daily backup"
)

$ErrorActionPreference = "Stop"

$script = Join-Path $PSScriptRoot "backup.ps1"
$arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$script`" -BackupFolder `"$BackupFolder`" -KeepDays $KeepDays -Database `"$Database`""
$action = New-ScheduledTaskAction -Execute "powershell.exe" -Argument $arguments
$trigger = New-ScheduledTaskTrigger -Daily -At $At
$principal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -LogonType ServiceAccount -RunLevel Highest

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Principal $principal -Force | Out-Null
Write-Host "Scheduled '$TaskName' daily at $At. Backups go to $BackupFolder (kept $KeepDays days)."
