namespace Eizo.Metadata.Recognition.Internal;

internal sealed record TitleExtractionResult(
    string? Title,
    double Confidence,
    IReadOnlyList<TitleCandidate> Candidates,
    IReadOnlyList<RecognitionEvidence> Evidence)
{
    internal static TitleExtractionResult Empty { get; } =
        new(null, 0.0, Array.Empty<TitleCandidate>(), Array.Empty<RecognitionEvidence>());
}
