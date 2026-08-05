# Operational Runbook

Written to IMPLEMENTATION_PLAN.md §M8.8's exact list. Every command/threshold below was read
directly out of the current code (file:line noted) rather than written from memory, so it should
stay accurate as long as the referenced files don't change out from under it.

## 1. Checking queue lag, and what to do about a backlog

Two independent signals, both scoped to `sms:otp` only (the architecture's core promise is that OTP
never waits behind bulk, so bulk lag is intentionally not alerted on the same way):

- **`/health/ready`** — `OtpQueueLagHealthCheck` (`src/Shortener.Infrastructure/Retention/OtpQueueLagHealthCheck.cs:13,30-32`)
  reports Unhealthy once the oldest pending (unacked) `sms:otp` entry has been idle **> 60s**.
- **Worker log** — `QueueMaintenanceService` (`src/Shortener.Worker/Workers/QueueMaintenanceService.cs:22,102-106`)
  logs `LogCritical` at **> 30s** idle, checked every 30s tick. This is the earlier-warning signal —
  act on this before it becomes an `/health/ready` failure.
- **Admin dashboard** — `GET /Reports/Dashboard` shows OTP lag and pending count inline (no
  dedicated queue page). Bulk-stream lag isn't surfaced in Admin at all; check it via `XPENDING
  sms:bulk bulk-workers` directly if you need it.

**Diagnose:**
```
redis-cli XPENDING sms:otp otp-workers          # summary: count, min/max id, per-consumer counts
redis-cli XPENDING sms:otp otp-workers - + 20    # detail: which entries, how long idle, delivery count
```
- If pending count is low but idle time is high → the Worker process is likely down or stuck.
  Check `docker ps`/service status, then the Worker's own `/health/live`.
- If pending count is high and growing → the Worker is running but can't keep up, or the "fake"/real
  SMS provider is timing out on every call (check Worker logs for provider errors).
- Recovery is automatic once the Worker is healthy again: `StreamClaimService.ClaimEligibleAsync`
  (`src/Shortener.Infrastructure/Queueing/StreamClaimService.cs`) reclaims anything idle past
  `ClaimIdleMinutes` (5 min) every `ClaimInterval` (2 min) — no manual XCLAIM needed in the normal
  case. Only intervene manually if the automatic claimer itself isn't running (Worker down).

## 2. Resending `sms:dead` messages

**There is no Admin UI that reads `sms:dead` directly.** `POST /Reports/ResendSms`
(`ReportsController.Sms.cs:83-105`, policy `OperatorOrAbove`) resends `SmsMessage` rows with
`Status=Failed AND MessageType=DownloadLink` — it explicitly **excludes Otp** dead-letters
(lines 91-95), and works off the `SmsMessages` table, not the `sms:dead` stream itself (both dead-letter
paths already set `Status=Failed`, so in practice this button covers most DownloadLink dead-letters).
It does not reset `TryCount` before requeueing.

For anything the UI doesn't cover (an Otp dead-letter, or resending by stream entry rather than by
known SmsMessage id), do it by hand:

```sql
-- 1. Find the message (or read smsMessageId straight off a sms:dead XRANGE entry)
SELECT Id, Status, LastError, MessageType, TryCount FROM SmsMessages WHERE Id = @id;

