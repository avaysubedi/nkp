using System;
using System.Collections.Generic;
using System.Linq;

public static class CaseClassifier
{
    public static readonly string[] TopicCatalog =
    {
        "जबरजस्ती करणी",
        "कर्तव्य ज्यान",
        "सवारी ज्यान",
        "सवारी अङ्गभङ्ग",
        "लागु औषध",
        "लागू औषध",
        "भ्रष्टाचार",
        "चोरी",
        "डकैती",
        "ठगी",
        "किर्ते",
        "अपहरण",
        "मानव बेचबिखन",
        "अंशबन्डा",
        "अंश जालसाजी",
        "अंश नामसारी",
        "अंश चलन",
        "अंश",
        "निज अर्जन",
        "सगोल",
        "लेनदेन",
        "उत्प्रेषण",
        "परमादेश",
        "निषेधाज्ञा",
        "प्रतिषेध",
        "बन्दी प्रत्यक्षीकरण",
        "बन्दीप्रत्यक्षीकरण"
    };

    private static readonly string[] WritMarkers =
    {
        "उत्प्रेषण",
        "परमादेश",
        "निषेधाज्ञा",
        "प्रतिषेध",
        "बन्दी प्रत्यक्षीकरण",
        "बन्दीप्रत्यक्षीकरण",
        "अधिकारपृच्छा",
        "रिट",
        "जो चाहिने आदेश"
    };

    private static readonly string[] CriminalMarkers =
    {
        "कर्तव्य ज्यान",
        "ज्यान मार्ने",
        "हत्या",
        "जबरजस्ती करणी",
        "बलात्कार",
        "लागु औषध",
        "लागू औषध",
        "सवारी ज्यान",
        "सवारी अङ्गभङ्ग",
        "सवारी दुर्घटना",
        "भ्रष्टाचार",
        "चोरी",
        "डकैती",
        "डाका",
        "ठगी",
        "किर्ते",
        "अपहरण",
        "शरीर बन्धक",
        "मानव बेचबिखन",
        "ओसारपसार",
        "हातहतियार",
        "खरखजाना",
        "कुटपिट",
        "अङ्गभङ्ग",
        "गाली बेइज्जती",
        "बाल यौन",
        "बहुविवाह",
        "तस्करी",
        "भन्सार",
        "आतंक"
    };

    private static readonly string[] CivilMarkers =
    {
        "अंश",
        "निज अर्जन",
        "सगोल",
        "लेनदेन",
        "जग्गा",
        "मोही",
        "लिखत",
        "दान",
        "बकस",
        "अपुताली",
        "जिउनी",
        "भरणपोषण",
        "सम्बन्ध विच्छेद",
        "नामसारी",
        "करार",
        "तमसुक",
        "गुठी",
        "मिलापत्र"
    };

    public static void Apply(Judgment judgment)
    {
        var fromName = Classify(judgment.CaseName ?? "");
        if (!string.IsNullOrWhiteSpace(fromName.Category) || !string.IsNullOrWhiteSpace(fromName.Topics))
        {
            if (!string.IsNullOrWhiteSpace(fromName.Category))
                judgment.Category = fromName.Category;
            if (!string.IsNullOrWhiteSpace(fromName.Topics))
                judgment.Topics = fromName.Topics;
            return;
        }

        var haystack = string.Join(" ",
            judgment.CaseName,
            judgment.CaseNumber,
            judgment.Summary);

        var (category, topics) = Classify(haystack);
        if (!string.IsNullOrWhiteSpace(category))
            judgment.Category = category;
        if (!string.IsNullOrWhiteSpace(topics))
            judgment.Topics = topics;
    }

    public static (string Category, string Topics) Classify(string haystack)
    {
        haystack ??= "";

        var topics = TopicCatalog
            .Where(topic => haystack.Contains(topic, StringComparison.Ordinal))
            .Select(topic => topic == "लागू औषध" ? "लागु औषध"
                : topic == "बन्दीप्रत्यक्षीकरण" ? "बन्दी प्रत्यक्षीकरण"
                : topic)
            .Distinct()
            .ToList();
        topics = topics
            .Where(topic => !topics.Any(other => other != topic && other.Contains(topic, StringComparison.Ordinal)))
            .ToList();

        string category;
        if (WritMarkers.Any(marker => haystack.Contains(marker, StringComparison.Ordinal)))
            category = "रिट";
        else if (CriminalMarkers.Any(marker => haystack.Contains(marker, StringComparison.Ordinal)))
            category = "फौजदारी";
        else if (CivilMarkers.Any(marker => haystack.Contains(marker, StringComparison.Ordinal)))
            category = "देवानी";
        else if (haystack.Contains("निवेदन", StringComparison.Ordinal))
            category = "निवेदन";
        else
            category = "";

        return (category, string.Join(" | ", topics));
    }
}
