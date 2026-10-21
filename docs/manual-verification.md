# restByAno manual verification guide

There are no unit-test projects. This guide lists the manual checks used to verify restByAno, how to repeat them, and what each
should show. `docs/implementation-progress.md` records what was actually run, with results; checks that need a managed company
machine are listed at the end as **pending**.

## Setup

1. Build: `dotnet build restByAno.slnx` (SDK 10.0.201+, see `global.json`).
2. Start the fixture API (loopback only, dummy credentials, no request logging):
   `dotnet run --project tools/restByAno.FixtureApi -- --environment-file C:\temp\fixture-env.json`
   It prints its base URL and writes a ready-to-import environment whose `baseUrl` is that URL (there is no hardcoded port).
   Use `--port <n>` for a fixed port and `POST /reset` to clear its in-memory state between checks.
3. Start restByAno. For destructive checks use a throwaway data folder: `restByAno.exe --data-dir D:\gb-verify`.
4. In **Manage…** import `C:\temp\fixture-env.json` and enter the dummy secrets: `bearerToken = fixture-token`,
   `apiKey = fixture-key`, `basicPassword = fixture-password`. Select that environment in the toolbar.
5. Import the samples with **Import…** in the collections pane: `samples/fixture-collection.json` (passes end to end),
   `samples/fixture-demo-failure.json` (intentional failure) and `samples/fixture-demo-cancel.json` (slow request).
   Never use real tokens for these checks.

## Fixture endpoints used by the checks

`/health`, `/echo` (any method), `/delay?ms=`, `/delay-cancelled-count`, `/invalid-json`, `/large?bytes=[&gzip=true][&chunked=true]`,
`/redirect`, `/redirect-count`, `/customers` (POST/GET/DELETE), `/protected/bearer|key|basic`, `/status/{code}`, `/json`
(numbers, booleans, null, `~0`/`~1` keys, repeated `X-Multi` header), `/text-latin1` (ISO-8859-1), `/text-unsupported-charset`,
`/binary`, `POST /reset`. See `tools/restByAno.FixtureApi/README.md`.

## Checklist

### Request editor and response (Steps 5–6)
- GET `/health` → `200 OK`, elapsed ms and byte count, Pretty and Raw tabs; Raw is unchanged when Pretty is viewed.
- POST `/echo?fixed=1` with two parameter rows `tag=a b` and `tag=c&d`, an enabled and a disabled header, and a JSON body → the
  server sees `fixed=1; tag=a b; tag=c&d` (each encoded once, in order), the enabled header only, `Content-Type: application/json`.
- Format JSON indents a valid body; on invalid JSON the body is untouched and a position-only message appears; Send is blocked.
- `/customers/<random guid>` → `404 Not Found` is shown as a response; each new Send clears the previous response.
- `/delay?ms=10000` then Cancel → status `Cancelled` within about a second; Send is disabled while sending; the fixture's
  `/delay-cancelled-count` increases. With timeout 1 s (Settings) → `Timed out`.
- `/large?bytes=11534336` → status `200` with a banner "Large response (11.0 MiB): showing the first 1.0 MiB as a sample"; "Load full body" shows all of it, "Save body…" writes it to a file of the same size. `/large?bytes=50000000` (above the 32 MiB display limit) offers only "Save body…". Same with
  `&gzip=true` (small on the wire, large decoded). `/redirect` shows `302`; `/redirect-count` stays `0`.
- `/text-latin1` shows `café crème`; `/text-unsupported-charset` shows a warning and UTF-8 text; `/binary` shows metadata only;
  `/invalid-json` shows the raw text and a warning on Pretty.
- A URL with `#fragment` or without `http(s)://` is rejected with a clear message.

### Storage and recovery (Step 7)
- New `--data-dir` is created; closing and reopening keeps settings and data; a second instance on the same folder shows
  "already running". An unwritable folder (for example under `C:\Windows`), a corrupt `restByAno.db`, a newer-version database and
  a missing database each show an actionable dark dialog and change nothing; "missing database" offers Restore / Create new / Exit.

### Collections (Step 8)
- Create collection → folder → requests; duplicate; reorder with ▲/▼ and Move to…; save; restart; the order is identical and is
  the order the runner uses. Editing a request and switching away prompts Save / Discard / Cancel; closing the window too.
  Deleting asks first; deleting a folder with requests requires choosing "move to root" or "delete with requests".

