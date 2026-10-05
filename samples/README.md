# restByAno samples

Native import files for the local fixture API (`tools/restByAno.FixtureApi`). They use `{{baseUrl}}` — no port is hardcoded — and
only the fixture's DUMMY credentials. Import them with **Import…** (collections pane or environment editor).

| File | What it is |
|---|---|
| `fixture-environment.json` | Environment template (`baseUrl` placeholder `http://127.0.0.1:5000`, `basicUser`, and the secrets `bearerToken`, `apiKey`, `basicPassword` as requirements only). Prefer the file the fixture writes for you: `dotnet run --project tools/restByAno.FixtureApi -- --environment-file C:\temp\fixture-env.json` fills in the URL it actually chose. |
| `fixture-collection.json` | Passing collection: customer create → read → delete (cleanup), bearer / API key (header and query) / basic authentication, JSON type and path assertions, an explicit 404 expectation. |
| `fixture-demo-failure.json` | Intentional assertion failure, a request skipped after it (stop on first failure) and a cleanup request that still runs. |
| `fixture-demo-cancel.json` | A 15-second request — press Stop while it runs to see cancellation; the cleanup request is then explicitly skipped. |

After importing the environment, enter the dummy secrets: `bearerToken` = `fixture-token`, `apiKey` = `fixture-key`,
`basicPassword` = `fixture-password`. Never put real credentials in these files or checks.
See `docs/manual-verification.md` for the full checklist.
