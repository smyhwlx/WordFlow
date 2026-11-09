using WordFlow.Application.Ports;

namespace WordFlow.App.ViewModels;

/// <summary>
/// One row in the prefix suggestion drop-down. Keeps display formatting out of the
/// application-layer <see cref="VocabularyWord"/> record.
/// </summary>
public sealed record WordSuggestion(VocabularyWord Word)
{
    public string Lemma => Word.Lemma;

    public string PhoneticText
    {
        get
        {
            string phonetic = Word.Phonetic.Trim().Trim('/');
            return phonetic.Length == 0 ? "" : $"/{phonetic}/";
        }
    }

    public string ChinesePreview
    {
        get
        {
            string chinese = Word.Chinese;
            if (string.IsNullOrWhiteSpace(chinese)) return "";
            int lineBreak = chinese.IndexOfAny(['\r', '\n']);
            string firstLine = (lineBreak >= 0 ? chinese[..lineBreak] : chinese).Trim();
            if (firstLine.Length == 0)
            {
                string flat = chinese.Replace('\r', ' ').Replace('\n', ' ').Trim();
                firstLine = flat;
            }
            return firstLine.Length <= 46 ? firstLine : firstLine[..46] + "…";
        }
    }
}

/// <summary>
/// Pure prefix matching for the control-center search boxes. Typing "sa" must offer
/// every headword that starts with "sa"; a typo simply produces no candidates, which
/// keeps the drop-down hidden instead of guessing.
/// </summary>
internal static class VocabularyPrefixMatcher
{
    internal const int DefaultLimit = 20;

    internal static IReadOnlyList<VocabularyWord> Match(IReadOnlyList<VocabularyWord> words, string? query, int limit = DefaultLimit)
    {
        if (words is null || words.Count == 0 || limit <= 0) return Array.Empty<VocabularyWord>();
        string needle = (query ?? "").Trim();
        if (needle.Length == 0) return Array.Empty<VocabularyWord>();

        bool asciiQuery = IsAsciiQuery(needle);
        var hits = new List<VocabularyWord>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var word in words)
        {
            if (string.IsNullOrWhiteSpace(word.Lemma)) continue;
            bool matched = asciiQuery
                ? word.Lemma.StartsWith(needle, StringComparison.OrdinalIgnoreCase)
                : word.Chinese.Contains(needle, StringComparison.OrdinalIgnoreCase);
            if (matched && seen.Add(word.Lemma)) hits.Add(word);
        }
        if (hits.Count == 0) return Array.Empty<VocabularyWord>();

        return hits
            .OrderBy(word => string.Equals(word.Lemma, needle, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(word => word.FrequencyRank ?? int.MaxValue)
            .ThenBy(word => word.Lemma.Length)
            .ThenBy(word => word.Lemma, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToArray();
    }

    private static bool IsAsciiQuery(string value)
    {
        foreach (char c in value)
        {
            if (c > 127) return false;
        }
        return true;
    }
}
