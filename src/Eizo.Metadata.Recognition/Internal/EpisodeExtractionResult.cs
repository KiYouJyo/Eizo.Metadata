namespace Eizo.Metadata.Recognition.Internal;

internal sealed record EpisodeExtractionResult(
    int? SeasonNumber,
    decimal? EpisodeNumber,
    decimal? EpisodeEndNumber,
    int? CourNumber,
    double Confidence,
    IReadOnlyList<RecognitionEvidence> Evidence)
{
    internal static EpisodeExtractionResult Empty { get; } =
        new(null, null, null, null, 0.0, Array.Empty<RecognitionEvidence>());
}
