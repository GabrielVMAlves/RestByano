# Terra implementation guide — restByAno

This document is an implementation prompt and ordered build specification. Create the application project in C:\dev\restByAno (under the requested C:\dev root). Use restByAno as the exact application name, solution/project prefix, root namespace prefix and window title. Create the directory if absent; inspect and preserve any existing files. Do not create application source beside these planning documents.

## Copy this opening prompt into Terra

> Create and implement restByAno in C:\dev\restByAno by following this guide in order. Create the directory if absent and use it as the working directory for all project commands. Preserve existing files; do not overwrite an unrelated project. Use restByAno as the application name, main window title, executable name, solution/project prefix and namespace prefix. It is a Windows desktop API client and integration-test runner built with C#, .NET 10, WPF, MVVM, HttpClient, System.Text.Json, SQLite and Windows DPAPI. Default storage is C:\temp. All app-owned views use a dark theme. Do not create unit-test projects or add testing frameworks. Keep the product's request assertions and collection runner. Start with Step 01 and complete each step with real code, a successful build where tooling permits, and its verification evidence. Maintain docs/implementation-progress.md so work can resume across sessions. Do not skip functionality by leaving placeholder implementations. Do not build later-release features. Continue through all implementable steps; report unavailable corporate-machine checks as pending without inventing evidence or blocking unrelated implementation. Ask only when a missing decision prevents meaningful work.

## Fixed requirements

- Project root: C:\dev\restByAno. Application name: restByAno. This source location does not change runtime storage: C:\temp.

- Windows 11 x64 baseline; C# / .NET 10. Target WPF with net10.0-windows.
- UI: WPF + CommunityToolkit.Mvvm. No browser/web frontend.
- Requests: REST/JSON with bearer tokens and API keys first. Include no-auth and basic auth.
- Persistence: C:\temp\restByAno.db. Backup directory: C:\temp\restByAno-backups.
- Configuration: C:\temp\restByAno.settings.json by default.
- No unit-test projects, xUnit, NUnit, MSTest or automated UI suite. Use builds and recorded manual checks.
- The application DOES run user-authored integration-test collections with assertions/extraction.
- No cloud accounts, synchronization, telemetry, remote assets, automatic external updates or JavaScript execution.
- Native HTTP transport; do not install a proxy/server for the desktop UI.
- Dark theme on first launch; no light-theme toggle required. Respect Windows high contrast.
- Do not store plaintext tokens/passwords in saved auth configuration. Use DPAPI CurrentUser and secret references.
- Preserve certificate validation. No automatic redirects, retries or cookies in MVP.
- Default full-operation timeout: 30 seconds. There is no response size limit: bodies over 1 MiB keep a 1 MiB sample in memory and are streamed to a temp file.
- Never modify/delete unrelated C:\temp files. Never replace a corrupt/newer database with an empty one automatically.

## How to execute every step

1. Read repository instructions and docs/implementation-progress.md if present. Preserve unrelated work.
2. Implement only the next unfinished step and necessary fixes to its dependencies.
3. Build the solution. Fix errors caused by the changes before marking the step complete.
4. Perform the specified manual checks when a Windows UI/runtime is available. If unavailable, record NOT RUN and the exact reason; do not claim a build proves UI behavior.
5. Update docs/implementation-progress.md with files changed, build command/result, acceptance evidence, limitations, and the next step.
6. Continue. Do not pause for routine confirmation between steps.

Progress entries must distinguish Implemented, Verified, and Pending external verification. Corporate approval, real corporate connectivity, signing and managed-device installation remain external checks unless access is provided. Do not fabricate company policies or credentials.

## Project layout and dependencies

All paths below are relative to C:\dev\restByAno. The desktop assembly/executable name is restByAno (restByAno.exe); project name restByAno.Desktop and root namespace restByAno.Desktop. Set AssemblyName explicitly in its .csproj. Set the main window Title to restByAno.

