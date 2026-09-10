using System;
using System.Collections.Generic;
using System.Linq;
using AngleSharp;
using AngleSharp.Dom;
using System.Text.RegularExpressions;

public class NkpParser
{
    // =========================================================
    // SEARCH RESULTS
    // =========================================================

    public SearchResult ParseSearchResults(string html, string? currentUrl = null)
    {
        var config = Configuration.Default;

        var context = BrowsingContext.New(config);

        var document = context.OpenAsync(req =>
                req.Content(html))
            .GetAwaiter()
            .GetResult();

        var result = new SearchResult();

        // -----------------------------------------------------
        // TOTAL RESULTS
        // -----------------------------------------------------

        var heading = document
            .QuerySelectorAll("h1, h2, h3, h4, h5")
            .FirstOrDefault(x =>
                x.TextContent.Contains("खोजी नतिजाहरु"));

        if (heading != null)
        {
            result.TotalResults =
                ExtractNepaliNumber(heading.TextContent);
        }

        Console.WriteLine(
            $"Result heading found: {CleanText(heading?.TextContent)}");

        var articles = document.QuerySelectorAll("article");
        if (articles.Length > 0)
        {
            Console.WriteLine($"Judgment articles found: {articles.Length}");
            foreach (var article in articles)
            {
                var link = article.QuerySelector("h3.post-title a[href], a[href*='full_detail']");
                var href = link?.GetAttribute("href");
                if (string.IsNullOrWhiteSpace(href) ||
                    !href.Contains("/full_detail/", StringComparison.OrdinalIgnoreCase))
                    continue;

                var text = CleanText(link!.TextContent);
                var details = article.QuerySelectorAll(".type-details");
                var meta = details.Length > 0 ? CleanText(details[0].TextContent) : "";
                var partyText = details.Length > 1 ? CleanText(details[1].TextContent) : "";
                var snippet = article
                    .QuerySelectorAll("p")
                    .Select(p => CleanText(p.TextContent))
                    .FirstOrDefault(p => p.Length > 40) ?? "";

                var bench = ExtractBench(meta);
                var date = ExtractDecisionDate(new List<string> { meta });
                var (petitioner, respondent) = ExtractParties(partyText);

                var listed = new Judgment
                {
                    DecisionNumber = ExtractDecisionNumber(text),
                    CaseName = ExtractCaseName(text),
                    DetailUrl = MakeAbsoluteUrl(href),
                    Court = bench,
                    DecisionDate = date,
                    Petitioner = petitioner,
                    Respondent = respondent,
                    Summary = snippet
                };
                CaseClassifier.Apply(listed);
                result.Judgments.Add(listed);
            }
        }
        else
        {
            var links = document
                .QuerySelectorAll("a")
                .Where(a =>
                {
                    var href = a.GetAttribute("href");
                    return !string.IsNullOrWhiteSpace(href) &&
                           href.Contains("/full_detail/", StringComparison.OrdinalIgnoreCase);
                })
                .GroupBy(a => a.GetAttribute("href"))
                .Select(g => g.First())
                .ToList();

            Console.WriteLine($"Judgment headings found: {links.Count}");

            foreach (var link in links)
            {
                var href = link.GetAttribute("href");
                if (string.IsNullOrWhiteSpace(href))
                    continue;

                var text = CleanText(link.TextContent);
                result.Judgments.Add(new Judgment
                {
                    DecisionNumber = ExtractDecisionNumber(text),
                    CaseName = ExtractCaseName(text),
                    DetailUrl = MakeAbsoluteUrl(href)
                });
            }
        }

        // -----------------------------------------------------
        // NEXT PAGE
        // -----------------------------------------------------

        var currentPerPage = GetPerPageValue(currentUrl) ?? 0;

        var paginationLinks = document
            .QuerySelectorAll("#pagination a")
            .Select(a => new
            {
                Href = MakeAbsoluteUrl(a.GetAttribute("href")),
                Text = CleanText(a.TextContent),
                IsActive = a.ClassList.Contains("active")
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Href))
            .Where(x => !string.Equals(x.Href, currentUrl, StringComparison.OrdinalIgnoreCase))
            .Where(x => !x.IsActive)
            .ToList();

        var nextCandidate = paginationLinks
            .Where(x =>
            {
                var perPageValue = GetPerPageValue(x.Href);
                if (!perPageValue.HasValue || perPageValue.Value <= currentPerPage)
                    return false;

                // NKP uses per_page as an offset. Ignore last-page jumps such as 2500.
                return perPageValue.Value - currentPerPage <= 40;
            })
            .OrderBy(x => GetPerPageValue(x.Href) ?? int.MaxValue)
            .ThenBy(x => x.Text.Contains("अर्को") || x.Text.Contains("Next", StringComparison.OrdinalIgnoreCase) || x.Text.Contains("अर्को पृष्ठ") ? 0 : 1)
            .ThenBy(x => x.Text.Contains("»") || x.Text.Contains("›") || x.Text.Contains("…") ? 0 : 1)
            .FirstOrDefault();

        if (nextCandidate != null)
        {
            result.NextPageUrl = nextCandidate.Href;
        }
        else
        {
            var arrowCandidate = paginationLinks
                .Where(x =>
                {
                    var perPageValue = GetPerPageValue(x.Href);
                    if (perPageValue.HasValue && perPageValue.Value - currentPerPage > 40)
                        return false;
                    return true;
                })
                .FirstOrDefault(x =>
                    x.Text.Contains("अर्को") ||
                    x.Text.Contains("Next", StringComparison.OrdinalIgnoreCase) ||
                    x.Text.Contains("अर्को पृष्ठ") ||
                    x.Text.Contains("»") ||
                    x.Text.Contains("›") ||
                    x.Text.Contains("…"));

            if (arrowCandidate != null)
            {
                result.NextPageUrl = arrowCandidate.Href;
            }
        }

        return result;
    }

