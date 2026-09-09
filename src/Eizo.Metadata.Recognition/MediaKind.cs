namespace Eizo.Metadata.Recognition;

/// <summary>
/// Broad media shape inferred from a path before provider matching.
/// </summary>
public enum MediaKind
{
    Unknown = 0,
    SeriesEpisode = 1,
    Movie = 2,
    Special = 3,
}
