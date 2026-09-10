using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

public class DatabaseHelper
{
    private readonly string _connectionString;

    public DatabaseHelper(string dbPath = "nkp.db")
    {
        _connectionString =
            $"Data Source={dbPath};Cache=Shared";
    }

    private SqliteConnection CreateConnection()
    {
        return new SqliteConnection(
            _connectionString);
    }

    private static readonly string[] OfficialTopicLabels =
    {
        "कर्तव्य ज्यान", "सवारी ज्यान", "सवारी अङ्गभङ्ग", "लागु औषध",
        "जबरजस्ती करणी", "भ्रष्टाचार",
        "उत्प्रेषण", "परमादेश", "निषेधाज्ञा", "बन्दी प्रत्यक्षीकरण", "बन्दीप्रत्यक्षीकरण",
        "अंश चलन", "अंश जालसाजी", "अंश नामसारी", "अंशबन्डा", "अंश",
        "निज अर्जन", "सगोल"
    };

    private const string DecisionDateSortSql = """
        REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
            IFNULL(NULLIF(TRIM(DecisionDate), ''), DecisionDateNepali),
        '०','0'),'१','1'),'२','2'),'३','3'),'४','4'),'५','5'),'६','6'),'७','7'),'८','8'),'९','9')
        """;

    // ========================================================
    // INITIALIZE DATABASE
    // ========================================================

    public async Task InitializeAsync()
    {
        await using var connection =
            CreateConnection();

        await connection.OpenAsync();

        var command =
            connection.CreateCommand();

        command.CommandText = """
        CREATE TABLE IF NOT EXISTS Judgments
        (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,

            DecisionNumber TEXT,
            CaseName TEXT,

            DetailUrl TEXT NOT NULL UNIQUE,
            SourceUrl TEXT,

            Volume TEXT,
            Year TEXT,
            Month TEXT,
            Edition TEXT,

            DecisionDate TEXT,
            DecisionDateNepali TEXT,

            CaseNumber TEXT,
            Court TEXT,
            Judges TEXT,

            Petitioner TEXT,
            Respondent TEXT,

            Laws TEXT,
            Precedents TEXT,

            Summary TEXT,
            FullText TEXT,

            ScrapeStatus TEXT,
            ScrapedAt TEXT,
            UpdatedAt TEXT
        );

        CREATE INDEX IF NOT EXISTS IX_Judgments_DecisionNumber
            ON Judgments(DecisionNumber);

        CREATE INDEX IF NOT EXISTS IX_Judgments_CaseNumber
            ON Judgments(CaseNumber);

        CREATE INDEX IF NOT EXISTS IX_Judgments_CaseName
            ON Judgments(CaseName);

        CREATE INDEX IF NOT EXISTS IX_Judgments_DecisionDate
            ON Judgments(DecisionDate);
        """;

        await command.ExecuteNonQueryAsync();

        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=8000;";
        await pragma.ExecuteNonQueryAsync();

        await EnsureColumnAsync(connection, "Category", "TEXT");
        await EnsureColumnAsync(connection, "Topics", "TEXT");
        await EnsureColumnAsync(connection, "MuddaType", "TEXT");

        using var indexCmd = connection.CreateCommand();
        indexCmd.CommandText = """
            CREATE INDEX IF NOT EXISTS IX_Judgments_Category ON Judgments(Category);
            CREATE INDEX IF NOT EXISTS IX_Judgments_Topics ON Judgments(Topics);
            CREATE INDEX IF NOT EXISTS IX_Judgments_MuddaType ON Judgments(MuddaType);
            """;
        await indexCmd.ExecuteNonQueryAsync();

        await BackfillSummariesAsync();
        await BackfillCategoriesAsync();
    }

    private static async Task EnsureColumnAsync(SqliteConnection connection, string name, string type)
    {
        using var info = connection.CreateCommand();
        info.CommandText = "PRAGMA table_info(Judgments);";
        var exists = false;
        await using var reader = await info.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (string.Equals(reader.GetString(1), name, StringComparison.OrdinalIgnoreCase))
            {
                exists = true;
                break;
            }
        }

