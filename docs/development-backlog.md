# Local API Workbench — small development tasks

Use with implementation-spec.md. Baseline: C# / .NET 10 + WPF. Sizes are rough focused-development estimates, not delivery promises: S = about half a day; M = about one day; L = about two days. Corporate review, environment setup, unfamiliar framework learning and release review are additional. Split any task that exceeds two days into an engine change and UI wiring.

Confirmed adjustments: no unit-test projects/framework; default storage C:\temp (database C:\temp\ApiWorkbench.db); dark theme throughout app-owned views. Verification criteria below mean manual acceptance checks unless explicitly referring to the product's integration-test runner. Keep that runner and its assertions in scope.

Each row is independently reviewable. Dependencies refer to task IDs. Read the specification before implementing security, storage or execution semantics. The initial goal is a working vertical slice, followed by a complete integration-test MVP.

## Phase 0 — validate the environment

| ID | Task | Depends on | Size | Acceptance criteria |
|---|---|---|---|---|
| T01 | Record corporate technical constraints | — | S | Target OS, approved runtimes/packages, proxy/auth, storage classification, target policy and deployment route are recorded; unknowns have owners |
| T02 | Prove WPF deployment on a managed machine | T01 | S | Minimal self-contained app launches using available user rights; offline deployment and package requirements recorded |
| T03 | Prove real API connectivity | T01 | M | Small .NET spike reaches an approved test API through required proxy/TLS/auth; sensitive data is not committed; any essential auth work is promoted into MVP |

## Phase 1 — working vertical slice

| ID | Task | Depends on | Size | Acceptance criteria |
|---|---|---|---|---|
| T04 | Create solution and dependency rules | T02 | S | Core, Infrastructure and Desktop build; Core has no UI/database reference; package versions pinned; no unit-test projects/framework added |
| T05 | Define request/response and error models | T04 | S | Immutable execution inputs distinguish HTTP responses, transport failure, cancellation and size errors |
| T06 | Build standalone development fixture API | T04 | M | Echo, delay and error endpoints run on a dynamically allocated loopback port for manual checks; tool is excluded from the installed app |
| T07 | Implement basic request executor | T05,T06,T03 | M | GET/POST, headers and raw body reach fixture; status/body/elapsed time return; handler pools reused; redirects/cookies/retries disabled |
| T08 | Implement timeout, cancellation and body limits | T07 | M | Delayed requests cancel/timeout; chunked and compressed large responses enforce decoded size cap; resources released |
| T09 | Build dark-themed single-request WPF screen | T07,T08 | M | Shared theme resources style window, inputs and results; readable contrast/focus/disabled states; user sends/cancels without UI freezing; Windows high-contrast respected |

Demo gate: launch → send request → inspect response → cancel delayed request. Resolve deployment or corporate transport failures before adding breadth.

## Phase 2 — reusable local requests

| ID | Task | Depends on | Size | Acceptance criteria |
|---|---|---|---|---|
| T10 | Add ordered parameter/header editor | T09 | M | Duplicate keys and enabled flags preserved; query values encoded once; CR/LF headers rejected |
| T11 | Add JSON and binary response presentation | T09 | S | Pretty/raw JSON works; malformed JSON shown as text; binary metadata shown; HTML stays inert |
| T12 | Add SQLite schema and migrations | T05 | M | Data defaults to C:\temp\ApiWorkbench.db; migration backups use C:\temp\ApiWorkbench-backups; transactional persistence, unwritable/missing storage and newer-version refusal manually verified; unrelated temp files untouched |
| T13 | Add collection tree and request saving | T09,T12 | M | Create/rename/duplicate/delete and reopen saved requests; unsaved changes handled; one-level folders supported |
| T14 | Implement variable resolver | T05 | M | Precedence, single-pass substitution, typed JSON-string escaping and missing-variable rejection tested |
| T15 | Add environment editing and selection | T13,T14 | M | Changing active environment changes resolved requests; persistence survives restart |
| T16 | Implement DPAPI secret store | T12 | M | Protected secret values round-trip; request fields hold references; absent/unreadable secrets fail clearly; planted plaintext absent from secret storage |
| T17 | Add auth controls | T14,T15,T16 | M | Bearer/basic/API-key requests work with fixture; manual Authorization conflicts detected; masked inputs use references |
| T18 | Implement shared diagnostic sanitizer | T16,T17 | M | Headers/query auth and known secret values are removed from error/report fixtures; raw bodies excluded |

Demo gate: save an authenticated request with {{baseUrl}}, restart, choose another environment and send it. No plaintext secret appears in diagnostics or exported definitions.

## Phase 3 — transport and local-data controls

