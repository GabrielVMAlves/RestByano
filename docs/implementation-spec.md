# Local API Workbench — implementation specification

Status: initial development baseline, 2 October 2026.
Audience: developer building an internal Windows API client and integration-test runner.
Confirmed: Windows environment; developer prefers C#/.NET; first-version APIs use REST/JSON with bearer tokens or API keys; existing API clients are blocked because requests can be stored in external accounts.
Additional requirements: no unit-test projects or unit-test framework; default storage directory C:\temp; dark theme by default.
Assumptions: single-user desktop use; no cloud accounts or synchronization; Windows 11 x64 initially. Confirm exact corporate Windows builds, deployment restrictions and proxy requirements before release. This specification is a proposal, not confirmation of corporate approval.

## 1. Product objective

Build a Windows application that lets a developer compose HTTP requests, organize reusable collections, select environments, and run repeatable integration tests. Collections, secrets and test results remain on the machine or in explicitly selected company-approved locations. Sending a request necessarily sends its configured data to its target API.

Success example: select a test environment, run “Create customer → Read customer → Delete customer”, extract the created customer ID, assert response status and JSON values, and see a clear result for each step without logging into an external service.

The product has no account system, sync service, analytics, remote error reporting, automatic external updates, remote asset loading, or plugin marketplace. Build dependencies may require approved package access during development; installed operation must not require downloading packages.

## 2. Technology decision

Recommended stack:

| Concern | Choice | Reason |
|---|---|---|
| Runtime | .NET 10 LTS, current approved servicing patch | Current LTS baseline; support currently extends to November 2028 |
| Desktop UI | WPF with XAML and MVVM | Windows-only UI, established desktop controls, one primary language |
| View models | CommunityToolkit.Mvvm | Small amount of observable-state and command boilerplate; approve and pin package version |
| HTTP | System.Net.Http.HttpClient with SocketsHttpHandler | Native .NET request execution and configurable transport |
| JSON | System.Text.Json | Built-in serialization and response parsing |
| Local database | SQLite via Microsoft.Data.Sqlite | Local, transactional persistence without a database service |
| Secrets | ProtectedData / DPAPI, CurrentUser scope | Windows user-bound protection for saved secret values |
| Development verification | Manual acceptance scenarios against approved APIs and a standalone local fixture API | No unit-test projects or unit-test framework required |
| Distribution | Self-contained Windows x64 publish, packaged through the approved internal installer process | No separately installed .NET runtime required; updates delivered internally |

Use a current approved stable version of each package, record licenses and dependencies, and commit package lock files. Do not add an ORM, embedded browser, JavaScript engine, or third-party editor control until a demonstrated requirement justifies it. Standard text boxes with a JSON formatting command are sufficient initially.

Alternatives considered:

| Stack | When to choose | Tradeoff for this project |
|---|---|---|
| C# + WPF | Windows-only desktop and C# familiarity | Recommended; visual polish requires deliberate XAML work |
| C# + WinUI 3 | Fluent Windows styling is a primary requirement | Credible alternative; validate Windows App SDK packaging and deployment early |
| React + Tauri | Strong web UI experience and a reason to support other OSes | Adds Rust and WebView2 deployment concerns |
| React + Electron | Existing JavaScript team or Electron codebase | Embeds Chromium and Node.js; extra runtime and dependency maintenance |

Microsoft recommends WinUI for new Windows applications generally. Choosing WPF here is an engineering judgment based on this app's Windows-only scope and a small C# team, not a claim that WPF is universally better. A frontend framework does not by itself determine whether data leaves the machine.

## 3. Scope and releases

### Milestone A — manual API client

