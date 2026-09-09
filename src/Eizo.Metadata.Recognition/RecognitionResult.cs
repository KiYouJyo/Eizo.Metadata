namespace Eizo.Metadata.Recognition;

/// <summary>
/// Structured output produced from filename and path signals only.
/// </summary>
public sealed record RecognitionResult(
    MediaKind MediaKind,
    string? Title,
    int? SeasonNumber,
    decimal? EpisodeNumber,
    decimal? EpisodeEndNumber,
    int? Year,
    double Confidence,
    IReadOnlyList<RecognitionEvidence> Evidence);