```text
restByAno.slnx
Directory.Build.props
global.json
src/
  restByAno.Core/
    Models/ Contracts/ Resolution/ Assertions/ Running/
  restByAno.Infrastructure/
    Http/ Persistence/ Secrets/ ImportExport/ Configuration/ Policy/
  restByAno.Desktop/
    Views/ ViewModels/ Themes/ Services/
tools/
  restByAno.FixtureApi/
docs/
  implementation-progress.md
  manual-verification.md
  environment-notes.md
  user-guide.md
samples/
  fixture-environment.json
  fixture-collection.json
```

References: Infrastructure → Core; Desktop → Core and Infrastructure. Core has no WPF, SQLite or Windows-specific APIs. FixtureApi is independent and excluded from desktop publishing. Do not create the future CLI project yet.

Core/Infrastructure target net10.0; Desktop targets net10.0-windows with UseWPF=true. Enable nullable reference types and implicit usings. Use explicit stable package versions and package lock files. Packages: CommunityToolkit.Mvvm in Desktop; Microsoft.Extensions.DependencyInjection in Desktop; Microsoft.Data.Sqlite and System.Security.Cryptography.ProtectedData in Infrastructure. Use the ASP.NET Core shared framework for FixtureApi. No ORM or extra UI library.

## Step 01 — inspect tooling and scaffold

Working directory: C:\dev\restByAno. Inspect it before scaffolding; create it if absent. Create: restByAno.slnx, three src projects, docs/implementation-progress.md and docs/environment-notes.md. Set Desktop AssemblyName=restByAno and RootNamespace=restByAno.Desktop.

Actions:

- Inspect installed SDKs using dotnet --list-sdks. Choose an installed .NET 10 SDK and pin it in global.json; allow later servicing patches. Do not silently retarget the app if .NET 10 is absent.
- Record unknown corporate proxy/deployment requirements as pending. Default to system proxy.
- Add project references as specified. Configure RestorePackagesWithLockFile=true.
- Add .gitignore entries for bin/, obj/, publish/, local databases, backups and secret-bearing local files.
- Create App.xaml and MainWindow.xaml with a minimal title. Wire a composition root, not a service locator in each view.
- Ensure only the desktop project has WPF references.

Verify: dotnet restore and dotnet build restByAno.slnx. Launch a blank desktop window if possible. Record exact SDK/package versions. Do not add test projects.

## Step 02 — build the dark shell

Create: Themes/Colors.xaml, Themes/Controls.xaml, MainWindowViewModel, Views/MainWindow.xaml.

- Window default size 1280×800, minimum 1000×650; resizing uses Grid and splitters.
- Left collections pane about 260 pixels; right editor/response; a resizable runner pane below.
- Theme tokens: background #121212, panel #1E1E1E, raised panel #252525, text #E6E6E6, secondary text #B3B3B3, border #3F3F46, accent #60A5FA.
- Style buttons, textbox/password controls, combo boxes AND popups, tabs, tree, lists/grids, scrollbars, menus, tooltips, checkboxes and app-owned dialogs.
- Include visible focus, hover, selection and disabled states. Use labels/icons as well as status colors.
- Switch to system brushes when SystemParameters.HighContrast is enabled; react when that setting changes.
- Add toolbar with environment selector, method, URL, Send and Cancel; tabs for Parameters, Headers, Body, Authentication, Assertions and Extraction.
- Placeholder panels are allowed during this step but must be replaced in later steps.

Verify: launch, resize, tab through controls and open each popup. No app-owned white panels; focus remains visible. Record native file dialogs as OS-controlled.

## Step 03 — establish domain contracts

Create Core models as records/enums. Use Guid IDs and DateTimeOffset UTC timestamps. Persist enum names as strings. Models must not carry WPF observable collections.

