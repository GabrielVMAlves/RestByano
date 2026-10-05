# restByAno environment notes

## Tooling (recorded 2026-10-02, Step 01)

| Item | Value |
|---|---|
| OS | Windows 11 Home 10.0.26200 (x64) |
| Installed SDKs | 8.0.419, 10.0.201 |
| Pinned SDK (global.json) | 10.0.201, `rollForward: latestFeature`, no prerelease |
| Installed WindowsDesktop runtimes | 6.0.28, 8.0.22, 8.0.25, 9.0.14, 10.0.5, 10.0.11 |
| Target frameworks | Core/Infrastructure `net10.0`; Desktop `net10.0-windows` (UseWPF) |

## Package versions (explicit, lock files enabled)

| Project | Package | Version |
|---|---|---|
| Desktop | CommunityToolkit.Mvvm | 8.4.2 |
| Desktop | Microsoft.Extensions.DependencyInjection | 10.0.12 |
| Infrastructure | Microsoft.Data.Sqlite | 10.0.12 |
| Infrastructure | System.Security.Cryptography.ProtectedData | 10.0.12 |

Exact resolved graphs are in each project's `packages.lock.json`. Licenses are to be recorded in Step 16.

## Managed target policy (Step 11)

- Location: `%PROGRAMDATA%\restByAno\target-policy.json` (normally `C:\ProgramData\restByAno\target-policy.json`). An administrator deploys it; restByAno only reads it and never writes it (not from settings, import or any other path). It is re-read when the file changes — no restart needed.
- Shape (see `docs/target-policy.sample.json`; the hosts in the sample are placeholders, not real company hosts):
  `{"schemaVersion":1,"allowedTargets":[{"scheme":"https","host":"api.company.example","port":443}]}`
- Matching is exact on scheme, normalized host name (case-insensitive, IDN → ASCII, trailing dot ignored) and effective port (80/443 if `port` is omitted). No wildcards, no suffix matching. Every request is checked after variable substitution, immediately before it is sent.
- No policy file: loopback only (`127.0.0.1`, `::1`, `localhost`), with a message explaining that a policy is needed for other hosts.
- Policy file present but unreadable, malformed, wrong/newer `schemaVersion`, or containing an invalid entry (wildcards, paths, bad port…): FAIL CLOSED — every destination, including loopback, is blocked until an administrator fixes it.
- Once a valid policy exists, loopback destinations must be listed explicitly too.
- Plain `http` to a non-loopback host needs both a matching policy entry and “Allow non-loopback HTTP” in the transport settings of the active environment.
- URLs with embedded credentials and non-http(s) schemes are always denied.
- This is an application-level guard. It does not replace corporate firewall or proxy controls, and restByAno does not run with administrator rights.

## Transport settings (Step 11)

- Request timeout (1–600 s, default 30), decoded response size limit (1 KiB–100 MiB, default 10 MiB) and history retention (1–3650 days, default 30) are global settings stored in `restByAno.settings.json`.
- Proxy mode (system proxy by default, or an explicit http/https proxy address) and “Allow non-loopback HTTP” belong to the active environment's transport profile (File → Settings…). The per-environment `timeoutSeconds`/`maxResponseBytes` fields stored in an environment are not used for sending; the global settings are.
- A failing proxy is a failed request: restByAno never retries without the proxy. Redirects, cookies, retries and integrated Windows credentials are always off. Proxy credentials are not supported.
- Changes apply to the next request; a request already in flight keeps the settings it started with.

## Corporate requirements — PENDING

- Corporate proxy configuration/PAC/authentication: unknown. Default is the system proxy.
- Managed target policy contents (`%PROGRAMDATA%\restByAno\target-policy.json`): unknown; no hosts invented.
- Deployment/signing/installer process: unknown; follows the organization's process.
- Managed-device behavior of the build: not checked.
