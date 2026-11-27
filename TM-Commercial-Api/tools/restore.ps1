<#
.SYNOPSIS
  Restores a backup made by backup.ps1 (CAT-244): the database, the uploaded files and the encryption keys.

.DESCRIPTION
  Stop the API's app pool first (the script refuses while the database has other connections unless -Force).
  The database is restored over the existing one (WITH REPLACE) or under a new name with -Database.
  Settings are NOT copied over: compare settings\ in the backup with the server by hand (BACKUP.md, step 4).
  Rehearse on a test server before doing it on production.

.EXAMPLE
  .\restore.ps1 -BackupFolder D:\Backups\TMPB\2026-11-23_0200 -SqlServer . -ApiFolder C:\inetpub\TM-Commercial-Api
.EXAMPLE
  # A copy beside production, to check a backup or recover single records:
  .\restore.ps1 -BackupFolder D:\Backups\TMPB\2026-11-23_0200 -Database TMPB_RestoreTest -SkipFiles
#>
param(
  [Parameter(Mandatory = $true)][string]$BackupFolder,
  [string]$SqlServer = '.',
  # The database to restore into. Another name = a side-by-side copy.
  [string]$Database = 'TMPB',
  [string]$ApiFolder = '',
  [string]$FileStoragePath = '',
  [string]$KeysPath = '',
  # Only the database (e.g. a check copy).
  [switch]$SkipFiles,
  # Close other connections to the database instead of refusing.
  [switch]$Force,
  [string]$SqlUser = '',
  [string]$SqlPassword = ''
)
$ErrorActionPreference = 'Stop'

function Invoke-Sql([string]$query, [switch]$Scalar) {
  [string[]]$auth = if ($SqlUser) { @('-U', $SqlUser, '-P', $SqlPassword) } else { @('-E') }
  $out = & sqlcmd -S $SqlServer @auth -b -C -h -1 -W -Q "SET NOCOUNT ON; $query"
  if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed ($LASTEXITCODE): $($out -join ' ')" }
  if ($Scalar) { return ($out | Where-Object { $_ -and $_.Trim() } | Select-Object -First 1).Trim() }
  $out
}
function Read-Setting([string]$section, [string]$key) {
  if (-not $ApiFolder) { return '' }
  foreach ($name in 'appsettings.Production.json', 'appsettings.json') {
    $file = Join-Path $ApiFolder $name
    if (-not (Test-Path $file)) { continue }
    $value = (Get-Content $file -Raw -Encoding UTF8 | ConvertFrom-Json).$section.$key
    if ($value) { return [string]$value }
  }
  return ''
}

$bak = Get-ChildItem $BackupFolder -Filter '*.bak' | Select-Object -First 1
if (-not $bak) { throw "No .bak file in $BackupFolder" }

# Check the files against the manifest written by backup.ps1.
$manifest = Join-Path $BackupFolder 'manifest.csv'
if (Test-Path $manifest) {
  foreach ($row in Import-Csv $manifest) {
    $path = Join-Path $BackupFolder $row.File
    if (-not (Test-Path $path) -or (Get-FileHash $path -Algorithm SHA256).Hash -ne $row.SHA256) { throw "Backup file changed or missing: $($row.File)" }
  }
  Write-Host 'Manifest: all files match.'
}