```text
CollectionDefinition:
  Id, Name, SchemaVersion=1, Defaults, Folders, Requests
FolderDefinition: Id, Name, Order
RequestDefinition:
  Id, FolderId?, Order, Name, Method, UrlTemplate,
  QueryParameters[], Headers[], Body, Auth, Assertions[], Extractors[], IsCleanup
KeyValueEntry: Name, Value, Enabled
BodyDefinition: Kind(None|Text|Json), Content, ContentType?
AuthDefinition:
  Kind(None|Bearer|Basic|ApiKey), TokenVariable?, UsernameVariable?,
  PasswordVariable?, ApiKeyName?, ApiKeyVariable?, ApiKeyLocation(Header|Query)?
EnvironmentDefinition: Id, Name, Variables[], TransportProfile
VariableDefinition: Name, Value?, SecretId?   [exactly one populated]
TransportProfile:
  TimeoutSeconds=30,
  ProxyMode(System|Explicit), ProxyUri?, AllowNonLoopbackHttp=false
AssertionDefinition:
  Id, Kind(StatusEquals|HeaderEquals|JsonExists|JsonEquals|DurationBelow),
  HeaderName?, JsonPointer?, ExpectedJson?, ExpectedStatus?, MaxMilliseconds?
ExtractorDefinition: Id, JsonPointer, VariableName, Sensitive=true
```

Auth fields reference variable names, not resolved values. The environment binds those names to SecretId. Collection defaults are nonsecret only. ExpectedJson stores a validated JSON scalar as JSON text to preserve type; JsonEquals initially compares scalar values only.

Contracts: IRequestExecutor.ExecuteAsync(ResolvedRequest, CancellationToken); ICollectionRepository load/list/save/delete; IEnvironmentRepository load/list/save/delete; ISecretStore save/read/delete; IRunRepository save/list/purge; ITargetPolicy validate; IReportExporter export.

Execution models: ResolvedRequest (immutable, memory-only), ResponseSnapshot (status, response/content headers, bounded body bytes, media type, elapsed ms), ExecutionResult (response or structured error). Error kinds: Validation, PolicyDenied, DnsOrConnection, Tls, Proxy, Timeout, Cancelled, ResponseLimit, OtherTransport. Avoid guessing a precise category when .NET only exposes a generic transport error.

Step outcomes: Passed, AssertionFailed, ExtractionFailed, TransportFailed, ValidationFailed, Cancelled, Skipped. Run outcomes: Passed, Failed, Cancelled. ExtractionFailed is an explicit refinement of the earlier product spec so a missing extraction is distinguishable.

Verify: solution builds; no Infrastructure/Desktop dependency from Core; resolved requests are not used as database/export models.

## Step 04 — create the standalone fixture API

Create tools/restByAno.FixtureApi as an ASP.NET Core minimal API. Bind only to loopback, choose a free port by default, print the chosen base URL. Permit an explicit development port. No sensitive request logging.

Implement:

| Endpoint | Behavior |
|---|---|
| GET /health | 200, JSON {"ok":true} |
| /echo, common HTTP methods | JSON containing method, query entries, headers and body; fixtures use dummy credentials only |
| GET /delay?ms=10000 | Delayed JSON response; observe request cancellation |
| GET /invalid-json | application/json with malformed text |
| GET /large?bytes=... | Stream a requested bounded amount without allocating the whole body; chunked and gzip variants |
| GET /redirect | 302 to /redirect-target |
| GET /redirect-target | Increment an observable counter; /redirect-count returns it |
| POST /customers | Create in-memory customer from {"name":"Ada"}; 201 with {"id":"...","name":"Ada"} |
| GET /customers/{id} | Existing object or 404 |
| DELETE /customers/{id} | 204 or 404 |
| GET /protected/bearer | Accept only dummy token fixture-token; else 401 |
| GET /protected/key | Accept X-API-Key: fixture-key or api_key=fixture-key; else 401 |
| GET /protected/basic | Accept fixture-user / fixture-password; else 401 |

Cap fixture requested delay/body size to prevent accidental excessive resource use. Document launch command and sample URLs. Run is manual; this is not a unit-test project.

