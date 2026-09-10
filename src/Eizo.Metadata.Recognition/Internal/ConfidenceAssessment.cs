namespace Eizo.Metadata.Recognition.Internal;

internal sealed record ConfidenceAssessment(
    double Score,
    RecognitionConfidenceLevel Level,
    bool IsAmbiguous,
    IReadOnlyList<RecognitionEvidence> Evidence);