| ID | Task | Depends on | Size | Acceptance criteria |
|---|---|---|---|---|
| T19 | Implement transport profile/proxy settings | T03,T08,T15 | M | System/approved explicit proxy works; no silent bypass; timeout and limits configurable with validation |
| T20 | Implement centrally managed destination policy | T01,T14,T19 | M | Resolved scheme/host/port checked before sending; imports cannot change policy; default redirect blocking verified |
| T21 | Implement native collection import/export | T13,T15,T18 | M | Versioned JSON round-trips; secrets excluded; malformed/oversize/unsupported-version imports fail atomically without damaging data |
| T22 | Add data retention and sanitized run storage foundation | T12,T18 | S | No raw bodies persisted; metadata retention configured; purge action and data-directory locking work |

Use transport controls in the executor, not only in UI buttons, so the runner and future CLI enforce the same rules.

## Phase 4 — integration-test MVP

| ID | Task | Depends on | Size | Acceptance criteria |
|---|---|---|---|---|
| T23 | Implement status/header assertions | T05 | S | Typed result records show expected/actual safely; header comparison semantics documented and tested |
| T24 | Implement JSON-pointer and duration assertions | T23,T11 | M | Typed equality/existence work; missing paths, invalid JSON and pointer escaping tested |
| T25 | Implement response extraction | T14,T24 | M | Extracted ID feeds next request; values run-scoped and sensitive by default; missing path fails without overwriting variables |
| T26 | Implement sequential runner | T08,T20,T23,T24,T25 | M | Snapshot inputs; stop/continue modes; cancellation prevents later sends; outcomes preserve failure category |
| T27 | Implement cleanup semantics | T26 | M | Marked cleanup runs after step failures; cleanup errors preserved; cancellation skips cleanup visibly |
| T28 | Add assertion/extractor editors | T13,T24,T25 | M | Users add/edit/delete rules and validate configuration before running |
| T29 | Build runner results pane | T22,T26,T27,T28 | M | Progress, pass/fail detail and cancelled/skipped states shown; no secret result values exposed |
| T30 | Export redacted JSON run report | T18,T29 | S | User selects output path; schemaVersion present; summaries and assertion results included; raw traffic absent |
| T31 | Add end-to-end collection scenarios | T21,T27,T29,T30 | M | Create/read/delete, failed assertion, missing extraction, cleanup failure and cancellation behave as specified |

Demo gate: import a secret-free collection, supply local credentials, run a create/read/delete flow and export a redacted test report. This completes the integration-test MVP feature scope.

## Phase 5 — release validation

| ID | Task | Depends on | Size | Acceptance criteria |
|---|---|---|---|---|
| T32 | Verify local-data and outbound-traffic behavior | T18,T20,T21,T30 | M | Planted secret checks pass; launch/idle/save/import/export/run captures show expected app traffic; limits of OS traffic and local-file controls documented |
| T33 | Exercise recovery, dark theme and accessibility | T31 | M | Keyboard navigation, resizing and dark styles across all views verified; unsaved edits, locked/corrupt database, C:\temp cleanup, failed migration and missing secrets handled; startup/memory baseline recorded |
| T34 | Package and write user guide | T02,T32,T33 | M | Approved package installs offline on target machine; dependency/license inventory included; internal update and uninstall/data-retention process documented |

Release gate: complete Milestone A and B behavior, managed-network checks, dependency review and internal distribution approval. A local-only architecture does not automatically override the organization's application policy.

## Phase 6 — optional follow-up tickets

| ID | Task | Depends on | Size | Acceptance criteria |
|---|---|---|---|---|
| T35 | Add CLI frontend | T31 | M | Uses shared engine/policy; exit codes and Ctrl+C cancellation match spec; secrets not passed in arguments |
| T36 | Add JUnit report adapter | T35 | S | Internal CI reads sample pass/fail reports; sensitive values omitted |
| T37 | Add limited Postman v2.1 importer | T21 | L | Supported fields mapped; unsupported scripts/auth explicitly reported; import never claims equivalent test coverage |
| T38 | Add form/multipart/file body support | T10,T20 | L | Correct content types, bounded file reads, cancellation and explicit file selection verified |
| T39 | Add required Windows auth or mTLS | T03,T16,T19,T20 | L | Approved-host scoping or certificate selection works on managed device; keys stay in approved storage; no TLS-validation bypass |
| T40 | Add required OAuth flow | T03,T16,T19,T20 | L | Provider-specific flow and approved endpoints documented; secure token handling/cancellation verified; no implicit consumer account integration |

T39/T40 may need multiple tickets after the discovery spike. Promote them before Milestone A if the intended APIs require them.

## Recommended first implementation session

Complete T01–T03, then T04–T09. Avoid spending the first week recreating Postman's appearance: first prove that a packaged .NET app can send the company's real requests and remain responsive. Continue with persistence, environments and secrets, then assertions and runner behavior.