Verify: start server, open /health and stop server. Never bundle this tool in the desktop publish output.

## Step 05 — implement native HTTP execution

Create Http/HttpRequestExecutor.cs and Http/HttpClientPool.cs.

- Keep bounded long-lived handlers/clients keyed by transport settings; retire/dispose unused profiles. Configure a pooled connection lifetime, initially 2 minutes.
- Handler: AllowAutoRedirect=false, UseCookies=false, UseDefaultCredentials=false; default system proxy. Enable gzip/deflate decompression.
- Build a new HttpRequestMessage per execution. Add general versus content headers to the appropriate collection; validate invalid combinations rather than silently dropping headers.
- Use SendAsync with ResponseHeadersRead. Never use EnsureSuccessStatusCode: 4xx/5xx are valid response data.
- Start elapsed timing immediately before sending; stop after the bounded response read.
- Use a linked cancellation token for the whole send/read timeout. Differentiate user cancellation from timeout.
- Read the decoded stream incrementally with no size limit. The first 1 MiB stays in memory (`ResponseSnapshot.Body`); a larger body is also streamed to a temp file (`SpoolPath`, `TotalBodyBytes`). The UI shows the sample and offers "Load full body" (up to 32 MiB) and "Save body…". JSON assertions/extractors read the complete body from the file (up to 256 MiB; above that they fail with a readable reason). Spool files are deleted when the response is cleared, a run step ends, or the app exits; stale ones are removed at startup. `ResponseLimit` is a legacy error kind kept only so old run history loads.
- Dispose messages/responses/streams. Do not dispose a shared client after each request.
- No bypass of invalid certificates, proxy failures or target-policy denial.
- Validate resolved target immediately before any send, even when called outside the UI.

For initial wiring use loopback-only policy until Step 11 supplies configured corporate targets.

Verify manually: /health, POST /echo, a 404, /delay timeout/cancel, /large above cap, compressed large body, /redirect count staying zero. Record results.

## Step 06 — wire the request editor and response view

Create RequestEditorViewModel, ResponseViewModel, associated views and desktop-only edit models.

- Implement commands for Send, Cancel, Add/Remove parameter/header, Format JSON, Clear response.
- Support all listed HTTP methods, enabled flags and duplicate keys in order.
- Do not concurrently send the same editor request. Cancel uses the active cancellation source.
- Map an editable view model to an immutable execution snapshot before Send.
- Preserve existing URL query items and append enabled query rows; encode row names/values once. Ignore disabled rows. Reject URL fragments rather than silently removing them.
- Body None sends no content. Text defaults to text/plain; Json defaults to application/json and must parse after substitution.
- Show status, elapsed milliseconds, byte count, response headers and Raw/Pretty body tabs. Pretty formatting must not overwrite raw text.
- Decode supported textual content with its declared charset; UTF-8 fallback. Unsupported charset gets a readable warning. Binary content gets metadata only.
- Response rendering is a read-only text control, never HTML/WebView.
- Use async commands; avoid .Result/.Wait() on the UI thread. Send/read remains responsive; marshal observable updates through Dispatcher.
- Clear the previous response when beginning a new send, so failures cannot appear to be the previous success.

Verify: compose POST /echo, duplicate query keys, disabled header, formatted JSON, invalid JSON, cancellation and visible 404. Copy response text only on explicit action.

## Step 07 — implement storage, configuration and recovery

Create Configuration/StorageOptions.cs, Persistence/DatabaseInitializer.cs, migrations and repositories.

Bootstrap selection: optional --data-dir <absolute path>, otherwise C:\temp. That directory contains restByAno.settings.json and restByAno.db. Save/restore an alternative location by launching with the same --data-dir; do not introduce an undocumented roaming registry pointer. The Settings UI can open instructions for restarting with the selected directory; live database switching is outside MVP.

