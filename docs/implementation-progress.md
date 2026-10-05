# restByAno implementation progress

Source of truth for resuming across sessions. Follow `terra-implementation-guide.md`.
Project root: `C:\Dev\restByAno`. Build: `dotnet build restByAno.slnx`.

## Status

| Step | Name | Status |
|---|---|---|
| 01 | Inspect tooling and scaffold | Implemented, Verified (build + launch) |
| 02 | Build the dark shell | Implemented, partially Verified (see below) |
| 03 | Establish domain contracts | Implemented, Verified (build + dependency greps) |
| 04 | Standalone fixture API | Implemented, Verified (manual, curl) |
| 05 | Native HTTP execution | Implemented, Verified (manual harness, 28 checks) |
| 06 | Request editor and response view | Implemented, Verified (UI automation against fixture) |
| 07 | Storage, configuration and recovery | Implemented, Verified (harness 55 checks + UI automation) |
| 08 | Collection management | Implemented, Verified (UI automation + Core harness) |
| 09 | Variables and environments | Implemented, Verified (Core harness 31 checks + UI automation) |
| 10 | Secrets, authentication and sanitization | Implemented, Verified (harness 36 checks + UI automation) |
| 11 | Settings and destination policy | Implemented, Verified (harness 41 checks + UI automation); managed-device proxy PENDING external |
| 12 | Safe native import/export | Implemented, Verified (harness 61 checks + UI automation incl. native file dialogs) |
| 13 | Assertions and extraction | Implemented, Verified (harness 51 checks + UI automation) |
| 14 | Sequential collection runner | Implemented, Verified (harness 25 checks + UI automation) |
| 15 | History, reports and sample collections | Implemented, Verified (harness 55 checks + UI automation) |
| 16 | UX finish and offline package | Not started |

## Step 01 — Inspect tooling and scaffold

**Implemented**
- `restByAno.slnx` with `src/restByAno.Core` (net10.0), `src/restByAno.Infrastructure` (net10.0), `src/restByAno.Desktop` (net10.0-windows, WPF; AssemblyName `restByAno`, RootNamespace `restByAno.Desktop`).
- References: Infrastructure → Core; Desktop → Core + Infrastructure. Only Desktop uses WPF.
- `global.json` pins SDK 10.0.201 with `rollForward: latestFeature`; `Directory.Build.props` enables nullable, implicit usings, `RestorePackagesWithLockFile`.
- Packages with explicit versions (see `environment-notes.md`); `packages.lock.json` generated per project.
- `.gitignore` for bin/obj/publish, local databases, backups, settings and secret files.
- Folder skeleton per guide layout (`.gitkeep` placeholders in empty folders; to be replaced by real code in later steps). `tools/` and `samples/` created empty.
- Composition root in `App.xaml.cs` (`ServiceCollection` → `MainWindow` + `MainWindowViewModel`); no `StartupUri`, no service locator in views.
- `Views/MainWindow.xaml` with Title `restByAno`, 1280×800, min 1000×650, dark background.
- `docs/environment-notes.md` with tooling and pending corporate items.

**Verified**
- `dotnet restore restByAno.slnx`: succeeded.
- `dotnet build restByAno.slnx -c Debug`: succeeded, 0 warnings, 0 errors.
- Launched `restByAno.exe` for 4 s: process responding, window title `restByAno`; then stopped.

**Pending external verification**
- Corporate proxy, managed policy, deployment/signing (unknown; see environment-notes).

**Limitations**
- Window is a minimal placeholder; shell/theme come in Step 02.
- No git repository initialised (not requested).

**Next:** Step 02 — build the dark shell. (done, see below)

## Step 02 — Build the dark shell