- Compose GET, POST, PUT, PATCH, DELETE, HEAD and OPTIONS requests.
- Edit URL, ordered query parameters, ordered headers and raw text/JSON body.
- Enable or disable individual parameter/header entries; preserve duplicate entries.
- Support no auth, bearer token, basic auth, and API key in a header or query parameter.
- Send asynchronously, cancel, and display status, headers, response size and elapsed time.
- Show raw text and pretty JSON; show binary response metadata without executing or rendering it.
- Save collections, requests and environments locally; reopen them after restart.
- Handle system proxy and normal Windows certificate trust; explain transport failures.

### Milestone B — integration-test MVP

- Ordered collection runs, with per-step results and a run summary.
- Declarative assertions: status, header value, JSON value/existence and elapsed-time ceiling.
- Extract a response JSON value into a run variable for a later request.
- Stop-on-failure or continue-on-failure policy.
- Mark cleanup steps to run after ordinary step failure; cancellation ends sending new requests.
- Import/export the app's versioned JSON collection format with secret values excluded.
- Retain local run metadata; export a redacted JSON result report on explicit user action.

Milestone B is the minimum usable integration-test release. A request composer alone does not satisfy the product goal.

### Later releases

- CLI runner using the same engine and result model.
- JUnit XML report output for internal CI.
- Limited Postman collection v2.1 importer; show every unsupported field/script.
- Multipart/file uploads, form URL encoding, cookies with explicit isolated cookie jars.
- Windows integrated authentication, mTLS and OAuth flows as required by discovery.
- OpenAPI import, richer JSON editing, reusable datasets and parallel independent test runs.

If Windows auth, mTLS, a special proxy, or OAuth is essential to the first real API, promote that support into Milestone A. Do not postpone it solely because it appears in the later-release list.

Excluded from MVP: JavaScript pre-request/test scripts, arbitrary executable extensions, cloud teamwork, mock hosting, load testing, WebSockets, gRPC and full Postman compatibility.

## 4. User interface

Use a dark theme on first launch and throughout all app-owned views. Define shared WPF resource tokens for window/panel backgrounds, text, borders, inputs, buttons, selection, focus and result states. Initial palette: window #121212, panels #1E1E1E, text #E6E6E6, secondary text #B3B3B3, accent #60A5FA. Verify readable contrast, visible keyboard focus, and hover/disabled states. Show pass/fail with text or icons as well as color. Respect Windows high-contrast accessibility settings. A light-theme toggle is outside MVP scope; native Windows file dialogs may follow the OS theme.

One main window:

1. Left: collections, one-level folders and saved requests; create, rename, duplicate and delete actions.
2. Top: active environment selector; request name, method, URL and Send/Cancel.
3. Editor tabs: Parameters, Headers, Body, Authentication, Assertions and Extraction.
4. Response pane: status, elapsed time, size, headers and body; Raw/Pretty JSON switch.
5. Runner pane: ordered steps, progress, assertion results and run summary.
6. Settings: storage location, proxy selection, default timeout, size limit, retention and target policy.

Required states: unsaved changes, missing variables, sending, cancelling, success, assertion failure and transport error. Sending must not freeze the UI. Cancel remains usable while receiving a slow response. Never display an incomplete response as a complete successful test.

Default timing measures wall-clock duration from starting transport execution through reading the bounded response body; exclude assertion evaluation. Distinguish HTTP 4xx/5xx responses from DNS, proxy, TLS and connection failures.

## 5. Architecture and project boundaries

```text
src/
  ApiWorkbench.Core/          Domain models, resolution, assertions, runner contracts
  ApiWorkbench.Infrastructure/ HTTP execution, SQLite, DPAPI, JSON import/export
  ApiWorkbench.Desktop/       WPF views/view models and composition root
  ApiWorkbench.Cli/           Later: headless execution and report output
tools/
  ApiWorkbench.FixtureApi/    Standalone local API for manual development checks
```

Core must not reference WPF or SQLite. Desktop composes services through dependency injection and calls the same runner the CLI will use. Keep a single desktop process; there is no localhost web service for the installed UI. A standalone loopback fixture server exists only as a development tool and is not shipped with the app. Do not create unit-test projects or add xUnit, NUnit or MSTest dependencies. The app's integration-test runner remains part of the product.

