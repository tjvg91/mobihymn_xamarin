using System.Net.Http.Headers;
using MobiHymn4.Shared;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient("HymnUpstream", client =>
{
    client.BaseAddress = new Uri(HymnApiPaths.UpstreamBase);
    client.Timeout = TimeSpan.FromMinutes(5);
});

// Server-side MIDI fetch — browsers cannot read Firebase Storage media due to missing CORS.
builder.Services.AddHttpClient("FirebaseStorage", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.UseWebAssemblyDebugging();
else
    app.UseExceptionHandler("/Error");

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.MapGet("/api/midi", async (
    string? u,
    string? n,
    IHttpClientFactory httpClientFactory,
    IConfiguration config,
    CancellationToken cancellationToken) =>
{
    var client = httpClientFactory.CreateClient("FirebaseStorage");
    var bucket = config["Firebase:StorageBucket"] ?? "mobihymn.appspot.com";

    // Prefer hymn number — same contract as Firebase Hosting midiProxy (?n=).
    // Local Host fetches the public media URL; production Cloud Function uses Admin SDK.
    if (!string.IsNullOrWhiteSpace(n) && System.Text.RegularExpressions.Regex.IsMatch(n.Trim(), @"^\d{1,6}$"))
    {
        var objectPath = "midi/h" + n.Trim() + ".mid";
        var encoded = Uri.EscapeDataString(objectPath);
        var mediaUrl = $"https://firebasestorage.googleapis.com/v0/b/{bucket}/o/{encoded}?alt=media";
        using var byNumber = await client.GetAsync(mediaUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (byNumber.StatusCode == System.Net.HttpStatusCode.NotFound)
            return Results.NotFound();
        if (byNumber.IsSuccessStatusCode)
        {
            var midiBytes = await byNumber.Content.ReadAsByteArrayAsync(cancellationToken);
            if (midiBytes.Length == 0)
                return Results.NotFound();
            return Results.File(midiBytes, "audio/midi");
        }
        // Fall through to ?u= if rules block unauthenticated media (403).
        if (string.IsNullOrWhiteSpace(u))
            return Results.StatusCode((int)byNumber.StatusCode);
    }

    if (string.IsNullOrWhiteSpace(u) || !Uri.TryCreate(u, UriKind.Absolute, out var uri))
        return Results.BadRequest("Missing hymn number or download URL.");
    if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest();
    if (!string.Equals(uri.Host, "firebasestorage.googleapis.com", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest();

    var prefix = "/v0/b/" + bucket + "/";
    if (!uri.AbsolutePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest();

    using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        return Results.NotFound();
    if (!response.IsSuccessStatusCode)
        return Results.StatusCode((int)response.StatusCode);

    var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
    if (bytes.Length == 0)
        return Results.NotFound();

    return Results.File(bytes, "audio/midi");
});

app.Map("/api/hymn/{**path}", async (HttpContext context, IHttpClientFactory httpClientFactory) =>
{
    var path = context.Request.RouteValues["path"]?.ToString() ?? "";
    var query = context.Request.QueryString.HasValue ? context.Request.QueryString.Value : "";

    // Map client paths onto upstream hymn server paths.
    // Client: /api/hymn/tim.dna?q=1  -> upstream /hymn/tim.dna?q=1
    // Client: /api/hymn/api/agent.cgi/search -> upstream /hymn/api/agent.cgi/search
    // Client: /api/hymn/audio/gccsatx/1.mp3 -> upstream /hymn/audio/gccsatx/1.mp3
    string upstreamPath;
    if (path.StartsWith("api/", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("tim.dna", StringComparison.OrdinalIgnoreCase))
    {
        upstreamPath = "/hymn/" + path + query;
    }
    else
    {
        upstreamPath = "/hymn/" + path + query;
    }

    var client = httpClientFactory.CreateClient("HymnUpstream");
    using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), upstreamPath);

    if (context.Request.ContentLength > 0 || HttpMethods.IsPost(context.Request.Method))
    {
        request.Content = new StreamContent(context.Request.Body);
        if (!string.IsNullOrEmpty(context.Request.ContentType))
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(context.Request.ContentType);
    }

    using var response = await client.SendAsync(
        request,
        HttpCompletionOption.ResponseHeadersRead,
        context.RequestAborted);

    context.Response.StatusCode = (int)response.StatusCode;
    foreach (var header in response.Headers)
        context.Response.Headers[header.Key] = header.Value.ToArray();
    foreach (var header in response.Content.Headers)
        context.Response.Headers[header.Key] = header.Value.ToArray();
    context.Response.Headers.Remove("transfer-encoding");

    await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
});

app.MapFallbackToFile("index.html");

app.Run();
