using System.Globalization;
using System.Text;
using AnitomyNg;

namespace Eizo.Metadata.Recognition.ShadowCompare;

public enum ShadowAgreement
{
    Exact = 0,
    Compatible = 1,
    Conflict = 2,
}

public sealed record AnitomyShadowSnapshot(
    string? Title,
    string? Season,
    IReadOnlyList<string> Episodes,
    string? Year,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Elements);

public sealed record ShadowComparisonResult(
    string Path,
    RecognitionResult Eizo,
    AnitomyShadowSnapshot Anitomy,
    ShadowAgreement Agreement,
    IReadOnlyList<string> Differences);

public sealed class ShadowComparisonEngine
{
    private readonly RecognitionEngine _eizo = new();

    public ShadowComparisonResult CompareSingle(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Compare(path, Anitomy.ParsePath(path));
    }

    public IReadOnlyList<ShadowComparisonResult> CompareTogether(
        IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.Count == 0)
        {
            return Array.Empty<ShadowComparisonResult>();
        }

        if (paths.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Paths must not contain blank values.", nameof(paths));
        }

        var parsed = Anitomy.ParseTogether(paths);
        if (parsed.Count != paths.Count)
        {
            return paths.Select(CompareSingle).ToArray();
        }

        var results = new ShadowComparisonResult[paths.Count];
        for (var i = 0; i < paths.Count; i++)
        {
            results[i] = Compare(paths[i], parsed[i]);
        }

        return results;
    }

    private ShadowComparisonResult Compare(
        string path,
        IReadOnlyList<Element> elements)
    {
        var eizo = _eizo.Recognize(new RecognitionRequest(path));
        var anitomy = Snapshot(elements);
        var differences = new List<string>();

        CompareTitle(eizo.Title, anitomy.Title, differences);
        CompareNumber(
            "season",
            eizo.SeasonNumber,
            TryParseInt(anitomy.Season),
            differences);
        CompareNumber(
            "episode",
            eizo.EpisodeNumber,
            TryParseDecimal(anitomy.Episodes.FirstOrDefault()),
            differences);
        CompareNumber(
            "year",
            eizo.Year,
            TryParseInt(anitomy.Year),
            differences);

        var comparableSignals = 0;
        if (!string.IsNullOrWhiteSpace(eizo.Title) &&
            !string.IsNullOrWhiteSpace(anitomy.Title))
        {
            comparableSignals++;
        }

        if (eizo.SeasonNumber is not null && TryParseInt(anitomy.Season) is not null)
        {
            comparableSignals++;
        }

        if (eizo.EpisodeNumber is not null &&
            TryParseDecimal(anitomy.Episodes.FirstOrDefault()) is not null)
        {
            comparableSignals++;
        }

        if (eizo.Year is not null && TryParseInt(anitomy.Year) is not null)
        {
            comparableSignals++;
        }

        var agreement = differences.Any(static item =>
                item.Contains("!=", StringComparison.Ordinal))
            ? ShadowAgreement.Conflict
            : comparableSignals >= 2
                ? ShadowAgreement.Exact
                : ShadowAgreement.Compatible;

        return new ShadowComparisonResult(
            path,
            eizo,
            anitomy,
            agreement,
            differences);
    }

    private static AnitomyShadowSnapshot Snapshot(
        IReadOnlyList<Element> elements)
    {
        var grouped = elements
            .GroupBy(static item => item.Kind.ToString(), StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => (IReadOnlyList<string>)group
                    .OrderBy(static item => item.Position)
                    .Select(static item => item.Value)
                    .ToArray(),
                StringComparer.Ordinal);

        return new AnitomyShadowSnapshot(
            First(elements, ElementKind.Title),
            First(elements, ElementKind.Season),
            All(elements, ElementKind.Episode),
            First(elements, ElementKind.Year),
            grouped);
    }

    private static string? First(
        IEnumerable<Element> elements,
        ElementKind kind) =>
        elements
            .Where(item => item.Kind == kind)
            .OrderBy(static item => item.Position)
            .Select(static item => item.Value)
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));

    private static IReadOnlyList<string> All(
        IEnumerable<Element> elements,
        ElementKind kind) =>
        elements
            .Where(item => item.Kind == kind)
            .OrderBy(static item => item.Position)
            .Select(static item => item.Value)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

    private static void CompareTitle(
        string? eizo,
        string? anitomy,
        ICollection<string> differences)
    {
        if (string.IsNullOrWhiteSpace(eizo) &&
            string.IsNullOrWhiteSpace(anitomy))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(eizo))
        {
            differences.Add($"title:eizo-missing|anitomy={anitomy}");
            return;
        }

        if (string.IsNullOrWhiteSpace(anitomy))
        {
            differences.Add($"title:eizo={eizo}|anitomy-missing");
            return;
        }

        if (!string.Equals(
                NormalizeTitle(eizo),
                NormalizeTitle(anitomy),
                StringComparison.Ordinal))
        {
            differences.Add($"title:{eizo}!={anitomy}");
        }
    }

    private static void CompareNumber<T>(
        string name,
        T? eizo,
        T? anitomy,
        ICollection<string> differences)
        where T : struct, IEquatable<T>
    {
        if (eizo is null && anitomy is null)
        {
            return;
        }

        if (eizo is null)
        {
            differences.Add($"{name}:eizo-missing|anitomy={anitomy}");
            return;
        }

        if (anitomy is null)
        {
            differences.Add($"{name}:eizo={eizo}|anitomy-missing");
            return;
        }

        if (!eizo.Value.Equals(anitomy.Value))
        {
            differences.Add($"{name}:{eizo}!={anitomy}");
        }
    }

    private static string NormalizeTitle(string value)
    {
        var normalized = value
            .Normalize(NormalizationForm.FormKC)
            .ToLowerInvariant();

        var builder = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    private static int? TryParseInt(string? value) =>
        int.TryParse(
            value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : null;

    private static decimal? TryParseDecimal(string? value) =>
        decimal.TryParse(
            value,
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : null;
}