Key boundaries: IRequestExecutor, ICollectionRepository, IEnvironmentRepository, ISecretStore, IRunRepository, ITargetPolicy and IReportExporter. Do not wrap every framework call in an interface. Isolate the components that carry policy, persistence or transport behavior.

Request pipeline:

```text
Saved request + environment + run variables + secret references
  → resolve and validate
  → check destination and transport settings
  → create immutable request snapshot
  → send and read bounded response
  → evaluate assertions
  → extract values for later steps
  → build redacted result
  → persist allowed metadata and update UI
```

Use asynchronous operations and CancellationToken throughout. Snapshot a run's collection and environment so editing the UI during execution cannot change an active run. Cache bounded long-lived clients/handlers by transport profile; dispose when retired. Configure connection lifetime and avoid creating a fresh connection pool for every request. Disable automatic cookies initially.

## 6. Data model and local storage

All entities have stable IDs; dates are stored as UTC and displayed in local time.

| Entity | Minimum fields |
|---|---|
| Collection | id, name, schemaVersion, ordered folder/request references |
| Folder | id, collectionId, name, order |
| Request | id, folderId/collectionId, name, method, URL template, ordered params/headers, body, auth, assertions, extractors |
| Environment | id, name, nonsecret variables, secret references, transport profile |
| Secret | id, environmentId, name, DPAPI ciphertext |
| Transport profile | timeout, proxy mode, redirect policy, target-policy reference |
| Assertion | id, type, expected typed value, header name or JSON pointer |
| Extractor | id, source JSON pointer, destination run-variable name, sensitivity flag |
| Run | id, start/end time, collection/environment ID, snapshot hash, outcome |
| Step result | request ID, outcome, status, elapsed time, bounded bytes read, assertion results, sanitized error |

Default storage directory: C:\temp, matching the company's convention. Use C:\temp\ApiWorkbench.db for the database, including protected secret records; use C:\temp\ApiWorkbench-backups for migration backups. Create the directory if absent and writable; otherwise show a clear storage error and allow selection of an approved alternative. Do not silently switch to another directory. Never delete unrelated contents of C:\temp. Verify actual folder permissions and sync policies; a local path alone cannot guarantee files will never be synchronized.

Treat saved data as subject to the company's C:\temp cleanup policy. If cleanup removes app files, report their absence and offer explicit creation of a fresh database. Document that durable retention depends on that policy and user-selected approved backups. DPAPI-protected secret values still require the original Windows user context.

Use SQLite transactions, foreign keys and an explicit migration version. Back up before migration, and fail safely on unsupported newer database versions. Support a single app instance per data directory initially.

Saved request definitions may contain confidential nonsecret data and are not automatically encrypted by SQLite. Secret fields must store references, not plaintext. Never imply that DPAPI encrypts the entire database or prevents other software running as the same user from accessing secrets. Full collection/database encryption is a separate decision if corporate classification requires it.

No request/response bodies are retained in history by default. Keep run metadata for 30 days, configurable. In-memory response contents are released when a tab/run is closed. Explicit saving of response bodies is deferred; report export includes sanitized test results, not raw traffic.

## 7. Variables, secrets and authentication

- Syntax: {{baseUrl}}, {{customerId}}, {{accessToken}}.
- Precedence: run variables > selected environment variables > collection defaults.
- Missing variables fail before any request is sent; show variable names without secret values.
- Resolve placeholders once; do not recursively interpret replacement values as templates.
- URL query values are encoded as values once. Full URL templates must resolve to a valid absolute HTTP(S) URI.
- JSON body placeholders insert escaped string content. MVP does not support arbitrary raw JSON fragment injection; numbers/objects require explicit typed binding in a later release.
- Headers reject CR/LF values. A configured auth mode conflicts with a manual Authorization header; validation asks the user to choose one.
- Basic auth uses username/password secret references. API keys and tokens use secret references in editable auth controls.
- Secret input is masked by default and never placed on the clipboard automatically.
- Run-extracted values are scoped to one run. Treat extracted values as sensitive by default; they are not saved back into an environment.
- Query-string auth requires query-value redaction in logs/reports because URLs can carry secrets.