    private static int? GetPerPageValue(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        try
        {
            var uri = new Uri(url);
            var query = uri.Query.TrimStart('?');
            var pairs = query.Split('&', StringSplitOptions.RemoveEmptyEntries);

            foreach (var pair in pairs)
            {
                var parts = pair.Split('=', 2);
                if (parts.Length == 2 && parts[0].Equals("per_page", StringComparison.OrdinalIgnoreCase))
                {
                    var raw = Uri.UnescapeDataString(parts[1]);
                    if (int.TryParse(raw, out var value))
                        return value;
                }
            }
        }
        catch
        {
            var match = Regex.Match(url, "[?&]per_page=([^&]+)", RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(Uri.UnescapeDataString(match.Groups[1].Value), out var value))
                return value;
        }

        return null;
    }


    // =========================================================
    // FULL JUDGMENT PAGE
    // =========================================================

    public Judgment ParseJudgmentDetail(
        string html,
        Judgment judgment)
    {
        var config = Configuration.Default;

        var context =
            BrowsingContext.New(config);

        var document =
            context.OpenAsync(req =>
                    req.Content(html))
                .GetAwaiter()
                .GetResult();

        // -----------------------------------------------------
        // GET TEXT BLOCKS
        // -----------------------------------------------------
        //
        // NKP pages are not completely consistent.
        //
        // Some pages contain #faisala_detail.
        // Some pages do not.
        //
        // Therefore:
        //
        // 1. Try #faisala_detail first.
        // 2. If not found, parse the page body.
        //
        // -----------------------------------------------------

        IElement? detail =
      document
          .QuerySelectorAll("div")
          .FirstOrDefault(x =>
              x.GetAttribute("id")?
                  .Trim()
                  .Equals(
                      "faisala_detail",
                      StringComparison.OrdinalIgnoreCase)
              == true);

        List<string> paragraphs;

        if (detail != null)
        {
            paragraphs = detail
                .QuerySelectorAll("p")
                .Select(p => CleanText(p.TextContent))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList();

            Console.WriteLine(
                "Using #faisala_detail.");
        }
        else
        {
            Console.WriteLine(
                "WARNING: #faisala_detail not found. Parsing page body.");

            paragraphs = document
                .QuerySelectorAll("body *")
                .Where(e =>
                    e.Children.Length == 0 &&
                    !string.IsNullOrWhiteSpace(e.TextContent))
                .Select(e => CleanText(e.TextContent))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList();
        }

        Console.WriteLine(
            $"Text blocks found: {paragraphs.Count}");

        // -----------------------------------------------------
        // NO TEXT
        // -----------------------------------------------------

        if (paragraphs.Count == 0)
        {
            Console.WriteLine(
                "WARNING: No text blocks found in judgment page.");

            return judgment;
        }

        // -----------------------------------------------------
        // FULL TEXT
        // -----------------------------------------------------

        judgment.FullText =
            string.Join(
                Environment.NewLine,
                paragraphs);

        Console.WriteLine(
            $"Extracted judgment text: {judgment.FullText.Length} characters");


        // =====================================================
        // DECISION DATE
        // =====================================================

        var decisionDate =
            ExtractDecisionDate(paragraphs);

        if (!string.IsNullOrWhiteSpace(decisionDate))
        {
            judgment.DecisionDate =
                decisionDate;
        }


        // =====================================================
        // CASE NUMBER
        // =====================================================

        var caseNumber =
            ExtractCaseNumber(paragraphs);

        if (!string.IsNullOrWhiteSpace(caseNumber))
        {
            judgment.CaseNumber =
                caseNumber;
        }


        // =====================================================
        // CASE NAME
        // =====================================================

        var caseName =
            ExtractJudgmentCaseName(paragraphs);

        if (!string.IsNullOrWhiteSpace(caseName))
        {
            judgment.CaseName =
                caseName;
        }


        // =====================================================
        // COURT
        // =====================================================

        var bench = ExtractBench(string.Join(" ", paragraphs));
        if (!string.IsNullOrWhiteSpace(bench))
        {
            judgment.Court = bench;
        }
        else if (string.IsNullOrWhiteSpace(judgment.Court))
        {
            var courtParagraph =
                paragraphs.FirstOrDefault(x =>
                    x.Contains("सर्वोच्च अदालत"));

            if (!string.IsNullOrWhiteSpace(courtParagraph) && courtParagraph.Length < 80)
            {
                judgment.Court = courtParagraph;
            }
        }


        // =====================================================
        // JUDGES
        // =====================================================

        var judges =
            paragraphs
                .Where(x =>
                    x.Contains("माननीय न्यायाधीश"))
                .ToList();

        if (judges.Count > 0)
        {
            judgment.Judges =
                string.Join(
                    " | ",
                    judges);
        }


        // =====================================================
        // PETITIONER
        // =====================================================

        var petitioner =
            paragraphs.FirstOrDefault(x =>
                x.Contains("पुनरावेदक / प्रतिवादी") ||
                x.Contains("पुनरावेदक/प्रतिवादी") ||
                x.Contains("पुनरावेदक"));

        if (!string.IsNullOrWhiteSpace(petitioner))
        {
            judgment.Petitioner =
                petitioner;
        }


        // =====================================================
        // RESPONDENT
        // =====================================================

        var respondent =
            paragraphs.FirstOrDefault(x =>
                x.Contains("प्रत्यर्थी / वादी") ||
                x.Contains("प्रत्यर्थी/वादी") ||
                x.Contains("प्रत्यर्थी"));

        if (!string.IsNullOrWhiteSpace(respondent))
        {
            judgment.Respondent =
                respondent;
        }


        // =====================================================
        // LAWS
        // =====================================================

        var lawIndex =
            FindIndexContaining(
                paragraphs,
                "सम्बद्ध कानून");

        if (lawIndex >= 0)
        {
            var laws =
                new List<string>();

            for (
                var i = lawIndex + 1;
                i < paragraphs.Count;
                i++)
            {
                var value =
                    paragraphs[i];

                // Stop at the next known section.
                if (
                    value.Contains("सुरू तहमा") ||
                    value.Contains("पुनरावेदन तहमा") ||
                    value.Contains("अवलम्बित नजिर") ||
                    value.Contains("प्रकरण नं")
                )
                {
                    break;
                }

                if (!string.IsNullOrWhiteSpace(value))
                {
                    laws.Add(value);
                }
            }

            judgment.Laws =
                string.Join(
                    " | ",
                    laws);
        }


        // =====================================================
        // PRECEDENTS
        // =====================================================

        var precedentIndex =
            FindIndexContaining(
                paragraphs,
                "अवलम्बित नजिर");

        if (precedentIndex >= 0)
        {
            var precedents =
                new List<string>();

            for (
                var i = precedentIndex + 1;
                i < paragraphs.Count;
                i++)
            {
                var value =
                    paragraphs[i];

                if (
                    value.Contains("सम्बद्ध कानून") ||
                    value.Contains("सुरू तहमा") ||
                    value.Contains("पुनरावेदन तहमा"))
                {
                    break;
                }

                if (!string.IsNullOrWhiteSpace(value))
                {
                    precedents.Add(value);
                }
            }

            judgment.Precedents =
                string.Join(
                    " | ",
                    precedents);
        }


        // =====================================================
        // SUMMARY / LEGAL PRINCIPLE
        // =====================================================

        judgment.Summary = ExtractSummary(paragraphs, judgment.FullText);


        // =====================================================
        // PRINT RESULT
        // =====================================================

        Console.WriteLine(
            $"Date: {judgment.DecisionDate}");

        Console.WriteLine(
            $"Case No: {judgment.CaseNumber}");

        Console.WriteLine(
            $"Court: {judgment.Court}");

        Console.WriteLine(
            $"Judges: {judgment.Judges}");

        Console.WriteLine(
            $"Text: {judgment.FullText?.Length ?? 0} characters");

        return judgment;
    }


