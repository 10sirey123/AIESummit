using System.Security.Claims;
using System.Text.Json;
using AIESummit.Agents;
using AIESummit.Api;
using AIESummit.Domain;
using Blueshift.Domain.Abstract;
using Blueshift.Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace AIESummit.Extraction.Tests;

public sealed class ClientReadEndpointTests
{
    [ExtractionDatabaseFact]
    public async Task SummaryIsOwnerScopedAndSelectsAttemptsWithoutReturningPromptOrDocumentText()
    {
        var connection = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("AIESUMMIT_EXTRACTION_TEST_CONNECTION")!);
        Assert.Equal("aiesummit_extraction_test", connection.Database);
        var schema = "client_api_" + Guid.NewGuid().ToString("N");
        connection.SearchPath = schema;
        await using var db = new AIESummitDb(new DbContextOptionsBuilder<AIESummitDb>().UseNpgsql(connection.ConnectionString).Options);
        await using (var sql = new NpgsqlConnection(connection.ConnectionString))
        {
            await sql.OpenAsync();
            // A unique test-only identifier, quoted as an identifier rather than interpolated as a SQL value.
            await using var command = new NpgsqlCommand("CREATE SCHEMA " + new NpgsqlCommandBuilder().QuoteIdentifier(schema), sql);
            await command.ExecuteNonQueryAsync();
        }
        await db.Database.ExecuteSqlRawAsync(db.Database.GenerateCreateScript());
        var upload = new UploadedFile { OwnerId = "owner", FileName = "synthetic.pdf", Source = [1],
            OcrStatus = ProcessedStatus.Processed, LLMStepStatus = StepProcessingStatus.Processed };
        var document = new Document { Upload = upload, Title = "Agreement", FirstPage = 1, LastPage = 1, Markdown = "private-document-sentinel" };
        var older = new DocumentAnalysis { Document = document, RequestKey = "initial", CompletedAt = DateTime.UtcNow,
            LLMStepStatus = StepProcessingStatus.Processed };
        db.AddRange(upload, document, older); db.ExtractionTypes.AddRange(ExtractionCatalog.Defaults());
        var step = ContractExtractionStep.Definition();
        step.Model = new LLMModel { Tag = "client-read-test", Name = "synthetic", Provider = LLMAPIProvider.OpenAI };
        db.LLMSteps.Add(step); await db.SaveChangesAsync();
        var latest = new DocumentAnalysis { Document = document, RequestKey = "retry", Error = "Rejected evidence",
            LLMStepStatus = StepProcessingStatus.PermanentError };
        db.DocumentAnalyses.Add(latest); await db.SaveChangesAsync();
        // A newer successful experiment must not replace the ordinary reader's failed/latest selection.
        var experimental = new DocumentAnalysis { Document = document,
            RequestKey = AnalysisFactory.ImprovementRequestPrefix + "synthetic-node-cache-key",
            CompletedAt = DateTime.UtcNow, LLMStepStatus = StepProcessingStatus.Processed };
        db.DocumentAnalyses.Add(experimental); await db.SaveChangesAsync();
        ClaimsPrincipal User(string sub) => new(new ClaimsIdentity([new Claim("sub", sub)], "test"));
        Assert.IsType<NotFound>(await ExtractionReadEndpoints.Summary(upload.ID, User("other"), db, default));
        var result = await ExtractionReadEndpoints.Summary(upload.ID, User("owner"), db, default);
        var json = JsonSerializer.SerializeToElement(((IValueHttpResult)result).Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal("failed", json.GetProperty("extraction").GetProperty("status").GetString());
        Assert.False(json.GetProperty("extraction").GetProperty("canPoll").GetBoolean());
        var selection = json.GetProperty("documents")[0].GetProperty("analysis");
        Assert.Equal(latest.ID, selection.GetProperty("latestAnalysisId").GetInt32());
        Assert.Equal(older.ID, selection.GetProperty("lastSuccessfulAnalysisId").GetInt32());
        Assert.DoesNotContain("private-document-sentinel", json.GetRawText());
        Assert.DoesNotContain("llmPrompts", json.GetRawText());
        latest.Error = null; latest.LLMStepStatus = StepProcessingStatus.CheckForPrompts; step.Enabled = false;
        await db.SaveChangesAsync();
        result = await ExtractionReadEndpoints.Summary(upload.ID, User("owner"), db, default);
        json = JsonSerializer.SerializeToElement(((IValueHttpResult)result).Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal("blocked", json.GetProperty("extraction").GetProperty("status").GetString());
        var catalog = await ExtractionReadEndpoints.Catalog(db, default);
        json = JsonSerializer.SerializeToElement(((IValueHttpResult)catalog).Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(10, json.GetArrayLength());
        Assert.All(json.EnumerateArray(), type => Assert.True(type.GetProperty("schemaSupported").GetBoolean()));
    }
}
