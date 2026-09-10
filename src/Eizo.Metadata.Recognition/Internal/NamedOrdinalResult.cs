namespace Eizo.Metadata.Recognition.Internal;

internal sealed record NamedOrdinalResult(
    string Marker,
    decimal Number,
    string SeriesTitle,
    string? EpisodeTitle,
    double Confidence);
