using System.Text;
using System.Text.RegularExpressions;
using Eizo.Metadata.Recognition;

namespace Eizo.Metadata.Core;

public enum MetadataSubjectKind
{
    Unknown = 0,
    Series = 1,
    Movie = 2,
}

public enum MetadataEpisodeKind
{
    Regular = 0,
    Special = 1,
    Opening = 2,
    Ending = 3,
    Trailer = 4,
    Other = 5,
}

public sealed record MetadataProviderItemId(
    string Provider,
    string Value,
    MetadataSubjectKind Kind);

public sealed record MetadataTitles(
    string Primary,
    string? Original,
    IReadOnlyDictionary<string, string> Localized,
    IReadOnlyList<string> Aliases)
{
    public IEnumerable<string> EnumerateAll()
    {
        if (!string.IsNullOrWhiteSpace(Primary))
        {
            yield return Primary;
        }

        if (!string.IsNullOrWhiteSpace(Original))
        {
            yield return Original;
        }

        foreach (var value in Localized.Values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return value;
            }
        }

        foreach (var alias in Aliases)
        {
            if (!string.IsNullOrWhiteSpace(alias))
            {
                yield return alias;
            }
        }
    }
}

public sealed record MetadataArtwork(
    string? PosterUrl,
    string? BackdropUrl,
    string? ThumbnailUrl);

public sealed record MetadataSearchRequest(
    IReadOnlyList<string> Titles,
    int? Year,
    MediaKind RecognitionMediaKind,
    int? SeasonNumber,
    decimal? EpisodeNumber,
    string? PreferredLanguage,
    int Limit = 10)
{
    public MetadataSearchRequest ForProviderSearch() =>
        MetadataSearchRequestNormalizer.Normalize(this);

    public static MetadataSearchRequest FromRecognition(
        RecognitionResult recognition,
        string? preferredLanguage = null,
        int limit = 10)
    {
        ArgumentNullException.ThrowIfNull(recognition);

        var titles = new List<string>();

        static void AddTitleAndSearchVariant(List<string> destination, string? value)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                MetadataSearchTitleNormalizer.IsWeakStandaloneTitle(value))
            {
                return;
            }

            var title = value.Trim();
            if (!destination.Contains(title, StringComparer.OrdinalIgnoreCase))
            {
                destination.Add(title);
            }

            var normalized = MetadataSearchTitleNormalizer.Normalize(title);
            if (!string.IsNullOrWhiteSpace(normalized) &&
                !MetadataSearchTitleNormalizer.IsWeakStandaloneTitle(normalized) &&
                !destination.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                destination.Add(normalized);
            }
        }

        AddTitleAndSearchVariant(titles, recognition.Title);

        foreach (var candidate in recognition.TitleCandidates
                     .OrderByDescending(static item => item.IsPrimary)
                     .ThenByDescending(static item => item.Confidence))
        {
            AddTitleAndSearchVariant(titles, candidate.Title);
        }

        // Never turn a low-quality recognition into a broad provider query. If
        // Recognition only produced a weak token, preserve it as a final fallback
        // so the resolver remains debuggable, but do not mix it with strong titles.
        if (titles.Count == 0 && !string.IsNullOrWhiteSpace(recognition.Title))
        {
            titles.Add(recognition.Title.Trim());
        }

        var year = recognition.Year ??
                   MetadataSearchTitleNormalizer.TryExtractTrailingYear(recognition.Title);

        return new MetadataSearchRequest(
            titles.Take(6).ToArray(),
            year,
            recognition.MediaKind,
            recognition.SeasonNumber,
            recognition.EpisodeNumber ?? recognition.SpecialNumber,
            preferredLanguage,
            Math.Clamp(limit, 1, 25));
    }
}

internal static class MetadataSearchTitleNormalizer
{
    private const RegexOptions Options =
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(50);

    private static readonly Regex LeadingLibraryOrdinalRegex = new(
        @"^\s*\d{1,2}\s*[._-]?\s*(?:\[(?:19|20)\d{2}\s*[-~–—−]\s*(?:19|20)\d{2}\]\s*)?",
        Options,
        Timeout);

    private static readonly Regex LeadingSeasonTokenRegex = new(
        @"^\s*S(?:EASON)?\s*0?\d{1,2}\s+(?=\p{L}|\p{N}|[\u3040-\u30ff\u3400-\u9fff])",
        Options,
        Timeout);

