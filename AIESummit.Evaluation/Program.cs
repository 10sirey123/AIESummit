using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AIESummit.Domain;
using AIESummit.Evaluation;
using Microsoft.EntityFrameworkCore;

var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
string Option(string name, string fallback) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback; }
async Task Write(string file, object value) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!); await File.WriteAllTextAsync(file, JsonSerializer.Serialize(value, json)); }
try
{
    if (args.Length >= 3 && args[0] == "enrich-results")
    {
        var runId = Option("--run", ""); if (string.IsNullOrWhiteSpace(runId)) throw new InvalidDataException("enrich-results requires --run RUN_ID.");
        var count = await MongoInstrumentation.EnrichRunAsync(Option("--env-file", ".env"), args[1], args[2], runId,
            Option("--output", Path.Combine("artifacts", "improvement-enrichment", runId)), Option("--partition", "development"));
        Console.WriteLine($"Attached expected labels and observations to {count} retained documents in run {runId}; no inference was performed."); return 0;
    }
    if (args.Length > 0 && args[0] == "history")
    {
        var history = await MongoHistory.OpenAsync(Option("--env-file", ".env"));
        var kind = Option("--kind", "runs");
        var data = kind == "matrix"
            ? await history.MatrixAsync(Option("--source", ""), Option("--expectation", ""))
            : await history.QueryAsync(kind, Option("--run", ""), Option("--sample", ""), Option("--source", ""), int.Parse(Option("--limit", "50")),
                Option("--step-tag", ""), Option("--page-number", "") is { Length: > 0 } pageNumber ? int.Parse(pageNumber) : null);
        Console.WriteLine(JsonSerializer.Serialize(data, json)); return 0;
    }
    if (args.Length > 0 && args[0] is "mongo-check" or "replay")
    {
        var root = Path.GetFullPath(Option("--output", "artifacts/mongo-check"));
        var instrumentation = await MongoInstrumentation.OpenAsync(Option("--env-file", ".env"), root, new { command = args[0] });
        if (args[0] == "replay")
        {
            if (args.Length < 2) throw new InvalidDataException("replay requires an outbox file.");
            await instrumentation.ReplayAsync(args[1]);
        }
        await instrumentation.FinishRunAsync(0, 0, new { check = "connected-and-written" });
        Console.WriteLine($"MongoDB connection and durable write verified; run {instrumentation.RunId}."); return 0;
    }
    if (args.Length >= 3 && args[0] == "import-results")
    {
        var root = Path.GetFullPath(args[2]);
        var store = await MongoInstrumentation.OpenAsync(Option("--env-file", ".env"), root,
            new { command = "import-results", manifest = Path.GetFullPath(args[1]), timingSource = "retained original runner measurements; instrumentation events created during import" }, args.Contains("--mongo-offline"));
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(args[1])); var count = 0; var failures = 0;
        foreach (var sample in manifest.RootElement.GetProperty("samples").EnumerateArray())
        {
            var id = sample.GetProperty("id").GetString()!; var file = Path.Combine(root, id + ".json"); if (!File.Exists(file)) continue;
            using var result = JsonDocument.Parse(await File.ReadAllTextAsync(file)); var data = result.RootElement;
            var documentRunId = await store.BeginDocumentAsync(id, sample.GetProperty("pdf").GetString()!, sample.GetProperty("sha256").GetString()!, file);
            if (sample.TryGetProperty("gold", out var goldPath) && goldPath.ValueKind == JsonValueKind.String)
                await store.RegisterExpectedAsync(documentRunId, goldPath.GetString()!, sample.GetProperty("sha256").GetString()!, Option("--partition", "development"));
            await store.FinishDocumentAsync(documentRunId, file, data, data.TryGetProperty("metrics", out var metrics) ? metrics : null);
            count++; if (data.GetProperty("status").GetString() != "ready") failures++;
        }
        await store.FinishRunAsync(count, failures, new { imported = count, failures });
        await Write(Path.Combine(root, "instrumentation-identity.json"), new { store.RunId, store.WorkflowId, store.IsOffline });
        Console.WriteLine($"Instrumented {count} retained results; run {store.RunId}; offline={store.IsOffline}."); return 0;
    }
    if (args.Length >= 3 && args[0] == "score")
    {
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(args[1]));
        var validated = new List<TargetScore>(); var reported = new List<TargetScore>(); var documents = new List<object>();
        var tokens = 0; var seconds = 0.0; var completed = 0; var reasons = new List<string>();
        foreach (var sample in manifest.RootElement.GetProperty("samples").EnumerateArray())
        {
            var id = sample.GetProperty("id").GetString()!; var file = Path.Combine(args[2], id + ".json");
            using var result = JsonDocument.Parse(await File.ReadAllTextAsync(file)); var data = result.RootElement;
            using var gold = JsonDocument.Parse(await File.ReadAllTextAsync(sample.GetProperty("gold").GetString()!));
            var actual = CompareSafely(false); var raw = CompareSafely(true);
            TargetScore[] CompareSafely(bool modelOnly) => BenchmarkEvaluator.Compare(gold.RootElement, data.GetProperty("prediction"), modelOnly);
            validated.AddRange(actual); reported.AddRange(raw);
            var ok = data.GetProperty("status").GetString() == "ready"; if (ok) completed++;
            tokens += data.GetProperty("metrics").GetProperty("reportedTotalTokens").GetInt32(); seconds += data.GetProperty("totalSeconds").GetDouble();
            var errors = data.GetProperty("prediction").GetProperty("analyses").EnumerateArray().Where(a => a.GetProperty("error").ValueKind == JsonValueKind.String)
                .Select(a => a.GetProperty("error").GetString()!).ToArray(); reasons.AddRange(errors);
            documents.Add(new { id, uploadId = data.GetProperty("uploadId").GetInt32(), status = ok ? "ready" : "failed",
                totalSeconds = data.GetProperty("totalSeconds").GetDouble(), tokens = data.GetProperty("metrics").GetProperty("reportedTotalTokens").GetInt32(), errors,
                validated = actual, modelReported = raw });
        }
        var score = new { documentsAttempted = documents.Count, completedDocuments = completed, failedDocuments = documents.Count - completed,
            reportedLlmTokens = tokens, sumDocumentWallSeconds = seconds,
            validatedFindings = BenchmarkEvaluator.Summarize(validated), modelReportedPresence = BenchmarkEvaluator.Summarize(reported),
            validationErrors = reasons.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count()), documents,
            limitations = "All selected test documents included. Diagnostic model outcomes are unvalidated; validated metrics reflect only published findings. No official CUAD character-span or structured-field accuracy is claimed. Input/output token breakdown, immutable provider model revision and OCR tokens are unavailable." };
        await Write(Path.Combine(args[2], "scores.json"), score); Console.WriteLine(JsonSerializer.Serialize(score, json)); return 0;
    }
    if (args is ["schema", var schemaPath])
    {
        using var db = new AIESummitDb(new DbContextOptionsBuilder<AIESummitDb>().UseNpgsql("Host=localhost;Database=model-only").Options);
        await File.WriteAllTextAsync(schemaPath, db.Database.GenerateCreateScript()); return 0;
    }
    if (args is ["select", var corpus, var manifestPath])
    {
        var root = Path.GetFullPath(corpus);
        var samples = Directory.EnumerateFiles(Path.Combine(root, "cuad"), "*.json").Select(path => {
            using var doc = JsonDocument.Parse(File.ReadAllText(path)); var d = doc.RootElement;
            return new { id = d.GetProperty("sample_id").GetString()!, pages = d.GetProperty("document").GetProperty("pages").GetInt32(),
                split = d.GetProperty("source").GetProperty("upstream_split").GetString(),
                sha256 = d.GetProperty("document").GetProperty("sha256").GetString()!,
                pdf = Path.ChangeExtension(path, ".pdf"), gold = path,
                rank = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("aiesummit-extraction-v1/" + d.GetProperty("sample_id").GetString()))) };
        }).Where(s => s.split == "test" && s.pages is > 0 and <= 10 && s.id != "0190").OrderBy(s => s.rank, StringComparer.Ordinal).Take(20).ToArray();
        if (samples.Length != 20) throw new InvalidDataException("Need 20 prepared test PDFs of at most 10 pages (application OCR limit remains 30).");
        await Write(manifestPath, new { version = "cuad-ten-v1", pilotExclusions = new[] { "0190" }, selection = "First 20 SHA256(aiesummit-extraction-v1/sample_id), official test split, 1–10 original PDF pages; exclude engineering preflight 0190; no label-based selection.", samples });
        Console.WriteLine($"Selected {samples.Length} documents, {samples.Sum(s => s.pages)} PDF pages."); return 0;
    }
    if (args.Length < 2 || args[0] is not ("run" or "batch"))
    {
        Console.WriteLine("Usage: run document.pdf [--gold sidecar.json] [--output result.json] [--origin http://localhost:18080] [--token-file path] [--timeout-seconds 900]\nbatch manifest.json --output artifacts/evaluation\nselect corporea/benchmarks artifacts/evaluation/manifest.json"); return 2;
    }
    var origin = new Uri(Option("--origin", "http://localhost:18080"));
    using var http = new HttpClient { BaseAddress = origin, Timeout = TimeSpan.FromMinutes(3) };
    var tokenFile = Option("--token-file", "");
    if (tokenFile != "") http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await File.ReadAllTextAsync(tokenFile)).Trim());
    else
    {
        if (!origin.IsLoopback) throw new InvalidOperationException("A token file is required outside localhost.");
        var config = await http.GetFromJsonAsync<JsonElement>("/firebase-config");
        if (!config.GetProperty("emulator").GetBoolean()) throw new InvalidOperationException("Automatic test signup requires the local Firebase emulator.");
        using var auth = new HttpClient();
        using var response = await auth.PostAsJsonAsync($"http://{origin.Host}:{config.GetProperty("emulatorPort").GetString()}/identitytoolkit.googleapis.com/v1/accounts:signUp?key=demo-key",
            new { email = $"evaluation-{Guid.NewGuid():N}@example.test", password = Guid.NewGuid().ToString("N"), returnSecureToken = true });
        response.EnsureSuccessStatusCode();
        var identity = await response.Content.ReadFromJsonAsync<JsonElement>();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identity.GetProperty("idToken").GetString());
    }
    var timeout = TimeSpan.FromSeconds(int.Parse(Option("--timeout-seconds", "900")));
    var defaultRoot = Path.Combine("artifacts", "extraction-evaluation", DateTime.UtcNow.ToString("yyyyMMddTHHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]);
    var outputPath = Option("--output", args[0] == "batch" ? defaultRoot : Path.Combine(defaultRoot, "result.json"));
    var artifactRoot = args[0] == "batch" ? Path.GetFullPath(outputPath) : Path.GetDirectoryName(Path.GetFullPath(outputPath))!;
    var mongo = args.Contains("--no-mongo") ? null : await MongoInstrumentation.OpenAsync(Option("--env-file", ".env"), artifactRoot,
        new { command = args[0], input = Path.GetFullPath(args[1]), apiOrigin = origin.GetLeftPart(UriPartial.Authority),
            targetCatalog = ExtractionCatalog.Version, includePageSections = args.Contains("--include-page-sections") }, args.Contains("--mongo-offline"));
    if (mongo != null) { await Write(Path.Combine(artifactRoot, "run-identity.json"), new { mongo.RunId, mongo.WorkflowId, mongo.IsOffline }); Console.WriteLine($"Telemetry ready; run {mongo.RunId}; offline={mongo.IsOffline}."); }
    var requests = new List<(string Id, string Pdf, string? Gold, string? Hash)>();
    if (args[0] == "run") requests.Add((Path.GetFileNameWithoutExtension(args[1]), Path.GetFullPath(args[1]), Option("--gold", "") is { Length: > 0 } g ? g : null, null));
    else
    {
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(args[1]));
        foreach (var s in manifest.RootElement.GetProperty("samples").EnumerateArray())
            requests.Add((s.GetProperty("id").GetString()!, s.GetProperty("pdf").GetString()!, s.GetProperty("gold").GetString(), s.GetProperty("sha256").GetString()));
    }
    var all = new List<TargetScore>(); var results = new List<object>(); var failed = 0;
    if (requests.Any(s => File.Exists(args[0] == "run" ? outputPath : Path.Combine(outputPath, s.Id + ".json"))))
        throw new InvalidOperationException("Result files already exist. Choose a fresh output folder to preserve previous runs.");
    foreach (var sample in requests)
    {
        var sw = Stopwatch.StartNew(); int? uploadId = null;
        var resultFile = args[0] == "run" ? outputPath : Path.Combine(outputPath, sample.Id + ".json");
        string? documentRunId = null;
        try
        {
            if (mongo != null) documentRunId = await mongo.BeginDocumentAsync(sample.Id, sample.Pdf, sample.Hash, resultFile);
            var bytes = await File.ReadAllBytesAsync(sample.Pdf);
            if (mongo != null) await mongo.SourceReadAsync(documentRunId!, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), bytes.LongLength);
            if (mongo != null && sample.Gold != null)
                await mongo.RegisterExpectedAsync(documentRunId!, sample.Gold, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), Option("--partition", "development"));
            if (sample.Hash != null && !Convert.ToHexString(SHA256.HashData(bytes)).Equals(sample.Hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("PDF checksum differs from frozen selection.");
            if (!bytes.AsSpan().StartsWith("%PDF-"u8)) throw new InvalidDataException("Input must be a PDF.");
            using var form = new MultipartFormDataContent();
            var content = new ByteArrayContent(bytes); content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf"); form.Add(content, "file", Path.GetFileName(sample.Pdf));
            using var upload = await http.PostAsync("/api/v1/uploads?pageSections=" + args.Contains("--include-page-sections").ToString().ToLowerInvariant(), form); upload.EnsureSuccessStatusCode();
            uploadId = (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
            if (mongo != null) await mongo.CheckpointAsync(documentRunId!, "intake", "accepted", new { uploadId, elapsedSeconds = sw.Elapsed.TotalSeconds });
            async Task<JsonElement> Wait(string path, Func<JsonElement, bool> completed, Func<JsonElement, bool> error)
            {
                while (sw.Elapsed < timeout)
                {
                    var response = await http.GetFromJsonAsync<JsonElement>(path);
                    if (error(response)) throw new InvalidOperationException("Workflow reported failure; inspect retained upload and prompts.");
                    if (completed(response)) return response;
                    await Task.Delay(1500);
                }
                throw new TimeoutException("Workflow deadline exceeded; retained work may still complete.");
            }
            var state = await Wait($"/api/v1/uploads/{uploadId}", d => d.GetProperty("upload").GetProperty("status").GetString() == "ready",
                d => d.GetProperty("upload").GetProperty("status").GetString() == "failed");
            var intakeSeconds = sw.Elapsed.TotalSeconds;
            if (mongo != null) await mongo.CheckpointAsync(documentRunId!, "split", "completed", new { uploadId, intakeSeconds });
            using var start = await http.PostAsync($"/api/v1/uploads/{uploadId}/extractions", null); start.EnsureSuccessStatusCode();
            var prediction = await Wait($"/api/v1/uploads/{uploadId}/extractions", d => d.GetProperty("analyses").GetArrayLength() > 0
                && d.GetProperty("analyses").EnumerateArray().All(a => a.GetProperty("status").GetString() == "ready"),
                d => d.GetProperty("analyses").EnumerateArray().Any(a => a.GetProperty("status").GetString() == "failed"));
            var extractionSeconds = sw.Elapsed.TotalSeconds;
            // Wait for optional page-section work so the full workflow metrics include those calls.
            await Wait($"/api/v1/uploads/{uploadId}", d => d.GetProperty("pages").EnumerateArray().All(p => p.GetProperty("analysisStatus").GetString() is "ready" or "failed" or "not_started"), _ => false);
            var metrics = await http.GetFromJsonAsync<JsonElement>($"/api/v1/uploads/{uploadId}/metrics");
            TargetScore[]? scores = null;
            if (sample.Gold != null) { using var gold = JsonDocument.Parse(await File.ReadAllTextAsync(sample.Gold)); scores = BenchmarkEvaluator.Compare(gold.RootElement, prediction); all.AddRange(scores); }
            TargetScore[]? modelReportedScores = null;
            if (sample.Gold != null) { using var gold = JsonDocument.Parse(await File.ReadAllTextAsync(sample.Gold)); modelReportedScores = BenchmarkEvaluator.Compare(gold.RootElement, prediction, true); }
            var result = new { sample.Id, status = "ready", uploadId, intakeSeconds, extractionReadySeconds = extractionSeconds, totalSeconds = sw.Elapsed.TotalSeconds,
                originalPdfSha256 = Convert.ToHexString(SHA256.HashData(bytes)), prediction, metrics, scores, modelReportedScores };
            await Write(resultFile, result);
            if (mongo != null) await mongo.FinishDocumentAsync(documentRunId!, resultFile, result, metrics);
            results.Add(new { sample.Id, uploadId, status = "ready", intakeSeconds, extractionReadySeconds = extractionSeconds, totalSeconds = sw.Elapsed.TotalSeconds, metrics });
            Console.WriteLine($"{sample.Id}: ready, upload {uploadId}, {sw.Elapsed.TotalSeconds:F1}s, {metrics.GetProperty("reportedTotalTokens")} reported LLM tokens.");
        }
        catch (Exception ex)
        {
            if (File.Exists(resultFile)) throw new InvalidOperationException("The completed result is retained; telemetry finalization failed. Replay its outbox or import the retained result.");
            JsonElement? failureMetrics = null;
            JsonElement? rejected = null; TargetScore[]? modelReportedFailureScores = null;
            try { if (uploadId != null) failureMetrics = await http.GetFromJsonAsync<JsonElement>($"/api/v1/uploads/{uploadId}/metrics"); } catch { }
            try { if (uploadId != null) rejected = await http.GetFromJsonAsync<JsonElement>($"/api/v1/uploads/{uploadId}/extractions");
                if (rejected != null && sample.Gold != null) { using var gold = JsonDocument.Parse(await File.ReadAllTextAsync(sample.Gold)); modelReportedFailureScores = BenchmarkEvaluator.Compare(gold.RootElement, rejected.Value, true); } } catch { }
            failed++; var failure = new { sample.Id, status = "failed", uploadId, totalSeconds = sw.Elapsed.TotalSeconds, prediction = rejected,
                metrics = failureMetrics, modelReportedScores = modelReportedFailureScores, error = ex.GetType().Name + ": " + ex.Message };
            results.Add(failure); await Write(resultFile, failure);
            if (mongo != null && documentRunId != null)
            {
                await mongo.FinishDocumentAsync(documentRunId, resultFile, failure, failureMetrics);
            }
            Console.WriteLine($"{sample.Id}: failed ({ex.GetType().Name}); upload {uploadId} retained.");
        }
        if (args[0] == "batch") await Write(Path.Combine(outputPath, "summary.json"), new { startedDataset = Path.GetFullPath(args[1]), samples = requests.Count, failed,
            scoredDocuments = all.Count / 10, metrics = BenchmarkEvaluator.Summarize(all), results,
            limitations = "Presence scoring treats abstentions as negative and excludes failed documents; report completion separately. EvidenceTokenF1 is descriptive lexical overlap, not official CUAD span scoring. Structured fields lack independent gold. OCR tokens unavailable." });
    }
    if (mongo != null) await mongo.FinishRunAsync(requests.Count, failed, new { scoredDocuments = all.Count / 10, metrics = BenchmarkEvaluator.Summarize(all) });
    return failed == 0 ? 0 : 1;
}
catch (Exception ex) { Console.Error.WriteLine($"Evaluation failed ({ex.GetType().Name}): {ex.Message}"); return 1; }