Default settings: timeout 30, decoded cap 10485760, retention days 30. Validate timeout 1–600 seconds, cap 1 KiB–100 MiB and retention 1–3650 days. This refines the previously unspecified validation ranges.

Schema version 1:

```text
schema_info(version INTEGER NOT NULL)
collections(id TEXT PRIMARY KEY, name TEXT NOT NULL, definition_json TEXT NOT NULL, updated_utc TEXT NOT NULL)
environments(id TEXT PRIMARY KEY, name TEXT NOT NULL, definition_json TEXT NOT NULL, updated_utc TEXT NOT NULL)
secrets(id TEXT PRIMARY KEY, environment_id TEXT NOT NULL REFERENCES environments(id) ON DELETE CASCADE,
        name TEXT NOT NULL, ciphertext BLOB NOT NULL, UNIQUE(environment_id,name))
runs(id TEXT PRIMARY KEY, collection_id TEXT NOT NULL, environment_id TEXT,
     started_utc TEXT NOT NULL, ended_utc TEXT NOT NULL, outcome TEXT NOT NULL,
     snapshot_hash TEXT NOT NULL, result_json TEXT NOT NULL)
```

Collections/environments store their nested domain definitions as versioned JSON; no secret values appear there. Run collection IDs are historical references rather than cascading foreign keys so deleting a collection does not silently erase history. Enable foreign keys, parameterize SQL, use transactions and a busy timeout. Prefer default rollback journal initially; handle SQLite files correctly if enabling WAL later.

- Single instance per canonical data directory using a named mutex held for the app lifetime.
- Initialize a new database explicitly on first launch. Record successful initialization in settings. If settings indicate an existing installation but its database is missing, show Restore/Create new choices instead of silently losing saved work.
- Validate migration version; reject a newer database. Back up with SQLite's backup API before migration into the app-specific backup directory.
- Configuration writes use a temporary sibling file and atomic replacement where supported.
- Corrupt/locked/unwritable data displays an actionable error without overwriting existing files. Clean up only app-owned temporary files.

Verify: new directory initialization, persistence after restart, second-instance rejection, unwritable path, missing database recovery, migration backup, newer version refusal. Work in a dedicated manually selected verification directory for destructive checks, not existing user data.

## Step 08 — build collection management

Create CollectionTreeViewModel and CRUD repository wiring.

- Collections, one-level folders, requests with explicit Order. Drag-and-drop is optional; Move Up/Down buttons are sufficient.
- New request starts unsaved. Save, rename, duplicate, move and delete persist actual domain data.
- Duplication creates new IDs. Folder deletion offers move requests to root or delete them; require an explicit choice.
- New collection starts with no requests. No silent demo requests mixed with user data.
- Track dirty state. Before selection change, close or deletion offer Save/Discard/Cancel in a themed dialog.
- Prompt before destructive collection/request deletion. Do not automatically send a newly selected request.

Verify: create collection/folder/request, duplicate, reorder, save, restart, reopen, dirty-state cancellation and deletion. Saved order matches runner order later.

## Step 09 — implement variables and environments

Create Resolution/VariableResolver.cs and EnvironmentEditorViewModel.

- Names are case-sensitive identifiers matching [A-Za-z_][A-Za-z0-9_]*; no duplicates within a scope.
- Resolve precedence: run > environment > collection defaults; references resolve once, non-recursively.
- Missing/unreadable variables prevent sending. Validation shows names, never values for secret variables.
- URL must resolve to absolute HTTP(S); reject embedded userinfo/credentials.
- Resolve plain text body and headers as text; reject header CR/LF after resolution.
- JSON: first parse the template as JSON; walk string VALUE nodes only, replace placeholders inside strings and serialize with System.Text.Json. Do not substitute property names or non-string nodes. This safely supports {"name":"{{customerName}}"}; reject unquoted {{value}} with an explanation. Do not double-escape.
- Header/query rows are resolved before encoding. Body formatting applies to the template without resolving secrets into the saved editor.
- Add/select/rename/delete environments and nonsecret variables. Selected environment persisted in settings.
- Snapshot variables at Send/Run; changes during execution affect only future executions.