**Implemented**
- `Themes/Colors.xaml`: brush tokens (#121212 background, #1E1E1E panel, #252525 raised, #E6E6E6 text, #B3B3B3 secondary, #3F3F46 line, #60A5FA accent) plus hover/pressed/selected/disabled/accent-hover tokens.
- `Themes/Controls.xaml`: re-templated Button (+ AccentButton), TextBox, PasswordBox, CheckBox, ComboBox + popup + items, TabControl/TabItem, TreeView/TreeViewItem, ListBox/ListBoxItem, DataGrid (+ header/row/cell), ScrollBar/ScrollViewer (no light corner), GridSplitter, Menu/MenuItem + popup, ToolTip, `AppWindowStyle` for app-owned windows/dialogs, shared `FocusRing`. All brushes are DynamicResource; hover/selected/focus/disabled states styled; status shown with glyph + text.
- `Services/ThemeService.cs`: in Windows high contrast, token brushes are replaced by system brushes in Application.Resources; reacts to `SystemParameters.HighContrast` changes and color preference changes; restores dark tokens when leaving high contrast.
- `Services/DarkTitleBar.cs`: requests a dark native title bar (DWM immersive dark mode; skipped in high contrast).
- `Views/MainWindow.xaml`: 1280×800 (clamped to work area), min 1000×650, centered; File→Exit menu; toolbar (environment, method, URL, Send, Cancel); collections pane (260 px, splitter); editor tabs Parameters/Headers/Body/Authentication/Assertions/Extraction; response pane; resizable runner pane below (splitters).
- `MainWindowViewModel`: lists and bound placeholder state only.

**Placeholders (to be replaced)**: Send/Cancel/Run/Stop are disabled; tab contents are static layout panels; Environments list contains only "No environment". Replaced in Steps 06–09, 13–14.

**Verified**
- `dotnet build restByAno.slnx -c Debug`: succeeded, 0 warnings, 0 errors.
- Launched and inspected via screenshots (1920×1080): all panels dark, no white panels; ComboBox popup, File menu popup dark; Authentication tab with TextBox/PasswordBox dark with accent border on focus; GridSplitter/tabs/DataGrid render.

**NOT RUN**
- High-contrast switching (toggling Windows high contrast was not done on this machine; code path implemented, unverified).
- Full keyboard Tab traversal and mouse drag-resizing of splitters; minimum-size behavior.
- Native file dialogs: OS-controlled (none exist yet).

**Next:** Step 03 — establish domain contracts. (done, see below)

## Step 03 — Establish domain contracts

**Implemented** (all in `restByAno.Core`, no package or project references)
- `Models/CollectionModels.cs`: enums (HttpMethodKind, BodyKind, AuthKind, ApiKeyLocation), CollectionDefinition (SchemaVersion=1), FolderDefinition, RequestDefinition, KeyValueEntry, BodyDefinition, AuthDefinition (variable-name references only).
- `Models/EnvironmentModels.cs`: EnvironmentDefinition, VariableDefinition (exactly one of Value/SecretId; `IsWellFormed`), TransportProfile (30 s, 10 MiB, System proxy, AllowNonLoopbackHttp=false), ProxyMode.
- `Models/RuleModels.cs`: AssertionKind, AssertionDefinition, ExtractorDefinition (Sensitive=true).
- `Models/ExecutionModels.cs`: HeaderEntry, ResolvedRequest (sealed class, memory-only, `ToString` withholds details — deliberately not a record), ResponseSnapshot, ExecutionErrorKind (the nine kinds), ExecutionError (optional metadata-only PartialResponse), ExecutionResult.
- `Models/RunModels.cs`: StepOutcome, RunOutcome, AssertionResult, StepRecord, RunRecord (metadata only), EntitySummary.
- `Contracts/Contracts.cs`: IRequestExecutor, ICollectionRepository, IEnvironmentRepository, ISecretStore (+SecretReadResult), IRunRepository, ITargetPolicy (+TargetPolicyResult), IReportExporter.
- Guid IDs, DateTimeOffset UTC timestamps, no WPF observable types. Enum-as-string persistence is a serializer concern, applied in Step 07 (JsonStringEnumConverter).

**Design decisions (guide gaps)**
- Collection `Defaults` reuse VariableDefinition; must carry Value only (validated in later steps).
- HeaderEquals expected value is stored in `ExpectedJson` as a JSON string (the guide lists no separate field).
- Run history ("clear") uses `IRunRepository.PurgeAsync(DateTimeOffset.MaxValue)`; no extra method added.
- Contracts take `ITargetPolicy.Validate(Uri, TransportProfile)` so the non-loopback-HTTP rule can be enforced.

**Verified**
- `dotnet build restByAno.slnx -c Debug`: succeeded, 0 warnings, 0 errors.
- Grep of Core for System.Windows / Sqlite / ProtectedData / Infrastructure / Desktop: no matches; Core csproj has no references.
- `ResolvedRequest` appears only in its definition and `IRequestExecutor`; no repository, report or run model references it.

**Next:** Step 04 — standalone fixture API. (done, see below)

## Step 04 — Standalone fixture API

**Implemented**
- `tools/restByAno.FixtureApi` (Microsoft.NET.Sdk.Web, net10.0, `IsPublishable=false`), added to `restByAno.slnx` under `/tools/`. No references to or from the desktop projects.
- Binds `http://127.0.0.1:<port>` only; port 0 (free port) by default, `--port <1-65535>` for a fixed one; prints the base URL. Logging providers cleared (no request logging).
- Endpoints per guide: `/health`, `/echo` (GET/POST/PUT/PATCH/DELETE/HEAD/OPTIONS; duplicate query keys preserved; body capped 1 MiB), `/delay` (cap 60 s, observes cancellation), `/invalid-json`, `/large` (streamed in 64 KiB chunks; `chunked=true`, `gzip=true`; cap 256 MiB), `/redirect` + `/redirect-target` + `/redirect-count`, `/customers` POST/GET/DELETE, `/protected/bearer|key|basic`.
- Extra beyond guide: `GET /delay-cancelled-count` (makes cancellation observable for Step 05/14 checks).
- `tools/restByAno.FixtureApi/README.md` documents launch commands, endpoints and sample URLs.

**Verified** (built Debug, ran the exe, exercised with curl.exe; stopped afterwards)
- `dotnet build restByAno.slnx`: 0 warnings, 0 errors.
- Startup printed `http://127.0.0.1:60076` (free port); only 127.0.0.1 listening (Get-NetTCPConnection). `--port 5099` works; `--port abc` exits with message.
- `/health` 200; `/echo` POST returned method, both `x=1`,`x=2` query entries, headers, JSON body.
- Customers: create 201 → get 200 → delete 204 → delete again 404; GET unknown 404; bad body 400.
- Auth: bearer/key (header and query)/basic succeed with dummy credentials, 401 otherwise.
- `/invalid-json` returns malformed text; `/redirect` returns 302 + Location, `/redirect-count` 0 when not followed.
- `/delay?ms=300` OK; a 1 s client timeout against `/delay?ms=10000` made `/delay-cancelled-count` go to 1.
- `/large`: Content-Length variant 1,048,576 bytes; `chunked=true` sends Transfer-Encoding chunked; `gzip=true` sends Content-Encoding gzip and decodes to exactly 5,000,000 bytes.
- No desktop project references the fixture project.

**Limitations**
- Verified with curl, not yet with the restByAno executor (Step 05). `/large` cap checks above the 10 MiB response cap come in Step 05.

**Next:** Step 05 — native HTTP execution. (done, see below)

## Step 05 — Native HTTP execution

**Implemented**
- `Infrastructure/Http/HttpClientPool.cs`: bounded (8) pool of long-lived `HttpClient`s keyed by proxy mode/address; `SocketsHttpHandler` with `AllowAutoRedirect=false`, `UseCookies=false`, no credentials, gzip/deflate decompression, 2-minute pooled connection lifetime, system proxy (`HttpClient.DefaultProxy`) or explicit `WebProxy`; `HttpClient.Timeout` infinite (timeout is per execution). Lease/ref-count: clients are retired (strict LRU when full, or idle > 10 min) and disposed only after the last lease is released; never disposed per request.
- `Infrastructure/Http/HttpRequestExecutor.cs`: validates shape (absolute http/https, timeout ≥ 1, cap ≥ 1, explicit proxy URI) → calls `ITargetPolicy` immediately before any send → new `HttpRequestMessage` per execution. Content headers (Content-Type, Content-Language, …) go to the content, others to the message; invalid combinations return Validation errors rather than being dropped (transport-owned headers such as Content-Length/Host/Transfer-Encoding, CR/LF in values, content headers without a body, duplicate Content-Type). `SendAsync(ResponseHeadersRead)`, no `EnsureSuccessStatusCode`, linked token with `CancelAfter(timeout)` (user cancel → Cancelled, otherwise Timeout), stopwatch started right before send and stopped after the bounded read, decoded stream read incrementally to limit+1 sentinel; over the limit the body is discarded and `ResponseLimit` is returned with metadata-only `PartialResponse`. Errors carry fixed, secret-free messages; precise categories only where .NET exposes them (`HttpRequestError`), otherwise OtherTransport. Expected failures never throw.
- `Infrastructure/Policy/LoopbackOnlyTargetPolicy.cs`: initial policy (http/https, no userinfo, localhost/127.0.0.0/8/::1 incl. IPv4-mapped only). Replaced by the managed policy in Step 11.
- Desktop composition root registers `ITargetPolicy`, `HttpClientPool`, `IRequestExecutor`.

**Verified**
- `dotnet build restByAno.slnx -c Debug`: 0 warnings, 0 errors.
- Manual harness (throwaway console app in the session scratchpad — not part of the repo, not a test project) started the fixture API and called the real executor. All 28 checks passed: `/health` 200; POST `/echo` with JSON body, duplicate `X-Test` header and duplicate query keys; 404 returned as a response; 1 s timeout vs `/delay?ms=5000` → Timeout (~1008 ms); user cancel at 500 ms → Cancelled, and the fixture counted the cancelled delays; pre-cancelled token → Cancelled without throwing; `/large` 11 MiB over the default 10 MiB cap → ResponseLimit with status 200 retained and 0 body bytes; exactly-at-cap OK and cap+1 → ResponseLimit; chunked over cap → ResponseLimit; gzip 5 MB decoded over a 1 MiB cap → ResponseLimit while a 500 KB gzip body decodes fully; `/redirect` returned 302 and `/redirect-count` stayed 0; bearer header sent; non-loopback and userinfo URLs → PolicyDenied; the header/proxy validation cases above → Validation; closed port → DnsOrConnection; dead explicit proxy → failure with no direct fallback; pool reuses a client per profile, stays bounded at 8, and disposes an evicted client once released.
- Found and fixed during verification: LRU eviction used a coarse tick counter that can tie; replaced by a monotonic sequence number.

**NOT RUN / pending**
- TLS certificate failure and real system/corporate proxy behavior (no HTTPS fixture or managed proxy available here). Category mapping for Tls/Proxy is implemented but unverified. Corporate proxy remains a pending external check.
- Not yet used from the UI (Step 06).

**Next:** Step 06 — request editor and response view. (done, see below)

## Step 06 — Request editor and response view

**Implemented**
- Core (pure): `Resolution/RequestBuilder.cs` turns an immutable `RequestDefinition` snapshot into a `ResolvedRequest`: requires absolute http(s); rejects `#` fragments; keeps existing query text as typed and appends enabled rows with name/value each encoded once (`Uri.EscapeDataString`), duplicates preserved in order; disabled rows ignored; enabled rows without a name are errors; headers in order; Body None → no content, Text → UTF-8 `text/plain; charset=utf-8`, Json → must parse (position-only error) and defaults to `application/json`; a user Content-Type header overrides the body default. `Presentation/ResponseTextDecoder.cs` (declared charset, UTF-8 fallback, warning for unsupported charset, binary → metadata only, BOM stripped) and `JsonText.TryFormat` (indent without changing values).
- Desktop: `RequestEditorViewModel` (Send/Cancel/FormatJson; `ToDefinition()` snapshot before Send; one send at a time; cancellation source per send; unexpected-exception guard), `KeyValueEditorViewModel`/`KeyValueRowViewModel` (Add/Remove for parameters and headers), `ResponseViewModel` (status glyph + text + tone, elapsed ms, byte count, headers, Raw and Pretty text kept separately, banner for errors/warnings, Copy body only on explicit click via `IClipboardService`, Clear; previous response cleared at the start of every send; decode/pretty-print off the UI thread; display capped at 2,000,000 characters with a visible note, Copy uses the full body). Views: `RequestEditorView`, `KeyValueEditorView`, `ResponseView` (read-only `TextBox`es — no HTML/WebView). Main window toolbar now bound to the editor; Enter in the URL box sends.
- Fixes found while verifying: TabControl template lacked `PART_SelectedContentHost`, so tab content was invisible to UI Automation/screen readers (fixed in `Themes/Controls.xaml`); glyphs in MainWindow.xaml were corrupted by a UTF-8/ANSI re-save and were restored; added `WarningBrush` to theme tokens and high-contrast map.

**Placeholders still present (replaced later)**: Authentication / Assertions / Extraction tabs, environment selector, runner pane.
**Known limit until Step 09**: `{{variable}}` placeholders are not resolved yet, so URLs containing them are rejected as invalid; `RequestBuilder` is where substitution is added.

**Verified** — built Debug (0 warnings, 0 errors); drove the real window with Windows UI Automation against the fixture API:
- POST `/echo` with URL `?fixed=1`, two rows `tag=a b` and `tag=c&d`, headers `X-Active` (enabled) and `X-Disabled` (unchecked), JSON body: fixture saw query `fixed=1; tag=a b; tag=c&d` (encoded once, order kept), `X-Active: yes`, no `X-Disabled`, `Content-Type: application/json`, exact body; status `200 OK`, ms and byte count shown.
- Raw stayed single-line and unchanged after viewing Pretty; Pretty was indented; Headers tab lists response headers.
- Format JSON indented a valid body; on invalid JSON the body was left untouched and an inline message with position appeared; Send with invalid JSON did not send and showed `The JSON body is not valid (line 1, position 7).`
- 404 shown as `404 Not Found` with its body; previous response cleared on each send.
- Cancellation: during a 10 s delay Send was disabled and Cancel enabled; after Cancel the status was `Cancelled` (~0.8 s), Send re-enabled, the fixture counted the cancelled request.
- Fragment URL and URL without scheme rejected with clear messages; Copy body left the clipboard untouched after Send and copied the body only after clicking Copy body; Clear returns to `No response yet`; binary response (octet-stream) showed metadata only; invalid-JSON response showed a warning while Raw kept the text.

**NOT RUN**
- Non-UTF-8 / unsupported-charset response (fixture has no such endpoint yet; unit logic written, not exercised), very large body display limit, keyboard-only walk-through of the new tabs.

**Next:** Step 07 — storage, configuration and recovery. (done, see below)

## Step 07 — Storage, configuration and recovery

**Implemented**
- `Configuration/StorageOptions.cs`: data directory = `--data-dir <absolute path>` (also `--data-dir=`) else `C:\temp`; `restByAno.settings.json`, `restByAno.db`, `restByAno-backups\` inside it; relative/missing/duplicate values rejected with a message; no registry or roaming pointer.
- `Configuration/AppSettings.cs` + `SettingsStore.cs`: defaults timeout 30 s, response cap 10,485,760 bytes (`responseLimitBytes`), retention 30 days; validation timeout 1–600, cap 1 KiB–100 MiB, retention 1–3650; atomic save (temp sibling + `File.Replace`/`Move`); corrupt, newer-version or out-of-range files are reported and never overwritten.
- `Configuration/SingleInstanceGuard.cs`: named mutex per canonical (case-insensitive, trailing-slash-insensitive) data directory, held for the app lifetime.
- `Persistence/`: `Migrations.cs` (schema v1 exactly as in the guide plus two run indexes), `SqliteConnectionFactory` (no pooling, foreign keys on, 5 s busy/command timeout, SQLite error → actionable `StorageProblem`), `DatabaseInitializer` (create-new via temp file + move, never replaces; open → validate → refuse newer/unrecognised → backup with SQLite's backup API into `restByAno-backups` as `restByAno-v<N>-<utc>.db` → migrate in a transaction with rollback), `StorageBootstrapper` (first launch initializes explicitly; settings say initialized + database missing → `MissingDatabase`; `RestoreFromBackup` validates the backup and never overwrites; `CreateNewDatabase` refuses an existing file; write probe; cleanup only of this app's own temp patterns), `StorageJson` (camelCase, enums as names), `JsonDefinitionTable`, `CollectionRepository`, `EnvironmentRepository` (parameterized SQL, transactions, upsert; collection defaults must be non-secret; environment variables must have exactly one of value/secret; deleting an environment cascades its secrets). Blocking SQLite work runs off the UI thread.
- Desktop: themed `DialogWindow` + `DialogService` (message/choice dialogs; native Open File dialog is OS-controlled), `StartupCoordinator` (Restore from backup / Create new database / Exit loop with a confirmation, failure dialogs with "Open data folder"), `App.xaml.cs` startup sequence (args → single instance → storage bootstrap → apply timeout/cap from settings → main window).
- Not built yet (later steps, per guide): secret store (Step 10), run repository (Step 15), retention purge (Step 15), Settings UI (Step 11).

**Verified**
- `dotnet build restByAno.slnx -c Debug`: 0 warnings, 0 errors.
- Throwaway harness (scratchpad, not in repo) against the real Infrastructure code in temp folders — 55 checks passed: option parsing; new nested directory initialization (settings + db, 5 tables, version 1, foreign keys on); collection and environment round trips after a "restart", enums stored by name, no secret value in stored JSON, upsert/delete, FK cascade of secrets and FK enforcement; single-instance mutex (second holder on another thread rejected, re-acquire after release, canonical key); unwritable path (`C:\Windows\…`, nothing created) and path under a file; missing database → `MissingDatabase`, never silently recreated, restore from backup keeps data, restore refuses to overwrite and refuses a non-database, create-new works and refuses to replace; migration v1→v2 with pre-migration backup (backup holds v1 data, db is v2 with data kept), failing migration rolled back with backup kept; newer-version database refused byte-for-byte untouched with no backup; corrupt and foreign SQLite files refused untouched; locked database reported after the busy timeout (7 s); settings atomic write, own-temp cleanup while unrelated files are preserved, corrupt/newer/invalid settings refused untouched, limits at both boundaries; database present without settings keeps data.
- Real app via UI Automation with throwaway `--data-dir` folders: fresh directory (created, files written, window titled restByAno); second instance on the same folder (even with a trailing slash) shows the themed "already running" dialog and exits with code 1 while the first keeps running; restart leaves the database byte-identical; missing database shows Restore/Create new/Exit, creates nothing until confirmed, Back works, Create new then opens the app, backups preserved, Exit leaves the folder untouched; corrupt database, unwritable folder, relative `--data-dir` and newer-version database each show an actionable dialog and leave files untouched.
- Found and fixed during verification: SQLite's default 30 s command timeout made a locked database hang startup for ~37 s; now 5 s.

**NOT RUN**
- Restore through the native Open File dialog in the real window (OS-controlled; restore logic itself verified by the harness), WAL mode (not enabled), real-disk-full behavior, behavior on a network share.

**Next:** Step 08 — collection management. (done, see below)

## Step 08 — Collection management

**Implemented**
- Core: `Models/CollectionLayout.cs` — `ExecutionOrder` (folders by `Order`, each folder's requests by `Order`, then root requests by `Order`; requests pointing at a missing folder are treated as root) — the order the tree shows and the Step 14 runner must use; `DefinitionCloner` (deep copies with fresh ids for collection, folders, requests, assertions, extractors; folder references remapped).
- Desktop: `CollectionTreeViewModel` + node view models (`CollectionNodes.cs`) and `CollectionsView` (themed TreeView with action buttons, F2 rename, Del delete). Collections load expanded; no demo data: a new collection is empty.
  - Create collection / folder (one level) / request. A new request is unsaved: it appears as "(unsaved)", is not written to the database until Save, and is dropped on Discard.
  - Rename (collections, folders, requests), Duplicate (collection, folder, request — all with new ids; a duplicated open request is copied as it appears on screen), Move Up/Down within the same group (folders and requests are separate groups; order values are normalized 0..n-1 on every save), Move to… (folder or collection root), Delete.
  - Deleting: every delete asks first with Cancel as the default button; deleting a folder with requests requires an explicit choice (move requests to the collection root / delete folder and its requests / cancel); deleting a collection keeps run history.
  - Structure changes are saved immediately; request edits are saved with Save/Ctrl+S. Commands run one at a time (busy guard).
- Dirty tracking: `RequestEditorViewModel` compares a JSON fingerprint of what is on screen with the saved baseline (folder/order excluded). Opening another request, creating a new request, or closing the window with unsaved edits shows a themed Save / Discard / Cancel dialog; Cancel returns the selection to the open request. Selecting a request never sends it. Editor header shows request name, "Cleanup request" flag, Save, location, and "● Unsaved / ✔ Saved". The editor still works as an unsaved scratch pad when no request is open.
- `DialogWindow`/`DialogService` extended with text-input and choice-list dialogs (`PromptText`, `PickChoice`), still dark-themed.

**Verified**
- Build: 0 warnings, 0 errors (full rebuild).
- Core harness (6 checks): execution order incl. orphan handling; collection/request/folder copies share no ids with the original and keep content, order and folder links.
- Real window via UI Automation with a throwaway `--data-dir` (database inspected with Python/sqlite3): no demo data on first run; created collection → folder → three requests (one JSON POST with body) and a root-level cleanup request; new request not in the database until saved; Save persisted and the label changed to "✔ Saved"; duplicate created "Get customer copy" right after the original with new ids (all ids unique across the database, 20 after duplicating a whole collection); Move Up/Down changed tree and stored order; stored order = displayed order; dirty edit → switching request prompts Save/Discard/Cancel — Cancel kept the edit and put the selection back, Discard restored the saved URL, Save persisted it; an unsaved new request discarded left nothing behind; renames of request and folder persisted; after closing and restarting the app the tree, order, request fields and JSON body were identical; delete request asked, Cancel kept it, confirm removed it; folder delete offered the three explicit choices and both "move to root" and "delete with requests" persisted correctly; Move to… listed destinations and moved the request into a folder and back to the root; collection delete asked with counts and removed it; blank names are rejected by the input dialog; closing the window with unsaved edits prompts, Cancel keeps the app open, Discard closes.
- Defects found and fixed during verification: (1) closing the window crashed the process (exit code 0xE0434352) because `Close()` was called while the closing event was still being processed — now deferred, and no prompt is created when nothing is dirty; (2) after "Move to…" WPF selected the parent node and the selection state drifted from the view model, leaving Move to disabled — selection is re-asserted explicitly.

**NOT RUN**
- Drag-and-drop (optional per guide; Move Up/Down/Move to… cover reordering), keyboard-only walkthrough of the tree, very large collections, high-contrast appearance of the new controls.
- Request fields not yet editable (Authentication, Assertions, Extraction tabs) are preserved untouched by Save but cannot be edited until Steps 10 and 13.

**Next:** Step 09 — variables and environments. (done, see below)

## Step 09 — Variables and environments

**Implemented**
- Core `Resolution/`: `VariableNames` (`[A-Za-z_][A-Za-z0-9_]*`, case-sensitive), `VariableSnapshot` (+builder; frozen at Send/Run; secret-value list for redaction), `TemplateResolver` (`{{name}}`, single non-recursive pass; problems list names only: undefined / unreadable / invalid references), `VariableSnapshotFactory` (precedence run > environment > collection defaults; secrets read through `ISecretStore`, missing/undecryptable → "unreadable"; extracted run values are treated as sensitive), `RequestBuilder` now resolves URL, query names/values (resolved, then encoded once), header names/values (CR/LF after substitution rejected), text bodies, and JSON bodies (template parsed as JSON first; only string VALUES substituted; property names and non-string nodes untouched; serialized by System.Text.Json so quotes/backslashes/newlines are escaped exactly once; unquoted `{{value}}` rejected with an explanation). Resolved URL must be absolute http(s) without userinfo or fragment.
- Desktop: toolbar environment selector (`ActiveEnvironmentViewModel`, "No environment" + saved environments) with the choice persisted as `selectedEnvironmentId` in settings via the new `SettingsService`; the environment is loaded fresh at each Send. `Manage…` opens the environment editor (`EnvironmentEditorViewModel`/window): add, select, rename, delete environments; add/remove non-secret variables; validation (identifier names, no duplicates, names only in messages); explicit Save and a Save/Discard/Cancel guard on switching or closing. `Variables…` in the collections pane edits a collection's default variables (`CollectionVariablesViewModel`/window). Collection defaults reach the editor through the open request's collection.
- Accessibility: list/combobox items expose their names to UI Automation.

**Verified**
- Build: 0 warnings, 0 errors.
- Core harness (31 checks): name rules; two environments → two targets; precedence incl. defaults-only without environment; run values marked sensitive; nested-looking replacement text stays literal (URL, text body, JSON); missing/invalid/case-mismatched variables block the request and are reported by name only (a secret value never appears in a message); absolute http(s)/userinfo/fragment rules after substitution; query rows resolved then encoded once with duplicates kept and disabled rows ignored; header CR/LF; JSON with quotes, backslash, newline, tab and non-ASCII round-trips exactly, numbers/bools/null untouched, number text preserved, property names not substituted, unquoted placeholder rejected; unreadable secret blocks with name only; snapshot frozen after build.
- Real window via UI Automation against two fixture servers: created environments Local and Other; duplicate and invalid names rejected with messages; unsaved edit + switching shows the prompt, Cancel keeps the edit and selection, Discard restores the saved values without touching the other environment; the same request `{{baseUrl}}/echo` reached server A under Local and server B under Other; with no environment or a missing variable the send was blocked and the fixture's hit counter did not change; a JSON body with `{{customerName}}` containing quotes and a backslash arrived intact; unquoted placeholder rejected; collection default `baseUrl` used with no environment and overridden by the environment while another default still applied; the selected environment survived a restart; deleting the active environment fell back to "No environment" and cleared the saved id.
- Defects found and fixed during verification: ListBox selection revert was unreliable (replaced by a view-level guard around the view model's `TrySelectAsync`); list items had no accessible names.

**NOT RUN**: secret variables and credential entry (Step 10), per-environment transport profile editing (Step 11).

**Next:** Step 10 — secrets, authentication and sanitization. (done, see below)

## Step 10 — Secrets, authentication and sanitization

**Implemented**
- Infrastructure `Secrets/DpapiProtector` (DPAPI CurrentUser; the secret id is mixed in as entropy so a ciphertext cannot be moved to another row; plaintext bytes zeroed) and `DpapiSecretStore` (`ISecretStore`: save/read/delete; read reports Ok / Missing / Unreadable). Infrastructure is marked Windows-only (`SupportedOSPlatform`).
- `IEnvironmentRepository.SaveAsync(environment, secretUpdates)` / `EnvironmentRepository`: environment definition, new/changed secret ciphertexts and removal of this environment's now-unreferenced secrets happen in ONE transaction; every secret variable must end with a stored value or nothing is saved; a secret id from another environment cannot be overwritten; deleting an environment cascades to its secrets. Only the secret reference (id) is in the environment JSON.
- Core `RequestBuilder` authentication: Bearer (`Authorization: Bearer <value>`), Basic (standard Base64 `user:password`, colon in user rejected), API key in header or query (query value encoded once). Auth variable names are validated; missing/unreadable/empty values block the request with names only. Enabled user header with the same name (case-insensitive), a query row or URL query with the same key, or any manual `Authorization` header together with Bearer/Basic are rejected before sending.
- Core `RedactionContext`: known secret values masked in plain, Base64 and percent-encoded forms; sensitive header-name list; `VariableSnapshot.SecretValues` feeds it (run-extracted values are sensitive). `ResolvedRequest` is not serializable-by-design (class with a withholding `ToString`).
- Desktop: Authentication tab is real (type, variable names per type, API key location) and saved with the request; environment editor rows have a Secret checkbox and a `PasswordBox` (via the `PasswordBoxBinder` adapter) with a hint ("Stored encrypted. Leave blank to keep it."); typed secrets are cleared after a successful save, toggling Secret never copies text between plain and secret values; unsaved secret entry counts as a change without keeping the text in any model; collection defaults cannot be secret.
- `VariableDefinition.IsWellFormed/IsSecret` are excluded from stored JSON.

**Verified**
- Build: 0 warnings, 0 errors.
- Harness (36 checks, real DPAPI and SQLite in a temp folder): ciphertext contains neither UTF-8 nor UTF-16 plaintext; round trip; ciphertext bound to its id; tampered/damaged ciphertext → Unreadable (no exception); atomic environment+secret save incl. failure rollback; keep-on-null, replace, removal and cross-environment isolation, cascade delete; database and settings files contain no plaintext secret; snapshot factory (readable/missing/unreadable); every auth type and conflict rule; redaction forms; `ToString` of `ResolvedRequest`.
- Real window via UI Automation + fixture with DUMMY credentials only: created an environment with four secrets (password fields reported `IsPassword`, no readable value, entry cleared and hints shown after Save, a secret row without value refused); bearer, API key (header and query) and Basic requests returned 200 and unauthenticated returned 401; the echo endpoint showed the exact `Authorization: Bearer <value>` and a Basic header decoding to `user:password`; Bearer + manual Authorization header blocked before sending; unknown credential variable blocked naming only the variable; after closing and reopening the app the same secrets still worked; with the ciphertext deliberately damaged the send was blocked with "Variable(s) that cannot be read: bearerToken …"; a byte search of the database, settings and backup folder found none of `fixture-token`, `fixture-key`, `fixture-password` or the distinctive dummy token (the plain, non-secret variable value was found in the database as expected).
- Defects found and fixed during verification: `PasswordBoxBinder` never attached its change handler when the bound value started as an empty string (typed secrets were lost) — default value changed to null; computed properties were being written into stored JSON.

**NOT RUN**: decrypting on another Windows account or machine (DPAPI CurrentUser makes secrets non-portable by design; transfer requires re-entry — documented in Step 16), real credentials (never used).

**Next:** Step 11 — settings and destination policy. (done, see below)

## Step 11 — Settings and destination policy

**Implemented**
- `Infrastructure/Policy/ManagedTargetPolicy.cs` (replaces the loopback-only policy in the composition root): reads `%PROGRAMDATA%\restByAno\target-policy.json` (cached until the file changes), exact scheme + normalized host + effective port matching, no wildcards/suffixes; no file → loopback only with a setup message; unreadable/malformed/unsupported-version/invalid-entry file → fail closed for everything; loopback must be listed once a policy exists; non-loopback HTTP needs a matching entry AND `AllowNonLoopbackHttp`; credentials in URL and non-http(s) schemes always denied. `GetStatus()` exposes mode, path and targets for the UI. Never written by the app. `LoopbackOnlyTargetPolicy` is kept as the no-policy rule.
- `TransportProfile.ForSend`: effective profile per send = global timeout/cap from settings + proxy/non-loopback-HTTP from the active environment (system proxy, HTTPS-only without an environment). The request editor composes it at Send time, so changes affect future sends only.
- Desktop `SettingsViewModel` / `SettingsWindow` (File → Settings…, Ctrl+,): timeout, response limit (KiB), retention (validated 1–600 / 1 KiB–100 MiB / 1–3650), active environment's proxy mode and address (http/https URL validated; no "retry without proxy" option), non-loopback HTTP switch, read-only policy status with allowed targets and file path, data folder with `--data-dir` instructions. Settings and the environment profile are saved atomically/transactionally; errors keep the old values.
- Docs: `docs/target-policy.sample.json`; `docs/environment-notes.md` describes the policy and transport rules.

**Verified**
- Build: 0 warnings, 0 errors.
- Policy harness (41 checks, temp policy files + real executor + fixture): defaults path; no policy (loopback incl. IPv6/localhost allowed, other host denied with the setup message); active policy (listed loopback allowed, port/scheme mismatch denied, loopback and localhost denied when unlisted, exact host, inferred 443, case-insensitive, no suffix/prefix matching, non-loopback HTTP needs listing AND the flag, flag alone insufficient, credentials denied); live edit picked up; empty list denies all; 11 invalid-policy variants (malformed, wrong types, newer version, wildcard, path in host, bad scheme/port, missing host, empty file) and an exclusively locked file all fail closed (loopback blocked too) and recover when fixed; through the executor: listed target sent, port mismatch → PolicyDenied before any send, listed corporate-style host passes policy and fails only as a transport error, redirects still disabled (302 returned, target counter 0), explicit proxy failure stays a failure, broken policy blocks loopback.
- Real window via UI Automation: settings window defaults; validation for 0, max+1 and non-numbers; boundary values (1/1/1 and 600/102400/3650) accepted; file content updated; timeout 1 s timed out a 5 s request after ~1.2 s and a 1 KiB cap stopped a 5,000-byte body ("Response too large"), restored limits worked; environment transport: invalid and ftp proxy addresses rejected, a dead explicit proxy produced "Connection failed … (a proxy is configured)" after ~2.3 s with no direct fallback, persisted across reopening, and switching back to the system proxy worked; with a temporary policy file in `C:\ProgramData\restByAno` (created and removed by the test; the folder did not exist before): listed loopback target allowed, port mismatch / unlisted `localhost` / unlisted host denied with explicit messages, settings window listed the targets, a malformed policy blocked even loopback and settings showed it as INVALID, and removing the policy restored loopback-only behavior with the corporate-host setup message.

**NOT RUN / pending external**
- Behavior behind a real corporate proxy (system proxy discovery, PAC, proxy authentication) and a real administrator-deployed policy on a managed device: PENDING — no corporate access here; instructions are in `docs/environment-notes.md`.
- TLS certificate failures (no HTTPS fixture); a request already in flight while settings change (guaranteed by design: the resolved request carries its own immutable transport profile).

**Next:** Step 12 — safe native import/export. (done, see below)

## Step 12 — Safe native import/export

**Implemented**
- `Infrastructure/ImportExport/CollectionJsonService.cs` and `EnvironmentJsonService.cs` (+ `ImportSupport.cs`: strict `FieldReader`, `ImportOutcome`, `AtomicFile`, UTF-8 BOM handling). Envelope `{"schemaVersion":1,"kind":"collection"|"environment","definition":{…}}`, camelCase properties, enums as names.
- Export: collections are exported as stored (secret-free; no history); environments export plain variables with their values and secret variables as `{"name":…,"requiresSecret":true}` only — no id, ciphertext or value; transport/proxy settings are never exported. Written atomically (temp file + move); the user chooses the file in a native Save dialog. The UI states that request content is exported as written (business data may be included) — no claim of anonymization.
- Import: file cap 10 MiB, JSON depth 64, UTF-8 BOM accepted. The WHOLE document is validated before anything is written; any problem rejects the import and lists up to 15 messages with their location. Checked: malformed JSON, unknown/missing `schemaVersion` or `kind` (e.g. Postman files are rejected — no Postman import), wrong kind for the importer, missing names, invalid method/body/auth/assertion/extractor shapes (JSON bodies must parse; JSON pointers must be valid; JsonEquals scalars only), invalid variable names, duplicate IDs / variable names, folder references to folders not in the file, header line breaks. Credentials: any literal credential field inside `auth` (`token`, `password`, `apiKey`, …) rejects the file; secrets with values/ciphertext in an environment file are rejected; collection defaults cannot be secret. Fresh ids are generated for the collection, folders, requests, assertions and extractors and folder references are remapped, so an import never overwrites anything (importing twice creates two collections).
- Unsupported content is never silently dropped or promised: scripts, cloud/account fields, resolved request values, `transportProfile`, history and any unknown field are ignored and listed in the confirmation dialog (Cancel changes nothing).
- Imported secret variables get a fresh local slot WITHOUT a value; the importer reports which secrets need entry, the environment editor shows them as "Enter the secret value." and refuses to save until entered (`IEnvironmentRepository.GetStoredSecretIdsAsync` tells the editor which secrets really exist).
- Core `Assertions/JsonPointerNavigator.cs` (RFC 6901 parse/validate/resolve; also used by Step 13).
- Desktop: collections pane `Import…`/`Export…`; environment editor `Import…`/`Export…`; `IDialogService.PickSaveFile`.

**Verified**
- Build: 0 warnings, 0 errors.
- Harness (61 checks, no UI): rich collection (folders, headers, JSON body, all auth types, five assertion kinds, extractor, cleanup, defaults) exports and re-imports to the same structure and run order with entirely fresh unique ids and remapped folder references; importing the same file twice never collides; environment export contains no secret id/value/ciphertext and no proxy settings; imported secrets get new bindings and are reported; 30 rejection cases each fail whole with a specific message (incl. numeric enum strings, duplicate ids, literal credentials, nesting > 64, oversized file, Postman-style file); unsupported/forbidden fields produce visible warnings; BOM files import; export leaves no temp files.
- Real window with the native Open/Save dialogs driven by keyboard: built a collection (JSON POST with body + auth reference, a request in a folder), exported it (file contains no secretId/ciphertext/history; status explains what is and is not exported), imported it (confirmation showed folder/request counts; a second collection with fresh ids appeared; database hash changed), then four invalid files (unsupported version, multiple errors incl. a literal credential, an 11 MiB file, non-JSON text) each showed an "Import failed" dialog with specific messages and left the database byte-identical; a file with scripts / `workspaceId` / `resolvedUrl` listed them as ignored, Cancel changed nothing, Import added the collection without them. Environment: export wrote `requiresSecret` and no secret value, import created a second environment whose secret showed "Enter the secret value.".
- Defects found and fixed during verification: files saved with a UTF-8 BOM (Notepad/PowerShell 5) were rejected as invalid JSON; the environment editor's Export button stayed disabled after selecting an environment.

**NOT RUN**: importing/exporting very large collections (near the 10 MiB cap) through the UI; Postman and OpenAPI import are out of scope by design.

**Next:** Step 13 — assertions and extraction. (done, see below)

## Step 13 — Assertions and extraction

**Implemented**
- Core `Assertions/JsonPointerNavigator` (RFC 6901: empty = root, `~1`/`~0`, invalid escapes rejected, array index = non-negative base-10 without leading zeros, `-` never resolves), `ResponseBodyJson` (parses the body once per step and shares it; invalid/empty/binary bodies produce a readable failure, never an exception), `AssertionEvaluator` (StatusEquals numeric; HeaderEquals case-insensitive name + ordinal value against any individual returned value, no comma splitting; JsonExists true for any resolvable value incl. null; JsonEquals typed scalar comparison — strings ordinal, booleans/null exact, numbers as decimals when representable otherwise finite doubles, objects/lists rejected; DurationBelow strictly `<`), `RuleValidator` (field-level rules shared by the editors).
- Each result is a value-free `AssertionResult` (kind, path/header, passed, safe reason) plus an on-screen-only `Detail` (expected/actual, shortened to 80 chars) that passes through `RedactionContext` first, so known secret values are masked.
- Core `Running/ResponseExtractor` (string → raw text; number/bool/null → JSON literal text; objects/lists rejected; all-or-nothing: if any extractor fails, none of the step's variables are produced; messages never contain values; duplicate variable in a step rejected), `Running/StepChecker` (assertions first; extraction only after all pass; outcome Passed / AssertionFailed / ExtractionFailed; zero-assertion steps are labelled).
- Desktop: real **Assertions** and **Extraction** tabs (`AssertionsEditorViewModel`, `ExtractorsEditorViewModel`, rows with add / remove / move up / move down, kind-specific fields, per-kind hints incl. the strict-boundary explanation, inline field-level errors); invalid rules block both Send and Save with a clear list; rules are saved with the request, included in dirty tracking, and keep their ids. The response pane has a **Checks** tab and a summary next to the metrics ("✔ 5 of 5 assertions passed", "✖ 1 of 2 assertions failed", "✖ Extraction failed", "○ No assertions…") — glyph + text, never colour alone; extracted values are never displayed ("value hidden"). A standalone Send evaluates checks but, by the guide, extracted values only live inside a collection run (Step 14).

**Verified**
- Build: 0 warnings, 0 errors.
- Harness (51 checks): pointer parsing and resolution incl. `~0`/`~1`, key `01`, empty key, indexes (`-`, `01`, `+1` not found), null existence; correct/wrong status; header-name casing and ordinal value, multi-value headers, no comma splitting; `"1"` vs `1`, `true` vs `"true"`, null, `1.50`==`1.5`==`1.500`, numbers beyond decimal range via doubles; objects/lists not comparable; missing path; invalid JSON / empty / plain text / binary bodies fail readably; duration boundary (499 pass, 500 and 501 fail); detail redaction and shortening; extraction literals, atomic failure, duplicates, scalar documents; step checker outcomes incl. "failed extraction leaves previously extracted variables intact".
- Real window (UI Automation + fixture): five assertion kinds all passed (`5 of 5`); wrong status / string-vs-boolean / header value case / missing path all failed with value-free reasons and on-screen details (`expected 201, got 200`); numeric `50` matched and string `"50"` was reported as a type mismatch; invalid JSON response failed JSON rules readably; extraction on POST `/customers` showed "Extracted customerId … (value hidden)" with no GUID anywhere; a failing extractor gave "Extraction failed" with a named, value-free message; a failed assertion skipped extraction with an explanation; invalid rule fields showed inline errors and blocked Send and Save; valid rules saved (database shows both assertions in the reordered order and the extractor with `sensitive: true`) and survived a restart.
- Fix made during verification: error text controls had accessible names that hid their content.

**NOT RUN**: header/duration/JSON-null cases through the UI (covered by the harness; the fixture has no null-valued endpoint until Step 15).

**Next:** Step 14 — sequential collection runner. (done, see below)

## Step 14 — Sequential collection runner

**Implemented**
- Core `Running/CollectionRunner` (+ `CollectionValidator`, `RunConfiguration`, `RunProgress`): validates the saved collection globally (empty collection, only cleanup requests, invalid method/URL/folder reference/JSON body/auth shape/assertion/extractor — undefined variables are deliberately NOT a global error because an earlier step may extract them), then runs ordinary requests in saved order (`CollectionLayout`) followed by every cleanup request once, each through the same pipeline: variable snapshot (run > environment > collection defaults) → `RequestBuilder` → `IRequestExecutor` → `StepChecker` (assertions, then extraction) → `StepRecord`. Extracted values become run variables (sensitive, redacted) for later steps and vanish with the run. Stop-on-failure skips the remaining ordinary requests but cleanup still runs; a failing cleanup never stops the other cleanups; cleanup with unresolved variables is `ValidationFailed` and sends nothing; a transport failure/validation failure/failed assertion/failed extraction is a failure; a response with an HTTP error status still gets its explicit assertions and a step without assertions is labelled ("No assertions defined: the request completed."). Cancellation stops the request in flight, marks later ordinary steps Skipped and the cleanup steps explicitly Skipped ("Cleanup skipped because the run was cancelled.") — nothing is sent after a cancel. Run outcome: Cancelled / Failed (any step, cleanup included) / Passed; original failures and cleanup failures stay separate records (`IsCleanup`). Progress is reported after every step with redacted text. `SnapshotHash` is a SHA-256 of the secret-free structure (collection + environment variable names/plain values/secret flags + stop mode), never of resolved values.
- Desktop: `RunnerViewModel` + `RunnerView` (replaces the placeholder pane): Run collection / Stop buttons, "Stop on first failure" checkbox, live step list (pending rows shown up front; glyph + outcome text, HTTP status, ms, bytes, assertion count, cleanup/no-assertion labels, safe message), summary line (passed / failed / skipped, cleanup counts, duration). `RunState` disables the standalone Send while a run is active (one run at a time); the run uses the SAVED state of the selected collection (`GetRunSnapshot`), so edits during a run cannot change it. Closing the window during a run asks whether to stop it. The finished `RunRecord` is kept in memory (`LastRun`, `RunFinished` event); persistence and history arrive in Step 15.

**Verified**
- Build: 0 warnings, 0 errors.
- Harness (25 checks, fake executor): create→read→delete with ids flowing between steps; cleanup listed once/last/flagged; progress counters; stop vs continue modes; cleanup after a failed assertion without sending the skipped step; cleanup with a missing id → ValidationFailed and no transport call; cleanup failure does not stop the next cleanup; failed extraction → ExtractionFailed and its variables not written; zero-assertion labelling and explicit 404 expectation; transport failure; cancellation during a step and before the start (no sends after cancel); global validation cases; undefined variable not a global error; saved-order execution (folders then root); second run starts with empty run variables; secret absent from the run record and every progress text; unreadable secret → ValidationFailed; snapshot hash ignores secret ids and changes with structure.
- Real window (UI Automation, collections imported through the native Open dialog, fixture API): create→read→delete passed (`Run passed`, 2 passed + cleanup 1 ok); intentional assertion failure with stop-on-failure skipped the later step with an explicit message while the cleanup delete still passed, and continue mode ran it; cleanup with a never-extracted id showed "Not sent (validation) … Undefined variable(s): customerId"; a failing cleanup was followed by a passing one; failed extraction named the extractor and the zero-assertion step was labelled "no assertions"; empty and cleanup-only collections showed "cannot be run" with reasons; cancellation during a 20 s request stopped it within ~0.45 s, later steps were Skipped, the cleanup was explicitly skipped, the fixture counted the cancelled request, and standalone Send was disabled during the run and enabled again afterwards; no extracted id appeared anywhere in the runner pane; closing during a run showed the stop/keep dialog.

**NOT RUN**: runs with secret credentials end to end in the UI (covered by the harness and by Step 10's request-level checks), very long runs / large collections, keyboard-only operation of the runner.

**Next:** Step 15 — history, reports and sample collections. (done, see below)

## Step 15 — History, reports and sample collections

**Implemented**
- `Persistence/RunRepository` (`IRunRepository`): stores metadata only in the `runs` table (ids, UTC timestamps, outcome, snapshot hash, step records with status/timing/size/assertion kind-path-result and safe reasons). `ListAsync` newest first (optionally per collection), `PurgeAsync(cutoff)` deletes runs that ended strictly before the cutoff (`DateTimeOffset.MaxValue` = clear all). The collection id is a historical reference (no foreign key), so deleting a collection keeps its history. A damaged history row cannot break the list.
- `ImportExport/JsonReportExporter` (`IReportExporter`): JSON report `schemaVersion` 1, `kind: runReport` — run metadata, summary counts and step results (outcome, status, ms, body bytes, error kind, safe message, assertion kind/path/header/passed/reason); bounded (≤ 10,000 steps, messages ≤ 1,000 chars, ≤ 5 MiB) and written atomically from an explicit Save dialog. It cannot contain URLs, headers, bodies, extracted values or expected/actual values because the model has no such fields.
- Desktop: `RunnerViewModel` saves every finished run (a storage problem is reported without hiding the result); new **History** tab (`HistoryViewModel`/`HistoryView`): newest-first list with collection name (or "(deleted collection)"), outcome glyph + text, steps and duration; selecting a run shows its steps; **Export report…**, **Refresh**, **Clear history…** (explicit confirmation, Cancel is the default; touches only the runs table). Retention purge runs once at startup using the configured retention days and reports "Removed N run(s) older than…". Runner and History share the bottom pane as two tabs.
- Samples: `samples/fixture-collection.json` (customer create/read/delete with cleanup, bearer / API key header+query / basic authentication, JSON type/path/header/duration assertions, explicit 404 expectation), `fixture-demo-failure.json` (intentional failure, skipped step, cleanup), `fixture-demo-cancel.json` (15 s request, cleanup skipped on cancel), `fixture-environment.json`, `samples/README.md`. No port is hardcoded; the fixture can write a ready environment with `--environment-file`.
- Fixture extended for the manual checks: `/status/{code}`, `/json` (numbers, booleans, null, `~0`/`~1` keys, repeated header), `/text-latin1`, `/text-unsupported-charset`, `/binary`, `POST /reset`, `--environment-file`.
- `docs/manual-verification.md` written (setup, endpoints, checklist per step, pending external checks).

**Verified**
- Build: 0 warnings, 0 errors.
- Harness (55 checks, real executor, SQLite, DPAPI, fixture): the shipped samples import with no errors or warnings, never hardcode a port or contain secrets; the sample collection runs end to end (10/10 steps passed, every step has assertions) using the fixture-generated environment with dummy secrets entered; failure and cancellation demos behave as described; non-UTF-8 charset decodes, unsupported charset gives a warning with UTF-8 fallback, binary shows metadata only; history lists newest first, filters by collection, and the stored rows contain none of the dummy secrets, base URL, `Authorization`, `Ada`, `http://` or any extracted id (only step/request/assertion GUIDs); retention boundary (31 days purged; exactly at the cutoff and newer kept); clear-all removes every run and leaves collections, environments and secrets; history survives deleting its collection; the JSON report has the right shape and contains no secrets/URLs/headers/values, is atomic and bounded (step cap enforced).
- Real window (UI Automation + native dialogs): imported the fixture-generated environment and entered the dummy secrets, imported the three shipped samples, ran them (`Run passed` 9 passed + cleanup 1 ok; `Run failed` with a skipped step and a cleanup that still ran; `Run cancelled` with the cleanup skipped); the History tab listed all three newest first and showed the steps of a past run; **Export report…** saved a report (`schemaVersion` 1, `runReport`, summary counts) that contains none of the dummy secrets, the base URL, `Authorization` or `http://`; a byte search of the data folder found none of the dummy secrets (the base URL and sample body text exist only in the configuration tables, as expected); history was still listed after a restart; with controlled old metadata inserted while the app was closed (31 days, 30 days + 2 h, 30 days − 2 h, yesterday) the next start removed exactly the two older ones and reported it; Clear history asked for confirmation (Cancel changed nothing), then emptied only the history while the collections (14 tree items), the environment, its 3 secrets and the 3 collections remained.

**NOT RUN**: report export of a very large run (> 10,000 steps is only covered by the unit-level cap check), keyboard-only operation of the history tab.

**Next:** Step 16 — UX finish and offline package.
