namespace Eizo.Metadata.Recognition.Internal;

internal sealed record TechnicalSuffixResult(
    int StartIndex,
    RecognitionToken? YearToken,
    string? ReleaseGroup,
    IReadOnlyList<RecognitionEvidence> Evidence);
