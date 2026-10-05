using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

// restByAno fixture API: a loopback-only test target for manual verification. Dummy credentials only.
// Usage: dotnet run --project tools/restByAno.FixtureApi [-- --port <1-65535>]

const int MaxDelayMs = 60_000;
const long MaxLargeBytes = 256L * 1024 * 1024;
const int MaxEchoBodyBytes = 1024 * 1024;
const string FixtureToken = "fixture-token";
const string FixtureKey = "fixture-key";
const string FixtureUser = "fixture-user";
const string FixturePassword = "fixture-password";

var port = 0;
string? environmentFile = null;
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--port")
    {
        if (i + 1 >= args.Length || !int.TryParse(args[i + 1], out port) || port is < 1 or > 65535)
        {
            Console.Error.WriteLine("--port requires a number from 1 to 65535.");
            return 2;
        }
    }
    else if (args[i] == "--environment-file")
    {
        if (i + 1 >= args.Length || !Path.IsPathFullyQualified(args[i + 1]))
        {
            Console.Error.WriteLine("--environment-file requires an absolute file path.");
            return 2;
        }

        environmentFile = args[++i];
    }
}

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders(); // no request logging: echoed headers/bodies must never reach a log
builder.WebHost.UseUrls($"http://127.0.0.1:{port}"); // loopback only; port 0 picks a free port

var app = builder.Build();

var customers = new ConcurrentDictionary<Guid, string>();
long redirectTargetHits = 0;
long delayCancellations = 0;

app.MapGet("/health", () => Results.Json(new { ok = true }));

// --- Echo -------------------------------------------------------------------------------------
app.MapMethods("/echo", ["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"], async (HttpContext ctx) =>
{
    string body;
    using (var limited = new MemoryStream())
    {
        var buffer = new byte[8192];
        int read;
        while ((read = await ctx.Request.Body.ReadAsync(buffer, ctx.RequestAborted)) > 0)
        {
            if (limited.Length + read > MaxEchoBodyBytes)
                return Results.Json(new { error = "body too large for echo" }, statusCode: 413);
            limited.Write(buffer, 0, read);
        }
        body = Encoding.UTF8.GetString(limited.ToArray());
    }

    return Results.Json(new
    {
        method = ctx.Request.Method,
        query = ctx.Request.Query.SelectMany(q => q.Value.Select(v => new { name = q.Key, value = v })),
        headers = ctx.Request.Headers.SelectMany(h => h.Value.Select(v => new { name = h.Key, value = v })),
        body,
    });
});

// --- Predictable responses for assertions, charsets and binary content ------------------------------
app.MapGet("/status/{code:int}", (int code) =>
    code is < 100 or > 599 ? Results.Json(new { error = "code must be 100-599" }, statusCode: 400)
    : code is 204 or 205 or 304 ? Results.StatusCode(code)
    : Results.Json(new { status = code }, statusCode: code));

app.MapGet("/json", (HttpContext ctx) =>
{
    ctx.Response.Headers.Append("X-Multi", "a, b");
    ctx.Response.Headers.Append("X-Multi", "c");
    ctx.Response.Headers["X-Case"] = "MixedCase";
    return Results.Content(
        "{\"id\":42,\"name\":\"Ada\",\"active\":true,\"missing\":null,\"price\":1.50,\"tags\":[\"a\",\"b\"],\"a/b\":{\"c~d\":\"deep\"},\"1\":\"digit key\"}",
        "application/json");
});

app.MapGet("/text-latin1", () => Results.Bytes(Encoding.Latin1.GetBytes("caf\u00e9 cr\u00e8me"), "text/plain; charset=iso-8859-1"));
app.MapGet("/text-unsupported-charset", () => Results.Bytes(Encoding.UTF8.GetBytes("plain ascii text"), "text/plain; charset=x-unknown-charset"));
app.MapGet("/binary", () => Results.Bytes(Enumerable.Range(0, 256).Select(i => (byte)i).ToArray(), "application/octet-stream"));

// Resets in-memory state so manual checks are repeatable.
app.MapPost("/reset", () =>
{
    customers.Clear();
    Interlocked.Exchange(ref redirectTargetHits, 0);
    Interlocked.Exchange(ref delayCancellations, 0);
    return Results.Json(new { reset = true });
});

// --- Timing, malformed and large responses -----------------------------------------------------
app.MapGet("/delay", async (HttpContext ctx, int? ms) =>
{
    var delay = Math.Clamp(ms ?? 1000, 0, MaxDelayMs);
    try
    {
        await Task.Delay(delay, ctx.RequestAborted);
    }
    catch (OperationCanceledException)
    {
        Interlocked.Increment(ref delayCancellations);
        throw;
    }
    return Results.Json(new { delayedMs = delay });
});

app.MapGet("/delay-cancelled-count", () => Results.Json(new { count = Interlocked.Read(ref delayCancellations) }));

app.MapGet("/invalid-json", () => Results.Text("{\"broken\": ", "application/json"));

