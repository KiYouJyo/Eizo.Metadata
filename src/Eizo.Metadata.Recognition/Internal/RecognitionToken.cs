namespace Eizo.Metadata.Recognition.Internal;

internal sealed record RecognitionToken(
    TokenKind Kind,
    string RawValue,
    string NormalizedValue,
    int Start,
    int Length,
    bool IsBracketed);
