using Eizo.Metadata.Recognition.Internal;

namespace Eizo.Metadata.Recognition.Tests;

public sealed class TokenClassifierTests
{
    public static TheoryData<string, bool, TokenKind> Cases => new()
    {
        { "480p", false, TokenKind.Resolution },
        { "720P", false, TokenKind.Resolution },
        { "1080p", false, TokenKind.Resolution },
        { "1440p", false, TokenKind.Resolution },
        { "2160P", false, TokenKind.Resolution },
        { "4K", false, TokenKind.Resolution },
        { "8k", false, TokenKind.Resolution },
        { "UHD", false, TokenKind.Resolution },
        { "FHD", false, TokenKind.Resolution },

        { "WEB", false, TokenKind.Source },
        { "WEB-DL", false, TokenKind.Source },
        { "WEBRip", false, TokenKind.Source },
        { "BluRay", false, TokenKind.Source },
        { "BDRip", false, TokenKind.Source },
        { "BDRemux", false, TokenKind.Source },
        { "HDTV", false, TokenKind.Source },
        { "Baha", true, TokenKind.Source },
        { "Netflix", true, TokenKind.Source },
        { "AMZN", true, TokenKind.Source },

        { "AVC", false, TokenKind.VideoCodec },
        { "HEVC", false, TokenKind.VideoCodec },
        { "H264", false, TokenKind.VideoCodec },
        { "H265", false, TokenKind.VideoCodec },
        { "x264", false, TokenKind.VideoCodec },
        { "x265", false, TokenKind.VideoCodec },
        { "AV1", false, TokenKind.VideoCodec },
        { "VP9", false, TokenKind.VideoCodec },

        { "AAC", false, TokenKind.AudioCodec },
        { "FLAC", false, TokenKind.AudioCodec },
        { "AC3", false, TokenKind.AudioCodec },
        { "EAC3", false, TokenKind.AudioCodec },
        { "DTS", false, TokenKind.AudioCodec },
        { "TrueHD", false, TokenKind.AudioCodec },
        { "Opus", false, TokenKind.AudioCodec },
        { "LPCM", false, TokenKind.AudioCodec },

        { "8bit", false, TokenKind.BitDepth },
        { "10BIT", false, TokenKind.BitDepth },
        { "12bit", false, TokenKind.BitDepth },

        { "CHT", true, TokenKind.Language },
        { "CHS", true, TokenKind.Language },
        { "JPN", true, TokenKind.Language },
        { "ENG", true, TokenKind.Language },
        { "简中", true, TokenKind.Language },
        { "繁中", true, TokenKind.Language },
        { "簡中", true, TokenKind.Language },

        { "ANi", true, TokenKind.ReleaseGroup },
        { "Lilith-Raws", true, TokenKind.ReleaseGroup },
        { "NC-Raws", true, TokenKind.ReleaseGroup },
        { "LoliHouse", true, TokenKind.ReleaseGroup },
        { "ReinForce", true, TokenKind.ReleaseGroup },
        { "Moozzi2", true, TokenKind.ReleaseGroup },

        { "2021", false, TokenKind.Year },
        { "1998", false, TokenKind.Year },
        { "2099", false, TokenKind.Year },
        { "14", false, TokenKind.Number },
        { "001", false, TokenKind.Number },

        { "A1B2C3D4", true, TokenKind.Checksum },
        { "0123456789ABCDEF0123456789ABCDEF", true, TokenKind.Checksum },

        { "AAC AVC", true, TokenKind.TechnicalGroup },
        { "1080p AAC", true, TokenKind.TechnicalGroup },
        { "HEVC 10bit", true, TokenKind.TechnicalGroup },

        { "S01E03", false, TokenKind.Text },
        { "第03話", false, TokenKind.Text },
        { "葬送のフリーレン", false, TokenKind.Text },
        { "VIVANT", false, TokenKind.Text },
        { "1888", false, TokenKind.Number },
        { "2100", false, TokenKind.Number },
        { "1080", false, TokenKind.Number },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Classify_ReturnsExpectedKind(
        string value,
        bool bracketed,
        TokenKind expected)
    {
        Assert.Equal(expected, TokenClassifier.Classify(value, bracketed));
    }
}
