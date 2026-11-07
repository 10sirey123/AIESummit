using System.Diagnostics;
using System.Text.Json;
using AIESummit.Agents;
using AIESummit.Domain;
using AIESummit.Evaluation;
using Blueshift.Agents.LLM;
using Blueshift.Domain.Abstract;
using Blueshift.Domain.Concrete;
using Blueshift.Domain.Entities;
using Blueshift.Domain.LLM;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AIESummit.WorkflowRunner;

public static class WorkflowRunner
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public static async Task<int> MainAsync(string[] args)
    {
        string Option(string name, string fallback) { var i = Array.IndexOf(args, name); return i < 0 ? fallback : args[i + 1]; }
        var output = Path.GetFullPath(Option("--output", "artifacts/improvement-run"));
        try
        {
            var services = new ServiceCollection();
            var connection = Environment.GetEnvironmentVariable("ConnectionStrings__AIESummitDb")
                ?? throw new InvalidOperationException("The application database connection must be supplied through the runner environment.");
            services.AddDbContext<AIESummitDb>(options => options.UseNpgsql(connection));
            services.AddScoped<EFBaseDbContext>(s => s.GetRequiredService<AIESummitDb>());
            services.AddScoped(typeof(IBasePersistenceRepository<>), typeof(EFBasePersistenceRepository<>));
            services.AddSingleton<IBroadcaster>(NullBroadcaster.Instance);
            services.AddHttpClient();
            services.AddMemoryCache();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AIESummitDb>();
            if (args.FirstOrDefault() == "snapshot")
            {
                var actual = await db.LLMSteps.Include(s => s.Model).SingleAsync(s => s.Tag == ContractExtractionStep.Tag);
                var graph = new WorkflowDefinition { Name = "Ten contract targets · Muse baseline", Nodes = [new WorkflowNode {
                    Key = "extract-contract-targets", Targets = ExtractionCatalog.Defaults().Select(t => t.Tag).ToList(),
                    SystemPrompt = actual.SystemRolePromptTemplate, Prompt = actual.Template, Model = "muse-wonder-1.0",
                    Temperature = actual.Temperature, ReasoningEffort = actual.ReasoningEffort ?? ""
                }] };
                graph.OrderedNodes();
                await Write(output, graph);
                await Write(output + ".source.json", new { capturedAt = DateTime.UtcNow, stepId = actual.ID, actual.Tag,
                    actual.Enabled, model = actual.Model?.Name, provider = actual.Model?.Provider.ToString(),
                    note = "Effective persisted prompt/settings captured. The experimental graph explicitly selects Muse; the running application selection is unchanged." });
                Console.WriteLine("Captured effective extraction step without changing persisted configuration.");
                return 0;
            }
            if (args.FirstOrDefault() != "run") throw new InvalidDataException("Use snapshot --output file | run --workflow file --manifest file --fixtures folder --output folder.");
            Directory.CreateDirectory(output);
            var graphFile = Option("--workflow", "improvement/workflow.json");
            var workflow = JsonSerializer.Deserialize<WorkflowDefinition>(await File.ReadAllTextAsync(graphFile), WorkflowDefinition.Json)
                ?? throw new InvalidDataException("Missing workflow.");
            var nodes = workflow.OrderedNodes();
            await Write(Path.Combine(output, "workflow.json"), workflow);
            var manifestPath = Option("--manifest", "artifacts/extraction-evaluation/manifest-final.json");
            var fixtures = Option("--fixtures", "artifacts/extraction-evaluation/final");
            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
            var mongo = await MongoInstrumentation.OpenAsync(Option("--env-file", ".env"), output,
                new { command = "workflow-run", workflowHash = workflow.Hash, fixtureRoot = fixtures,
                    manifestHash = WorkflowDefinition.HashText(await File.ReadAllTextAsync(manifestPath)),
                    attemptId = Option("--attempt-id", "manual"), partition = "development" },
                args.Contains("--mongo-offline"), new { name = workflow.Name, version = workflow.Version, workflowHash = workflow.Hash,
                    nodes = nodes.Select(n => new { key = n.Key, kind = "llm", scope = "logical-document", stepTag = StepTag(n),
                        definitionHash = n.DefinitionHash, n.Targets, n.Model, n.Temperature, n.ReasoningEffort }),
                    edges = nodes.SelectMany(n => n.DependsOn.Select(p => new { from = p, to = n.Key, relation = "depends_on" })) });
            await Write(Path.Combine(output, "run-identity.json"), new { mongo.RunId, mongo.WorkflowId, workflowHash = workflow.Hash });
            // One runner owns experiment writes. This lock does not pause or mutate ordinary application work.
            await db.Database.OpenConnectionAsync();
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_lock(825833004)");
            try
            {
                var scores = new List<TargetScore>(); var failed = 0; var count = 0;
                var inputTokens = 0L; var outputTokens = 0L; var totalTokens = 0L;
                var newInputTokens = 0L; var newOutputTokens = 0L; var newTotalTokens = 0L;
                var cacheHits = 0; var executedNodes = 0; var unknownUsagePromptCount = 0;
                var knownInputTokensSubtotal = 0L; var knownOutputTokensSubtotal = 0L;
                var tokenBreakdownAvailable = true; var tokenTotalsAvailable = true;
                var newTokenBreakdownAvailable = true; var newTokenTotalsAvailable = true;
                foreach (var sample in manifest.RootElement.GetProperty("samples").EnumerateArray())
                {
                    var id = sample.GetProperty("id").GetString()!;
                    var resultFile = Path.Combine(output, id + ".json");
                    if (File.Exists(resultFile)) throw new InvalidOperationException("Choose a fresh output folder; completed result files are immutable.");
                    var pdf = ResolvePath(sample.GetProperty("pdf").GetString()!);
                    var gold = ResolvePath(sample.GetProperty("gold").GetString()!);
                    var sourceHash = sample.GetProperty("sha256").GetString()!;
                    var docRun = await mongo.BeginDocumentAsync(id, pdf, sourceHash, resultFile);
                    await mongo.RegisterExpectedAsync(docRun, gold, sourceHash);
                    JsonElement result;
                    var sampleClock = Stopwatch.StartNew();
                    var progress = new SampleProgress();
                    try
                    {
                        result = await EvaluateSample(db, scope.ServiceProvider, nodes, workflow, docRun, sourceHash,
                            Path.Combine(fixtures, id + ".json"), args.Contains("--cache-only"), progress);
                    }
                    catch (Exception ex)
                    {
                        result = FailedSample(progress, ex.GetType().Name, sampleClock.Elapsed.TotalSeconds);
                        db.ChangeTracker.Clear();
                    }
                    using var goldDoc = JsonDocument.Parse(await File.ReadAllTextAsync(gold));
                    var measured = BenchmarkEvaluator.Compare(goldDoc.RootElement, result.GetProperty("prediction"));
                    scores.AddRange(measured); count++;
                    if (result.GetProperty("status").GetString() != "ready") failed++;
                    // Store scores in the original evaluator result shape, with the same scorer authority.
                    var payload = JsonSerializer.SerializeToElement(new { id, status = result.GetProperty("status"),
                        uploadId = result.GetProperty("uploadId"), totalSeconds = result.GetProperty("totalSeconds"),
                        intakeSeconds = 0, extractionReadySeconds = result.GetProperty("totalSeconds"),
                        originalPdfSha256 = sourceHash, prediction = result.GetProperty("prediction"),
                        metrics = result.GetProperty("metrics"), graph = result.GetProperty("graph"), scores = measured,
                        error = result.TryGetProperty("error", out var error) ? error.GetString() : null,
                        errorCode = result.TryGetProperty("errorCode", out var errorCode) ? errorCode.GetString() : null,
                        errorStage = result.TryGetProperty("errorStage", out var errorStage) ? errorStage.GetString() : null,
                        workflowHash = workflow.Hash, note = "OCR and split reused from frozen persisted fixture. Timing covers extraction graph only." }, Json);
                    await Write(resultFile, payload);
                    await mongo.FinishDocumentAsync(docRun, resultFile, payload, payload.GetProperty("metrics"));
                    await mongo.RecordGraphAsync(docRun, payload.GetProperty("graph").GetProperty("nodes"), payload.GetProperty("graph").GetProperty("edges"));
                    var metrics = payload.GetProperty("metrics");
                    long Counter(string key) => metrics.TryGetProperty(key, out var counter) && counter.ValueKind == JsonValueKind.Number ? counter.GetInt64() : 0;
                    tokenTotalsAvailable &= metrics.GetProperty("reportedTotalTokens").ValueKind == JsonValueKind.Number;
                    tokenBreakdownAvailable &= metrics.GetProperty("inputOutputBreakdownAvailable").GetBoolean();
                    newTokenBreakdownAvailable &= metrics.TryGetProperty("newInputOutputBreakdownAvailable", out var newBreakdown) && newBreakdown.GetBoolean();
                    newTokenTotalsAvailable &= metrics.GetProperty("newlyConsumedTotalTokens").ValueKind == JsonValueKind.Number;
                    inputTokens += Counter("reportedInputTokens"); outputTokens += Counter("reportedOutputTokens"); totalTokens += Counter("reportedTotalTokens");
                    newInputTokens += Counter("newlyConsumedInputTokens"); newOutputTokens += Counter("newlyConsumedOutputTokens"); newTotalTokens += Counter("newlyConsumedTotalTokens");
                    knownInputTokensSubtotal += Counter("knownInputTokensSubtotal"); knownOutputTokensSubtotal += Counter("knownOutputTokensSubtotal");
                    unknownUsagePromptCount += (int)Counter("unknownUsagePromptCount");
                    foreach (var node in payload.GetProperty("graph").GetProperty("nodes").EnumerateArray())
                        if (node.TryGetProperty("cacheHit", out var hit)) { if (hit.GetBoolean()) cacheHits++; else executedNodes++; }
                    Console.WriteLine(JsonSerializer.Serialize(new { sampleId = id, status = payload.GetProperty("status"), completed = count,
                        tokens = payload.GetProperty("metrics").GetProperty("reportedTotalTokens"), runId = mongo.RunId }));
                    db.ChangeTracker.Clear();
                }
                var summary = new { documentsAttempted = count, completedDocuments = count - failed, failedDocuments = failed,
                    validatedFindings = BenchmarkEvaluator.Summarize(scores), workflowHash = workflow.Hash,
                    metrics = new { reportedInputTokens = tokenBreakdownAvailable ? (long?)inputTokens : null,
                        reportedOutputTokens = tokenBreakdownAvailable ? (long?)outputTokens : null,
                        reportedTotalTokens = tokenTotalsAvailable ? (long?)totalTokens : null,
                        newlyConsumedInputTokens = newTokenBreakdownAvailable ? (long?)newInputTokens : null,
                        newlyConsumedOutputTokens = newTokenBreakdownAvailable ? (long?)newOutputTokens : null,
                        newlyConsumedTotalTokens = newTokenTotalsAvailable ? (long?)newTotalTokens : null,
                        inputOutputBreakdownAvailable = tokenBreakdownAvailable, tokenTotalsAvailable, cacheHits, executedNodes,
                        newInputOutputBreakdownAvailable = newTokenBreakdownAvailable,
                        knownInputTokensSubtotal, knownOutputTokensSubtotal, unknownUsagePromptCount } };
                await Write(Path.Combine(output, "scores.json"), summary);
                await Write(Path.Combine(output, "summary.json"), summary);
                await mongo.FinishRunAsync(count, failed, summary);
                return 0;
            }
            finally { await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_unlock(825833004)"); }
        }
        catch (Exception ex)
        {
            // Provider/connection exception messages can contain request data. Persist only the safe error type.
            Console.Error.WriteLine($"Workflow runner failed ({ex.GetType().Name}). Inspect retained artifacts and database configuration.");
            return 1;
        }
    }

    private static string StepTag(WorkflowNode node) => "improvement-" + node.DefinitionHash;
    private static Task Write(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        return File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, Json));
    }

    public static string ResolvePath(string path)
    {
        if (File.Exists(path)) return path;
        // Frozen manifests originate on Windows. Resolve only repository-owned benchmark/artifact paths.
        var normalized = path.Replace('\\', '/');
        foreach (var marker in new[] { "/corporea/", "/artifacts/" })
        {
            var at = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (at >= 0)
            {
                var root = Environment.GetEnvironmentVariable("AIESUMMIT_REPOSITORY_ROOT") ?? (Directory.Exists("/repo") ? "/repo" : Directory.GetCurrentDirectory());
                var candidate = Path.GetFullPath(Path.Combine(root, normalized[(at + 1)..]));
                var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (candidate.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
                    && File.Exists(candidate)) return candidate;
            }
        }
        throw new FileNotFoundException("Frozen benchmark source is unavailable.");
    }

    private static async Task<JsonElement> EvaluateSample(AIESummitDb db, IServiceProvider services, WorkflowNode[] nodes,
        WorkflowDefinition workflow, string documentRunId, string sourceHash, string fixtureFile, bool cacheOnly, SampleProgress progress)
    {
        var sw = Stopwatch.StartNew();
        progress.Mark("fixture_read");
        using var fixture = JsonDocument.Parse(await File.ReadAllTextAsync(fixtureFile));
        var uploadId = fixture.RootElement.GetProperty("uploadId").GetInt32();
        progress.UploadId = uploadId;
        progress.Mark("source_identity", "fixture_source_mismatch");
        var upload = await db.Uploads.SingleAsync(u => u.ID == uploadId);
        // Verify identity before reusing any OCR/split input; the manifest hash alone cannot identify a database row.
        var actualHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(upload.Source)).ToLowerInvariant();
        if (!actualHash.Equals(sourceHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Fixture upload bytes do not match the frozen source.");
        var fixtureDocumentIds = fixture.RootElement.GetProperty("prediction").GetProperty("analyses").EnumerateArray()
            .Select(a => a.GetProperty("documentId").GetInt32()).Distinct().ToArray();
        progress.Mark("fixture_split", "fixture_split_unavailable");
        var documents = await db.Documents.Where(d => fixtureDocumentIds.Contains(d.ID) && d.UploadId == uploadId).OrderBy(d => d.FirstPage).ToArrayAsync();
        if (documents.Length != fixtureDocumentIds.Length || documents.Length == 0) throw new InvalidDataException("Frozen split documents are unavailable.");
        var analyses = new List<object>(); var execution = new List<object>(); var prompts = new List<object>();
        var logicalInput = 0; var logicalOutput = 0; var logicalTotal = 0; var newInput = 0; var newOutput = 0; var newTotal = 0;
        var breakdown = true; var newBreakdown = true; var totalAvailable = true; var newTotalAvailable = true;
        var ready = true; var unknownUsagePromptCount = 0;
        var knownInputTokensSubtotal = 0; var knownOutputTokensSubtotal = 0;
        progress.LastResult = Capture();
        foreach (var document in documents)
        {
            progress.Mark("snapshot_load", "fixture_snapshot_unavailable");
            // Frozen source snapshots, not the latest mutable OCR page projection, define this experiment's input.
            var baseAnalysisId = fixture.RootElement.GetProperty("prediction").GetProperty("analyses").EnumerateArray()
                .First(a => a.GetProperty("documentId").GetInt32() == document.ID).GetProperty("id").GetInt32();
            var baseline = await db.DocumentAnalyses.Include(a => a.Pages).Include(a => a.Targets).ThenInclude(t => t.ExtractionType)
                .SingleAsync(a => a.ID == baseAnalysisId && a.DocumentId == document.ID);
            var results = new Dictionary<string, NodeResult>(StringComparer.Ordinal);
            foreach (var node in nodes)
            {
                var nodeId = $"{documentRunId}/{document.ID}/{node.Key}";
                progress.NodeId = nodeId; progress.NodeKey = node.Key;
                progress.ParentNodeIds = node.DependsOn.Select(p => $"{documentRunId}/{document.ID}/{p}").ToArray();
                progress.Mark("node_input");
                if (node.DependsOn.Any(p => !results[p].Succeeded))
                {
                    ready = false;
                    execution.Add(new { id = nodeId, key = node.Key, stepTag = StepTag(node), kind = "llm", state = "skipped",
                        parentNodeIds = node.DependsOn.Select(p => $"{documentRunId}/{document.ID}/{p}"), definitionHash = node.DefinitionHash,
                        reason = "A prerequisite failed; no provider call was made." });
                    results[node.Key] = new NodeResult(false, JsonSerializer.SerializeToElement(new { targets = Array.Empty<object>() }));
                    progress.LastResult = Capture(); progress.NodeId = null;
                    continue;
                }
                var definitions = JsonDocument.Parse(baseline.TargetsJson).RootElement.EnumerateArray()
                    .Where(t => node.Targets.Contains(t.GetProperty("tag").GetString()!, StringComparer.Ordinal)).Select(t => t.Clone()).ToArray();
                var input = JsonSerializer.Serialize(new { targets = definitions,
                    pages = baseline.Pages.OrderBy(p => p.Number).Select(p => new { page = p.Number, markdown = p.Markdown }),
                    prerequisites = node.DependsOn.Order(StringComparer.Ordinal).Select(p => new { key = p, result = results[p].Projection }) });
                var types = new LlmTypeRegistry([typeof(DocumentAnalysis)], [typeof(ContractExtractionResult)]);
                var templates = new TemplateManipulation(types, [typeof(DocumentAnalysis)], ["AIESummit.Domain"]);
                var schema = templates.GetJsonSchema(typeof(ContractExtractionResult).FullName!)
                    ?? throw new InvalidOperationException("The native response schema could not be generated.");
                var cacheKey = node.CacheKey(document.ID, input, schema);
                var requestKey = AnalysisFactory.ImprovementRequestPrefix + cacheKey;
                progress.Mark("node_cache_lookup");
                var existing = await LoadAnalysis(db).SingleOrDefaultAsync(a => a.DocumentId == document.ID && a.RequestKey == requestKey);
                var cacheHit = existing != null;
                DocumentAnalysis analysis;
                LLMPrompt prompt;
                if (existing != null)
                {
                    analysis = existing;
                    prompt = analysis.LLMPrompts.Single();
                    progress.Mark("cache_identity", "cache_identity_mismatch");
                    if (analysis.InputHash != WorkflowDefinition.HashText(input) || prompt.Prompt != node.Prompt + "\n" + input
                        || prompt.SystemPrompt != node.SystemPrompt || prompt.ExpectedResponseJsonFormat != schema
                        || prompt.Temperature != node.Temperature / 100.0 || (prompt.ReasoningEffort ?? "") != node.ReasoningEffort
                        || prompt.UsedModel?.Name != node.Model || prompt.UsedModel.Provider != LLMAPIProvider.Meta)
                        throw new InvalidDataException("Retained node content no longer matches its experiment identity.");
                }
                else
                {
                    if (cacheOnly)
                    {
                        // A diagnostic replay must never poison the reusable identity with an artificial failure.
                        ready = false;
                        execution.Add(new { id = nodeId, key = node.Key, stepTag = StepTag(node), kind = "llm", state = "cache_miss",
                            parentNodeIds = node.DependsOn.Select(p => $"{documentRunId}/{document.ID}/{p}"),
                            definitionHash = node.DefinitionHash, cacheKey, reason = "No retained node execution; cache-only forbids provider access." });
                        results[node.Key] = new NodeResult(false, JsonSerializer.SerializeToElement(new { targets = Array.Empty<object>() }));
                        progress.LastResult = Capture(); progress.NodeId = null;
                        continue;
                    }
                    // Keep intermediate framework save states invisible to the ordinary queue worker.
                    await using var transaction = await db.Database.BeginTransactionAsync();
                    progress.Mark("step_configuration");
                    var model = await db.LLMModels.SingleOrDefaultAsync(m => m.Provider == LLMAPIProvider.Meta && m.Name == node.Model)
                        ?? throw new InvalidOperationException("The selected Muse model must be configured in the existing application database.");
                    var stepTag = StepTag(node);
                    var step = await db.LLMSteps.SingleOrDefaultAsync(s => s.Tag == stepTag);
                    if (step == null)
                    {
                        step = ContractExtractionStep.Definition(); step.Tag = stepTag; step.Name = "Improvement · " + node.Key;
                        step.Enabled = false; step.Lambda = "o => false"; step.Model = model;
                        step.Template = node.Prompt; step.SystemRolePromptTemplate = node.SystemPrompt;
                        step.Temperature = node.Temperature; step.ReasoningEffort = node.ReasoningEffort;
                        db.LLMSteps.Add(step); await db.SaveChangesAsync();
                    }
                    var registry = new LlmCodeStepRegistry(types, [step]);
                    analysis = new DocumentAnalysis { Document = document, RequestKey = requestKey,
                        InputHash = WorkflowDefinition.HashText(input), TargetsJson = JsonSerializer.Serialize(definitions),
                        LLMStepStatus = StepProcessingStatus.New,
                        Pages = baseline.Pages.Select(p => new PageTextRevision { OcrPageId = p.OcrPageId, Number = p.Number,
                            Markdown = p.Markdown, ContentHash = p.ContentHash }).ToList(),
                        Targets = baseline.Targets.Where(t => node.Targets.Contains(t.ExtractionType.Tag)).Select(t => new AnalysisTargetResult {
                            ExtractionTypeId = t.ExtractionTypeId }).ToList() };
                    db.DocumentAnalyses.Add(analysis); await db.SaveChangesAsync();
                    // Prompt inputs are plain data, assembled without evaluating candidate-authored Razor/code.
                    prompt = new LLMPrompt { LLMStep = step, LLMStep_ID = step.ID, DocumentType = nameof(DocumentAnalysis),
                        DocumentID = analysis.ID, DTOResponseType = typeof(ContractExtractionResult).FullName!,
                        Prompt = node.Prompt + "\n" + input, SystemPrompt = node.SystemPrompt,
                        ExpectedResponseJsonFormat = schema, Temperature = node.Temperature / 100.0,
                        PreferedModel = model, PreferedModel_ID = model.ID, ReasoningEffort = node.ReasoningEffort,
                        ProcessingStatus = PromptProcessingStatus.New };
                    analysis.LLMPrompts.Add(prompt); await db.SaveChangesAsync();
                    var worker = new LlmPromptWorker(null!, new InstrumentedPromptPolicy(),
                        new LlmProviderFactory(services.GetRequiredService<IHttpClientFactory>(), services.GetRequiredService<IConfiguration>()),
                        types, new LambdaManipulation(), registry, services.GetRequiredService<IMemoryCache>());
                    progress.Mark("native_execution"); progress.ProviderStarted = true;
                    await worker.ExecuteAsync(prompt, services.GetRequiredService<IBasePersistenceRepository<LLMPrompt>>(),
                        new LlmPromptExecutionOptions { MD5HashCheck = false, CacheOnly = cacheOnly });
                    analysis.LLMStepStatus = analysis.CompletedAt != null ? StepProcessingStatus.Processed : StepProcessingStatus.PermanentError;
                    if (analysis.CompletedAt == null) analysis.Error ??= "Native extraction failed; inspect its retained prompt.";
                    await db.SaveChangesAsync();
                    await transaction.CommitAsync();
                    progress.Mark("node_result");
                    analysis = await LoadAnalysis(db).SingleAsync(a => a.ID == analysis.ID);
                }
                var succeeded = analysis.CompletedAt != null;
                ready &= succeeded;
                var projected = ProjectAnalysis(analysis);
                analyses.Add(projected);
                results[node.Key] = new NodeResult(succeeded, JsonSerializer.SerializeToElement(new {
                    targets = JsonSerializer.SerializeToElement(projected, Json).GetProperty("targets") }));
                var tokensAcrossAttempts = Math.Max(prompt.Tokens, prompt.AccumulatedTokens);
                logicalInput += prompt.InputTokens; logicalOutput += prompt.OutputTokens; logicalTotal += tokensAcrossAttempts;
                var promptBreakdownAvailable = InstrumentedPromptPolicy.BreakdownAvailable(prompt);
                var promptTotalAvailable = InstrumentedPromptPolicy.TotalAvailable(prompt);
                totalAvailable &= promptTotalAvailable;
                if (!cacheHit) {
                    newInput += prompt.InputTokens; newOutput += prompt.OutputTokens; newTotal += tokensAcrossAttempts;
                    newBreakdown &= promptBreakdownAvailable; newTotalAvailable &= promptTotalAvailable;
                }
                breakdown &= promptBreakdownAvailable;
                if (promptBreakdownAvailable) { knownInputTokensSubtotal += prompt.InputTokens; knownOutputTokensSubtotal += prompt.OutputTokens; }
                else unknownUsagePromptCount++;
                var metrics = new { id = prompt.ID, documentType = prompt.DocumentType, documentID = prompt.DocumentID,
                    step = StepTag(node), stepId = prompt.LLMStep_ID, model = node.Model, modelTag = prompt.UsedModel?.Tag,
                    provider = "Meta", modelVersion = (string?)null, systemPromptHash = WorkflowDefinition.HashText(prompt.SystemPrompt),
                    inputPromptHash = WorkflowDefinition.HashText(prompt.Prompt), outputSchemaHash = WorkflowDefinition.HashText(prompt.ExpectedResponseJsonFormat),
                    generationParameters = new { temperature = prompt.Temperature, node.ReasoningEffort },
                    prompt.InputTokens, prompt.OutputTokens, prompt.Tokens, prompt.AccumulatedTokens, prompt.ApiAttemps,
                    prompt.ProcessingStatus, prompt.DateCreated, prompt.LastModified, cacheHit, sourcePromptId = prompt.ID,
                    inputOutputBreakdownAvailable = promptBreakdownAvailable,
                    incrementalInputTokens = cacheHit ? 0 : prompt.InputTokens, incrementalOutputTokens = cacheHit ? 0 : prompt.OutputTokens };
                prompts.Add(metrics);
                execution.Add(new { id = nodeId, key = node.Key, stepTag = StepTag(node), kind = "llm", state = succeeded ? "succeeded" : "failed",
                    parentNodeIds = node.DependsOn.Select(p => $"{documentRunId}/{document.ID}/{p}"), definitionHash = node.DefinitionHash,
                    cacheKey, cacheHit, sourceAnalysisId = analysis.ID, sourcePromptId = prompt.ID, prompt = metrics,
                    model = node.Model, targetDefinitions = definitions });
                progress.LastResult = Capture(); progress.ProviderStarted = false; progress.NodeId = null;
            }
        }
        return Capture();

        JsonElement Capture()
        {
            var instantiated = execution.Select(n => JsonSerializer.SerializeToElement(n, Json).GetProperty("id").GetString()).ToHashSet();
            return JsonSerializer.SerializeToElement(new { status = ready ? "ready" : "failed", uploadId, totalSeconds = sw.Elapsed.TotalSeconds,
            prediction = new { analyses }, metrics = new { prompts,
                reportedInputTokens = breakdown ? (int?)logicalInput : null, reportedOutputTokens = breakdown ? (int?)logicalOutput : null,
                reportedTotalTokens = totalAvailable ? (int?)logicalTotal : null,
                newlyConsumedInputTokens = newBreakdown ? (int?)newInput : null, newlyConsumedOutputTokens = newBreakdown ? (int?)newOutput : null,
                newlyConsumedTotalTokens = newTotalAvailable ? (int?)newTotal : null,
                inputOutputBreakdownAvailable = breakdown, newInputOutputBreakdownAvailable = newBreakdown, ocrTokens = (int?)null,
                knownInputTokensSubtotal, knownOutputTokensSubtotal, unknownUsagePromptCount,
                note = "Logical counters include reused responses. Newly consumed counters include only newly executed nodes; native counters are not billing verification." },
            graph = new { workflowHash = workflow.Hash, nodes = execution,
                edges = documents.SelectMany(d => nodes.SelectMany(n => n.DependsOn.Select(p => new { from = $"{documentRunId}/{d.ID}/{p}", to = $"{documentRunId}/{d.ID}/{n.Key}", relation = "depends_on" })))
                    .Where(e => instantiated.Contains(e.from) && instantiated.Contains(e.to)) } }, Json);
        }
    }

    internal static JsonElement FailedSample(SampleProgress progress, string exceptionType, double elapsed)
    {
        var previous = progress.LastResult;
        var metrics = previous.HasValue
            ? previous.Value.GetProperty("metrics").EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone())
            : new Dictionary<string, object?> { ["prompts"] = Array.Empty<object>(), ["reportedInputTokens"] = 0,
                ["reportedOutputTokens"] = 0, ["reportedTotalTokens"] = 0, ["newlyConsumedInputTokens"] = 0,
                ["newlyConsumedOutputTokens"] = 0, ["newlyConsumedTotalTokens"] = 0,
                ["inputOutputBreakdownAvailable"] = true, ["newInputOutputBreakdownAvailable"] = true,
                ["knownInputTokensSubtotal"] = 0, ["knownOutputTokensSubtotal"] = 0, ["unknownUsagePromptCount"] = 0 };
        if (progress.ProviderStarted)
        {
            foreach (var field in new[] { "reportedInputTokens", "reportedOutputTokens", "reportedTotalTokens",
                         "newlyConsumedInputTokens", "newlyConsumedOutputTokens", "newlyConsumedTotalTokens" }) metrics[field] = null;
            metrics["inputOutputBreakdownAvailable"] = false; metrics["newInputOutputBreakdownAvailable"] = false;
            var previousUnknown = previous?.GetProperty("metrics").GetProperty("unknownUsagePromptCount").GetInt32() ?? 0;
            metrics["unknownUsagePromptCount"] = previousUnknown + 1;
        }
        metrics["note"] = progress.ProviderStarted
            ? "Completed node results are retained. The interrupted node's provider consumption is unknown."
            : "Failure occurred before a new provider call. Completed node results and known usage are retained.";
        var graphNodes = previous?.GetProperty("graph").GetProperty("nodes").EnumerateArray().Select(n => n.Clone()).ToList() ?? [];
        var graphEdges = previous?.GetProperty("graph").GetProperty("edges").EnumerateArray().Select(n => n.Clone()).ToList() ?? [];
        if (progress.NodeId != null && !graphNodes.Any(n => n.GetProperty("id").GetString() == progress.NodeId))
        {
            graphNodes.Add(JsonSerializer.SerializeToElement(new { id = progress.NodeId, key = progress.NodeKey,
                kind = "llm", state = "runner_failed", parentNodeIds = progress.ParentNodeIds,
                errorCode = progress.ErrorCode, errorStage = progress.Stage }));
            foreach (var parent in progress.ParentNodeIds.Where(p => graphNodes.Any(n => n.GetProperty("id").GetString() == p)))
                graphEdges.Add(JsonSerializer.SerializeToElement(new { from = parent, to = progress.NodeId, relation = "depends_on" }));
        }
        return JsonSerializer.SerializeToElement(new { status = "failed", uploadId = progress.UploadId, totalSeconds = elapsed,
            error = $"Runner stopped at {progress.Stage} ({progress.ErrorCode}; {exceptionType}).",
            errorCode = progress.ErrorCode, errorStage = progress.Stage,
            prediction = previous?.GetProperty("prediction") ?? JsonSerializer.SerializeToElement(new { analyses = Array.Empty<object>() }),
            metrics, graph = new { nodes = graphNodes, edges = graphEdges } }, Json);
    }

    internal sealed class SampleProgress
    {
        public int? UploadId { get; set; }
        public string Stage { get; private set; } = "fixture_read";
        public string ErrorCode { get; private set; } = "fixture_read_failed";
        public string? NodeId { get; set; }
        public string? NodeKey { get; set; }
        public string[] ParentNodeIds { get; set; } = [];
        public bool ProviderStarted { get; set; }
        public JsonElement? LastResult { get; set; }
        public void Mark(string stage, string? code = null) { Stage = stage; ErrorCode = code ?? stage + "_failed"; }
    }

    private static IQueryable<DocumentAnalysis> LoadAnalysis(AIESummitDb db) => db.DocumentAnalyses.Include(a => a.Document)
        .Include(a => a.Pages).Include(a => a.LLMPrompts).ThenInclude(p => p.UsedModel)
        .Include(a => a.Targets).ThenInclude(t => t.ExtractionType)
        .Include(a => a.Targets).ThenInclude(t => t.Extractions).ThenInclude(e => e.Evidence).AsSplitQuery();

    private static object ProjectAnalysis(DocumentAnalysis a)
    {
        object[] reported;
        try
        {
            using var response = JsonDocument.Parse(a.LLMPrompts.Single().Response);
            reported = response.RootElement.GetProperty("targets").EnumerateArray().Select(t => (object)new {
                tag = t.GetProperty("tag").GetString(), outcome = t.GetProperty("outcome").GetString(), evidenceValidated = false }).ToArray();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException) { reported = []; }
        return new { id = a.ID, documentId = a.DocumentId, isCurrent = true, a.RequestKey,
            status = a.CompletedAt != null ? "ready" : "failed", a.Error, definitions = JsonSerializer.Deserialize<JsonElement>(a.TargetsJson),
            modelReportedOutcomes = reported, targets = a.Targets.Select(t => new { tag = t.ExtractionType.Tag, t.Outcome, t.Explanation,
                extractions = t.Extractions.Select(e => new { id = e.ID, e.Title, e.Summary, e.SchemaVersion,
                    values = JsonSerializer.Deserialize<JsonElement>(e.ValuesJson), evidence = e.Evidence.Select(v => new {
                        page = a.Pages.Single(p => p.ID == v.PageTextRevisionId).Number, v.Quote, v.ModelQuote,
                        v.StartOffset, v.EndOffset, v.MatchMethod, v.MatchScore, v.Role,
                        supportedFields = JsonSerializer.Deserialize<string[]>(v.SupportedFieldsJson) }) }) }) };
    }

    private sealed record NodeResult(bool Succeeded, JsonElement Projection);
}
