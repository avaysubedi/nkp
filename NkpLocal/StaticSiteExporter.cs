using System.Text.Encodings.Web;
using System.Text.Json;

static class StaticSiteExporter
{
    public const int PageSize = 200;

    public static async Task ExportAsync(DatabaseHelper database)
    {
        var wwwroot = FindWwwroot();
        var dataDir = Path.Combine(wwwroot, "data");
        Directory.CreateDirectory(dataDir);

        foreach (var old in Directory.GetFiles(dataDir, "judgments-*.json"))
            File.Delete(old);

        var generatedAt = DateTime.UtcNow.ToString("o");
        var stats = await database.GetStatsAsync();
        var statsJson = JsonSerializer.Serialize(stats);
        using var statsDoc = JsonDocument.Parse(statsJson);
        var total = statsDoc.RootElement.TryGetProperty("TotalJudgments", out var totalEl)
            ? totalEl.GetInt64()
            : statsDoc.RootElement.GetProperty("totalJudgments").GetInt64();

        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        if (total == 0)
            totalPages = 0;

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        for (var page = 1; page <= Math.Max(totalPages, 1); page++)
        {
            if (total == 0 && page > 1)
                break;

            var (items, _) = await database.ExportJudgmentsAsync(page, PageSize);
            var payload = new
            {
                Total = total,
                Page = page,
                PageSize = PageSize,
                TotalPages = Math.Max(totalPages, 1),
                GeneratedAt = generatedAt,
                Judgments = items
            };

            var pagePath = Path.Combine(dataDir, $"judgments-{page}.json");
            await File.WriteAllTextAsync(pagePath, JsonSerializer.Serialize(payload, jsonOptions));

            if (items.Count == 0)
                break;
        }

        var manifest = new
        {
            GeneratedAt = generatedAt,
            Total = total,
            PageSize = PageSize,
            TotalPages = totalPages,
            Stats = stats
        };

        await File.WriteAllTextAsync(
            Path.Combine(dataDir, "manifest.json"),
            JsonSerializer.Serialize(manifest, jsonOptions));

        Console.WriteLine($"Static JSON exported: {total} judgments, {totalPages} page(s)");
        Console.WriteLine($"  {dataDir}");
    }

    private static string FindWwwroot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(start);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "NkpLoader.csproj")))
                    return Path.Combine(dir.FullName, "wwwroot");

                if (File.Exists(Path.Combine(dir.FullName, "NkpLocal", "NkpLoader.csproj")))
                    return Path.Combine(dir.FullName, "NkpLocal", "wwwroot");

                dir = dir.Parent;
            }
        }

        throw new DirectoryNotFoundException("Could not find wwwroot (NkpLoader.csproj not found).");
    }
}
