using System.Reflection;

namespace IdentityCenter.API;

/// <summary>
/// Renders the anonymous branded landing / status page served at the API root ("/").
/// Self-contained HTML: inline CSS + an inline SVG mark, NO external CDN/font/script
/// dependencies, because the API runs under a strict Content-Security-Policy. The page
/// surface gets a deliberately relaxed page-CSP (style-src 'unsafe-inline') carved out
/// in Program.cs; the JSON/API surface keeps default-src 'none'.
///
/// IMPORTANT: this file is kept byte-identical between the standalone
/// (IdentityCenter.Api) and canonical (IdentityCenter/Software) forks.
/// </summary>
public static class LandingPage
{
    /// <summary>
    /// Resolve the running assembly's version for display. Prefers the informational
    /// version (e.g. a SemVer / git-stamped string) and falls back to the file/assembly
    /// version. Mirrors the AdminApiController /health version source.
    /// </summary>
    public static string GetVersion()
    {
        var asm = Assembly.GetExecutingAssembly();
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            // Strip the "+<git-sha>" build-metadata suffix the SDK appends, if any.
            var plus = info.IndexOf('+');
            return plus > 0 ? info[..plus] : info;
        }
        return asm.GetName().Version?.ToString() ?? "1.0.0";
    }

    /// <summary>Build the full HTML document with version + environment injected.</summary>
    public static string Render(string environmentName)
    {
        var version = System.Net.WebUtility.HtmlEncode(GetVersion());
        var env = System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(environmentName) ? "Production" : environmentName);
        var year = DateTime.UtcNow.Year;

        return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <meta name="robots" content="noindex, nofollow" />
  <title>IdentityCenter API</title>
  <style>
    * { box-sizing: border-box; margin: 0; padding: 0; }
    :root {
      --navy: #0f172a;
      --panel: rgba(22, 33, 56, 0.9);
      --panel-2: rgba(10, 14, 26, 0.8);
      --sky: #0284c7;
      --cyan: #00bcd4;
      --cyan-2: #00acc1;
      --cyan-bright: #00e5ff;
      --cyan-soft: #22d3ee;
      --text: #f1f5f9;
      --muted: #94a3b8;
      --border: rgba(34, 211, 238, 0.18);
    }
    html, body { height: 100%; }
    body {
      font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
      color: var(--text);
      background:
        radial-gradient(1100px 700px at 78% -8%, rgba(0, 188, 212, 0.16), transparent 60%),
        radial-gradient(900px 620px at 12% 108%, rgba(2, 132, 199, 0.18), transparent 58%),
        linear-gradient(160deg, #0b1326 0%, var(--navy) 45%, #0a0e1a 100%);
      min-height: 100%;
      display: flex;
      align-items: center;
      justify-content: center;
      padding: 32px 18px;
      -webkit-font-smoothing: antialiased;
    }
    .card {
      width: 100%;
      max-width: 560px;
      background: linear-gradient(180deg, var(--panel) 0%, var(--panel-2) 100%);
      border: 1px solid var(--border);
      border-radius: 20px;
      padding: 44px 40px 30px;
      box-shadow: 0 24px 70px rgba(0, 0, 0, 0.55), 0 0 0 1px rgba(255, 255, 255, 0.02) inset;
      backdrop-filter: blur(14px);
      -webkit-backdrop-filter: blur(14px);
    }
    .mark { width: 76px; height: 76px; display: block; margin-bottom: 22px; }
    .wordmark { font-size: 30px; font-weight: 700; letter-spacing: -0.6px; line-height: 1; }
    .wordmark .suffix {
      font-weight: 500;
      background: linear-gradient(90deg, var(--cyan-soft), var(--cyan-bright));
      -webkit-background-clip: text;
      background-clip: text;
      -webkit-text-fill-color: transparent;
      color: var(--cyan-bright);
      margin-left: 8px;
    }
    .tagline { color: var(--muted); font-size: 14.5px; margin-top: 10px; letter-spacing: 0.2px; }
    .status {
      display: flex; flex-wrap: wrap; align-items: center; gap: 10px 18px;
      margin: 26px 0 4px; padding: 14px 16px;
      background: rgba(2, 6, 18, 0.35);
      border: 1px solid rgba(148, 163, 184, 0.14);
      border-radius: 12px;
      font-size: 13px; color: var(--muted);
    }
    .status .item { display: inline-flex; align-items: center; gap: 8px; }
    .status .val { color: var(--text); font-weight: 600; }
    .dot {
      width: 9px; height: 9px; border-radius: 50%;
      background: #22c55e;
      box-shadow: 0 0 0 3px rgba(34, 197, 94, 0.18);
      display: inline-block;
    }
    .actions { display: flex; flex-wrap: wrap; gap: 12px; margin-top: 26px; }
    .btn {
      flex: 1 1 auto; min-width: 130px; text-align: center;
      text-decoration: none; font-size: 14px; font-weight: 600;
      padding: 12px 16px; border-radius: 11px;
      transition: transform 0.12s ease, box-shadow 0.12s ease, border-color 0.12s ease;
    }
    .btn-primary {
      color: #04121c;
      background: linear-gradient(90deg, var(--cyan), var(--cyan-bright));
      box-shadow: 0 8px 22px rgba(0, 188, 212, 0.28);
    }
    .btn-primary:hover { transform: translateY(-1px); box-shadow: 0 12px 28px rgba(0, 188, 212, 0.4); }
    .btn-ghost {
      color: var(--text);
      background: rgba(34, 211, 238, 0.06);
      border: 1px solid var(--border);
    }
    .btn-ghost:hover { border-color: var(--cyan-soft); transform: translateY(-1px); }
    .blurb {
      margin-top: 26px; padding-top: 22px;
      border-top: 1px solid rgba(148, 163, 184, 0.12);
      color: var(--muted); font-size: 13.5px; line-height: 1.65;
    }
    .footer { margin-top: 22px; color: #5b6b85; font-size: 12px; text-align: center; }
    a.plain { color: var(--cyan-soft); text-decoration: none; }
    a.plain:hover { text-decoration: underline; }
    @media (max-width: 480px) {
      .card { padding: 34px 24px 26px; border-radius: 16px; }
      .wordmark { font-size: 25px; }
      .actions { flex-direction: column; }
    }
  </style>
</head>
<body>
  <main class="card" role="main">
    <svg class="mark" viewBox="0 0 64 64" fill="none" xmlns="http://www.w3.org/2000/svg" aria-label="IdentityCenter mark">
      <defs>
        <linearGradient id="icg" x1="8" y1="8" x2="56" y2="56" gradientUnits="userSpaceOnUse">
          <stop offset="0" stop-color="#22d3ee" />
          <stop offset="0.55" stop-color="#00bcd4" />
          <stop offset="1" stop-color="#0284c7" />
        </linearGradient>
      </defs>
      <rect x="2.5" y="2.5" width="59" height="59" rx="16" stroke="url(#icg)" stroke-width="2.5" opacity="0.55" />
      <circle cx="32" cy="22" r="8.5" stroke="url(#icg)" stroke-width="3" />
      <path d="M16 49c1.8-9.2 8.4-14 16-14s14.2 4.8 16 14" stroke="url(#icg)" stroke-width="3" stroke-linecap="round" />
      <circle cx="32" cy="22" r="2.6" fill="url(#icg)" />
    </svg>
    <div class="wordmark">IdentityCenter<span class="suffix">API</span></div>
    <div class="tagline">Identity &amp; Access Governance — REST API</div>

    <div class="status" role="status">
      <span class="item"><span class="dot"></span> Service running</span>
      <span class="item">Version <span class="val">{{version}}</span></span>
      <span class="item">Environment <span class="val">{{env}}</span></span>
    </div>

    <div class="actions">
      <a class="btn btn-primary" href="/swagger">API Docs (Swagger)</a>
      <a class="btn btn-ghost" href="/api/admin/health">Health</a>
      <a class="btn btn-ghost" href="/swagger/v1/swagger.json">OpenAPI JSON</a>
    </div>

    <div class="blurb">
      This is the IdentityCenter REST API — the programmatic surface for identity sync,
      governance, and licensing. All data endpoints require an <strong style="color:var(--text)">X-API-Key</strong>
      header. Interactive documentation is available via
      <a class="plain" href="/swagger">Swagger</a> where enabled.
    </div>

    <div class="footer">IdentityCenter API · © {{year}} · Identity &amp; Access Governance</div>
  </main>
</body>
</html>
""";
    }
}
