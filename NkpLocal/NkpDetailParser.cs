using System;
using System.Collections.Generic;
using System.Linq;
using AngleSharp;
using AngleSharp.Dom;

public class NkpDetailParser
{
    public Judgment Parse(string html, string url)
    {
        var config = Configuration.Default;
        var context = BrowsingContext.New(config);

        var document = context
            .OpenAsync(req => req.Content(html))
            .GetAwaiter()
            .GetResult();

        var judgment = new Judgment
        {
            SourceUrl = url
        };

        ExtractHeader(document, judgment);

        var detail = document
            .QuerySelectorAll("div")
            .FirstOrDefault(x =>
                x.GetAttribute("id")?
                    .Trim() == "faisala_detail");

        if (detail != null)
        {
            var paragraphs = detail
                .QuerySelectorAll("p")
                .Select(p => CleanText(p.TextContent))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            judgment.FullText = string.Join(
                Environment.NewLine + Environment.NewLine,
                paragraphs);

            judgment.Summary = ExtractSummary(paragraphs);
        }

        return judgment;
    }

    private void ExtractHeader(
        IDocument document,
        Judgment judgment)
    {
        var title = document
            .QuerySelector("#decision_summary .post-title");

        if (title != null)
        {
            var text = CleanText(title.TextContent);

            ExtractDecisionInformation(
                text,
                judgment);
        }

        var edition = document
            .QuerySelector("#edition-info");

        if (edition != null)
        {
            var spans = edition.QuerySelectorAll("span");

            foreach (var span in spans)
            {
                var text = CleanText(span.TextContent);

                if (text.StartsWith("भाग:"))
                    judgment.Volume = ExtractStrongValue(span);

                else if (text.StartsWith("साल:"))
                    judgment.Year = ExtractStrongValue(span);

                else if (text.StartsWith("महिना:"))
                    judgment.Month = ExtractStrongValue(span);

                else if (text.StartsWith("अंक:"))
                    judgment.Edition = ExtractStrongValue(span);
            }
        }

        var meta = document
            .QuerySelector("#decision_summary .post-meta");

        if (meta != null)
        {
            var text = CleanText(meta.TextContent);

            var dateIndex = text.IndexOf("फैसला मिति");

            if (dateIndex >= 0)
            {
                var dateText = text[(dateIndex + "फैसला मिति".Length)..];

                var separator = dateText.IndexOf("४६९");

                if (separator >= 0)
                    dateText = dateText[..separator];

                judgment.DecisionDateNepali =
                    CleanText(dateText);
            }
        }
    }

    private static void ExtractDecisionInformation(
        string text,
        Judgment judgment)
    {
        const string prefix = "निर्णय नं.";

        var index = text.IndexOf(prefix);

        if (index < 0)
            return;

        var remaining =
            text[(index + prefix.Length)..]
                .Trim();

        var separator = remaining.IndexOf('-');

        if (separator >= 0)
        {
            judgment.DecisionNumber =
                remaining[..separator].Trim();

            judgment.CaseName =
                remaining[(separator + 1)..].Trim();
        }
        else
        {
            judgment.DecisionNumber = remaining;
        }
    }

    private static string ExtractStrongValue(IElement element)
    {
        var strong = element.QuerySelector("strong");

        return strong == null
            ? ""
            : CleanText(strong.TextContent);
    }

    private static string ExtractSummary(
        List<string> paragraphs)
    {
        for (int i = 1; i < paragraphs.Count; i++)
        {
            if (paragraphs[i].Contains("(प्रकरण नं."))
            {
                return paragraphs[i - 1];
            }
        }

        return "";
    }

    private static string CleanText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        return string.Join(
            " ",
            text.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries));
    }
}