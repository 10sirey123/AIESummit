using System.Text.Json;
using AIESummit.Configuration;
using AIESummit.Domain;
using Microsoft.EntityFrameworkCore;

try
{
    if (args.Length is 2 or 3 && args[0] == "export" && (args.Length == 2 || args[2] == "--include-bindings"))
    {
        var output = args[1];
        var manifest = WorkflowSeeds.Create(args.Length == 3);
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(new { status = "exported", records = manifest.Records.Count, includeBindings = args.Length == 3 }));
        return 0;
    }
    if (args is ["schema"])
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__AIESummitDb")
            ?? throw new InvalidOperationException();
        await using var db = new AIESummitDb(new DbContextOptionsBuilder<AIESummitDb>().UseNpgsql(connection).Options);
        // Refuse an empty/wrong database; normal application bootstrap creates the initial schema.
        await db.Uploads.Take(0).ToListAsync();
        await SeedSchema.ApplyAsync(db);
        Console.WriteLine("{\"status\":\"upgraded\",\"scope\":\"seed-registry-only\"}");
        return 0;
    }
    Console.Error.WriteLine("Use: export <manifest.json> [--include-bindings] | schema");
    return 2;
}
catch
{
    Console.Error.WriteLine("Configuration operation failed. Check the output directory, database connection and initialized application schema. No submitted values are included.");
    return 1;
}
