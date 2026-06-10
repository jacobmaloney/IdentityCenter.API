# Deploy IdentityCenter.API to a server as a Windows Service

Self-contained (no .NET install needed on the server), runs on **port 8080**, auto-start + auto-restart.
Target in this lab: **192.168.1.56** (SQL + DC box), reachable from the laptop at `http://192.168.1.56:8080`.

## 1. Publish (on the dev box)

```powershell
cd C:\Users\jacob\source\repos\IdentityCenter.Api
.\publish.ps1 -SelfContained          # -> .\publish  (~57 MB, win-x64, runtime included)
```

## 2. Copy to the server

Copy these to a folder on the server, e.g. `C:\Apps\IdentityCenterApi\`:
- the whole `publish\` folder
- `install-service.ps1`
- `uninstall-service.ps1`

## 3. Install the service (ELEVATED PowerShell on the server)

SQL is local on the server, so the connection strings use `Server=localhost`. Replace `<SA_PASSWORD>`
with the real sa password (do NOT commit it).

```powershell
cd C:\Apps\IdentityCenterApi
.\install-service.ps1 -PublishPath .\publish -Port 8080 -BindAll `
  -DefaultConnection "Server=localhost;Database=IdentityCenter15;User Id=sa;Password=<SA_PASSWORD>;TrustServerCertificate=True;Multiple Active Result Sets=True" `
  -ControlPlaneConnection "Server=localhost;Database=IdentityCenterControlPlane;User Id=sa;Password=<SA_PASSWORD>;TrustServerCertificate=True;Multiple Active Result Sets=True"
```

`-BindAll` makes it listen on `http://0.0.0.0:8080` so other machines (the laptop's Conduit) can reach it.
The service is `IdentityCenterApi`, set to **Automatic** start with **auto-restart on failure**.

## 4. Open the firewall (ELEVATED, on the server)

```powershell
New-NetFirewallRule -DisplayName "IdentityCenter API 8080" -Direction Inbound -Protocol TCP -LocalPort 8080 -Action Allow
```

## 5. Verify (from any machine on the LAN)

- `http://192.168.1.56:8080/` → branded landing page (200, anonymous)
- `http://192.168.1.56:8080/api/admin/health` → 200 (anonymous health)
- `http://192.168.1.56:8080/api/objects` → 401 (correct — needs an `X-API-Key`)

Swagger is **off in Production** by default. To expose it on the server, either install with
`-Environment Development`, or set the machine env var `Swagger__EnableInProduction=true` and restart
the service.

## 6. Point Conduit at the server

In Conduit → **Connected Systems** → the **IdentityCenter** connection → set **Base URL** to
`http://192.168.1.56:8080`, **Test**, **Save**. (Was `http://localhost:5062` when the API ran on the laptop.)

## Admin UI (added 2026-06-09)

The service now hosts a browser admin surface alongside the REST API — same binary, same port:

- **`http://<server>:8080/admin`** — dashboard: live requests-per-host line graph (5/15/30-min
  windows), 4xx/5xx/latency stat cards, recent warnings/errors feed. Updates every 2 s.
- **`http://<server>:8080/admin/logs`** — live log viewer (in-memory stream, newest first,
  level filter + text search + follow/pause) and a file mode that tails the rolling logs in
  `C:\ProgramData\IdentityCenter\logs` (locked to that directory).
- **`/admin/login`** — sign in with your **IdentityCenter portal credentials**
  (`admin@identitycenter.local` / portal password). Same ASP.NET Identity stack and the same
  `AspNetUsers` table as the WebPortal — lockout state is shared. Admin role required.

Notes:
- The REST surface is unchanged: `/api/*` still authenticates with `X-API-Key` only; the admin
  cookie is honored on `/admin` paths only.
- The admin cookie is `Secure` only when served over HTTPS (the service runs plain HTTP on the
  LAN today — front it with HTTPS before exposing it beyond the LAN).
- External IDP sign-in is NOT wired yet; the login page has the extension point marked for the
  shared IdentityProviders configuration.
- Telemetry/log stream are in-memory and reset on service restart (files remain the durable log).

## Service management

```powershell
Get-Service IdentityCenterApi
Restart-Service IdentityCenterApi
.\uninstall-service.ps1            # stop + remove
```

## Notes
- **Self-contained**: the server needs nothing pre-installed (no .NET runtime).
- **Keyring**: only needed to decrypt `enc:` connection strings. The plaintext connection strings above
  need no keyring. (If you switch to encrypted strings, the DataProtection keyring at
  `C:\ProgramData\IdentityCenter\Keys`, app name `IdentityCenter`, must be present on the server.)
- **Logs**: the service writes a rolling daily log to **`C:\ProgramData\IdentityCenter\logs\identitycenter-api-<date>.log`**
  (14-file retention). Override the directory with the `Logging__Directory` env var. This is where to look
  when the service is running headless.
- **Re-deploy**: re-run `publish.ps1 -SelfContained`, copy `publish\` over, `Restart-Service IdentityCenterApi`.