    // =========================================================
    // FIND INDEX CONTAINING
    // =========================================================

    private static int FindIndexContaining(
        List<string> values,
        string search)
    {
        return values.FindIndex(x =>
            x.Contains(
                search,
                StringComparison.Ordinal));
    }


    private static (string Petitioner, string Respondent) ExtractParties(string? text)
    {
        text = CleanText(text);
        if (string.IsNullOrWhiteSpace(text))
            return ("", "");

        var split = Regex.Split(text, @"बिरुद्ध|विरुद्ध");
        var petitioner = split.Length > 0 ? split[0].Trim(" :".ToCharArray()) : "";
        var respondent = split.Length > 1 ? split[1].Trim(" :".ToCharArray()) : "";
        return (petitioner, respondent);
    }

    private static string ExtractBench(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        string? bench = null;
        if (text.Contains("पूर्ण इजलास"))
            bench = "पूर्ण इजलास";
        else if (text.Contains("संयुक्त इजलास"))
            bench = "संयुक्त इजलास";
        else if (text.Contains("एकल इजलास"))
            bench = "एकल इजलास";

        if (string.IsNullOrWhiteSpace(bench))
            return text.Contains("सर्वोच्च अदालत") ? "सर्वोच्च अदालत" : "";

        return "सर्वोच्च अदालत · " + bench;
    }

