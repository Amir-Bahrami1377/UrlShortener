# IIS Deployment Guide (Windows)

Documentation only — this sandbox has no real IIS server to deploy to and verify against. Written
to IMPLEMENTATION_PLAN.md §M8.6's exact spec; treat as a go-live checklist, not something already
proven end-to-end the way the Docker path was (built and run for real — see `docker-compose.yml`).

## Topology

Three ASP.NET Core apps behind IIS as independent sites/Application Pools, plus the Worker running
as a Windows Service (IIS does not host long-lived non-HTTP background processes well):

| Component | Hosting | Notes |
|---|---|---|
| `Shortener.Api` | IIS, dedicated App Pool `Api` | Internal or public, depending on who calls the upload API |
| `Shortener.Admin` | IIS, dedicated App Pool `Admin` | **Internal network or VPN only — never expose to the public internet** (doc §M8.5) |
| `Shortener.PublicWeb` | IIS, dedicated App Pool `PublicWeb` | Public-facing (OTP/download flow) |
| `Shortener.Worker` | Windows Service (NSSM or `sc create`) | Not IIS-hosted — it's BackgroundServices, not a request/response app |

Three separate Application Pools, each running as its own least-privilege identity — not one pool
shared across sites. A crash or memory issue in one app must not recycle the others.

## Prerequisites on the IIS box

- Windows Server with IIS + the **ASP.NET Core Hosting Bundle** for .NET 10 installed (this installs
  the `AspNetCoreModuleV2` IIS module all three sites' `web.config` reference).
- Each Application Pool set to **No Managed Code** / **.NET CLR Version: No Managed Code** — the
  Hosting Bundle's out-of-process/in-process ASP.NET Core module handles the CLR itself; IIS's own
  managed pipeline must stay out of the way.
- `IIS_IUSRS` and each pool identity need read/execute on the app's publish folder.

## HTTPS / SSL bindings

Doc §M8.5 requires HTTPS + HSTS everywhere; Appendix C lists procuring an SSL certificate for the
production domain as an organizational prerequisite. On IIS, TLS is terminated by **IIS itself**
via each site's HTTPS binding — unlike the Docker path (`docker-compose.yml`), which puts Caddy in
front of the apps as a separate reverse proxy (see `./Caddyfile`), there's no separate proxy layer
here, so no `UseForwardedHeadersForReverseProxy()`-style config is needed on the IIS path; each
app's own `UseHsts()`/`UseHttpsRedirection()` (already unconditional outside Development) works
against the connection IIS hands it directly.

1. Install the certificate into the server's certificate store (**Local Computer > Personal**) —
   via IIS Manager's own **Server Certificates** feature (import a `.pfx`), or `certutil`/PowerShell's
   `Import-PfxCertificate` for automation.
2. In IIS Manager, add an **https** binding on port 443 to each of the three sites (`Api`, `Admin`,
   `PublicWeb`), selecting that certificate. Use distinct hostnames (SNI) per site if they share one
   IP, matching whatever DNS names Appendix C's certificate actually covers.
3. Add an **http** binding on port 80 too, then redirect it to https — either an IIS URL Rewrite
   rule, or rely on the app's own `UseHttpsRedirection()` (already active); URL Rewrite is cheaper
   since it avoids round-tripping into the .NET pipeline just to redirect.
4. Confirm `UseHsts()` is actually taking effect once real traffic hits it — `Strict-Transport-
   Security` should be present on the response headers of a plain `curl -I https://<site>` from
   outside the box.

`Admin`'s binding should still only be reachable from the internal network/VPN (doc §M8.5) — a
valid HTTPS binding doesn't change that requirement, it's still a network/firewall-level control
(same caveat as the Docker path's Caddyfile).

## Publish

```powershell
dotnet publish src\Shortener.Api\Shortener.Api.csproj -c Release -o C:\inetpub\shortener-api
dotnet publish src\Shortener.Admin\Shortener.Admin.csproj -c Release -o C:\inetpub\shortener-admin
dotnet publish src\Shortener.PublicWeb\Shortener.PublicWeb.csproj -c Release -o C:\inetpub\shortener-publicweb
dotnet publish src\Shortener.Worker\Shortener.Worker.csproj -c Release -o C:\inetpub\shortener-worker
```

Set `ASPNETCORE_ENVIRONMENT=Production` and the real connection strings via each site's
`web.config` `<environmentVariables>` block or (preferably) machine-level environment variables —
never commit a Production `appsettings.Production.json` with real secrets in it.

## web.config — request size limit

Doc §M8.6 names this explicitly: without it, IIS's own request-size ceiling rejects large multipart
uploads with a generic IIS-level error *before* `UploadEndpoint`'s own `IHttpMaxRequestBodySizeFeature`
override (set to the same 4 MiB in code, see §M3.2) ever gets a chance to run. Both limits need to
agree — `maxAllowedContentLength` is in **bytes** (`4194304` = 4 MiB), matching the code-level limit
exactly so IIS never contradicts what the app itself enforces:

```xml
<!-- web.config, inside <system.webServer>, for Shortener.Api specifically -->
<security>
  <requestFiltering>
    <requestLimits maxAllowedContentLength="4194304" />
  </requestFiltering>
</security>
```

This block is only meaningful on `Shortener.Api` (the only site that accepts file uploads) — leave
Admin's and PublicWeb's `web.config` at the IIS default.

## File storage permissions

`FileStorage:RootPath` (e.g., `D:\ShortenerData\FileStorage`) must be writable by **every** pool
identity that touches it — in practice that's `Api` (writes on upload) and `PublicWeb` (reads on
download) at minimum; grant Modify to be safe if Admin ever gains a manual-delete feature:

```powershell
icacls "D:\ShortenerData\FileStorage" /grant "IIS AppPool\Api:(OI)(CI)M"
icacls "D:\ShortenerData\FileStorage" /grant "IIS AppPool\PublicWeb:(OI)(CI)M"
```

Put the folder on its own volume/drive if practical — disk-space health checks (`DiskSpaceHealthCheck`,
§M7) and the retention job's cleanup both assume `RootPath` isn't sharing free space with the OS or
SQL Server's own data files.

## Worker as a Windows Service

IIS doesn't run long-lived BackgroundServices — the Worker needs its own always-on process. Two
supported options; NSSM is simpler to operate day-to-day (auto-restart on crash, standard service
manager UI):

**Option A — NSSM (recommended):**
```powershell
nssm install ShortenerWorker "C:\Program Files\dotnet\dotnet.exe" "C:\inetpub\shortener-worker\Shortener.Worker.dll"
nssm set ShortenerWorker AppDirectory "C:\inetpub\shortener-worker"
nssm set ShortenerWorker AppEnvironmentExtra ASPNETCORE_ENVIRONMENT=Production
nssm set ShortenerWorker Start SERVICE_AUTO_START
nssm start ShortenerWorker
```

**Option B — `sc create` directly:**
```powershell
sc create ShortenerWorker binPath= "C:\Program Files\dotnet\dotnet.exe C:\inetpub\shortener-worker\Shortener.Worker.dll" start= auto
sc start ShortenerWorker
```

The Worker also listens on its own port for `/health/live`, `/health/ready`, `/metrics` (§M8.2/§M8.3)
— point the service's monitoring/alerting at that port the same way it would scrape a container.

## Migrations

Never call `Database.Migrate()` from any Startup path (checked — grep for `.Migrate(` under `src/`
returns nothing outside test fixtures). Generate and review an idempotent script instead, then run it
manually against the target database before the first deploy and before every deploy that adds a
migration:

```powershell
dotnet ef migrations script --idempotent -p src\Shortener.Infrastructure -s src\Shortener.Api -o migrate.sql
```

`--idempotent` wraps every migration in an `IF NOT EXISTS`-style guard keyed off
`__EFMigrationsHistory`, so re-running the same script against a database that's already partially or
fully migrated is a safe no-op rather than an error — this is what makes it safe to run by hand
outside of any app's own startup path.

Verified for real against a throwaway database on the dev SQL Server container: applying the
generated script from empty created all 22 tables, and running the identical script a second time
produced zero errors/output (true no-op). One thing that verification surfaced worth knowing before
running this manually: **the script needs `QUOTED_IDENTIFIER ON`**, or several `CREATE INDEX`
statements fail with `Msg 1934`. `sqlcmd` enables it with `-I`; SSMS and Azure Data Studio already
default it on, so this only bites you from the CLI:

```powershell
sqlcmd -S <server> -d UrlShortener -I -i migrate.sql
```

## First-boot baseline data

`DbSeeder.SeedBaselineAsync` (roles, the bootstrap admin user, the SMS provider catalog, the global
`RetentionPolicy`) runs automatically on Api's and Admin's first startup in **every** environment,
not just Development — confirmed by actually hitting this in Docker verification: without the global
retention policy, `UploadLinkService` can't insert a `ShortLink` at all (`RetentionResolver` has
nothing to fall back to), so this genuinely has to run before the app is usable, in Production too.
Two things worth knowing before the first real deploy:

