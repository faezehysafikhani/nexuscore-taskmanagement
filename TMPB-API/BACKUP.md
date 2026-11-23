# Backup and restore — TMPB (CAT-244)

## 1. What makes up the system

| What | Where | Without it after a restore |
|---|---|---|
| **Database** | SQL Server, database `TMPB` (`ConnectionStrings:DefaultConnection`; chat uses the same database) | everything is lost |
| **Uploaded files** (task attachments, chat files, documents, company logos) | `FileStorage:RootPath`; empty = `<API folder>\file-storage` | the records stay, the files cannot be opened |
| **Encryption keys** (they protect the saved SMS panel API key and LDAP password) | `DataProtection:KeysPath` | the SMS key and LDAP password must be entered again |
| **Settings** | API: `appsettings.Production.json`, `web.config`; UI: `web.config`, `config\runtime-config.json` | the JWT key, connection string, CORS and switches must be set again |

The UI and API program files are not backed up: they come from the release package.

**Set `DataProtection:KeysPath` and `FileStorage:RootPath` to folders outside the site folder**
(for example `D:\TMPB-Data\keys` and `D:\TMPB-Data\files`). Otherwise a reinstall of the site can delete them.

## 2. Backup

`tools\backup.ps1` takes all four into one dated folder (`2026-11-23_0200`), checks the database backup and
writes `manifest.csv` (size and SHA-256 of every file):

```
.\backup.ps1 -SqlServer . -Destination D:\Backups\TMPB -ApiFolder C:\inetpub\TMPB-API -UiFolder C:\inetpub\TMPB-UI -KeepDays 14
```

- The API keeps running; nothing is stopped.
- The database is backed up first, then the files. So every file the database refers to is in the backup.
- `COPY_ONLY`: if a DBA has their own backup plan (full + log), this one does not break its chain.
- **The SQL Server service account needs write access to `-Destination`.** The backup file is written by SQL
  Server, not by the script. Without access the script stops with «SQL Server cannot write to …».
- On SQL Server Express (no compression) the backup is taken uncompressed.
- The run needs: `sqlcmd`, a Windows user with `db_backupoperator` (or sysadmin) and read access to the folders.
  For a SQL login: `-SqlUser` / `-SqlPassword`.

**The backup folder contains secrets** (JWT key, connection string, encryption keys): only administrators may
read it.

### Schedule

| | Recommendation |
|---|---|
| How often | every night (Task Scheduler, at an hour with no use, e.g. 02:00) |
| Keep | 14 daily (`-KeepDays 14`); also one copy a month kept for a year |
| Where | **not only on the same server**: copy the folder to another server or storage every day |
| Losing less than a day | the DBA adds a full recovery model with a transaction log backup every 15 to 60 minutes; this script does not replace it |

Task Scheduler, as a user with the rights above:

```
powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\inetpub\TMPB-API\tools\backup.ps1 -Destination D:\Backups\TMPB -ApiFolder C:\inetpub\TMPB-API -UiFolder C:\inetpub\TMPB-UI
```

Check the result: the task's last run result must be 0, and a new dated folder must exist.

## 3. Restore

**Rehearse first** on a test server or under another name. A backup that was never restored is not known to work:

```
.\restore.ps1 -BackupFolder D:\Backups\TMPB\2026-11-23_0200 -Database TMPB_RestoreTest -SkipFiles
```

Restoring production:

1. Stop the API's app pool (and tell users).
2. ```
   .\restore.ps1 -BackupFolder D:\Backups\TMPB\2026-11-23_0200 -SqlServer . -ApiFolder C:\inetpub\TMPB-API
   ```
   The script:
   - checks every file against `manifest.csv` and stops if one changed;
   - refuses while there are still connections to the database (`-Force` closes them);
   - restores the database over the existing one;
   - replaces the files folder (the current one is kept beside it as `file-storage.before-restore-<time>`);
   - restores the keys.
3. On a new server, first install the release (API, UI) and SQL Server, then run the script.
4. **Settings**: compare `settings\` in the backup with the server by hand. The script does not overwrite them.
   On a new server, copy them, then change what differs (server name in the connection string, addresses in
   `Cors:AllowedOrigins`).
5. Start the app pool and check:
   - sign-in works;
   - a task with an attachment opens its file;
   - Settings → SMS panel still shows the key as saved (if not, the keys were not restored: enter it again);
   - `db\check-upgrades.sql` says all steps are applied. If the backup is older than the installed release, run the
     missing upgrade scripts (`DEPLOYMENT-CONFIG.md`, section 6.1).

Restored to an earlier point, everything after that point is gone: tasks, messages and files created since.
Recovering a few records instead (one deleted task): restore under another name (`-Database TMPB_Copy
-SkipFiles`) and copy the rows across; ask the developers.

## 4. Checked

The scripts were run against SQL Server 2022:
- backup, then damage (rows changed, a file deleted, a file changed, a key deleted), then restore: the
  database matched the backup row for row, and the files and keys came back;
- the refusal with an open connection, and `-Force`;
- a side-by-side copy under another name (its own data and log files);
- a backup made without checksums (not by this script): verified and restored with a warning.
