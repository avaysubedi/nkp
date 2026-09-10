using System.Collections.Generic;

public class SearchResult
{
    public int TotalResults { get; set; }

    public List<Judgment> Judgments { get; set; } = new();

    public string? NextPageUrl { get; set; }
}