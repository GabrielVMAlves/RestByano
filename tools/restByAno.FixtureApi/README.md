# restByAno.FixtureApi

Loopback-only ASP.NET Core minimal API used as a manual test target for restByAno. It is a development tool:
it is not referenced by the desktop app and is never part of the desktop publish output. It uses dummy credentials
only and writes no request logs. State (customers, counters) is in memory and resets on restart.

## Launch

```powershell
# free port chosen automatically; the base URL is printed
dotnet run --project tools/restByAno.FixtureApi

# explicit development port
dotnet run --project tools/restByAno.FixtureApi -- --port 5099
```

Output: `restByAno fixture API listening on http://127.0.0.1:<port>`. Use that as `baseUrl` in a restByAno environment.
Stop with Ctrl+C.

```powershell
# also write a ready-to-import restByAno environment whose baseUrl is the URL chosen at startup
dotnet run --project tools/restByAno.FixtureApi -- --environment-file C:\temp\fixture-env.json
```

The environment declares the secrets `bearerToken`, `apiKey` and `basicPassword` as requirements only; after importing it enter
the dummy values `fixture-token`, `fixture-key` and `fixture-password`.

## Endpoints

| Request | Behavior |
|---|---|
| `GET /health` | `200 {"ok":true}` |
| `GET/POST/PUT/PATCH/DELETE/HEAD/OPTIONS /echo?a=1&a=2` | JSON with `method`, `query` (duplicates kept, in order), `headers`, `body` (body capped at 1 MiB) |
| `GET /delay?ms=10000` | Delayed JSON; delay capped at 60000 ms; observes request cancellation |
| `GET /delay-cancelled-count` | `{"count":n}` delays whose request was cancelled |
| `GET /invalid-json` | `application/json` with malformed text |
| `GET /large?bytes=N[&chunked=true][&gzip=true]` | Streams N decoded bytes (capped at 256 MiB). Default sets Content-Length; `chunked=true` omits it; `gzip=true` sends `Content-Encoding: gzip` (small on the wire, N bytes decoded) |
| `GET /redirect` | `302` to `/redirect-target` |
| `GET /redirect-target` | Increments a counter |
| `GET /redirect-count` | `{"count":n}`; stays `0` if the client does not follow redirects |
| `POST /customers` body `{"name":"Ada"}` | `201 {"id":"...","name":"Ada"}`; `400` if name is missing |
| `GET /customers/{id}` | Customer or `404` |
| `DELETE /customers/{id}` | `204` or `404` |
| `GET /protected/bearer` | `Authorization: Bearer fixture-token`, else `401` |
| `GET /protected/key` | `X-API-Key: fixture-key` or `?api_key=fixture-key`, else `401` |
| `GET /protected/basic` | Basic `fixture-user` / `fixture-password`, else `401` |
| `GET /status/{code}` | Responds with that status (100–599); `204`/`205`/`304` have no body |
| `GET /json` | Fixed document with numbers, booleans, `null`, a `~`/`/` key and a repeated `X-Multi` header (for assertion checks) |
| `GET /text-latin1` | `text/plain; charset=iso-8859-1` body (`café crème`) |
| `GET /text-unsupported-charset` | `text/plain` with a charset .NET does not know (readable warning expected) |
| `GET /binary` | 256 bytes, `application/octet-stream` |
| `POST /reset` | Clears customers and the redirect/cancellation counters |

## Sample URLs

```text
{{baseUrl}}/health
{{baseUrl}}/echo?tag=a&tag=b
{{baseUrl}}/delay?ms=10000
{{baseUrl}}/large?bytes=11534336
{{baseUrl}}/large?bytes=11534336&gzip=true
{{baseUrl}}/redirect
```