    // =========================================================
    // DECISION DATE
    // =========================================================

    private static string ExtractDecisionDate(
        List<string> paragraphs)
    {
        foreach (var paragraph in paragraphs)
        {
            if (!paragraph.Contains("फैसला मिति"))
                continue;

            // -------------------------------------------------
            // Example:
            //
            // फैसला मिति : २०८१/०१/०४
            //
            // -------------------------------------------------

            var match =
                Regex.Match(
                    paragraph,
                    @"फैसला\s*मिति\s*[:：]?\s*" +
                    @"([०-९0-9]{2,4}" +
                    @"[./।-]" +
                    @"[०-९0-9]{1,2}" +
                    @"[./।-]" +
                    @"[०-९0-9]{1,2})");

            if (match.Success)
            {
                return
                    match.Groups[1]
                        .Value
                        .Trim();
            }
        }

        return "";
    }


    // =========================================================
    // CASE NUMBER
    // =========================================================

    private static string ExtractCaseNumber(
        List<string> paragraphs)
    {
        foreach (var paragraph in paragraphs)
        {
            // -------------------------------------------------
            // FORMAT 1
            //
            // मुद्दा नं : ०७४-CR-०१७१
            //
            // -------------------------------------------------

            var match =
                Regex.Match(
                    paragraph,
                    @"मुद्दा\s*नं\s*[:：]?\s*" +
                    @"([०-९0-9]+[-–—][A-Za-z]+[-–—][०-९0-9]+)",
                    RegexOptions.IgnoreCase);

            if (match.Success)
            {
                return
                    match.Groups[1]
                        .Value
                        .Trim();
            }

            // -------------------------------------------------
            // FORMAT 2
            //
            // ०७४-CR-०१७१
            //
            // -------------------------------------------------

            match =
                Regex.Match(
                    paragraph,
                    @"\b" +
                    @"[०-९0-9]{2,4}" +
                    @"[-–—]" +
                    @"[A-Za-z]{2,5}" +
                    @"[-–—]" +
                    @"[०-९0-9]{2,6}" +
                    @"\b",
                    RegexOptions.IgnoreCase);

            if (match.Success)
            {
                return
                    match.Value
                        .Trim();
            }
        }

        return "";
    }


