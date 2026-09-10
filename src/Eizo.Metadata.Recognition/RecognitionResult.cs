namespace Eizo.Metadata.Recognition;

/// <summary>
/// Structured output produced from filename and path signals only.
/// </summary>
public sealed record RecognitionResult(
    MediaKind MediaKind,
    SpecialKind SpecialKind,
    EpisodePart EpisodePart,
    bool IsFinalEpisode,
    string? Title,
    IReadOnlyList<RecognitionTitleCandidate> TitleCandidates,
    int? SeasonNumber,
    int? CourNumber,
    decimal? EpisodeNumber,
    decimal? EpisodeEndNumber,
    decimal? SpecialNumber,
    int? Year,
    double Confidence,
    RecognitionConfidenceLevel ConfidenceLevel,
    bool IsAmbiguous,
    IReadOnlyList<RecognitionEvidence> Evidence);