Verify: two environments with different baseUrl, overriding defaults, missing variable preventing fixture hit, literal quotes/newlines in JSON string replacement, nested-looking replacement text remaining literal.

## Step 10 — implement secrets, authentication and sanitization

Create Secrets/DpapiSecretStore.cs, Auth controls and diagnostic sanitizer.

- Protect UTF-8 secret bytes with ProtectedData.Protect using CurrentUser; store only ciphertext BLOB plus ID/name. Explain unreadable secrets without exposing ciphertext.
- Secret entry uses PasswordBox with a desktop adapter; do not persist the password in view-model JSON. Clear entry controls after save.
- Secret creation/update and environment binding must be transactional; avoid deleting a secret still referenced elsewhere.
- Bearer: Authorization: Bearer <resolved variable>. Basic: standard Base64 username:password; no automatic logging. API key: configured name in header or query. Header key collision with an enabled user entry is a validation error; query key collision likewise.
- Enabled auth conflicts with a manually provided Authorization header. Reject before sending.
- Mask secret variables in environment lists. Never automatically copy them or resolve them into persisted templates.
- All diagnostics/reports use allowlisted fields and a sanitizer for known secret values/sensitive fields. Never serialize ResolvedRequest.
- Reports omit raw URL, request/response headers/bodies, extracted values and assertion actual/expected values. Keep assertion kind/path and a safe pass/fail reason. UI assertion value detail also masks sensitive data.
- Include run-extracted sensitive values in the run's redaction context. Do not log fixture echo content containing dummy or real credentials.

Verify: bearer/key/basic fixture routes, auth conflict, close/reopen secret use, missing-secret failure; inspect DB definitions/settings/export text for a distinctive dummy token. Secret ciphertext is not expected to be plaintext-searchable. Never perform this check using real tokens.

## Step 11 — finish settings and destination policy

Create Policy/ManagedTargetPolicy.cs, SettingsViewModel and sample policy documentation.

- Implement System and Explicit proxy modes; explicit proxy URI must be valid HTTP(S). Do not offer “retry without proxy” after failure. Proxy credentials are outside baseline scope unless corporate discovery requires them.
- Settings edit timeout, body cap, retention and active transport profile. Changing profiles affects future sends, not in-flight requests.
- Managed policy path: %PROGRAMDATA%\restByAno\target-policy.json; administrator deploys it. App reads it and never writes it from an import or regular settings UI.
- Versioned policy shape: {"schemaVersion":1,"allowedTargets":[{"scheme":"https","host":"api.company.example","port":443}]}.
- Validate exact scheme/normalized hostname/effective port; no arbitrary suffix matching or wildcards in MVP. Check every request after substitution. Deny unsupported scheme, credentials in URL or disallowed target.
- If no managed policy exists, allow loopback only, show a clear setup message for corporate hosts. If policy exists but is malformed/unreadable, fail closed. Under managed policy, loopback must also be explicitly listed if desired.
- Nonloopback HTTP requires both matching managed policy and AllowNonLoopbackHttp=true.
- Policy checks are not presented as a replacement for corporate firewall controls. Do not fabricate production hosts or grant the app administrator privileges.

Verify: allowed loopback, denied arbitrary host, port mismatch, malformed managed policy, redirects still disabled, configured proxy failure remaining a failure. Managed-device proxy verification may remain pending with documented instructions.

## Step 12 — implement safe native import/export

Create ImportExport/CollectionJsonService.cs and EnvironmentJsonService.cs.

Envelope: {"schemaVersion":1,"kind":"collection"|"environment","definition":{...}}. Emit camelCase properties and string enums. Collection fields follow Step 03. Environment exports represent secret variables with name and requiresSecret=true only; omit SecretId/ciphertext/value. On import, allocate local secret bindings and show secrets needing entry.

