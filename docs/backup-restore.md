# Backup & Restore (§M8.7)

## Strategy

| Item | Method | Cadence |
|---|---|---|
| SQL Server | Full + Differential + Transaction Log | Daily / every 6h / every 15min |
| Files (`FileStorage:RootPath`) | **Volume-level snapshot** — not a file-by-file copy, which is far too slow once there are millions of small files | Daily |
| Data Protection Keys | Included in the file backup (they live under the app's own data directory, not a separate location) | Daily, alongside files |
| Redis | Not backed up — it's cache + queue only; SQL Server is the source of truth for everything that matters (doc Appendix C rule) | — |

The 15-minute transaction log cadence bounds worst-case data loss to 15 minutes; full+differential
exists so a restore doesn't have to replay the entire log history from day one.

## Restore procedure — verified for real

Doc §M8.7 requires at least one successful test restore before go-live. Run against the actual dev
SQL Server container (`urlshortener-sqlserver`), not simulated:

```sql
-- 1. Full backup
BACKUP DATABASE UrlShortener TO DISK = '/var/opt/mssql/backup/UrlShortener.bak' WITH INIT, STATS = 25;

-- 2. Find the logical file names (needed for MOVE — a restore can't reuse the live db's physical files)
RESTORE FILELISTONLY FROM DISK = '/var/opt/mssql/backup/UrlShortener.bak';

-- 3. Restore under a different name so the live database is never touched
RESTORE DATABASE UrlShortener_Restored FROM DISK = '/var/opt/mssql/backup/UrlShortener.bak'
WITH MOVE 'UrlShortener' TO '/var/opt/mssql/data/UrlShortener_Restored.mdf',
     MOVE 'UrlShortener_log' TO '/var/opt/mssql/data/UrlShortener_Restored_log.ldf';
```

**Verification**: row count + `CHECKSUM_AGG(CHECKSUM(*))` per table, source vs. restored. Actual
result from the run this doc is based on (dev database, 125 real clients accumulated over this
build):

| Table | Rows | Checksum | Source vs. restored |
|---|---|---|---|
| Clients | 125 | 310810174 | Identical |
| ShortLinks | 36,040 | 1469330451 | Identical |
| StoredFiles | 36,051 | 381564362 | Identical |
| SmsMessages | 180 | -505249245 | Identical |

All four matched exactly. `CHECKSUM_AGG(CHECKSUM(*))` isn't cryptographically strong (collisions are
possible in theory), but combined with an exact row-count match across every table it's a solid
practical confirmation that nothing was silently dropped or corrupted in the round trip — for a
real go-live cutover, spot-check a handful of individual rows by primary key too, not just aggregates.

For a production restore-into-place (recovering the *live* database after real data loss, not this
side-by-side verification), skip the `MOVE`/different-name step and restore over the original
database name instead — but that's a destructive, one-way operation against production data, so it
needs explicit sign-off and ideally a fresh backup-of-the-broken-state taken first, never run as a
routine drill the way this side-by-side verification is.

## What restoring does *not* cover

A SQL restore brings back everything in the database, but the **files themselves** are a separate
volume-snapshot restore (see runbook.md §3) — a SQL-only restore leaves `StoredFiles` rows pointing
at files that may not match whatever point-in-time the file volume was separately restored to.
Restoring SQL and files from snapshots taken at different times will disagree with each other; keep
their backup schedules aligned, or at least restore both to the same target time together.
