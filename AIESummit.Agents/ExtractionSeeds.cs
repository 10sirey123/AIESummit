using AIESummit.Domain;
using Blueshift.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIESummit.Agents;

public static class ExtractionSeeds
{
    // Caller holds the same advisory transaction lock as Bootstrap. Never updates existing rows.
    public static async Task EnsureAsync(AIESummitDb db, LLMModel model)
    {
        foreach (var type in ExtractionCatalog.Defaults())
            if (!await db.ExtractionTypes.AnyAsync(t => t.Tag == type.Tag)) db.ExtractionTypes.Add(type);
        if (!await db.LLMSteps.AnyAsync(s => s.Tag == ContractExtractionStep.Tag))
        {
            var step = ContractExtractionStep.Definition(); step.Model = model;
            db.LLMSteps.Add(step);
        }
    }
}
