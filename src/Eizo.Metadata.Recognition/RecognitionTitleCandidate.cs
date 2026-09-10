namespace Eizo.Metadata.Recognition;

/// <summary>
/// Provider-neutral title candidate produced from filename or directory evidence.
/// </summary>
public sealed record RecognitionTitleCandidate(
    string Title,
    double Confidence,
    string Source,
    bool IsPrimary);
