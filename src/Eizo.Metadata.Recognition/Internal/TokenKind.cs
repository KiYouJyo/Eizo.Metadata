namespace Eizo.Metadata.Recognition.Internal;

internal enum TokenKind
{
    Text = 0,
    Number = 1,
    Separator = 2,
    BracketGroup = 3,
    ReleaseGroup = 4,
    Resolution = 5,
    Source = 6,
    VideoCodec = 7,
    AudioCodec = 8,
    BitDepth = 9,
    Language = 10,
    Checksum = 11,
    Year = 12,
    TechnicalGroup = 13,
}