app.MapGet("/large", async (HttpContext ctx, long? bytes, bool? gzip, bool? chunked) =>
{
    var size = Math.Clamp(bytes ?? 1024, 0, MaxLargeBytes);
    ctx.Response.ContentType = "application/octet-stream";
    ctx.Response.Headers["X-Fixture-Decoded-Bytes"] = size.ToString();

    if (gzip == true)
    {
        // Decoded size is `size`; the compressed wire size is far smaller.
        ctx.Response.Headers.ContentEncoding = "gzip";
        await using var gz = new GZipStream(ctx.Response.Body, CompressionLevel.Fastest, leaveOpen: true);
        await WriteFillerAsync(gz, size, ctx.RequestAborted);
        return;
    }

    if (chunked != true) ctx.Response.ContentLength = size; // chunked=true leaves the length unset
    await WriteFillerAsync(ctx.Response.Body, size, ctx.RequestAborted);
});

// --- Redirects (the client must NOT follow them) -----------------------------------------------
app.MapGet("/redirect", () => Results.Redirect("/redirect-target", permanent: false));
app.MapGet("/redirect-target", () =>
{
    Interlocked.Increment(ref redirectTargetHits);
    return Results.Json(new { reached = true });
});
app.MapGet("/redirect-count", () => Results.Json(new { count = Interlocked.Read(ref redirectTargetHits) }));

// --- Customers (in memory) -----------------------------------------------------------------------
app.MapPost("/customers", async (HttpContext ctx) =>
{
    string? name = null;
    try
    {
        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>(ctx.RequestAborted);
        if (json.ValueKind == JsonValueKind.Object
            && json.TryGetProperty("name", out var n)
            && n.ValueKind == JsonValueKind.String)
            name = n.GetString();
    }
    catch (JsonException)
    {
    }
    catch (InvalidOperationException)
    {
    }

    if (string.IsNullOrWhiteSpace(name))
        return Results.Json(new { error = "name is required" }, statusCode: 400);

    var id = Guid.NewGuid();
    customers[id] = name;
    return Results.Json(new { id = id.ToString(), name }, statusCode: 201);
});

app.MapGet("/customers/{id}", (string id) =>
    Guid.TryParse(id, out var guid) && customers.TryGetValue(guid, out var name)
        ? Results.Json(new { id = guid.ToString(), name })
        : Results.Json(new { error = "not found" }, statusCode: 404));

app.MapDelete("/customers/{id}", (string id) =>
    Guid.TryParse(id, out var guid) && customers.TryRemove(guid, out _)
        ? Results.NoContent()
        : Results.Json(new { error = "not found" }, statusCode: 404));

// --- Protected routes (dummy credentials) ----------------------------------------------------------
app.MapGet("/protected/bearer", (HttpContext ctx) =>
    ctx.Request.Headers.Authorization.ToString() == $"Bearer {FixtureToken}"
        ? Results.Json(new { ok = true, scheme = "bearer" })
        : Unauthorized(ctx, "Bearer"));

app.MapGet("/protected/key", (HttpContext ctx) =>
    ctx.Request.Headers["X-API-Key"].ToString() == FixtureKey || ctx.Request.Query["api_key"].ToString() == FixtureKey
        ? Results.Json(new { ok = true, scheme = "apikey" })
        : Unauthorized(ctx, "ApiKey"));

app.MapGet("/protected/basic", (HttpContext ctx) =>
{
    var header = ctx.Request.Headers.Authorization.ToString();
    if (header.StartsWith("Basic ", StringComparison.Ordinal))
    {
        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header["Basic ".Length..]));
            if (decoded == $"{FixtureUser}:{FixturePassword}")
                return Results.Json(new { ok = true, scheme = "basic" });
        }
        catch (FormatException)
        {
        }
    }
    return Unauthorized(ctx, "Basic realm=\"fixture\"");
});

app.Lifetime.ApplicationStarted.Register(() =>
{
    var addresses = app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
        .Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()?.Addresses ?? [];
    foreach (var address in addresses)
        Console.WriteLine($"restByAno fixture API listening on {address}");

    // Optionally write a ready-to-import restByAno environment whose baseUrl is the URL chosen at startup (no hardcoded port).
    if (environmentFile is not null && addresses.FirstOrDefault() is { } baseUrl)
    {
        var environment = new
        {
            schemaVersion = 1,
            kind = "environment",
            definition = new
            {
                name = "Fixture API (local)",
                variables = new object[]
                {
                    new { name = "baseUrl", value = baseUrl },
                    new { name = "basicUser", value = "fixture-user" },
                    new { name = "bearerToken", requiresSecret = true },
                    new { name = "apiKey", requiresSecret = true },
                    new { name = "basicPassword", requiresSecret = true },
                },
            },
        };
        File.WriteAllText(environmentFile, JsonSerializer.Serialize(environment, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Wrote environment file {environmentFile} (enter the dummy secrets after importing: bearerToken=fixture-token, apiKey=fixture-key, basicPassword=fixture-password).");
    }
    Console.WriteLine("Press Ctrl+C to stop.");
});

app.Run();
return 0;

static IResult Unauthorized(HttpContext ctx, string challenge)
{
    ctx.Response.Headers.WWWAuthenticate = challenge;
    return Results.Json(new { error = "unauthorized" }, statusCode: 401);
}

static async Task WriteFillerAsync(Stream destination, long count, CancellationToken cancellationToken)
{
    var chunk = new byte[64 * 1024];
    Array.Fill(chunk, (byte)'x');
    for (var remaining = count; remaining > 0;)
    {
        var take = (int)Math.Min(remaining, chunk.Length);
        await destination.WriteAsync(chunk.AsMemory(0, take), cancellationToken);
        remaining -= take;
    }
}