# 1. Database
# A backup made outside backup.ps1 may have no checksums: then verify without them.
$checksum = 'CHECKSUM'
try { Invoke-Sql "RESTORE VERIFYONLY FROM DISK = N'$($bak.FullName)' WITH CHECKSUM;" | Out-Null }
catch {
  if ("$_" -notmatch 'does not contain checksum') { throw }
  Write-Warning 'This backup has no checksums (not made by backup.ps1); verifying without them.'
  $checksum = 'CONTINUE_AFTER_ERROR'
  Invoke-Sql "RESTORE VERIFYONLY FROM DISK = N'$($bak.FullName)';" | Out-Null
}
$exists = Invoke-Sql "SELECT COUNT(*) FROM sys.databases WHERE name = N'$Database';" -Scalar
if ($exists -eq '1') {
  $others = Invoke-Sql "SELECT COUNT(*) FROM sys.dm_exec_sessions WHERE database_id = DB_ID(N'$Database') AND session_id <> @@SPID;" -Scalar
  if ([int]$others -gt 0 -and -not $Force) { throw "$others connection(s) to $Database. Stop the API's app pool, or pass -Force." }
  if ([int]$others -gt 0) { Invoke-Sql "ALTER DATABASE [$Database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;" | Out-Null }
}
# Put the data and log files where this server keeps its databases, under the target name.
$dataDir = Invoke-Sql "SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(400));" -Scalar
$logDir = Invoke-Sql "SELECT CAST(SERVERPROPERTY('InstanceDefaultLogPath') AS nvarchar(400));" -Scalar
$files = Invoke-Sql "SET NOCOUNT ON; DECLARE @f TABLE (LogicalName nvarchar(128), PhysicalName nvarchar(260), Type char(1), FileGroupName nvarchar(128), Size numeric(20,0), MaxSize numeric(20,0), FileId bigint, CreateLSN numeric(25,0), DropLSN numeric(25,0), UniqueId uniqueidentifier, ReadOnlyLSN numeric(25,0), ReadWriteLSN numeric(25,0), BackupSizeInBytes bigint, SourceBlockSize int, FileGroupId int, LogGroupGUID uniqueidentifier, DifferentialBaseLSN numeric(25,0), DifferentialBaseGUID uniqueidentifier, IsReadOnly bit, IsPresent bit, TDEThumbprint varbinary(32), SnapshotUrl nvarchar(360)); INSERT @f EXEC('RESTORE FILELISTONLY FROM DISK = N''$($bak.FullName)'''); SELECT LogicalName + '|' + Type FROM @f;"
$moves = @()
$n = 0
foreach ($line in $files | Where-Object { $_ -match '\|' }) {
  $logical, $type = $line.Trim().Split('|')
  $n++
  $dir = if ($type -eq 'L') { $logDir } else { $dataDir }
  $ext = if ($type -eq 'L') { '_log.ldf' } elseif ($n -eq 1) { '.mdf' } else { "_$n.ndf" }
  $moves += "MOVE N'$logical' TO N'$(Join-Path $dir "$Database$ext")'"
}
Write-Host "1/3 restoring database $Database from $($bak.Name)"
Invoke-Sql "RESTORE DATABASE [$Database] FROM DISK = N'$($bak.FullName)' WITH REPLACE, $(if ($checksum -eq 'CHECKSUM') { 'CHECKSUM, ' } else { '' })RECOVERY, $($moves -join ', '), STATS = 25;" | Out-Null
Invoke-Sql "ALTER DATABASE [$Database] SET MULTI_USER;" | Out-Null

if ($SkipFiles) { Write-Host "Done (database only): $Database"; return }

# 2. Uploaded files: replaced by the backup's (the folder's current content is kept beside it, renamed).
if (-not $FileStoragePath) { $FileStoragePath = Read-Setting 'FileStorage' 'RootPath' }
if (-not $FileStoragePath -and $ApiFolder) { $FileStoragePath = Join-Path $ApiFolder 'file-storage' }
$zip = Join-Path $BackupFolder 'file-storage.zip'
if ((Test-Path $zip) -and $FileStoragePath) {
  Write-Host "2/3 files -> $FileStoragePath"
  if (Test-Path $FileStoragePath) { Rename-Item $FileStoragePath "$FileStoragePath.before-restore-$(Get-Date -Format yyyyMMddHHmm)" }
  Expand-Archive $zip -DestinationPath $FileStoragePath
} else { Write-Warning 'Files not restored (no file-storage.zip or no -ApiFolder / -FileStoragePath).' }

# 3. Encryption keys
if (-not $KeysPath) { $KeysPath = Read-Setting 'DataProtection' 'KeysPath' }
$keys = Join-Path $BackupFolder 'dataprotection-keys.zip'
if ((Test-Path $keys) -and $KeysPath) {
  Write-Host "3/3 keys -> $KeysPath"
  Expand-Archive $keys -DestinationPath $KeysPath -Force
} else { Write-Warning 'Keys not restored: enter the SMS API key and LDAP password again in the UI after starting.' }

Write-Host 'Done. Compare settings\ with the server (BACKUP.md step 4), then start the app pool and check (step 5).'
