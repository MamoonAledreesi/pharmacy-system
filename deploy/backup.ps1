<#
.SYNOPSIS
    Daily backup of the pharmacy database (compressed, checksummed, verified), keeping the last N days.
.EXAMPLE
    .\backup.ps1 -BackupFolder D:\PharmacyBackups -KeepDays 30
#>
[CmdletBinding()]
param(
    [string]$ServerInstance = ".",
    [string]$Database = "PharmacyDb",
    [Parameter(Mandatory = $true)][string]$BackupFolder,
    [int]$KeepDays = 30
)

$ErrorActionPreference = "Stop"

function Invoke-Sql([string]$Sql) {
    $connection = New-Object System.Data.SqlClient.SqlConnection "Server=$ServerInstance;Database=master;Trusted_Connection=True;TrustServerCertificate=True"
    $connection.Open()
    try {
        $command = $connection.CreateCommand()
        $command.CommandText = $Sql
        $command.CommandTimeout = 0
        [void]$command.ExecuteNonQuery()
    }
    finally {
        $connection.Close()
    }
}

New-Item -ItemType Directory -Force -Path $BackupFolder | Out-Null
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$file = Join-Path $BackupFolder "$Database-$stamp.bak"
$log = Join-Path $BackupFolder "backup.log"

try {
    Invoke-Sql "BACKUP DATABASE [$Database] TO DISK = N'$file' WITH COMPRESSION, CHECKSUM, INIT, NAME = N'$Database $stamp'"
    # A backup that cannot be read back is worthless: verify it right away.
    Invoke-Sql "RESTORE VERIFYONLY FROM DISK = N'$file' WITH CHECKSUM"

    Get-ChildItem -Path $BackupFolder -Filter "$Database-*.bak" |
        Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-$KeepDays) } |
        Remove-Item -Force

    Add-Content -Path $log -Value "$(Get-Date -Format s) OK     $file ($([math]::Round((Get-Item $file).Length / 1MB, 1)) MB)"
}
catch {
    Add-Content -Path $log -Value "$(Get-Date -Format s) FAILED $($_.Exception.Message)"
    throw
}