    // =========================================================
    // CASE NAME FROM JUDGMENT PAGE
    // =========================================================

    private static string ExtractJudgmentCaseName(
        List<string> paragraphs)
    {
        foreach (var paragraph in paragraphs)
        {
            if (!paragraph.StartsWith("मुद्दा"))
                continue;

            var caseName =
                paragraph
                    .Replace("मुद्दा नं :", "")
                    .Replace("मुद्दा नं:", "")
                    .Replace("मुद्दा नं", "")
                    .Replace("मुद्दा:", "")
                    .Replace("मुद्दा :", "")
                    .Replace("मुद्दा:-", "")
                    .Replace("मुद्दा :-", "")
                    .Replace("मुद्दा:–", "")
                    .Replace("मुद्दा :–", "")
                    .Trim();

            // If this is actually a case number line,
            // don't use it as case name.
            if (Regex.IsMatch(
                caseName,
                @"^[०-९0-9]{2,4}[-–—][A-Za-z]{2,5}[-–—][०-९0-9]{2,6}$"))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(caseName))
            {
                return caseName;
            }
        }

        return "";
    }


    // =========================================================
    // SEARCH RESULT DECISION NUMBER
    // =========================================================

    private static string ExtractDecisionNumber(
        string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        // -----------------------------------------------------
        // Normal format:
        //
        // निर्णय नं. ११४७८ - लागु औषध
        //
        // -----------------------------------------------------

        var match =
            Regex.Match(
                text,
                @"निर्णय\s*नं\.?\s*[:：]?\s*([०-९0-9]+)");

        if (match.Success)
        {
            return
                match.Groups[1]
                    .Value
                    .Trim();
        }

        // -----------------------------------------------------
        // If decision number doesn't exist, try a standalone
        // Nepali number near the beginning.
        // -----------------------------------------------------

        match =
            Regex.Match(
                text,
                @"^[^०-९0-9]*([०-९0-9]{3,6})");

        if (match.Success)
        {
            return
                match.Groups[1]
                    .Value
                    .Trim();
        }

        return "";
    }


    // =========================================================
    // SEARCH RESULT CASE NAME
    // =========================================================

    private static string ExtractCaseName(
        string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        // -----------------------------------------------------
        // Example:
        //
        // निर्णय नं. ९३१९ - उत्प्रेषण
        //
        // Result:
        //
        // उत्प्रेषण
        //
        // -----------------------------------------------------

        var match =
            Regex.Match(
                text,
                @"निर्णय\s*नं\.?\s*[:：]?\s*" +
                @"[०-९0-9]+\s*" +
                @"[-–—]\s*" +
                @"(.+)$");

        if (match.Success)
        {
            return
                match.Groups[1]
                    .Value
                    .Trim();
        }

        return text.Trim();
    }


    // =========================================================
    // NEPALI NUMBER
    // =========================================================

    private static int ExtractNepaliNumber(
        string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        var match =
            Regex.Match(
                text,
                @"[०-९]+");

        if (!match.Success)
            return 0;

        var english =
            new string(
                match.Value
                    .Select(ConvertNepaliDigit)
                    .ToArray());

        return int.TryParse(
            english,
            out var number)
            ? number
            : 0;
    }


    // =========================================================
    // NEPALI DIGIT -> ENGLISH DIGIT
    // =========================================================

    private static char ConvertNepaliDigit(
        char c)
    {
        return c switch
        {
            '०' => '0',
            '१' => '1',
            '२' => '2',
            '३' => '3',
            '४' => '4',
            '५' => '5',
            '६' => '6',
            '७' => '7',
            '८' => '8',
            '९' => '9',
            _ => c
        };
    }


    // =========================================================
    // CLEAN TEXT
    // =========================================================

    private static string CleanText(
        string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        return string.Join(
            " ",
            text.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries));
    }


    // =========================================================
    // ABSOLUTE URL
    // =========================================================

    private static string MakeAbsoluteUrl(
        string href)
    {
        if (string.IsNullOrWhiteSpace(href))
            return "";

        if (href.StartsWith(
            "http",
            StringComparison.OrdinalIgnoreCase))
        {
            return href;
        }

        return
            "https://nkp.gov.np" +
            (
                href.StartsWith("/")
                    ? href
                    : "/" + href
            );
    }

    public static string ExtractSummary(List<string> paragraphs, string fullText)
    {
        if (paragraphs != null && paragraphs.Count > 0)
        {
            var prakaranIdx = paragraphs.FindIndex(p => Regex.IsMatch(p, @"\(प्रकरण\s*नं\.?\s*[०-९0-9]+\s*\)", RegexOptions.IgnoreCase));
            if (prakaranIdx >= 0)
            {
                if (prakaranIdx > 0 && paragraphs[prakaranIdx - 1].Length > 20)
                {
                    return paragraphs[prakaranIdx - 1].Trim() + " " + paragraphs[prakaranIdx].Trim();
                }
                return paragraphs[prakaranIdx].Trim();
            }

            var principle = paragraphs.FirstOrDefault(p =>
                (p.Contains("भन्‍ने") || p.Contains("सिद्धान्त") || p.Contains("अदालत")) &&
                p.Length > 40 &&
                !p.StartsWith("सर्वोच्च अदालत") &&
                !p.StartsWith("माननीय न्यायाधीश") &&
                !p.StartsWith("पुनरावेदक") &&
                !p.StartsWith("प्रत्यर्थी") &&
                !p.StartsWith("फैसला मिति"));

            if (!string.IsNullOrWhiteSpace(principle))
            {
                return principle.Trim();
            }
        }

        if (!string.IsNullOrWhiteSpace(fullText))
        {
            var cleaned = Regex.Replace(fullText, @"\s+", " ").Trim();
            var bodyIndex = cleaned.IndexOf("फैसला");
            if (bodyIndex > 0 && bodyIndex + 50 < cleaned.Length)
            {
                cleaned = cleaned.Substring(bodyIndex);
            }

            return cleaned.Length <= 250 ? cleaned : cleaned.Substring(0, 247) + "...";
        }

        return "";
    }
}