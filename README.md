# IdentityCenter API

Standalone REST API for the IdentityCenter Identity & Access Governance platform.

This repository is a **deliberate fork** of the IdentityCenter API surface, extracted so the API
can build, run, publish, and deploy **independently** of the IdentityCenter WebPortal — mirroring how
[Conduit](../Conduit) is its own repository. The IdentityCenter repo remains fully intact; this is a
copy-out, not a move.

---

## What this is

The IdentityCenter REST API. It exposes:

- **Objects / Identities bulk ingest** (`/api/objects/bulk`, `/api/identities/bulk`, `/query`) — the
  sink endpoints [Conduit](../Conduit) writes directory data into.
- **Provisioning** — the SaaS control-plane `POST /api/provision` endpoint (DB-per-tenant) plus the
  control-plane tenant registry bootstrap.
- **Discovery / Inventory / Compliance** controllers — SCIM-ish read/scan endpoints used by clients.

It is consumed by **Conduit** (as a sink) and by the **SaaS control plane** (provisioning + per-tenant
request resolution).

### Projects (5)

| Project | Role |
|---|---|
| `IdentityCenter.API` | ASP.NET Core 8 Web API host (this is what you run/publish) |
| `DataAccessLibrary` | EF Core models + Dapper repositories + **embedded SQL migrations** (`Migrations/Scripts/`, V001–V135) |
| `Common` | Shared utilities, DataProtection-based encryption |
| `Logging` | `IGlobalLogger` centralized logging (Serilog) |
| `ChangeHistory` | Audit trail — **required transitive dependency** of DataAccessLibrary |

Dependency graph:

```
IdentityCenter.API ─▶ Common, DataAccessLibrary, Logging
DataAccessLibrary  ─▶ Common, Logging, ChangeHistory
ChangeHistory      ─▶ Logging
Common, Logging    ─▶ (leaf)
```

> Note: `ChangeHistory` is not listed in the original 4-project extraction set but is a hard
> ProjectReference of `DataAccessLibrary`, so it ships here too. The solution includes all 5.

---

## Migrations (Dapper-only, runtime)

Schema is managed at runtime by the migration runner using the embedded SQL scripts in
`DataAccessLibrary/Migrations/Scripts/` (V001…V135). **There are no EF Core migrations.** Do not add
any. The migrator records applied versions but does not validate checksums, so editing old scripts is
safe.

---

## Configure the connection string

The API reads its database connection from **user-secrets** in development (the committed
`appsettings.json` contains placeholders only — no real connection string, no password).

The `UserSecretsId` is **unchanged** from the IdentityCenter repo
(`fa9924b7-da89-4a73-b346-810f137d3a56`), so any machine that already has the IdentityCenter API
user-secrets set will resolve them here automatically.

Keys:

- `ConnectionStrings:DefaultConnection` — the tenant/app database (dev target: `.56` /
  `IdentityCenter15`).
- `ConnectionStrings:ControlPlane` — **optional**; the SaaS control-plane registry DB. If absent, the
  control-plane bootstrap is skipped (the API still runs).

Set them in dev with:

```powershell
cd IdentityCenter.API
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Data Source=192.168.1.56;Initial Catalog=IdentityCenter15;User ID=...;Password=...;Trust Server Certificate=True"
dotnet user-secrets set "ConnectionStrings:ControlPlane"     "Data Source=192.168.1.56;Initial Catalog=IdentityCenterControlPlane;..."
```

In production, supply these via environment variables or a secret store — never commit them.
`appsettings.template.json` shows the shape.

---

## ⚠️ DataProtection keyring (required to decrypt `enc:` connection strings)

`Program.cs` configures DataProtection with:

```csharp
builder.Services.AddDataProtection()
    .SetApplicationName("IdentityCenter")
    .PersistKeysToFileSystem(new DirectoryInfo(@"C:\ProgramData\IdentityCenter\Keys"));
```

If a configured connection string is stored encrypted (prefixed `enc:`), the API can only decrypt it
when the **exact same DataProtection keyring is present byte-for-byte** — same application name
(`IdentityCenter`) and same key directory (`C:\ProgramData\IdentityCenter\Keys`) as the WebPortal that
encrypted it.

**On a server that does not have that keyring**, do one of:

1. Copy `C:\ProgramData\IdentityCenter\Keys` from the box that created the `enc:` value, **or**
2. Provide a **plaintext** connection string via user-secrets / env var (no `enc:` prefix).

