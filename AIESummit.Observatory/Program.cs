using System.Net;
using AIESummit.Evaluation;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
var root = Path.GetFullPath(builder.Configuration["repo"] ?? Path.Combine(builder.Environment.ContentRootPath, ".."));
builder.WebHost.UseUrls("http://127.0.0.1:18114");
var app = builder.Build();
app.Use(async (context, next) => {
    if (context.Connection.RemoteIpAddress is not { } ip || !IPAddress.IsLoopback(ip)
        || context.Request.Host.Host is not ("127.0.0.1" or "localhost") || context.Request.Method is not ("GET" or "HEAD"))
    { context.Response.StatusCode = 403; return; }
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Cache-Control"] = "no-store";
    try { await next(); }
    catch { await Results.Problem("History is unavailable. Check local MongoDB connectivity and settings.").ExecuteAsync(context); }
});
app.MapGet("/api/history", async (string? kind, string? runId, string? sampleId) => {
    if (kind is not ("runs" or "documents" or "steps" or "workflows" or "observations" or "expectations")) return Results.BadRequest();
    var history = await MongoHistory.OpenAsync(Path.Combine(root, ".env"));
    return Results.Ok(await history.QueryAsync(kind, runId, sampleId, limit: 200));
});
app.MapGet("/api/attempts", async () => {
    var env = MongoInstrumentation.ReadSettings(Path.Combine(root, ".env"));
    var uri = env["MONGODB_URI"]; var settings = MongoClientSettings.FromConnectionString(uri);
    settings.ServerSelectionTimeout = TimeSpan.FromSeconds(10);
    if (env.GetValueOrDefault("MONGODB_TLS12") == "true") settings.SslSettings = new() { EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 };
    var db = new MongoClient(settings).GetDatabase(env.GetValueOrDefault("MONGODB_DATABASE", new MongoUrl(uri).DatabaseName ?? "aiesummit_evaluation"));
    var rows = await db.GetCollection<BsonDocument>("improvement_attempts").Find(new BsonDocument()).Sort(new BsonDocument("createdAt", -1)).Limit(200)
        .Project(new BsonDocument { { "_id", 1 }, { "title", 1 }, { "status", 1 }, { "runId", 1 }, { "createdAt", 1 } }).ToListAsync();
    return Results.Ok(rows.Select(r => BsonTypeMapper.MapToDotNetValue(r)));
});
app.MapGet("/health", () => new { status = "ok" });
app.Run();