DPAPI CurrentUser ciphertext is machine/user-context dependent; moving a collection to another machine requires entering secrets again. Reports and exports use the same sanitizer, stripping known sensitive header/query fields and resolved secret values from diagnostic text. Arbitrary bodies and free text may contain unknown secrets, so omit raw traffic from these artifacts instead of promising perfect redaction.

## 8. Network and trust behavior

- The app does not create outbound connections while idle for analytics, accounts or update checks.
- API traffic runs through native .NET transport, so browser CORS rules do not govern request execution.
- Default proxy mode uses system settings. Explicit approved proxy configuration may be selected; do not silently fall back to bypassing it after failure.
- Preserve normal certificate validation and corporate certificate trust. MVP has no global “disable TLS validation” switch.
- HTTP is allowed for loopback fixtures by default; non-loopback HTTP requires a transport profile explicitly permitting it under company policy.
- Automatic redirects are off initially. Show the redirect status and Location value. A later redirect implementation must revalidate every destination and prevent secret headers/cookies from crossing origins.
- No automatic retries: repeated POST/DELETE requests may change server state. User-triggered reruns are explicit.
- Default timeout: 30 seconds for the complete request/body-read operation, configurable within a validated range.
- No response size limit. Stream reading keeps a 1 MiB sample in memory and spools larger decoded bodies (also compressed or chunked ones) to a temp file; the UI shows the sample with options to load the rest or save it to a file.
- Render response bodies as plain text; do not execute HTML, resolve external images, or navigate links automatically.
- Windows authentication must be enabled only for an approved host scope; credentials must not be offered to arbitrary imported targets.

Because external access is the original concern, provide a centrally managed destination policy before broad rollout: allowed scheme/host/port combinations, checked after template resolution and before sending. Admin policy takes precedence over user settings. Include required proxy and identity-provider endpoints in policy discovery. Imports cannot modify policy.

An in-app destination list is defense in depth, not a firewall. DNS changes, shared hosting and other local programs can defeat claims of strict isolation; enforce required network boundaries through existing corporate controls as well. The app cannot prevent a user from manually copying or exporting a local file.

## 9. Test execution semantics

Assertions use a constrained declarative model, with JSON Pointer paths such as /customer/id. Do not promise JSONPath compatibility. Compare JSON strings, numbers, booleans and null by type; existence and equality are separate assertion kinds. Invalid JSON fails JSON assertions with a readable message.

Execution is sequential initially. Each step: resolve → send → assert → extract. Extract only after transport succeeds and all assertions pass. Missing extraction paths fail that step and do not overwrite existing run variables.

Runner modes: stop after an ordinary step fails, or continue through ordinary steps. Marked cleanup requests run at the end in either mode after failures, subject to their own variable validation. Record cleanup failures separately while preserving the original failure. Cancelling stops the active request and prevents new steps, including cleanup; show remaining cleanup as skipped so developers know server state may remain.

Run states: Passed, Failed, Cancelled. Step states: Passed, AssertionFailed, TransportFailed, ValidationFailed, Cancelled, Skipped. A failed cleanup makes the run fail. Successful cancellation must never report a passed run.

Suggested later CLI contract:

```text
ApiWorkbench.Cli run --collection collection.json --environment test.json --report results.json
```

Exit codes: 0 passed; 1 test/transport failure; 2 invalid configuration; 3 cancelled. Secret values come from a local store or an approved CI secret channel, never command-line arguments. The desktop and CLI must produce equivalent results for the same inputs.

## 10. Acceptance scenarios and verification