    private static readonly Regex NamedSeasonSemanticRegex = new(
        @"^\s*(?:(?:S(?:EASON)?\s*0?(?<en>\d{1,2}))|(?<ord>\d{1,2})(?:ST|ND|RD|TH)\s+SEASON|第\s*(?<cn>[一二三四五六七八九十两兩〇零壹贰貳叁參肆伍陆陸柒捌玖拾\d]{1,3})\s*(?:季|期))\s*[:：._-]?\s*(?<title>.+?)\s*$",
        Options,
        Timeout);

    private static readonly Regex SeasonCoverageRangeRegex = new(
        @"(?<![\p{L}\p{N}])(?:S(?:EASON)?\s*0?\d{1,2}\s*(?:-|~|～|–|—|−|TO|THROUGH|至|到)\s*(?:S(?:EASON)?\s*)?0?\d{1,2}|第?\s*[一二三四五六七八九十两兩〇零壹贰貳叁參肆伍陆陸柒捌玖拾\d]{1,3}\s*季\s*(?:-|~|～|–|—|−|TO|THROUGH|至|到)\s*第?\s*[一二三四五六七八九十两兩〇零壹贰貳叁參肆伍陆陸柒捌玖拾\d]{1,3}\s*季)(?:\s*(?:全|全集|COMPLETE|ALL))?",
        Options,
        Timeout);

    private static readonly Regex TrailingYearRegex = new(
        @"(?:[. _-]+|\s*\()(?<year>(?:19|20)\d{2})\)?\s*$",
        Options,
        Timeout);

    private static readonly Regex ProviderIdSuffixRegex = new(
        @"\s*\{\s*(?:tmdb|tmdbid|tvdb|imdb)\s*[-_:]?\s*[^}]+\}\s*$",
        Options,
        Timeout);

    private static readonly Regex WeakStandaloneTitleRegex = new(
        @"^\s*(?:\d{1,2}[. _-]*)?(?:S(?:EASON)?\s*0?\d{1,2}|第\s*[一二三四五六七八九十两兩〇零壹贰貳叁參肆伍陆陸柒捌玖拾\d]{1,3}\s*季|PART\s*\d{1,2}|COUR\s*\d{1,2}|VOL(?:UME)?\s*\d{1,3})\s*$",
        Options,
        Timeout);

    private static readonly Regex BracketOnlyTitleRegex = new(
        @"^\s*(?:\[[^\]]{1,80}\]|【[^】]{1,80}】|\([^)]{1,80}\))\s*$",
        RegexOptions.CultureInvariant,
        Timeout);

    private static readonly Regex ReleaseGroupOnlyRegex = new(
        @"^\s*[\p{L}\p{N}][\p{L}\p{N} ._&+-]{0,48}(?:STUDIO|RAWS?|SUBS?|字幕(?:组|組|社))\s*$",
        Options,
        Timeout);

    private static readonly Regex MultiSeparatorRegex = new(
        @"[._]+",
        RegexOptions.CultureInvariant,
        Timeout);

    private static readonly Regex MultiWhitespaceRegex = new(
        @"\s+",
        RegexOptions.CultureInvariant,
        Timeout);

    internal static int? TryExtractTrailingYear(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Normalize(NormalizationForm.FormKC).Trim();
        var match = TrailingYearRegex.Match(normalized);
        return match.Success &&
               int.TryParse(match.Groups["year"].Value, out var year)
            ? year
            : null;
    }

    internal static bool TryExtractNamedSeasonSemanticTitle(
        string? value,
        out string semanticTitle)
    {
        semanticTitle = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Normalize(NormalizationForm.FormKC).Trim();
        if (SeasonCoverageRangeRegex.IsMatch(normalized))
        {
            return false;
        }

        var match = NamedSeasonSemanticRegex.Match(normalized);
        if (!match.Success)
        {
            return false;
        }

        semanticTitle = match.Groups["title"].Value
            .Trim(' ', '-', '–', '—', '−', '_', '.', ':', '：');

        return semanticTitle.Length >= 2 &&
               semanticTitle.Any(static c => char.IsLetterOrDigit(c));
    }

    internal static bool IsSeasonCoverageRange(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return SeasonCoverageRangeRegex.IsMatch(
            value.Normalize(NormalizationForm.FormKC));
    }

    internal static bool IsWeakStandaloneTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var normalized = value.Normalize(NormalizationForm.FormKC).Trim();
        var withoutCoverage = SeasonCoverageRangeRegex
            .Replace(normalized, string.Empty)
            .Trim();

