using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

public class JudgmentRepository
{
    private readonly SqliteDatabase _database;

    public JudgmentRepository(SqliteDatabase database)
    {
        _database = database;
    }

    public async Task UpsertAsync(Judgment judgment)
    {
        await using var connection =
            _database.CreateConnection();

        await connection.OpenAsync();

        var command =
            connection.CreateCommand();

        command.CommandText = """
        INSERT INTO Judgments
        (
            DecisionNumber,
            CaseNumber,
            CaseName,
            DecisionDate,
            DecisionDateNepali,
            Court,
            Judges,
            Petitioner,
            Respondent,
            Volume,
            Year,
            Month,
            Edition,
            Summary,
            Laws,
            Precedents,
            FullText,
            SourceUrl,
            ScrapedAt,
            UpdatedAt,
            ScrapeStatus
        )
        VALUES
        (
            $DecisionNumber,
            $CaseNumber,
            $CaseName,
            $DecisionDate,
            $DecisionDateNepali,
            $Court,
            $Judges,
            $Petitioner,
            $Respondent,
            $Volume,
            $Year,
            $Month,
            $Edition,
            $Summary,
            $Laws,
            $Precedents,
            $FullText,
            $SourceUrl,
            $ScrapedAt,
            $UpdatedAt,
            $ScrapeStatus
        )
        ON CONFLICT(SourceUrl)
        DO UPDATE SET
            DecisionNumber = excluded.DecisionNumber,
            CaseNumber = excluded.CaseNumber,
            CaseName = excluded.CaseName,
            DecisionDate = excluded.DecisionDate,
            DecisionDateNepali = excluded.DecisionDateNepali,
            Court = excluded.Court,
            Judges = excluded.Judges,
            Petitioner = excluded.Petitioner,
            Respondent = excluded.Respondent,
            Volume = excluded.Volume,
            Year = excluded.Year,
            Month = excluded.Month,
            Edition = excluded.Edition,
            Summary = excluded.Summary,
            Laws = excluded.Laws,
            Precedents = excluded.Precedents,
            FullText = excluded.FullText,
            UpdatedAt = excluded.UpdatedAt,
            ScrapeStatus = excluded.ScrapeStatus;
        """;

        command.Parameters.AddWithValue(
            "$DecisionNumber",
            judgment.DecisionNumber ?? "");

        command.Parameters.AddWithValue(
            "$CaseNumber",
            judgment.CaseNumber ?? "");

        command.Parameters.AddWithValue(
            "$CaseName",
            judgment.CaseName ?? "");

        command.Parameters.AddWithValue(
            "$DecisionDate",
            judgment.DecisionDate ?? "");

        command.Parameters.AddWithValue(
            "$DecisionDateNepali",
            judgment.DecisionDateNepali ?? "");

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
            "$Summary",
            judgment.Summary ?? "");

        command.Parameters.AddWithValue(
            "$Laws",
            judgment.Laws ?? "");

        command.Parameters.AddWithValue(
            "$Precedents",
            judgment.Precedents ?? "");

        command.Parameters.AddWithValue(
            "$FullText",
            judgment.FullText ?? "");

        command.Parameters.AddWithValue(
            "$SourceUrl",
            judgment.SourceUrl ?? "");

        var now =
            DateTime.UtcNow.ToString("O");

        command.Parameters.AddWithValue(
            "$ScrapedAt",
            now);

        command.Parameters.AddWithValue(
            "$UpdatedAt",
            now);

        command.Parameters.AddWithValue(
            "$ScrapeStatus",
            "SUCCESS");

        await command.ExecuteNonQueryAsync();
    }
}