namespace Eizo.Metadata.Recognition.Internal;

internal sealed record TitleCandidate(
    string Title,
    string Source,
    double Confidence,
    int SourcePriority);
