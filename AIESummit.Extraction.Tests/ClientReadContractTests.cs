using System.Text.Json;
using System.Text.Json.Nodes;
using AIESummit.Api;
using AIESummit.Domain;
using Blueshift.Domain.Entities;
using Xunit;

namespace AIESummit.Extraction.Tests;

public sealed class ClientReadContractTests
{
    [Fact]
    public void PublishedPerTargetSchemasMatchRuntimeCatalogShapes()
    {
        using var stream = typeof(ClientReadContractTests).Assembly.GetManifestResourceStream("AIESummit.Extraction.Tests.ClientExtractionSchemas.json")!;
        using var reader = new StreamReader(stream);
        var components = JsonNode.Parse(reader.ReadToEnd())!;
        var mappings = components["ExtractionFinding"]!["properties"]!["values"]!["x-target-value-schemas"]!;
        foreach (var type in ExtractionCatalog.Defaults())
        {
            var name = mappings[type.Tag]!["1"]!.GetValue<string>().Split('/').Last();
            var expected = components[name]!;
            var actual = ExtractionReadModel.Describe(type).ValueSchema!;
            Assert.Equal(expected["properties"]!.AsObject().Count, actual["properties"]!.AsObject().Count);
            foreach (var property in actual["properties"]!.AsObject())
            {
                var shape = property.Value!.DeepClone().AsObject(); shape.Remove("description");
                Assert.True(JsonNode.DeepEquals(expected["properties"]![property.Key], shape), type.Tag + "/" + property.Key);
            }
            Assert.True(JsonNode.DeepEquals(expected["oneOf"], actual["oneOf"]));
        }
    }
    [Fact]
    public void EverySeededTypeExposesItsExactAllowedFieldsAndSupportedValueSchema()
    {
        foreach (var type in ExtractionCatalog.Defaults())
        {
            var contract = ExtractionReadModel.Describe(type);
            Assert.True(contract.SchemaSupported);
            Assert.Equal("openapi-3.0", contract.ValueSchemaDialect);
            Assert.Equal(JsonSerializer.Deserialize<string[]>(type.ParametersJson), contract.Parameters.Select(p => p.Name));
            Assert.Equal(contract.Parameters.Length, contract.ValueSchema!["properties"]!.AsObject().Count);
            Assert.False(contract.ValueSchema["additionalProperties"]!.GetValue<bool>());
        }
    }

    [Fact]
    public void ValueSchemasPreserveDateCurrencyAndPairedDurationSemantics()
    {
        var date = ExtractionReadModel.Describe(ExtractionCatalog.Defaults()[1]);
        Assert.Equal("date", date.ValueSchema!["properties"]!["date"]!["format"]!.GetValue<string>());
        Assert.NotNull(date.ValueSchema["oneOf"]);
        var cap = ExtractionReadModel.Describe(ExtractionCatalog.Defaults()[6]);
        Assert.Equal("^[A-Z]{3}$", cap.ValueSchema!["properties"]!["currency"]!["pattern"]!.GetValue<string>());
        Assert.Equal(0m, cap.ValueSchema["properties"]!["amount"]!["minimum"]!.GetValue<decimal>());
        Assert.True(cap.ValueSchema["properties"]!["duration"]!["exclusiveMinimum"]!.GetValue<bool>());
    }

    [Theory]
    [InlineData("invalid-json", 1)]
    [InlineData("[\"newField\"]", 1)]
    [InlineData("[\"date\",\"date\"]", 1)]
    [InlineData("[\"date\"]", 2)]
    public void UnknownVersionsAndMalformedDefinitionsNeverAdvertiseASupportedSchema(string fields, int version)
    {
        var type = new ExtractionType { ParametersJson = fields, DefinitionVersion = version };
        var result = ExtractionReadModel.Describe(type);
        Assert.False(result.SchemaSupported); Assert.Null(result.ValueSchema);
        Assert.Equal(fields, result.ParametersJson);
    }

    [Fact]
    public void SelectionKeepsLatestFailureSeparateFromLastSuccessAndOtherDocuments()
    {
        var result = ExtractionReadModel.Select(10, [
            new(1, 10, DateTime.UtcNow, null, StepProcessingStatus.Processed, true),
            new(2, 10, null, "Rejected evidence", StepProcessingStatus.PermanentError, true),
            new(3, 20, DateTime.UtcNow, null, StepProcessingStatus.Processed, true)
        ], true);
        Assert.Equal(2, result.LatestAnalysisId); Assert.Equal(1, result.LastSuccessfulAnalysisId);
        Assert.Equal("failed", result.Status); Assert.False(result.CanPoll);
    }

    [Fact]
    public void NoResultAndDisabledNoPromptRunsStopPollingWithoutInventingAbsence()
    {
        var processed = ExtractionReadModel.Select(10, [new(1, 10, null, null, StepProcessingStatus.Processed, false)], true);
        Assert.Equal("completed_without_result", processed.Reason); Assert.False(processed.CanPoll);
        var blocked = ExtractionReadModel.Select(10, [new(1, 10, null, null, StepProcessingStatus.CheckForPrompts, false)], false);
        Assert.Equal("blocked", blocked.Status); Assert.False(blocked.CanPoll);
        var active = ExtractionReadModel.Select(10, [new(1, 10, null, null, StepProcessingStatus.CheckForPrompts, true)], false);
        Assert.Equal("analyzing", active.Status); Assert.True(active.CanPoll);
        Assert.Equal("not_started", ExtractionReadModel.Select(10, [], true).Status);
    }

    [Fact]
    public void AggregateDistinguishesPendingPartialFailureAndMissingWork()
    {
        var ready = new DocumentAnalysisSelection(1, 1, 1, "ready", null, false);
        var failed = new DocumentAnalysisSelection(2, 2, null, "failed", "analysis_failed", false);
        var missing = new DocumentAnalysisSelection(3, null, null, "not_started", "analysis_not_requested", false);
        Assert.Equal("partial_failure", ExtractionReadModel.Aggregate([ready, failed], false, true));
        Assert.Equal("incomplete", ExtractionReadModel.Aggregate([ready, missing], false, true));
        Assert.Equal("analyzing", ExtractionReadModel.Aggregate([ready, failed with { Status = "analyzing", CanPoll = true }], false, true));
        Assert.Equal("waiting_for_documents", ExtractionReadModel.Aggregate([], false, false));
        Assert.Equal("failed", ExtractionReadModel.Aggregate([], false, true));
    }
}