        if (exists)
            return;

        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE Judgments ADD COLUMN {name} {type};";
        await alter.ExecuteNonQueryAsync();
    }

    public async Task BackfillSummariesAsync()
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();

        using var select = connection.CreateCommand();
        select.CommandText = """
            SELECT Id, FullText
            FROM Judgments
            WHERE (Summary IS NULL OR TRIM(Summary) = '')
              AND FullText IS NOT NULL AND TRIM(FullText) != '';
            """;

        var pending = new List<(long Id, string FullText)>();
        await using (var reader = await select.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                pending.Add((reader.GetInt64(0), GetSafeString(reader, 1)));
            }
        }

        foreach (var (id, fullText) in pending)
        {
            var paragraphs = fullText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).ToList();
            var summary = NkpParser.ExtractSummary(paragraphs, fullText);
            if (string.IsNullOrWhiteSpace(summary))
                continue;

            using var updateCmd = connection.CreateCommand();
            updateCmd.CommandText = "UPDATE Judgments SET Summary = $summary WHERE Id = $id;";
            updateCmd.Parameters.AddWithValue("$summary", summary);
            updateCmd.Parameters.AddWithValue("$id", id);
            await updateCmd.ExecuteNonQueryAsync();
        }
    }

    public async Task BackfillCategoriesAsync()
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();

        using var select = connection.CreateCommand();
        select.CommandText = """
            SELECT Id, CaseName, CaseNumber, Summary, Category, Topics
            FROM Judgments
            WHERE Category IS NULL OR TRIM(Category) = '' OR Topics IS NULL;
            """;

        var pending = new List<Judgment>();
        await using (var reader = await select.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                pending.Add(new Judgment
                {
                    Id = reader.GetInt64(0),
                    CaseName = GetSafeString(reader, 1),
                    CaseNumber = GetSafeString(reader, 2),
                    Summary = GetSafeString(reader, 3),
                    Category = GetSafeString(reader, 4),
                    Topics = GetSafeString(reader, 5)
                });
            }
        }

        foreach (var judgment in pending)
        {
            CaseClassifier.Apply(judgment);
            using var updateCmd = connection.CreateCommand();
            updateCmd.CommandText = """
                UPDATE Judgments
                SET Category = $category, Topics = $topics
                WHERE Id = $id;
                """;
            updateCmd.Parameters.AddWithValue("$category", judgment.Category ?? "");
            updateCmd.Parameters.AddWithValue("$topics", judgment.Topics ?? "");
            updateCmd.Parameters.AddWithValue("$id", judgment.Id);
            await updateCmd.ExecuteNonQueryAsync();
        }
    }


    // ========================================================
    // INSERT / UPDATE
    // ========================================================

    public async Task UpsertJudgmentAsync(
        Judgment judgment)
    {
        await using var connection =
            CreateConnection();

        await connection.OpenAsync();

        var command =
            connection.CreateCommand();

        command.CommandText = """
        INSERT INTO Judgments
        (
            DecisionNumber,
            CaseName,
            DetailUrl,
            SourceUrl,

            Volume,
            Year,
            Month,
            Edition,

            DecisionDate,
            DecisionDateNepali,

            CaseNumber,
            Court,
            Judges,

            Petitioner,
            Respondent,

            Laws,
            Precedents,

            Summary,
            FullText,

            Category,
            Topics,
            MuddaType,

            ScrapeStatus,
            ScrapedAt,
            UpdatedAt
        )
        VALUES
        (
            $DecisionNumber,
            $CaseName,
            $DetailUrl,
            $SourceUrl,

            $Volume,
            $Year,
            $Month,
            $Edition,

            $DecisionDate,
            $DecisionDateNepali,

            $CaseNumber,
            $Court,
            $Judges,

            $Petitioner,
            $Respondent,

            $Laws,
            $Precedents,

            $Summary,
            $FullText,

            $Category,
            $Topics,
            $MuddaType,

            $ScrapeStatus,
            $ScrapedAt,
            $UpdatedAt
        )

        ON CONFLICT(DetailUrl)
        DO UPDATE SET

            DecisionNumber =
                excluded.DecisionNumber,

            CaseName =
                excluded.CaseName,

            SourceUrl =
                excluded.SourceUrl,

            Volume =
                CASE WHEN excluded.Volume = '' THEN Judgments.Volume ELSE excluded.Volume END,

            Year =
                CASE WHEN excluded.Year = '' THEN Judgments.Year ELSE excluded.Year END,

            Month =
                CASE WHEN excluded.Month = '' THEN Judgments.Month ELSE excluded.Month END,

            Edition =
                CASE WHEN excluded.Edition = '' THEN Judgments.Edition ELSE excluded.Edition END,

            DecisionDate =
                CASE WHEN excluded.DecisionDate = '' THEN Judgments.DecisionDate ELSE excluded.DecisionDate END,

            DecisionDateNepali =
                CASE WHEN excluded.DecisionDateNepali = '' THEN Judgments.DecisionDateNepali ELSE excluded.DecisionDateNepali END,

            CaseNumber =
                CASE WHEN excluded.CaseNumber = '' THEN Judgments.CaseNumber ELSE excluded.CaseNumber END,

            Court =
                CASE WHEN excluded.Court = '' THEN Judgments.Court ELSE excluded.Court END,

            Judges =
                CASE WHEN excluded.Judges = '' THEN Judgments.Judges ELSE excluded.Judges END,

            Petitioner =
                CASE WHEN excluded.Petitioner = '' THEN Judgments.Petitioner ELSE excluded.Petitioner END,

            Respondent =
                CASE WHEN excluded.Respondent = '' THEN Judgments.Respondent ELSE excluded.Respondent END,

            Laws =
                CASE WHEN excluded.Laws = '' THEN Judgments.Laws ELSE excluded.Laws END,

            Precedents =
                CASE WHEN excluded.Precedents = '' THEN Judgments.Precedents ELSE excluded.Precedents END,

            Summary =
                CASE
                    WHEN Judgments.FullText IS NOT NULL AND TRIM(Judgments.FullText) != '' THEN Judgments.Summary
                    WHEN excluded.Summary = '' THEN Judgments.Summary
                    ELSE excluded.Summary
                END,

            FullText =
                CASE WHEN excluded.FullText = '' THEN Judgments.FullText ELSE excluded.FullText END,

            Category =
                CASE WHEN excluded.Category = '' THEN Judgments.Category ELSE excluded.Category END,

            Topics =
                CASE WHEN excluded.Topics = '' THEN Judgments.Topics ELSE excluded.Topics END,

            MuddaType =
                CASE WHEN excluded.MuddaType = '' THEN Judgments.MuddaType ELSE excluded.MuddaType END,

            ScrapeStatus =
                CASE WHEN Judgments.ScrapeStatus = 'SUCCESS' THEN Judgments.ScrapeStatus ELSE excluded.ScrapeStatus END,

            UpdatedAt =
                excluded.UpdatedAt;
        """;


        command.Parameters.AddWithValue(
            "$DecisionNumber",
            judgment.DecisionNumber ?? "");

        command.Parameters.AddWithValue(
            "$CaseName",
            judgment.CaseName ?? "");

        command.Parameters.AddWithValue(
            "$DetailUrl",
            judgment.DetailUrl ?? "");

        command.Parameters.AddWithValue(
            "$SourceUrl",
            judgment.SourceUrl ?? "");

        command.Parameters.AddWithValue(
            "$Volume",
            judgment.Volume ?? "");

        command.Parameters.AddWithValue(
            "$Year",
            judgment.Year ?? "");

        command.Parameters.AddWithValue(
            "$Month",
            judgment.Month ?? "");

        command.Parameters.AddWithValue(
            "$Edition",
            judgment.Edition ?? "");

        command.Parameters.AddWithValue(
            "$DecisionDate",
            judgment.DecisionDate ?? "");

        command.Parameters.AddWithValue(
            "$DecisionDateNepali",
            judgment.DecisionDateNepali ?? "");

        command.Parameters.AddWithValue(
            "$CaseNumber",
            judgment.CaseNumber ?? "");

        command.Parameters.AddWithValue(
            "$Court",
            judgment.Court ?? "");

        command.Parameters.AddWithValue(
            "$Judges",
            judgment.Judges ?? "");

        command.Parameters.AddWithValue(
            "$Petitioner",
            judgment.Petitioner ?? "");

        command.Parameters.AddWithValue(
            "$Respondent",
            judgment.Respondent ?? "");

        command.Parameters.AddWithValue(
            "$Laws",
            judgment.Laws ?? "");

        command.Parameters.AddWithValue(
            "$Precedents",
            judgment.Precedents ?? "");

        command.Parameters.AddWithValue(
            "$Summary",
            judgment.Summary ?? "");

        command.Parameters.AddWithValue(
            "$FullText",
            judgment.FullText ?? "");

        command.Parameters.AddWithValue(
            "$Category",
            judgment.Category ?? "");

        command.Parameters.AddWithValue(
            "$Topics",
            judgment.Topics ?? "");

        command.Parameters.AddWithValue(
            "$MuddaType",
            judgment.MuddaType ?? "");

        command.Parameters.AddWithValue(
            "$ScrapeStatus",
            judgment.ScrapeStatus ?? "");

        var now =
            DateTime.UtcNow.ToString("O");

        command.Parameters.AddWithValue(
            "$ScrapedAt",
            string.IsNullOrWhiteSpace(
                judgment.ScrapedAt)
                ? now
                : judgment.ScrapedAt);

        command.Parameters.AddWithValue(
            "$UpdatedAt",
            now);


        await command.ExecuteNonQueryAsync();
    }


    // ========================================================
    // GET ALL JUDGMENTS
    // ========================================================

    public async Task<List<Judgment>>
        GetJudgmentsAsync()
    {
        var judgments =
            new List<Judgment>();

        await using var connection =
            CreateConnection();

        await connection.OpenAsync();

        var command =
            connection.CreateCommand();

        command.CommandText = """
        SELECT
            Id,
            DecisionNumber,
            CaseName,
            DetailUrl,
            SourceUrl,

            Volume,
            Year,
            Month,
            Edition,

            DecisionDate,
            DecisionDateNepali,

            CaseNumber,
            Court,
            Judges,

            Petitioner,
            Respondent,

            Laws,
            Precedents,

            Summary,
            FullText,

            ScrapeStatus,
            ScrapedAt,
            UpdatedAt,
            Category,
            Topics,
            MuddaType

        FROM Judgments

        ORDER BY Id;
        """;

        await using var reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            judgments.Add(
                new Judgment
                {
                    Id = reader.IsDBNull(0) ? 0 : reader.GetInt64(0),
                    DecisionNumber = GetSafeString(reader, 1),
                    CaseName = GetSafeString(reader, 2),
                    DetailUrl = GetSafeString(reader, 3),
                    SourceUrl = GetSafeString(reader, 4),
                    Volume = GetSafeString(reader, 5),
                    Year = GetSafeString(reader, 6),
                    Month = GetSafeString(reader, 7),
                    Edition = GetSafeString(reader, 8),
                    DecisionDate = GetSafeString(reader, 9),
                    DecisionDateNepali = GetSafeString(reader, 10),
                    CaseNumber = GetSafeString(reader, 11),
                    Court = GetSafeString(reader, 12),
                    Judges = GetSafeString(reader, 13),
                    Petitioner = GetSafeString(reader, 14),
                    Respondent = GetSafeString(reader, 15),
                    Laws = GetSafeString(reader, 16),
                    Precedents = GetSafeString(reader, 17),
                    Summary = GetSafeString(reader, 18),
                    FullText = GetSafeString(reader, 19),
                    ScrapeStatus = GetSafeString(reader, 20),
                    ScrapedAt = GetSafeString(reader, 21),
                    UpdatedAt = GetSafeString(reader, 22),
                    Category = reader.FieldCount > 23 ? GetSafeString(reader, 23) : "",
                    Topics = reader.FieldCount > 24 ? GetSafeString(reader, 24) : "",
                    MuddaType = reader.FieldCount > 25 ? GetSafeString(reader, 25) : ""
                });
        }

        return judgments;
    }


    // ========================================================
    // COUNT
    // ========================================================

    public async Task<HashSet<string>> GetSavedDetailUrlsAsync()
    {
        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT DetailUrl
            FROM Judgments
            WHERE FullText IS NOT NULL AND TRIM(FullText) != '';
            """;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var url = GetSafeString(reader, 0);
            if (!string.IsNullOrWhiteSpace(url))
                urls.Add(url);
        }

        return urls;
    }

    public async Task<long>
        GetJudgmentCountAsync()
    {
        await using var connection =
            CreateConnection();

        await connection.OpenAsync();

        var command =
            connection.CreateCommand();

        command.CommandText =
            "SELECT COUNT(*) FROM Judgments";

        var result =
            await command.ExecuteScalarAsync();

        return Convert.ToInt64(result);
    }

    private static string GetSafeString(SqliteDataReader reader, int index)
    {
        return reader.IsDBNull(index) ? "" : reader.GetString(index);
    }

    public async Task PrintDatabaseSummaryAsync()
    {
        var judgments = await GetJudgmentsAsync();
        Console.WriteLine();
        Console.WriteLine("==================================================");
        Console.WriteLine("             SQLITE DATABASE AUDIT REPORT         ");
        Console.WriteLine("==================================================");
        Console.WriteLine($"Total Records in DB  : {judgments.Count}");
        Console.WriteLine("--------------------------------------------------");
        Console.WriteLine("Field Population Statistics:");
        Console.WriteLine($"  - Decision Number  : {judgments.Count(j => !string.IsNullOrWhiteSpace(j.DecisionNumber))}/{judgments.Count}");
        Console.WriteLine($"  - Case Name        : {judgments.Count(j => !string.IsNullOrWhiteSpace(j.CaseName))}/{judgments.Count}");
        Console.WriteLine($"  - Case Number      : {judgments.Count(j => !string.IsNullOrWhiteSpace(j.CaseNumber))}/{judgments.Count}");
        Console.WriteLine($"  - Decision Date    : {judgments.Count(j => !string.IsNullOrWhiteSpace(j.DecisionDate))}/{judgments.Count}");
        Console.WriteLine($"  - Court            : {judgments.Count(j => !string.IsNullOrWhiteSpace(j.Court))}/{judgments.Count}");
        Console.WriteLine($"  - Judges           : {judgments.Count(j => !string.IsNullOrWhiteSpace(j.Judges))}/{judgments.Count}");
        Console.WriteLine($"  - Petitioner       : {judgments.Count(j => !string.IsNullOrWhiteSpace(j.Petitioner))}/{judgments.Count}");
        Console.WriteLine($"  - Respondent       : {judgments.Count(j => !string.IsNullOrWhiteSpace(j.Respondent))}/{judgments.Count}");
        Console.WriteLine($"  - Laws Cited       : {judgments.Count(j => !string.IsNullOrWhiteSpace(j.Laws))}/{judgments.Count}");
        Console.WriteLine($"  - Precedents Cited : {judgments.Count(j => !string.IsNullOrWhiteSpace(j.Precedents))}/{judgments.Count}");
        Console.WriteLine($"  - Full Text        : {judgments.Count(j => !string.IsNullOrWhiteSpace(j.FullText))}/{judgments.Count}");
        Console.WriteLine($"  - Summary          : {judgments.Count(j => !string.IsNullOrWhiteSpace(j.Summary))}/{judgments.Count}");
        Console.WriteLine("--------------------------------------------------");

        if (judgments.Count > 0)
        {
            Console.WriteLine("Sample Judgment Record #1:");
            var first = judgments[0];
            Console.WriteLine($"  ID              : {first.Id}");
            Console.WriteLine($"  Decision No     : {first.DecisionNumber}");
            Console.WriteLine($"  Case Name       : {first.CaseName}");
            Console.WriteLine($"  Case Number     : {first.CaseNumber}");
            Console.WriteLine($"  Decision Date   : {first.DecisionDate}");
            Console.WriteLine($"  Court           : {first.Court}");
            Console.WriteLine($"  Judges          : {first.Judges}");
            Console.WriteLine($"  Petitioner      : {first.Petitioner}");
            Console.WriteLine($"  Respondent      : {first.Respondent}");
            Console.WriteLine($"  Laws            : {first.Laws}");
            Console.WriteLine($"  Precedents      : {first.Precedents}");
            Console.WriteLine($"  FullText Length : {first.FullText?.Length ?? 0} chars");
            Console.WriteLine($"  Detail URL      : {first.DetailUrl}");
        }
        Console.WriteLine("==================================================");
    }

    public async Task<(List<Judgment> Items, int TotalCount)> SearchJudgmentsAsync(
        string? query = null,
        string? court = null,
        string? category = null,
        string? topic = null,
        int page = 1,
        int pageSize = 20)
    {
        var judgments = new List<Judgment>();
        await using var connection = CreateConnection();
        await connection.OpenAsync();

        var whereClauses = new List<string>();
        using var countCmd = connection.CreateCommand();
        using var searchCmd = connection.CreateCommand();

        if (!string.IsNullOrWhiteSpace(query))
        {
            whereClauses.Add("(CaseName LIKE $q OR DecisionNumber LIKE $q OR CaseNumber LIKE $q OR FullText LIKE $q OR Laws LIKE $q OR Judges LIKE $q OR Summary LIKE $q)");
            countCmd.Parameters.AddWithValue("$q", $"%{query.Trim()}%");
            searchCmd.Parameters.AddWithValue("$q", $"%{query.Trim()}%");
        }

        if (!string.IsNullOrWhiteSpace(court))
        {
            whereClauses.Add("Court LIKE $court");
            countCmd.Parameters.AddWithValue("$court", $"%{court.Trim()}%");
            searchCmd.Parameters.AddWithValue("$court", $"%{court.Trim()}%");
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            whereClauses.Add("(Category = $category OR MuddaType = $category OR (IFNULL(Category, '') = '' AND IFNULL(MuddaType, '') = '' AND (CaseName LIKE $categoryLike OR Summary LIKE $categoryLike)))");
            countCmd.Parameters.AddWithValue("$category", category.Trim());
            countCmd.Parameters.AddWithValue("$categoryLike", $"%{category.Trim()}%");
            searchCmd.Parameters.AddWithValue("$category", category.Trim());
            searchCmd.Parameters.AddWithValue("$categoryLike", $"%{category.Trim()}%");
        }

        if (!string.IsNullOrWhiteSpace(topic))
        {
            var topicValue = topic.Trim();
            whereClauses.Add(@"
                (
                    (',' || REPLACE(REPLACE(IFNULL(Topics, ''), ' | ', ','), '|', ',') || ',') LIKE $topicToken
                    OR (',' || REPLACE(REPLACE(IFNULL(Topics, ''), ' | ', ','), '|', ',') || ',') LIKE $topicAlias
                    OR (
                        CaseName LIKE $topicLike
                        " + string.Concat(OfficialTopicLabels
                            .Where(label => label != topicValue && label.Contains(topicValue, StringComparison.Ordinal))
                            .Select((label, i) => $" AND CaseName NOT LIKE $topicEx{i}")) + @"
                    )
                )");
            countCmd.Parameters.AddWithValue("$topicToken", "%," + topicValue + ",%");
            searchCmd.Parameters.AddWithValue("$topicToken", "%," + topicValue + ",%");
            var alias = topicValue == "बन्दी प्रत्यक्षीकरण" ? "बन्दीप्रत्यक्षीकरण"
                : topicValue == "बन्दीप्रत्यक्षीकरण" ? "बन्दी प्रत्यक्षीकरण"
                : topicValue;
            countCmd.Parameters.AddWithValue("$topicAlias", "%," + alias + ",%");
            searchCmd.Parameters.AddWithValue("$topicAlias", "%," + alias + ",%");
            countCmd.Parameters.AddWithValue("$topicLike", "%" + topicValue + "%");
            searchCmd.Parameters.AddWithValue("$topicLike", "%" + topicValue + "%");
            var exclusions = OfficialTopicLabels
                .Where(label => label != topicValue && label.Contains(topicValue, StringComparison.Ordinal))
                .ToArray();
            for (var i = 0; i < exclusions.Length; i++)
            {
                countCmd.Parameters.AddWithValue($"$topicEx{i}", "%" + exclusions[i] + "%");
                searchCmd.Parameters.AddWithValue($"$topicEx{i}", "%" + exclusions[i] + "%");
            }
        }

        var whereSql = whereClauses.Count > 0 ? " WHERE " + string.Join(" AND ", whereClauses) : "";

        countCmd.CommandText = $"SELECT COUNT(*) FROM Judgments{whereSql};";
        var totalCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

        var offset = (Math.Max(1, page) - 1) * pageSize;
        searchCmd.CommandText = $@"
        SELECT
            Id, DecisionNumber, CaseName, DetailUrl, SourceUrl,
            Volume, Year, Month, Edition, DecisionDate,
            DecisionDateNepali, CaseNumber, Court, Judges,
            Petitioner, Respondent, Laws, Precedents, Summary,
            FullText, ScrapeStatus, ScrapedAt, UpdatedAt, Category, Topics, MuddaType
        FROM Judgments
        {whereSql}
        ORDER BY
            CASE WHEN TRIM(IFNULL(DecisionDate, '')) = '' AND TRIM(IFNULL(DecisionDateNepali, '')) = '' THEN 1 ELSE 0 END,
            {DecisionDateSortSql} DESC,
            Id DESC
        LIMIT {pageSize} OFFSET {offset};";

        await using var reader = await searchCmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            judgments.Add(new Judgment
            {
                Id = reader.IsDBNull(0) ? 0 : reader.GetInt64(0),
                DecisionNumber = GetSafeString(reader, 1),
                CaseName = GetSafeString(reader, 2),
                DetailUrl = GetSafeString(reader, 3),
                SourceUrl = GetSafeString(reader, 4),
                Volume = GetSafeString(reader, 5),
                Year = GetSafeString(reader, 6),
                Month = GetSafeString(reader, 7),
                Edition = GetSafeString(reader, 8),
                DecisionDate = GetSafeString(reader, 9),
                DecisionDateNepali = GetSafeString(reader, 10),
                CaseNumber = GetSafeString(reader, 11),
                Court = GetSafeString(reader, 12),
                Judges = GetSafeString(reader, 13),
                Petitioner = GetSafeString(reader, 14),
                Respondent = GetSafeString(reader, 15),
                Laws = GetSafeString(reader, 16),
                Precedents = GetSafeString(reader, 17),
                Summary = GetSafeString(reader, 18),
                FullText = GetSafeString(reader, 19),
                ScrapeStatus = GetSafeString(reader, 20),
                ScrapedAt = GetSafeString(reader, 21),
                UpdatedAt = GetSafeString(reader, 22),
                Category = GetSafeString(reader, 23),
                Topics = GetSafeString(reader, 24),
                MuddaType = reader.FieldCount > 25 ? GetSafeString(reader, 25) : ""
            });
        }

        return (judgments, totalCount);
    }

    public async Task<(List<Judgment> Items, int TotalCount)> ExportJudgmentsAsync(int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 500);
        var offset = (page - 1) * pageSize;

        await using var connection = CreateConnection();
        await connection.OpenAsync();

        using var countCmd = connection.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM Judgments;";
        var totalCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

        using var searchCmd = connection.CreateCommand();
        searchCmd.CommandText = $@"
        SELECT
            Id, DecisionNumber, CaseName, DetailUrl, SourceUrl,
            Volume, Year, Month, Edition, DecisionDate,
            DecisionDateNepali, CaseNumber, Court, Judges,
            Petitioner, Respondent, Laws, Precedents, Summary,
            FullText, ScrapeStatus, ScrapedAt, UpdatedAt, Category, Topics, MuddaType
        FROM Judgments
        ORDER BY Id
        LIMIT {pageSize} OFFSET {offset};";

        var judgments = new List<Judgment>();
        await using var reader = await searchCmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            judgments.Add(ReadJudgment(reader));
        }

        return (judgments, totalCount);
    }

    public async Task<object> GetStatsAsync()
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                COUNT(*),
                SUM(CASE WHEN FullText IS NOT NULL AND TRIM(FullText) != '' THEN 1 ELSE 0 END),
                SUM(CASE WHEN Laws IS NOT NULL AND TRIM(Laws) != '' THEN 1 ELSE 0 END),
                SUM(CASE WHEN Precedents IS NOT NULL AND TRIM(Precedents) != '' THEN 1 ELSE 0 END),
                SUM(CASE WHEN Summary IS NOT NULL AND TRIM(Summary) != '' THEN 1 ELSE 0 END),
                SUM(CASE WHEN Category IS NOT NULL AND TRIM(Category) != '' THEN 1 ELSE 0 END)
            FROM Judgments;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return new
        {
            TotalJudgments = reader.IsDBNull(0) ? 0 : reader.GetInt64(0),
            WithFullText = reader.IsDBNull(1) ? 0 : reader.GetInt64(1),
            WithLaws = reader.IsDBNull(2) ? 0 : reader.GetInt64(2),
            WithPrecedents = reader.IsDBNull(3) ? 0 : reader.GetInt64(3),
            WithSummary = reader.IsDBNull(4) ? 0 : reader.GetInt64(4),
            WithCategory = reader.IsDBNull(5) ? 0 : reader.GetInt64(5)
        };
    }

    private static Judgment ReadJudgment(SqliteDataReader reader)
    {
        return new Judgment
        {
            Id = reader.IsDBNull(0) ? 0 : reader.GetInt64(0),
            DecisionNumber = GetSafeString(reader, 1),
            CaseName = GetSafeString(reader, 2),
            DetailUrl = GetSafeString(reader, 3),
            SourceUrl = GetSafeString(reader, 4),
            Volume = GetSafeString(reader, 5),
            Year = GetSafeString(reader, 6),
            Month = GetSafeString(reader, 7),
            Edition = GetSafeString(reader, 8),
            DecisionDate = GetSafeString(reader, 9),
            DecisionDateNepali = GetSafeString(reader, 10),
            CaseNumber = GetSafeString(reader, 11),
            Court = GetSafeString(reader, 12),
            Judges = GetSafeString(reader, 13),
            Petitioner = GetSafeString(reader, 14),
            Respondent = GetSafeString(reader, 15),
            Laws = GetSafeString(reader, 16),
            Precedents = GetSafeString(reader, 17),
            Summary = GetSafeString(reader, 18),
            FullText = GetSafeString(reader, 19),
            ScrapeStatus = GetSafeString(reader, 20),
            ScrapedAt = GetSafeString(reader, 21),
            UpdatedAt = GetSafeString(reader, 22),
            Category = reader.FieldCount > 23 ? GetSafeString(reader, 23) : "",
            Topics = reader.FieldCount > 24 ? GetSafeString(reader, 24) : "",
            MuddaType = reader.FieldCount > 25 ? GetSafeString(reader, 25) : ""
        };
    }

    public async Task<Judgment?> GetJudgmentByIdAsync(long id)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = @"
        SELECT
            Id, DecisionNumber, CaseName, DetailUrl, SourceUrl,
            Volume, Year, Month, Edition, DecisionDate,
            DecisionDateNepali, CaseNumber, Court, Judges,
            Petitioner, Respondent, Laws, Precedents, Summary,
            FullText, ScrapeStatus, ScrapedAt, UpdatedAt, Category, Topics, MuddaType
        FROM Judgments
        WHERE Id = $id
        LIMIT 1;";
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return ReadJudgment(reader);
        }

        return null;
    }
}