Plaintext connection strings need no keyring. Only `enc:` values depend on it.

---

## Run (local)

```powershell
.\run.ps1            # build (Debug) + dotnet run the API
.\run.ps1 -Watch     # hot reload
.\run.ps1 build      # build only
```

Linux/Mac: `./run.sh` (same commands: `build` / `run` / `clean`, options `--release` `--watch`).

The API listens on **http://localhost:5062** (Swagger UI at **/swagger**). This is the same port the
API used inside the IdentityCenter repo, so the existing Conduit `IdentityCenter` connection BaseUrl
keeps working unchanged.

---

## Publish + deploy (publish-folder, no Docker)

```powershell
.\publish.ps1                  # framework-dependent (default) -> ./publish
.\publish.ps1 -SelfContained   # win-x64 runtime-included -> ./publish
```

- **Framework-dependent (default):** smaller artifact; the target server must have the **ASP.NET Core
  8 Runtime** installed. Deploy by copying `./publish` to the server and running:
  ```
  dotnet IdentityCenter.API.dll
  ```
- **Self-contained (`-SelfContained`):** larger folder, **no .NET install needed** on the server.
  Deploy by copying `./publish` and running the native host:
  ```
  .\IdentityCenter.API.exe
  ```

Either way it binds **http://localhost:5062**. Configure the connection string on the server
(env var or user-secrets) and ensure the keyring is present if any `enc:` value is used.

Linux/Mac: `./publish.sh` / `./publish.sh --self-contained [rid]`.

> **Why the published app binds a port at all:** `launchSettings.json` is a **development-only**
> file and is **not** included in a publish build. The committed `appsettings.json` carries a
> custom `"DefaultUrls": "http://localhost:5062"` key; `Program.cs` applies it **only when the
> `ASPNETCORE_URLS` env var is not set** — so the published app binds 5062 out of the box, and
> `ASPNETCORE_URLS` (e.g. `http://0.0.0.0:5062` for network binding) always overrides it.
>
> A note on why it isn't the literal `Urls` key: an appsettings **`Urls`** key is authoritative
> and **cannot** be overridden by `ASPNETCORE_URLS` (verified empirically). That would defeat
> per-machine network binding, so we use a custom fallback key instead.

---

## Deploy to a server as a Windows Service (deploy-and-forget)

Run the API as a long-running Windows Service: **auto-start at boot, auto-restart on crash,
survives reboot, no console window.** `Program.cs` calls `builder.Host.UseWindowsService()`
(a no-op for console/dev runs, so the same build works everywhere).

**Steps (on the server, elevated PowerShell):**

1. **Publish.** On a dev box (or the server, if it has the SDK):
   ```powershell
   .\publish.ps1 -SelfContained     # no .NET install needed on the server (recommended for forget-about-it)
   # or: .\publish.ps1              # framework-dependent — requires the ASP.NET Core 8 Runtime on the server
   ```