        return normalized.Length < 2 ||
               WeakStandaloneTitleRegex.IsMatch(normalized) ||
               (SeasonCoverageRangeRegex.IsMatch(normalized) &&
                withoutCoverage is "" or "全" or "全集") ||
               BracketOnlyTitleRegex.IsMatch(normalized) ||
               ReleaseGroupOnlyRegex.IsMatch(normalized);
    }

    internal static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value
            .Normalize(NormalizationForm.FormKC)
            .Replace('꞉', ':')
            .Trim();

        normalized = ProviderIdSuffixRegex.Replace(normalized, string.Empty);
        normalized = LeadingLibraryOrdinalRegex.Replace(normalized, string.Empty);
        normalized = SeasonCoverageRangeRegex.Replace(normalized, " ");

        if (TryExtractNamedSeasonSemanticTitle(normalized, out var namedSeasonTitle))
        {
            normalized = namedSeasonTitle;
        }

        normalized = LeadingSeasonTokenRegex.Replace(normalized, string.Empty);
        normalized = TrailingYearRegex.Replace(normalized, string.Empty);
        normalized = MultiSeparatorRegex.Replace(normalized, " ");
        normalized = MultiWhitespaceRegex.Replace(normalized, " ").Trim();

        return normalized.Trim(' ', '-', '–', '—', '−', '_', '.');
    }
}

internal static class MetadataSearchRequestNormalizer
{
    internal static MetadataSearchRequest Normalize(MetadataSearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var titles = new List<string>();
        string? fallback = null;

        foreach (var value in request.Titles)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var title = value.Trim();
            fallback ??= title;

            if (MetadataSearchTitleNormalizer.IsWeakStandaloneTitle(title))
            {
                continue;
            }

            if (!titles.Contains(title, StringComparer.OrdinalIgnoreCase))
            {
                titles.Add(title);
            }

            var normalized = MetadataSearchTitleNormalizer.Normalize(title);
            if (!string.IsNullOrWhiteSpace(normalized) &&
                !MetadataSearchTitleNormalizer.IsWeakStandaloneTitle(normalized) &&
                !titles.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                titles.Add(normalized);
            }
        }

        if (titles.Count == 0 && fallback is not null)
        {
            titles.Add(fallback);
        }

        return request with
        {
            Titles = titles.Take(8).ToArray(),
            Limit = Math.Clamp(request.Limit, 1, 25),
        };
    }
}

public sealed record MetadataSearchCandidate(
    MetadataProviderItemId Id,
    MetadataTitles Titles,
    int? Year,
    int ProviderRank,
    double? Popularity = null);

public sealed record MetadataSubject(
    MetadataProviderItemId Id,
    MetadataTitles Titles,
    string? Overview,
    DateOnly? ReleaseDate,
    int? EpisodeCount,
    MetadataArtwork Artwork,
    IReadOnlyDictionary<string, string> ExternalIds);

public sealed record MetadataEpisode(
    string ProviderEpisodeId,
    MetadataProviderItemId SubjectId,
    int? SeasonNumber,
    decimal? EpisodeNumber,
    MetadataEpisodeKind Kind,
    MetadataTitles Titles,
    string? Overview,
    DateOnly? AirDate,
    string? ThumbnailUrl);

public sealed record MetadataProviderError(
    string Provider,
    string ErrorType,
    string Message);

public sealed record MetadataResolutionCandidate(
    MetadataSearchCandidate Candidate,
    double Score,
    IReadOnlyList<string> Evidence);

public sealed record MetadataResolution(
    MetadataResolutionCandidate? Best,
    bool IsResolved,
    double Confidence,
    IReadOnlyList<MetadataResolutionCandidate> Candidates,
    IReadOnlyList<MetadataProviderError> ProviderErrors);

public sealed record MetadataEnrichmentResult(
    MetadataResolution Resolution,
    MetadataSubject? Subject,
    MetadataEpisode? Episode,
    IReadOnlyList<MetadataProviderError> ProviderErrors);

public sealed record MetadataResolverOptions(
    double AutoResolveThreshold = 0.82,
    double MinimumLead = 0.06,
    int MaxCandidates = 20);

public sealed record MetadataCachePolicy(
    TimeSpan SearchTtl,
    TimeSpan SubjectTtl,
    TimeSpan EpisodesTtl)
{
    public static MetadataCachePolicy Default { get; } = new(
        TimeSpan.FromHours(12),
        TimeSpan.FromDays(1),
        TimeSpan.FromHours(6));
}
