using Eizo.Metadata.Recognition.Internal;

namespace Eizo.Metadata.Recognition.Tests;

public sealed class TokenClassifierTests
{
    public static TheoryData<string, bool, string> Cases => new()
    {
        { "480p", false, nameof(TokenKind.Resolution) },
        { "720P", false, nameof(TokenKind.Resolution) },
        { "1080p", false, nameof(TokenKind.Resolution) },
        { "1440p", false, nameof(TokenKind.Resolution) },
        { "2160P", false, nameof(TokenKind.Resolution) },
        { "4K", false, nameof(TokenKind.Resolution) },
        { "8k", false, nameof(TokenKind.Resolution) },
        { "UHD", false, nameof(TokenKind.Resolution) },
        { "FHD", false, nameof(TokenKind.Resolution) },

        { "WEB", false, nameof(TokenKind.Source) },
        { "WEB-DL", false, nameof(TokenKind.Source) },
        { "WEBRip", false, nameof(TokenKind.Source) },
        { "BluRay", false, nameof(TokenKind.Source) },
        { "BDRip", false, nameof(TokenKind.Source) },
        { "BDRemux", false, nameof(TokenKind.Source) },
        { "HDTV", false, nameof(TokenKind.Source) },
        { "Baha", true, nameof(TokenKind.Source) },
        { "Netflix", true, nameof(TokenKind.Source) },
        { "AMZN", true, nameof(TokenKind.Source) },

        { "AVC", false, nameof(TokenKind.VideoCodec) },
        { "HEVC", false, nameof(TokenKind.VideoCodec) },
        { "H264", false, nameof(TokenKind.VideoCodec) },
        { "H265", false, nameof(TokenKind.VideoCodec) },
        { "x264", false, nameof(TokenKind.VideoCodec) },
        { "x265", false, nameof(TokenKind.VideoCodec) },
        { "AV1", false, nameof(TokenKind.VideoCodec) },
        { "VP9", false, nameof(TokenKind.VideoCodec) },

        { "AAC", false, nameof(TokenKind.AudioCodec) },
        { "FLAC", false, nameof(TokenKind.AudioCodec) },
        { "AC3", false, nameof(TokenKind.AudioCodec) },
        { "EAC3", false, nameof(TokenKind.AudioCodec) },
        { "DTS", false, nameof(TokenKind.AudioCodec) },
        { "TrueHD", false, nameof(TokenKind.AudioCodec) },
        { "Opus", false, nameof(TokenKind.AudioCodec) },
        { "LPCM", false, nameof(TokenKind.AudioCodec) },

        { "8bit", false, nameof(TokenKind.BitDepth) },
        { "10BIT", false, nameof(TokenKind.BitDepth) },
        { "12bit", false, nameof(TokenKind.BitDepth) },

        { "CHT", true, nameof(TokenKind.Language) },
        { "CHS", true, nameof(TokenKind.Language) },
        { "JPN", true, nameof(TokenKind.Language) },
        { "ENG", true, nameof(TokenKind.Language) },
        { "简中", true, nameof(TokenKind.Language) },
        { "繁中", true, nameof(TokenKind.Language) },
        { "簡中", true, nameof(TokenKind.Language) },

        { "ANi", true, nameof(TokenKind.ReleaseGroup) },
        { "Lilith-Raws", true, nameof(TokenKind.ReleaseGroup) },
        { "NC-Raws", true, nameof(TokenKind.ReleaseGroup) },
        { "LoliHouse", true, nameof(TokenKind.ReleaseGroup) },
        { "ReinForce", true, nameof(TokenKind.ReleaseGroup) },
        { "Moozzi2", true, nameof(TokenKind.ReleaseGroup) },

        { "2021", false, nameof(TokenKind.Year) },
        { "1998", false, nameof(TokenKind.Year) },
        { "2099", false, nameof(TokenKind.Year) },
        { "14", false, nameof(TokenKind.Number) },
        { "001", false, nameof(TokenKind.Number) },

        { "A1B2C3D4", true, nameof(TokenKind.Checksum) },
        { "0123456789ABCDEF0123456789ABCDEF", true, nameof(TokenKind.Checksum) },

        { "AAC AVC", true, nameof(TokenKind.TechnicalGroup) },
        { "1080p AAC", true, nameof(TokenKind.TechnicalGroup) },
        { "HEVC 10bit", true, nameof(TokenKind.TechnicalGroup) },

        { "S01E03", false, nameof(TokenKind.Text) },
        { "第03話", false, nameof(TokenKind.Text) },
        { "葬送のフリーレン", false, nameof(TokenKind.Text) },
        { "VIVANT", false, nameof(TokenKind.Text) },
        { "1888", false, nameof(TokenKind.Number) },
        { "2100", false, nameof(TokenKind.Number) },
        { "1080", false, nameof(TokenKind.Number) },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Classify_ReturnsExpectedKind(
        string value,
        bool bracketed,
        string expected)
    {
        Assert.Equal(expected, TokenClassifier.Classify(value, bracketed).ToString());
    }
}
