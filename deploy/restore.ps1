<#
.SYNOPSIS
    Restores the pharmacy database from a backup file, replacing the current data.
.DESCRIPTION
    Stop the pharmacy service first. The current database is overwritten, so the script asks for confirmation
    (or pass -Force). Take a fresh backup of the current state first if there is any chance you need it.
.EXAMPLE
    .\restore.ps1 -BackupFile D:\PharmacyBackups\PharmacyDb-20261029-020000.bak
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$BackupFile,
    [string]$ServerInstance = ".",
    [string]$Database = "PharmacyDb",
    [switch]$Force
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $BackupFile)) {
    throw "Backup file not found: $BackupFile"
}

if (-not $Force) {
    $answer = Read-Host "This REPLACES the database '$Database' with '$BackupFile'. Type YES to continue"
    if ($answer -ne "YES") {
        Write-Host "Cancelled."
        return
    }
}

$connection = New-Object System.Data.SqlClient.SqlConnection "Server=$ServerInstance;Database=master;Trusted_Connection=True;TrustServerCertificate=True"
$connection.Open()
try {
    $command = $connection.CreateCommand()
    $command.CommandTimeout = 0
    foreach ($sql in @(
            "IF DB_ID(N'$Database') IS NOT NULL ALTER DATABASE [$Database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE",
            "RESTORE DATABASE [$Database] FROM DISK = N'$BackupFile' WITH REPLACE, CHECKSUM, RECOVERY",
            "ALTER DATABASE [$Database] SET MULTI_USER")) {
        $command.CommandText = $sql
        [void]$command.ExecuteNonQuery()
    }
}
finally {
    $connection.Close()
}

Write-Host "Restored '$Database' from '$BackupFile'. Start the pharmacy service and run Admin > Books check."
