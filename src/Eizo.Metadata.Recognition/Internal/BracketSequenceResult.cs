namespace Eizo.Metadata.Recognition.Internal;

internal sealed record BracketSequenceResult(
    RecognitionToken ReleaseGroupToken,
    RecognitionToken TitleToken,
    RecognitionToken EpisodeToken,
    IReadOnlyList<RecognitionToken> TechnicalTokens,
    double Confidence);