- Import file cap 10 MiB; JSON max depth 64. Reject malformed/unknown version/kind, duplicate names/IDs, invalid method/body/auth/assertion shapes and invalid folder references.
- Validate whole document before a transaction. Generate fresh IDs for imported entities and remap folder references, preventing overwriting existing data.
- Never import transport policy, resolved request values, executable scripts or cloud/account fields. Report unsupported fields visibly rather than silently promising compatible behavior.
- Export is explicitly selected by the user through a file dialog; exclude secrets and history. Exporting request templates may include user-authored business data; do not claim universal anonymization.
- For nonsecret variables, preserve ordinary values. For secret bindings, export only requirements.
- No Postman import in MVP.

Verify: round trip a collection/environment, missing secrets prompt, bad version, invalid references, oversized file, unsupported fields and existing-data preservation on failed import.

## Step 13 — implement assertions and extraction

Create Assertions/AssertionEvaluator.cs, JsonPointerNavigator.cs, Running/ResponseExtractor.cs and rule-editor view models.

- StatusEquals compares numeric status; HeaderEquals uses case-insensitive header NAME and ordinal VALUE equality against any individual returned value. Do not split arbitrary values on commas.
- JsonExists means the pointer resolves, including a JSON null value. JsonEquals compares a resolved scalar by type: strings ordinal, booleans exact, null exact; numbers compare decimal when representable, otherwise finite double. Reject object/array equality in the editor for MVP.
- JSON Pointer: empty pointer is root, / separates tokens, ~1 means / and ~0 means ~. Invalid escape sequences fail validation. Array indices are nonnegative base-10 integers with no leading zeros except 0; '-' does not identify an existing value.
- DurationBelow passes only when elapsedMilliseconds < MaxMilliseconds. Explain strict boundary in UI.
- Invalid JSON fails JSON rules without crashing; reuse one parsed document per response.
- Each rule returns ID, kind, passed flag and safe reason; user view detail remains sanitized.
- After all assertions pass, extract pointer scalar values. String → raw string; number/bool/null → JSON literal text; object/array extraction rejected for MVP.
- Apply extraction atomically after every extractor validates. On failure, none of that step's extracted variables are written. Sensitive=true enforced in MVP; scope ends with run.
- Editors support add/remove/reorder and field-level validation.

Verify manually: correct/wrong status, header name casing, typed "1" versus 1, null existence, ~0/~1 paths, missing path, invalid JSON, duration boundary and extraction failure leaving prior variables intact.

## Step 14 — implement the sequential collection runner

Create Running/CollectionRunner.cs and RunnerViewModel/RunnerView.

Algorithm:

```text
snapshot collection + selected environment + secret bindings
validate run configuration; partition ordered requests into ordinary and cleanup
create empty run variables and redaction context
for each ordinary request:
  if user cancelled: mark remaining steps skipped; finish Cancelled
  resolve snapshot → execute → assert → extract → record result
  on failure and StopOnFailure: skip remaining ordinary requests; break
if not cancelled:
  execute every cleanup request in order through the same pipeline
  cleanup failure does not prevent subsequent cleanup requests
finish: Cancelled if cancelled; otherwise Failed if any step failed; else Passed
```

- Exclude cleanup requests from ordinary execution; execute them once at the end.
- Missing variables in cleanup produce ValidationFailed for that step, not a transport call.
- Global validation catches invalid saved shapes before run. Run-dependent unresolved variables are validated per step, since extraction may supply them later.
- No response means no assertions/extraction. HTTP error status still permits explicitly configured assertions; no automatic success expectation.
- No assertions means transport completion succeeds unless extraction fails. Label zero-assertion steps so users understand their coverage.
- One desktop collection run at a time; standalone Send is disabled during a run. Editing can remain enabled but cannot mutate the snapshot.
- Report progress after each step and on cancellation; never expose secrets in progress text. Cancellation prevents new cleanup sends and shows skipped cleanup explicitly.
- Preserve original failure and cleanup errors separately. Empty collections fail validation rather than showing a misleading passed run.

