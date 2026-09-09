namespace Eizo.Metadata.Recognition.Internal;

internal sealed record DomainClassificationResult(
    MediaKind MediaKind,
    SpecialKind SpecialKind,
    EpisodePart EpisodePart,
    bool IsFinalEpisode,
    decimal? SpecialNumber,
    double Confidence,
    IReadOnlyList<RecognitionEvidence> Evidence)
{
    internal static DomainClassificationResult Empty { get; } =
        new(
            MediaKind.Unknown,
            SpecialKind.None,
            EpisodePart.None,
            IsFinalEpisode: false,
            SpecialNumber: null,
            Confidence: 0.0,
            Array.Empty<RecognitionEvidence>());
}
