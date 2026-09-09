namespace Eizo.Metadata.Recognition.Internal;

internal sealed record NormalizedMediaPath(
    string OriginalPath,
    IReadOnlyList<string> DirectorySegments,
    IReadOnlyList<string> NormalizedDirectorySegments,
    string FileName,
    string Stem,
    string NormalizedStem,
    string? Extension,
    IReadOnlyList<RecognitionToken> Tokens);
