public class Judgment
{
    public long Id { get; set; }

    public string DecisionNumber { get; set; } = "";
    public string CaseName { get; set; } = "";

    public string DetailUrl { get; set; } = "";
    public string SourceUrl { get; set; } = "";

    public string Volume { get; set; } = "";
    public string Year { get; set; } = "";
    public string Month { get; set; } = "";
    public string Edition { get; set; } = "";

    public string DecisionDate { get; set; } = "";
    public string DecisionDateNepali { get; set; } = "";

    public string CaseNumber { get; set; } = "";
    public string Court { get; set; } = "";
    public string Judges { get; set; } = "";

    public string Petitioner { get; set; } = "";
    public string Respondent { get; set; } = "";

    public string Laws { get; set; } = "";
    public string Precedents { get; set; } = "";

    public string Summary { get; set; } = "";
    public string FullText { get; set; } = "";

    public string Category { get; set; } = "";
    public string Topics { get; set; } = "";
    public string MuddaType { get; set; } = "";

    public string ScrapeStatus { get; set; } = "";
    public string ScrapedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
}