2. **Copy** the `.\publish` folder (and this repo's `install-service.ps1` / `uninstall-service.ps1`)
   to the server.

3. **Set the connection string.** Two supported forms — pick one:

   - **Environment-variable form** (the service env block, written for you by `install-service.ps1`,
     uses this exact `__` mapping):
     ```
     ConnectionStrings__DefaultConnection=Data Source=192.168.1.56;Initial Catalog=IdentityCenter15;User ID=sa;Password=...;Trust Server Certificate=True
     ConnectionStrings__ControlPlane=Data Source=192.168.1.56;Initial Catalog=IdentityCenterControlPlane;User ID=sa;Password=...;Trust Server Certificate=True
     ```
     (`__` maps to the `:` nesting — i.e. `ConnectionStrings:DefaultConnection`.) Either pass these to
     `install-service.ps1` via `-DefaultConnection` / `-ControlPlaneConnection`, or set them once as
     **machine** env vars and pass `-UseMachineEnvVars`.

   - **`appsettings.Production.json` form** — place it **next to the exe** in the publish folder
     (this file is **git-ignored**, so it never gets committed):
     ```json
     {
       "ConnectionStrings": {
         "DefaultConnection": "Data Source=192.168.1.56;Initial Catalog=IdentityCenter15;User ID=sa;Password=...;Trust Server Certificate=True",
         "ControlPlane": "Data Source=192.168.1.56;Initial Catalog=IdentityCenterControlPlane;User ID=sa;Password=...;Trust Server Certificate=True"
       }
     }
     ```

4. **Install the service:**
   ```powershell
   # loopback-only (safe default), connection strings written into the service env block:
   .\install-service.ps1 -DefaultConnection "..." -ControlPlaneConnection "..."

   # reachable from other machines (e.g. Conduit on another box) — binds 0.0.0.0:5062:
   .\install-service.ps1 -BindAll -UseMachineEnvVars
   ```
   This registers `IdentityCenterApi` with **StartupType=Automatic** and **auto-restart on failure**
   (`sc.exe failure ... actions= restart/5000`), writes `ASPNETCORE_ENVIRONMENT`, `ASPNETCORE_URLS`,
   and (optionally) the connection strings into the service's registry environment block, and starts it.
   Uninstall with `.\uninstall-service.ps1`.

5. **Verify:**
   ```powershell
   Get-Service IdentityCenterApi          # Status Running, StartType Automatic
   Invoke-WebRequest http://localhost:5062/   # the API responds
   ```
   `http://<server>:5062/swagger` is **Development-only** by default. In Production the Swagger UI is
   off (the API surface is not disclosed) unless you set `Swagger:EnableInProduction=true`. Hit the API
   endpoints directly, or grab `swagger.json` from a dev box.

**Windows-Server hand-steps (not done by the script):**

- **Firewall.** Loopback (`localhost`) needs nothing. If you used `-BindAll` (or set
  `ASPNETCORE_URLS=http://0.0.0.0:5062`), **open the port** so other machines can reach it:
  ```powershell
  New-NetFirewallRule -DisplayName "IdentityCenter API 5062" -Direction Inbound -Action Allow -Protocol TCP -LocalPort 5062
  ```
- **DataProtection keyring.** Only needed if a connection string is stored encrypted (`enc:` prefix):
  copy `C:\ProgramData\IdentityCenter\Keys` (app name `IdentityCenter`) to the server. **Plaintext /
  env-var connection strings need no keyring.**
- **DB access.** The service runs as **LocalSystem** by default. The lab uses SQL auth (`sa`), so
  nothing extra is required. If you switch the service to a **domain account** *and* use Windows-auth
  SQL, grant that account database access.

> **Repo divergence:** the `builder.Host.UseWindowsService()` line in `Program.cs` is a deliberate,
> deployment-only difference from the IdentityCenter-repo copy. Do **not** mirror it upstream.

### Safe redeploy of the .56 service (`deploy-api.ps1`)

`deploy-api.ps1` mirrors a fresh self-contained publish to the running `IdentityCenterApi`
service on `192.168.1.56` and health-checks it. It uses `robocopy /MIR` but excludes
runtime-written dirs by **full remote path** (`/XD`) so the mirror can never delete
server-only state, and it relaxes `$ErrorActionPreference` around the one `net use /delete`
call (whose native stderr would otherwise abort the script). The API writes nothing under its
publish root — logs go to `C:\ProgramData\IdentityCenter\logs` and the DataProtection keyring
to `C:\ProgramData\IdentityCenter\Keys`, both **outside** the publish root — so the exclude
set is purely defensive (`log`, `App_Data`, `uploads`, `temp`; no `MLModels`, unlike WebPortal).

```powershell
.\publish.ps1 -SelfContained                          # stage ./publish first
.\deploy-api.ps1 -DryRun                               # prove the excludes (prompts for SMB pwd)
.\deploy-api.ps1 -SmbUser "domain\administrator"       # real deploy (stop -> mirror -> start -> health)
```

Pass the SMB credential via `-Credential`, or `-SmbUser` (+`-SmbPassword`), or be prompted —
**no secret is stored in the script.**

---

## Drift note (IMPORTANT)

`DataAccessLibrary`, `Common`, `Logging`, and `ChangeHistory` are a **fork copied from the
IdentityCenter repo** at commit:

```
IdentityCenter @ a0a22704c6a1c4903631702af11f358389e32ee3   (master, 2026-06-05)
```

These four shared projects **also live in the IdentityCenter repo** (WebPortal uses them). The two
copies will drift. Until/unless the data layer is consolidated, any change to the shared data layer
must be **mirrored between the two repos by hand**. Treat IdentityCenter as the upstream source of
truth for these four projects.