### Variables and environments (Step 9)
- Two environments with different `baseUrl`: the same `{{baseUrl}}/echo` reaches different servers. With no environment or an
  undefined `{{name}}` the send is blocked and names the variable (never a value). A JSON body `{"name":"{{customerName}}"}`
  with a value containing quotes and a backslash arrives intact; an unquoted `{{value}}` is rejected with an explanation.
  Collection **Variables…** defaults apply unless the environment defines the same name.

### Secrets and authentication (Step 10)
- Secret variables use a masked field; after Save the field is cleared and shows "Stored encrypted". Bearer, API key (header and
  query) and Basic against `/protected/*` return `200`; no auth returns `401`; `/echo` shows the exact headers sent. An enabled
  manual `Authorization` header together with Bearer/Basic is rejected before sending. Secrets keep working after a restart.
  A byte search of `restByAno.db`, the settings file and the backup folder must not find the dummy secret values (the ciphertext is
  not plaintext-searchable; plain variables such as `baseUrl` are expected to be found).

### Settings and destination policy (Step 11)
- File → Settings…: limits validate at their boundaries (timeout 1–600 s, cap 1 KiB–100 MiB, retention 1–3650 days); the new
  limits apply to the next send. An explicit proxy that is unreachable makes the request fail (no direct retry).
- Policy file `%PROGRAMDATA%\restByAno\target-policy.json` (see `docs/target-policy.sample.json`; test with a temporary file and remove it):
  listed loopback target allowed; port mismatch, `localhost` when unlisted and an unlisted host denied; a malformed policy blocks
  everything (Settings shows it as INVALID); no policy = loopback only with the setup message.

### Import / export (Step 12)
- Export a collection and an environment (native Save dialog); the files contain no secrets or history. Import them back: a new
  collection with fresh ids appears, the environment's secrets show "Enter the secret value". Invalid files (unsupported version,
  invalid references, a literal credential in `auth`, > 10 MiB, not JSON) show "Import failed" with specific messages and change
  nothing. A file with scripts / cloud fields lists them as ignored.

### Assertions and extraction (Step 13)
- Assertions tab: status, header (name case-insensitive, value case-sensitive), JSON path exists (null counts), JSON equals
  (`50` ≠ `"50"`, `true` ≠ `"true"`, `1.50` = `1.5`), response time (strictly below). `/json` exercises all of them. Invalid rule
  fields show inline errors and block Send/Save. Extraction: POST `/customers` with extractor `/id → customerId` shows
  "Extracted customerId (value hidden)"; a missing path gives "Extraction failed"; a failed assertion skips extraction.

### Collection runner (Step 14)
- `fixture-collection.json`: `Run passed`, cleanup deleted the created customer. `fixture-demo-failure.json` with *Stop on first
  failure*: failed step, skipped step, cleanup still passes; unchecked: the later step runs. A collection whose cleanup uses a
  variable that was never extracted shows "Not sent (validation)". Failed extraction shows "Extraction failed". Zero-assertion steps
  are labelled. An empty collection or one with only cleanup requests cannot be run. `fixture-demo-cancel.json`: press Stop while the
  slow request runs → `Run cancelled`, later steps skipped, cleanup explicitly skipped, Send re-enabled.

### History and reports (Step 15)
- Each finished run appears in the **History** tab (newest first) with its steps. **Export report…** writes a JSON report
  (`schemaVersion` 1; run metadata and step results only) — it must not contain URLs, headers, bodies, extracted values or the
  dummy secrets. History survives a restart. **Clear history…** asks first and only empties history. Retention: insert runs
  whose `ended_utc` is just before / just after `now − retention days` while the app is closed; at the next start only the older
  ones are removed ("Removed N run(s) older than…").

### Packaging (Step 16)
- See `docs/user-guide.md` and the publish checklist in `docs/implementation-progress.md`.

## Pending external verification (needs a managed company machine)

- Behavior behind the real corporate proxy (system proxy discovery, PAC, proxy authentication) and TLS inspection certificates.
- A real administrator-deployed `target-policy.json` with real corporate hosts.
- Installation, signing, antivirus/application-control approval and deployment through the organization's process.
- DPAPI secrets after a Windows profile move or reset (they are intentionally not portable; re-enter secrets).
