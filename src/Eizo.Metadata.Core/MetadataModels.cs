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
    public static MetadataSearchRequest FromRecognition(
        RecognitionResult recognition,
        string? preferredLanguage = null,
        int limit = 10)
    {
        ArgumentNullException.ThrowIfNull(recognition);

        var titles = new List<string>();
        if (!string.IsNullOrWhiteSpace(recognition.Title))
        {
            titles.Add(recognition.Title);
        }

        foreach (var candidate in recognition.TitleCandidates
                     .OrderByDescending(static item => item.IsPrimary)
                     .ThenByDescending(static item => item.Confidence))
        {
            if (!string.IsNullOrWhiteSpace(candidate.Title) &&
                !titles.Contains(candidate.Title, StringComparer.OrdinalIgnoreCase))
            {
                titles.Add(candidate.Title);
            }
        }

        return new MetadataSearchRequest(
            titles.Take(4).ToArray(),
            recognition.Year,
            recognition.MediaKind,
            recognition.SeasonNumber,
            recognition.EpisodeNumber ?? recognition.SpecialNumber,
            preferredLanguage,
            Math.Clamp(limit, 1, 25));
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