Verify: create→read→delete success, stop/continue modes, cleanup after failed assertion, cleanup with missing ID, cleanup failure, failed extraction, cancellation during delay and zero-assertion labeling.

## Step 15 — add history, reports and sample collections

Create run repository wiring, history view, JSON report exporter, samples and manual-verification.md.

- Persist metadata only: run/step IDs, timestamps, outcome, status, elapsed/body size, assertion kind/path/result and safe reasons. No raw traffic or extracted values.
- Snapshot hash must be computed from secret-free collection/environment structure, never from resolved tokens. It is an identity aid, not proof of deterministic response behavior.
- On startup purge metadata older than configured retention; “Clear history” affects only runs table after explicit confirmation.
- JSON report schemaVersion=1 includes metadata and step results only; explicit save action, bounded output and atomic write.
- In-memory response buffers are dropped when replacing response/closing the view. Dispose JsonDocuments and streams.
- Include samples using {{baseUrl}}, dummy auth requirements, create/read/delete, intentional assertion failure, cleanup and delay cancellation. Base URL is filled in from fixture startup; do not hardcode a random port.
- Extend fixture for all response behaviors required by manual-verification.md.

Verify: run passes/fails/cancels, inspect saved history and report for dummy secrets/raw bodies, restart history, clear history preserving collections, import supplied sample, retention boundary using controlled old metadata.

## Step 16 — finish UX and build the offline package

Create docs/user-guide.md, docs/environment-notes.md updates and desktop publish output.

- Remove placeholder commands and “coming soon” controls from MVP screens. Unsupported later features are omitted.
- Check all app-owned dialogs and popup surfaces use the dark theme. Verify focus and high contrast, long URLs, small window size, invalid input, database recovery and cancellation responsiveness.
- Publish only Desktop: dotnet publish src/restByAno.Desktop/restByAno.Desktop.csproj -c Release -r win-x64 --self-contained true -o publish/win-x64.
- Keep trimming disabled for WPF unless separately proven. A folder deployment is acceptable as the first reviewable package; do not claim it is signed/approved/installed centrally.
- Verify app opens without external downloads on a suitable Windows machine. Confirm FixtureApi, sample credentials and development artifacts are absent from distribution.
- Guide documents storage paths, --data-dir, temp cleanup behavior, secret re-entry after transfer, managed target policy, proxy, collections, assertions, cleanup/cancellation and report export.
- Record dependency versions/licenses and any outstanding managed-device checks. Signing/installer selection follows the organization's existing process; do not create external update services.

Final acceptance: manual client + collection runner both functional; data defaults to C:\temp; dark UI; no unit-test projects; no plaintext saved auth secrets; import/export works; cancellation/cleanup semantics correct; Release build/publish succeeds where SDK is available.

## Exact reporting format for Terra

After each step, update progress and report briefly:

```text
Step: <number and name>
Implemented: <concrete behavior>
Build: <command and result, or NOT RUN with reason>
Manual verification: <scenario/result, or NOT RUN with reason>
Pending external checks: <only genuine unavailable checks>
Next: <next step>
```

Final report links to source entry points, progress, manual checks, user guide and publish directory. Distinguish implemented functionality from checks that still need a managed company Windows machine.

## Scope guardrails

Do not build CLI, OAuth, mTLS, Windows integrated auth, Postman import, multipart, cookies, parallel runs or script execution unless later authorized. Preserve extension points only where they are already required. Do not substitute a mock screen for the runner or replace SQLite with JSON files. Do not add unit tests because a template or coding convention suggests them. Builds and manual verification are still required.

This guide refines the earlier specification with exact schema, startup configuration, import behavior, policy defaults, JSON scalar rules and ExtractionFailed outcome. For these implementation details, follow this guide. Preserve the user's confirmed requirements over earlier planning defaults.

