namespace Eizo.Metadata.Recognition.Internal;

internal sealed record EpisodeTitleExtractionResult(
    string? EpisodeTitle,
    string? SeriesTitle,
    double Confidence,
    IReadOnlyList<RecognitionEvidence> Evidence)
{
    internal static EpisodeTitleExtractionResult Empty { get; } =
        new(null, null, 0.0, Array.Empty<RecognitionEvidence>());
}