Use a local fixture API, not public demo services or production data. Include endpoints for echo, delayed responses, invalid JSON, large/chunked/gzip bodies, redirect, deterministic errors, basic auth and create/read/delete resources.

1. Save a POST request with headers/body, restart, and retrieve the same definition.
2. Resolve environment variables; undefined values prevent network traffic.
3. A create/read/delete collection extracts its resource ID and passes all assertions.
4. A mismatched typed JSON value fails visibly and follows the selected runner mode.
5. Cancel a delayed request; the UI remains usable and later steps do not execute.
6. A redirect does not automatically contact its destination or expose a secret there.
7. A response exceeding the decoded limit returns a limit error without allocating its full body.
8. Export/import a collection; secret references survive but secret values are absent.
9. Search database, logs and report fixtures for planted tokens: plaintext tokens must not appear in protected secret records or diagnostics. Test request definitions separately to confirm UI secret bindings use references.
10. Observe installed app traffic on launch, idle, save, run, import and export: app-originated connections correspond only to requested APIs and configured transport/auth requirements. Account for OS-managed DNS/proxy/certificate traffic separately.
11. On a managed Windows device, verify corporate proxy, certificate trust and any required authentication.
12. Install, launch and uninstall the packaged application with the rights available to target users, without an internet connection. Decide whether uninstall retains or removes user data.

Verify resolution, assertions, redaction, migrations, import validation, runner semantics and transport behavior through recorded manual acceptance scenarios. Also check keyboard access, cancellation responsiveness, resizing, dark-theme consistency, C:\temp storage and managed-device deployment. Project unit tests and automated UI tests are not required. Product integration-test collections remain supported and can be used to exercise the fixture API.

Target: normal request editor interactions remain responsive while a 10 MiB response is received. Record startup time and memory on the target device and agree measurable release thresholds after the first vertical slice rather than guessing exact hardware-independent figures.

## 11. Definition of done

- Acceptance criteria for the task pass against controlled inputs.
- Recorded manual checks cover new policy or engine behavior; visual edits include dark-theme and keyboard-access checks. No project unit tests are required.
- Cancellation, error handling and sensitive-data handling are reviewed where relevant.
- Build succeeds with pinned approved dependencies.
- Documentation identifies unsupported behavior; imports never silently discard test semantics.
- For release: an internally approved package, offline deployment evidence and managed-device network verification exist.

## 12. Decisions to confirm during discovery

1. Target Windows versions/architectures, permitted .NET versions, build tools and package sources.
2. System proxy/PAC, proxy authentication, Windows auth, mTLS and OAuth needs.
3. Exact API protocols, body formats and existing collection migration needs.
4. Permitted target hosts and whether company policy permits local export.
5. Data classification: must all collection data be encrypted, or only secrets?
6. Installer/signing process, administrator rights, and managed update channel.
7. Whether CLI/internal CI support belongs in the first release.

## 13. Technical sources

- [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy): .NET 10 LTS and lifecycle; recheck before choosing the release baseline.
- [WPF overview](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/): Windows/XAML desktop framework.
- [Choose a Windows development path](https://learn.microsoft.com/en-us/windows/apps/get-started/): Microsoft's general WinUI recommendation.
- [HttpClient guidelines](https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/http/httpclient-guidelines): connection pooling and handler lifetime guidance.
- [AllowAutoRedirect](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpclienthandler.allowautoredirect): redirect and header behavior.
- [Microsoft.Data.Sqlite](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/): lightweight SQLite provider.
- [Windows data protection](https://learn.microsoft.com/en-us/dotnet/standard/security/how-to-use-data-protection): ProtectedData and DPAPI.
- [Tauri architecture](https://v2.tauri.app/concept/architecture/) and [Windows installer](https://v2.tauri.app/distribute/windows-installer/): native host and WebView2 distribution.
- [Electron introduction](https://www.electronjs.org/docs/latest/): bundled Chromium and Node.js.