- **The bootstrap admin password is fixed** (`admin` / a hardcoded initial password) and is written
  to stdout, not the structured log sinks, specifically so it never lands in the 30-day-retained log
  files (§M8.1) — but that means it's still sitting in `docker logs`/the Windows Event Log/wherever
  stdout is captured until you rotate it. `MustChangePassword` is set on the seeded user, so it can't
  be used past the first login, but treat first login as urgent, not optional, and restrict who can
  read the container/service's startup log in the meantime.
- Adding a new `SmsProvider` row (see runbook.md §5) still needs a manual `INSERT` — the baseline
  seed only creates the four providers already known at build time (kavenegar/melipayamak/farazsms/fake).

## Post-deploy smoke check

1. `Invoke-WebRequest http://localhost/health/ready` on each of Api/PublicWeb/Worker — all `Healthy`.
2. One real upload → OTP → download round trip through the actual IIS-hosted URLs, not just
   `dotnet run` — IIS-specific issues (wrong Application Pool identity, missing Hosting Bundle,
   `web.config` request-limit mismatch) only surface once real requests hit IIS itself.
3. Log in as `admin` with the bootstrap password from the startup log and change it immediately.
3. Confirm the Admin site is **not** reachable from outside the internal network/VPN (doc §M8.5) —
   test from a machine outside it, not just via `localhost` on the server itself.
