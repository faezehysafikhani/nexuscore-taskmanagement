<#
.SYNOPSIS
  Full backup of TMTejarat (CAT-244): the database, the uploaded files, the encryption keys and the settings, in one
  dated folder.

.DESCRIPTION
  Order matters: the database first, then the files. A file uploaded in between is then only an extra file
  in the backup (harmless); the other order could leave the database pointing at files the backup lacks.
  Run it as a user that may back up the database (sysadmin or db_backupoperator) and read the folders.
  Nothing is stopped: SQL Server backs up while the API runs.
  See BACKUP.md for the schedule, keeping copies off the server and restoring.

.EXAMPLE
  .\backup.ps1 -SqlServer . -Destination D:\Backups\TMTejarat -ApiFolder C:\inetpub\TMTejarat-API
.EXAMPLE
  .\backup.ps1 -SqlServer db-host -Destination \\backup-host\tmtejarat -ApiFolder C:\inetpub\TMTejarat-API -UiFolder C:\inetpub\TMTejarat-UI -KeepDays 30
#>
param(
  # SQL Server instance (as in the connection string); '.' = this server.
  [string]$SqlServer = '.',
  [string]$Database = 'TMTejarat',
  # Where the dated backup folders go. On a remote SQL Server, a share the SQL Server service account can write.
  [Parameter(Mandatory = $true)][string]$Destination,
  # The API's folder (holds appsettings.Production.json; the default file-storage folder is inside it).
  [Parameter(Mandatory = $true)][string]$ApiFolder,
  # Optional: the UI's folder (web.config, config\runtime-config.json).
  [string]$UiFolder = '',
  # Uploaded files. Empty: FileStorage:RootPath from appsettings, else <ApiFolder>\file-storage.
  [string]$FileStoragePath = '',
  # Encryption keys (SMS API key, LDAP password). Empty: DataProtection:KeysPath from appsettings.
  [string]$KeysPath = '',
  # Delete dated folders older than this many days; 0 = keep all.
  [int]$KeepDays = 14,
  # SQL login instead of Windows authentication.
  [string]$SqlUser = '',
  [string]$SqlPassword = ''
)
$ErrorActionPreference = 'Stop'

function Read-Setting([string]$section, [string]$key) {
  foreach ($name in 'appsettings.Production.json', 'appsettings.json') {
    $file = Join-Path $ApiFolder $name
    if (-not (Test-Path $file)) { continue }
    $json = Get-Content $file -Raw -Encoding UTF8 | ConvertFrom-Json
    $value = $json.$section.$key
    if ($value) { return [string]$value }
  }
  return ''
}

function Invoke-Sql([string]$query) {
  [string[]]$auth = if ($SqlUser) { @('-U', $SqlUser, '-P', $SqlPassword) } else { @('-E') }
  $out = & sqlcmd -S $SqlServer @auth -b -C -Q $query
  if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed ($LASTEXITCODE): $($out -join ' ')" }
  $out | Where-Object { $_ -match 'percent|processed|successfully' } | ForEach-Object { Write-Host "    $_" }
}

$stamp = Get-Date -Format 'yyyy-MM-dd_HHmm'
$target = Join-Path $Destination $stamp
New-Item -ItemType Directory -Force -Path $target | Out-Null
Write-Host "Backup to $target"

# 1. Database (full, compressed, checked). COPY_ONLY: does not disturb a DBA's own backup chain.
$bak = Join-Path $target "$Database.bak"
Write-Host "1/4 database $Database"
try { Invoke-Sql "BACKUP DATABASE [$Database] TO DISK = N'$bak' WITH COPY_ONLY, COMPRESSION, CHECKSUM, INIT, STATS = 25;" }
catch {
  if ("$_" -match 'Msg 3201|Operating system error 5') { throw "SQL Server cannot write to $target. Give the SQL Server service account write access to $Destination. $_" }
  if ("$_" -notmatch 'COMPRESSION') { throw }
  # SQL Server Express has no backup compression.
  Invoke-Sql "BACKUP DATABASE [$Database] TO DISK = N'$bak' WITH COPY_ONLY, CHECKSUM, INIT, STATS = 25;"
}
Invoke-Sql "RESTORE VERIFYONLY FROM DISK = N'$bak' WITH CHECKSUM;"

# 2. Uploaded files (task attachments, chat files, documents, logos).
if (-not $FileStoragePath) { $FileStoragePath = Read-Setting 'FileStorage' 'RootPath' }
if (-not $FileStoragePath) { $FileStoragePath = Join-Path $ApiFolder 'file-storage' }
Write-Host "2/4 files $FileStoragePath"
if (Test-Path $FileStoragePath) {
  Compress-Archive -Path (Join-Path $FileStoragePath '*') -DestinationPath (Join-Path $target 'file-storage.zip') -CompressionLevel Optimal -Force
} else {
  Write-Warning "No file-storage folder at $FileStoragePath (nothing uploaded yet?)"
}

# 3. Encryption keys: without them the saved SMS API key and LDAP password cannot be read after a restore.
if (-not $KeysPath) { $KeysPath = Read-Setting 'DataProtection' 'KeysPath' }
Write-Host "3/4 keys $KeysPath"
if ($KeysPath -and (Test-Path $KeysPath)) {
  Compress-Archive -Path (Join-Path $KeysPath '*') -DestinationPath (Join-Path $target 'dataprotection-keys.zip') -Force
} else {
  Write-Warning 'DataProtection:KeysPath is not set: after a restore on another server, enter the SMS API key and LDAP password again.'
}

# 4. Settings (they hold secrets: the JWT key, connection strings; keep the backup folder protected).
Write-Host '4/4 settings'
$settings = Join-Path $target 'settings'
New-Item -ItemType Directory -Force -Path (Join-Path $settings 'api') | Out-Null
Get-ChildItem $ApiFolder -Filter 'appsettings*.json' | Copy-Item -Destination (Join-Path $settings 'api')
if (Test-Path (Join-Path $ApiFolder 'web.config')) { Copy-Item (Join-Path $ApiFolder 'web.config') (Join-Path $settings 'api') }
if ($UiFolder) {
  New-Item -ItemType Directory -Force -Path (Join-Path $settings 'ui\config') | Out-Null
  foreach ($f in 'web.config', '.env') { if (Test-Path (Join-Path $UiFolder $f)) { Copy-Item (Join-Path $UiFolder $f) (Join-Path $settings 'ui') } }
  $rc = Join-Path $UiFolder 'config\runtime-config.json'
  if (Test-Path $rc) { Copy-Item $rc (Join-Path $settings 'ui\config') }
}

# What was taken, with sizes and hashes, so a restore can be checked against it.
Get-ChildItem $target -Recurse -File | Where-Object { $_.Name -ne 'manifest.csv' } | ForEach-Object {
  [pscustomobject]@{ File = $_.FullName.Substring($target.Length + 1); Bytes = $_.Length; SHA256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash }
} | Export-Csv (Join-Path $target 'manifest.csv') -NoTypeInformation -Encoding UTF8

if ($KeepDays -gt 0) {
  Get-ChildItem $Destination -Directory | Where-Object { $_.Name -match '^\d{4}-\d{2}-\d{2}_\d{4}$' -and $_.CreationTime -lt (Get-Date).AddDays(-$KeepDays) } |
    ForEach-Object { Write-Host "Removing old backup $($_.Name)"; Remove-Item $_.FullName -Recurse -Force }
}
Write-Host "Done: $target"