-- 2. Reset it to Queued so the next XREADGROUP/claim cycle picks it up again
UPDATE SmsMessages SET Status = 1 /* Queued */, LastError = NULL, TryCount = 0 WHERE Id = @id;
```
```
# 3. Re-add it to whichever stream matches MessageType (DownloadLink -> sms:bulk, Otp -> sms:otp)
redis-cli XADD sms:bulk '*' smsMessageId <id>
```

`sms:dead` itself is **never trimmed** — `QueueMaintenanceService`'s hourly trim only touches
`sms:otp`/`sms:bulk` (`QueueMaintenanceService.cs:49-54`). If it's been live a long time, periodically
review and `XTRIM`/archive it manually so it doesn't grow unbounded.

## 3. Restoring a file from backup

Per doc §M8.7, file backup is a **volume-level snapshot**, not a file-by-file copy (copying millions
of small files individually is far too slow to be a viable daily backup strategy at this scale).
Practically:

1. Identify the file's `StorageKey` from `StoredFiles` (e.g. `2026/08/05/a3/<guid>.bin`) — this is
   its path relative to `FileStorage:RootPath`.
2. Mount or restore the relevant volume snapshot to a temporary location (mechanism depends on the
   host: VSS shadow copy on Windows/IIS, LVM/ZFS snapshot restore on Linux, or the cloud
   provider's volume-snapshot restore for a containerized deployment).
3. Copy just that one file (`<snapshot-mount>/<StorageKey>`) back into the live `RootPath` at the
   same relative path — do **not** restore the whole volume over the live one unless you intend to
   roll back every file changed since the snapshot.
4. Verify: `StoredFiles.Sha256` for that row should match a fresh hash of the restored file.
5. If the DB row's `Status` was already `Deleted` (past its retention grace period), decide
   deliberately whether restoring makes sense — the retention job will just delete it again on its
   next pass unless `FileExpiresAt`/`Status` are also adjusted.

## 4. Rotating a client's API key

`ApiKeysController` (`src/Shortener.Admin/Controllers/ApiKeysController.cs`), policy `OperatorOrAbove`.
**There is no single "regenerate" action** — rotation is two separate steps, and the old key stays
active until you explicitly revoke it:

1. `GET/POST /ApiKeys/Create?clientId=<id>` — creates a new, immediately-active key. The raw key is
   shown exactly once on the confirmation page (never persisted or displayable again) — get it to the
   client before navigating away.
2. Update the client's integration to use the new key.
3. Once confirmed working, `POST /ApiKeys/Revoke` (form fields `id`, `clientId`) on the **old** key —
   sets `IsActive=false`, `RevokedAt=UtcNow`, and deletes its Redis cache entry
   (`apikey:{KeyHash}`) so it stops authenticating immediately rather than waiting out the 5-minute
   cache TTL.

Skipping step 3 is the most likely operator mistake — both keys stay valid indefinitely otherwise.

## 5. Adding a new SMS provider

Five files, all keyed by the same provider `code` string (must match exactly across every one):

1. `src/Shortener.Infrastructure/SmsProviders/<Name>Provider.cs` — implement `ISmsProvider`,
   following `KavenegarProvider.cs`'s shape (map the provider's real status codes to
   `SmsSendResult.IsRetryable` correctly — this is what keeps a provider outage from silently
   dead-lettering messages that should have retried).
2. `SmsHttpClientNames.cs` — add the named-HttpClient constant.
3. `DependencyInjection.cs` (~line 86-96) — `AddKeyedScoped<ISmsProvider, <Name>Provider>("code")`
   plus `AddHttpClient(name, ...).AddSmsRetryPolicy()`.
4. `SmsProviderFieldCatalog.cs` (~line 13-20) — add the `"code" => [...]` case; this drives which
   credential fields the Admin SmsAccount create/edit form renders.
5. `DbSeeder.cs`'s `SeedSmsProvidersAsync` (~line 89-95) — add the `SmsProvider` row.

**Production caveat**: `DbSeeder.SeedAsync` only runs when `Environment.IsDevelopment()` (checked in
both Admin's and Api's `Program.cs`). In a real deployment, step 5 doesn't happen automatically —
insert the `SmsProviders` row manually, since there's no Admin UI for managing the provider catalog
itself (only `SmsAccounts`, which reference it).

## 6. Disk full response

`FileStorageOptions`: `DiskWarningThresholdPercent=70`, `DiskRejectThresholdPercent=90`.

- **≥ 70% used** → `DiskSpaceHealthCheck` reports `/health/ready` as Degraded. Early warning —
  uploads still work.
- **> 90% used** → `/health/ready` reports Unhealthy, **and** new uploads are actively rejected with
  HTTP 507 (checked in `LinksEndpoints.cs` before the file body is even read, `ErrorCodes.DiskFull`).
  Downloads and OTP verification keep working — only new uploads stop.

**Response, in order:**
1. Confirm which volume — `FileStorage:RootPath` should be on its own volume (see the IIS/Docker
   deployment docs); check that volume specifically, not just system-wide disk usage.
2. Check whether the retention job (`RetentionJobRunner`, nightly 1-5 AM window) and orphan scan
   (`OrphanScanJobRunner`) are both running — a stuck Worker means expired files aren't being
   physically deleted even though the DB thinks they should be.
3. If space is needed immediately: manually trigger physical deletion of already-`Expired`/
   `PendingDelete` files rather than waiting for the nightly window, or provision more disk. Do not
   delete files directly off disk without going through the app — that leaves `StoredFiles` rows
   pointing at nothing, which `OrphanScanJobRunner` will flag but not silently fix.
4. Once back under 90%, uploads resume automatically — no restart needed, the check is live per-request.

## 7. Peak-day checklist

Run through this before a known high-traffic day (a big batch upload, a marketing push driving
download volume, etc.):

- [ ] `/health/ready` is `Healthy` on every host (Api, PublicWeb, Worker) — not just `/health/live`.
- [ ] Disk usage on the `FileStorage:RootPath` volume is comfortably under 70% (see §6) — headroom for
      the day's expected upload volume, not just current usage.
- [ ] Queue lag is near-zero on both `sms:otp` and `sms:bulk` *before* the day starts — starting with
      an existing backlog eats into the 10s OTP-latency budget under the day's own load.
- [ ] Every active client that will send OTP traffic today has an active `Purpose=Otp` SmsAccount +
      template (`OtpAccountCoverageHealthCheck` covers this on `/health/ready`, but verify for the
      *specific* clients expected to be busy, not just "no coverage gaps at all").
- [ ] `PermitLimit`/rate-limit ceilings (60/min per IP on `/s/{code}` + OTP endpoints, doc §M8.5;
      per-client upload rate limit, §M3.1) are sized for the expected concurrent user count — a
      legitimate traffic spike can trip the same limiter built to stop enumeration attacks.
  - [ ] Confirm this *before* the day, since raising a `PermitLimit` requires a code change + deploy,
        not a runtime config flip.
- [ ] Recent backup completed successfully (§M8.7) — the worst day to discover a broken backup job is
      the day you actually need it.
- [ ] Someone is watching `/health/ready` + the Worker's Critical-level queue-lag logs during the
      window itself, not just checking once beforehand.
