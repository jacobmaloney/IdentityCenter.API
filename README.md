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
