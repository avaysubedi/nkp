using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

class Program
{
    static async Task Main(string[] args)
    {
        var database = new DatabaseHelper("nkp.db");
        await database.InitializeAsync();

        if (args.Length > 0 && args.Any(a => a.Equals("inspect", StringComparison.OrdinalIgnoreCase) || a.Equals("check", StringComparison.OrdinalIgnoreCase)))
        {
            await database.PrintDatabaseSummaryAsync();
            return;
        }

        if (args.Length > 0 && args.Any(a => a.Equals("export", StringComparison.OrdinalIgnoreCase)))
        {
            await StaticSiteExporter.ExportAsync(database);
            return;
        }

        if (args.Length > 0 && args.Any(a =>
                a.Equals("scrape", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("resume", StringComparison.OrdinalIgnoreCase)))
        {
            await RunScraperAsync(database);
            await StaticSiteExporter.ExportAsync(database);
            return;
        }

        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddSingleton(database);
        builder.Services.AddHttpClient("nkp", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(2);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/142.0 Safari/537.36");
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All
        });
        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
                policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
        });

        var app = builder.Build();

        app.UseCors();
        app.UseDefaultFiles();
        app.UseStaticFiles();

        app.MapGet("/api/judgments", async (DatabaseHelper db, string? q, string? court, string? category, string? topic, int page = 1, int pageSize = 20) =>
        {
            var (items, total) = await db.SearchJudgmentsAsync(q, court, category, topic, page, pageSize);
            return Results.Ok(new
            {
                Total = total,
                Page = page,
                PageSize = pageSize,
                TotalPages = (int)Math.Ceiling((double)total / pageSize),
                Judgments = items
            });
        });

        app.MapGet("/api/judgments/{id:long}", async (DatabaseHelper db, IHttpClientFactory httpFactory, long id, bool hydrate = false) =>
        {
            var judgment = await db.GetJudgmentByIdAsync(id);
            if (judgment == null)
                return Results.NotFound(new { Message = "Judgment not found" });

            if (hydrate && string.IsNullOrWhiteSpace(judgment.FullText) && !string.IsNullOrWhiteSpace(judgment.DetailUrl))
            {
                try
                {
                    var http = httpFactory.CreateClient("nkp");
                    var html = await http.GetStringAsync(judgment.DetailUrl);
                    var parsed = new NkpParser().ParseJudgmentDetail(html, judgment);
                    if (!string.IsNullOrWhiteSpace(parsed.FullText))
                    {
                        parsed.ScrapeStatus = "SUCCESS";
                        CaseClassifier.Apply(parsed);
                        await db.UpsertJudgmentAsync(parsed);
                        judgment = await db.GetJudgmentByIdAsync(id) ?? parsed;
                    }
                }
                catch
                {
                    // Keep the listing/summary record if NKP is unreachable.
                }
            }

            return Results.Ok(judgment);
        });

        app.MapGet("/api/stats", async (DatabaseHelper db) =>
        {
            return Results.Ok(await db.GetStatsAsync());
        });

        app.MapGet("/api/export", async (DatabaseHelper db, int page = 1, int pageSize = 40) =>
        {
            var (items, total) = await db.ExportJudgmentsAsync(page, pageSize);
            return Results.Ok(new
            {
                Total = total,
                Page = page,
                PageSize = pageSize,
                TotalPages = Math.Max(1, (int)Math.Ceiling((double)total / pageSize)),
                Judgments = items
            });
        });

        var localIp = GetLocalIpAddress();

        Console.WriteLine("\n==================================================");
        Console.WriteLine("  NKP LAW JUDGMENTS WEB APP & API IS RUNNING      ");
        Console.WriteLine("==================================================");
        Console.WriteLine($"  Local PC UI : http://localhost:5000              ");
        Console.WriteLine($"  Mobile UI   : http://{localIp}:5000              ");
        Console.WriteLine($"  API Status  : http://{localIp}:5000/api/stats    ");
        Console.WriteLine($"  Publish JSON: dotnet run -- export  (then git push)");
        Console.WriteLine("==================================================\n");

        await app.RunAsync("http://0.0.0.0:5000");
    }

    private static string GetLocalIpAddress()
    {
        try
        {
            using var socket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530);
            var endPoint = socket.LocalEndPoint as System.Net.IPEndPoint;
            return endPoint?.Address.ToString() ?? "localhost";
        }
        catch
        {
            return "localhost";
        }
    }

    private static readonly (string TypeId, string TypeName, string NameId, string Topic, string Category)[] MajorMuddaNames =
    {
        ("4", "सरकारवादी फौजदारी", "131", "कर्तव्य ज्यान", "फौजदारी"),
        ("4", "सरकारवादी फौजदारी", "135", "कर्तव्य ज्यान", "फौजदारी"),
        ("4", "सरकारवादी फौजदारी", "133", "सवारी ज्यान", "फौजदारी"),
        ("4", "सरकारवादी फौजदारी", "46", "सवारी अङ्गभङ्ग", "फौजदारी"),
        ("4", "सरकारवादी फौजदारी", "101", "लागु औषध", "फौजदारी"),
        ("4", "सरकारवादी फौजदारी", "137", "जबरजस्ती करणी", "फौजदारी"),
        ("4", "सरकारवादी फौजदारी", "94", "भ्रष्टाचार", "फौजदारी"),
        ("5", "रिट", "125", "उत्प्रेषण", "रिट"),
        ("5", "रिट", "428", "उत्प्रेषण", "रिट"),
        ("5", "रिट", "127", "परमादेश", "रिट"),
        ("5", "रिट", "126", "निषेधाज्ञा", "रिट"),
        ("5", "रिट", "129", "बन्दी प्रत्यक्षीकरण", "रिट"),
        ("1", "दुनियाबादी देवानी", "1", "अंश", "देवानी"),
        ("1", "दुनियाबादी देवानी", "437", "अंश चलन", "देवानी"),
        ("1", "दुनियाबादी देवानी", "314", "अंश जालसाजी", "देवानी"),
        ("1", "दुनियाबादी देवानी", "438", "अंश नामसारी", "देवानी"),
        ("1", "दुनियाबादी देवानी", "186", "अंशबन्डा", "देवानी")
    };

    private static readonly HashSet<string> NewNameIdsThisRun = new(StringComparer.Ordinal)
    {
        "129", "127", "126", "428", "437", "314", "438", "186"
    };

    private static async Task RunScraperAsync(DatabaseHelper database)
    {
        Console.WriteLine("Listing scrape by mudda_name IDs (faisala 2074/01/01–2083/03/31).");
        var toScrape = MajorMuddaNames.Where(item => NewNameIdsThisRun.Contains(item.NameId)).ToArray();
        foreach (var item in toScrape)
            Console.WriteLine($"  type={item.TypeId} name={item.NameId}  {item.Topic}  ({item.TypeName})");

        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All
        };

        using var http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(2)
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
            "AppleWebKit/537.36 Chrome/142.0 Safari/537.36"
        );

        var parser = new NkpParser();
        var saved = 0;
        var failed = 0;

        foreach (var item in toScrape)
        {
            Console.WriteLine($"\n########## mudda_name={item.NameId} {item.Topic} ##########");
            var (typeSaved, typeFailed) = await ScrapeMuddaTypeAsync(
                database, http, parser, item.TypeId, item.TypeName, item.Category, item.NameId, item.Topic);
            saved += typeSaved;
            failed += typeFailed;
        }

        var dbCount = await database.GetJudgmentCountAsync();
        Console.WriteLine("\nListing ingestion completed.");
        Console.WriteLine($"Saved this run: {saved}, Failed: {failed}, In DB: {dbCount}");
    }

    private static async Task<(int Saved, int Failed)> ScrapeMuddaTypeAsync(
        DatabaseHelper database,
        HttpClient http,
        NkpParser parser,
        string muddaTypeId,
        string muddaTypeName,
        string category,
        string muddaNameId,
        string topicLabel)
    {
        const int pageSize = 20;
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pageNumber = 1;
        var offset = 0;
        var totalResults = 0;
        var listed = 0;
        var saved = 0;
        var failed = 0;
        var label = $"{topicLabel} [{muddaTypeName}]";

        while (true)
        {
            var currentUrl = BuildSearchUrl(offset, muddaTypeId, muddaNameId);
            Console.WriteLine($"\n========== {label} PAGE {pageNumber} (offset {offset}) ==========");
            Console.WriteLine($"Downloading: {currentUrl}");

            SearchResult result;
            try
            {
                var html = await http.GetStringAsync(currentUrl);
                Console.WriteLine($"Downloaded: {html.Length:N0} characters");
                result = parser.ParseSearchResults(html, currentUrl);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nERROR downloading search page: {ex.Message}");
                break;
            }

            if (result.TotalResults > 0)
                totalResults = result.TotalResults;

            if (result.TotalResults == 0 &&
                result.Judgments.Count > 0 &&
                result.Judgments.All(j => string.IsNullOrWhiteSpace(j.Summary)))
            {
                Console.WriteLine($"No listing articles for {label}; skipping junk page.");
                break;
            }

            var newOnPage = new List<Judgment>();
            foreach (var judgment in result.Judgments)
            {
                if (string.IsNullOrWhiteSpace(judgment.DetailUrl) || !seenUrls.Add(judgment.DetailUrl))
                    continue;
                newOnPage.Add(judgment);
            }

            listed += newOnPage.Count;
            Console.WriteLine($"Page {pageNumber}: {newOnPage.Count} new, {listed} / {totalResults} for {label}");

            foreach (var judgment in newOnPage)
            {
                CaseClassifier.Apply(judgment);
                judgment.MuddaType = muddaTypeName;
                judgment.Category = category;
                var topics = (judgment.Topics ?? "")
                    .Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .ToHashSet(StringComparer.Ordinal);
                topics.Add(topicLabel);
                if (muddaNameId == "428")
                {
                    topics.Add("उत्प्रेषण");
                    topics.Add("परमादेश");
                }
                if (muddaNameId == "129")
                    topics.Add("बन्दीप्रत्यक्षीकरण");
                judgment.Topics = string.Join(" | ", topics);
                judgment.SourceUrl = currentUrl;
                judgment.ScrapeStatus = string.IsNullOrWhiteSpace(judgment.FullText) ? "LISTING" : "SUCCESS";
                try
                {
                    await database.UpsertJudgmentAsync(judgment);
                    saved++;
                    Console.WriteLine($"  {judgment.DecisionNumber} | {judgment.CaseName} | {topicLabel} | summary {judgment.Summary?.Length ?? 0} chars");
                }
                catch (Exception ex)
                {
                    failed++;
                    Console.WriteLine($"  ERROR saving {judgment.DetailUrl}: {ex.Message}");
                }
            }

            if (newOnPage.Count == 0)
            {
                Console.WriteLine($"No new URLs for {label}; next name.");
                break;
            }

            offset += pageSize;
            if (totalResults > 0 && offset >= totalResults)
            {
                Console.WriteLine($"Reached NKP total for {label} ({totalResults}).");
                break;
            }

            if (pageNumber >= 500)
                break;

            pageNumber++;
            await Task.Delay(250);
        }

        return (saved, failed);
    }

    private static string BuildSearchUrl(int offset, string muddaTypeId, string muddaNameId)
    {
        var query =
            "mudda_number=&" +
            "faisala_date_from=2074%2F01%2F01&" +
            "faisala_date_to=2083%2F03%2F31&" +
            "mudda_type=" + muddaTypeId + "&" +
            "mudda_name=" + muddaNameId + "&" +
            "badi=&" +
            "pratibadi=&" +
            "judge=&" +
            "ijlas_type=&" +
            "nirnaya_number=&" +
            "faisala_type=&" +
            "keywords=&" +
            "edition=&" +
            "year=&" +
            "month=&" +
            "volume=&" +
            "Submit=%E0%A4%96%E0%A5%8B%E0%A4%9C%E0%A5%8D%E2%80%8D%E0%A4%A8%E0%A5%81%E0%A4%B9%E0%A5%8B%E0%A4%B8%E0%A5%8D";

        if (offset <= 0)
            return "https://nkp.gov.np/?" + query;

        return "https://nkp.gov.np/advance_search/?" + query + "&per_page=" + offset;
    }
}