# restByAno

Local-first Windows desktop API client and test runner — compose HTTP requests, organize collections,
switch environments. Offline, no accounts.

See [`docs/implementation-spec.md`](docs/implementation-spec.md) for the full product spec.

## Layout

```
restByAno/
  src/
    restByAno.Core            # C# .NET 10 — models, request building, assertions, runner
    restByAno.Infrastructure  # HTTP executor, SQLite storage, secrets, import/export
    restByAno.Desktop         # WPF UI (MVVM)
  tools/    # Local fixture API for manual testing
  samples/  # Importable demo environment and collections
  docs/     # Specification and design notes
```

## Prerequisites

- .NET 10 SDK (pinned via `global.json`)
- Windows x64

## Build

```sh
# Build all
dotnet build restByAno.slnx

# Run the app
dotnet run --project src/restByAno.Desktop

# Publish a single-file exe
dotnet publish src/restByAno.Desktop -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```
