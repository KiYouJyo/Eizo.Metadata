using System.Text;

namespace Eizo.Metadata.Recognition.Internal;

internal static class TokenClassifier
{
    private static readonly HashSet<string> Sources = new(StringComparer.Ordinal)
    {
        "WEB", "WEBDL", "WEBRIP", "BLURAY", "BD", "DVD", "BDRIP", "BDREMUX", "HDTV",
        "DVDRIP", "REMUX", "BAHA", "NETFLIX", "NF", "AMZN", "CR", "ATX",
        "IQIYI", "YOUKU", "TENCENT", "DISNEYPLUS", "BILIBILI",
    };

    private static readonly HashSet<string> VideoCodecs = new(StringComparer.Ordinal)
    {
        "AVC", "HEVC", "H264", "H265", "X264", "X265", "AV1", "VP9", "MPEG2",
    };

    private static readonly HashSet<string> AudioCodecs = new(StringComparer.Ordinal)
    {
        "AAC", "FLAC", "FLA", "AC3", "EAC3", "DDP", "DTS", "TRUEHD", "DOLBY", "ATMOS",
        "OPUS", "MP3", "LPCM",
    };

    private static readonly HashSet<string> Languages = new(StringComparer.Ordinal)
    {
        "CHT", "CHS", "ZH", "JPN", "JAP", "JA", "JP", "ENG", "EN", "BIG5", "GB", "GBK",
        "SC", "TC", "简中", "繁中", "簡中", "繁體", "简体", "CHTC",
    };

    private static readonly HashSet<string> ReleaseGroups = new(StringComparer.Ordinal)
    {
        "ANI", "LILITHRAWS", "NCRAWS", "LOLIHOUSE", "REINFORCE", "MOOZZI2",
        "NEKOMOEKISSATEN", "DBDRAWS",
    };

    private static readonly HashSet<string> TechnicalQualifiers = new(StringComparer.Ordinal)
    {
        "NVENC", "MULTI", "SUB", "SUBS", "MULTISUB", "MULTISUBS", "DUAL",
        "DUALAUDIO", "HDR", "HDR10", "HDR10PLUS", "DV", "DOVI", "MA10P",
        "HI10P", "HDMA", "ASS", "SRT", "PGS",
    };

    internal static TokenKind Classify(string value, bool bracketed)
    {
        var normalized = Normalize(value);
        if (normalized.Length == 0)
        {
            return bracketed ? TokenKind.BracketGroup : TokenKind.Text;
        }

        var atomic = ClassifyAtomic(normalized, bracketed);
        if (atomic != TokenKind.Text)
        {
            return atomic;
        }

        if (bracketed && IsTechnicalGroup(normalized))
        {
            return TokenKind.TechnicalGroup;
        }

        return bracketed ? TokenKind.BracketGroup : TokenKind.Text;
    }

    internal static string Normalize(string value) =>
        value.Normalize(NormalizationForm.FormKC).Trim();

    private static TokenKind ClassifyAtomic(string normalized, bool bracketed)
    {
        var key = ToKey(normalized);

        if (ReleaseGroups.Contains(key))
        {
            return TokenKind.ReleaseGroup;
        }

        if (Sources.Contains(key))
        {
            return TokenKind.Source;
        }

        if (VideoCodecs.Contains(key))
        {
            return TokenKind.VideoCodec;
        }

        if (AudioCodecs.Contains(key))
        {
            return TokenKind.AudioCodec;
        }

        if (Languages.Contains(key))
        {
            return TokenKind.Language;
        }

        if (TechnicalQualifiers.Contains(key) || IsTechnicalQualifier(key))
        {
            return TokenKind.TechnicalGroup;
        }

        if (IsResolution(key))
        {
            return TokenKind.Resolution;
        }

        if (IsBitDepth(key))
        {
            return TokenKind.BitDepth;
        }

        if (IsYear(normalized))
        {
            return TokenKind.Year;
        }

        if (bracketed && IsChecksum(normalized))
        {
            return TokenKind.Checksum;
        }

        if (normalized.All(char.IsDigit))
        {
            return TokenKind.Number;
        }

        return TokenKind.Text;
    }

    private static bool IsTechnicalGroup(string value)
    {
        // Preserve dotted codec spellings (x.264 / h.265) as one token before
        // splitting a composite technical group.
        var normalized = value
            .Replace("x.264", "x264", StringComparison.OrdinalIgnoreCase)
            .Replace("x.265", "x265", StringComparison.OrdinalIgnoreCase)
            .Replace("h.264", "h264", StringComparison.OrdinalIgnoreCase)
            .Replace("h.265", "h265", StringComparison.OrdinalIgnoreCase);

        var parts = normalized.Split(
            new[] { ' ', '\t', ',', '+', '/', ';', '-', '_', '.' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length < 2)
        {
            return false;
        }

        foreach (var part in parts)
        {
            var kind = ClassifyAtomic(Normalize(part), bracketed: true);
            if (kind is TokenKind.Text or TokenKind.Number or TokenKind.Year or TokenKind.ReleaseGroup)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsResolution(string key)
    {
        if (key is "4K" or "8K" or "UHD" or "FHD")
        {
            return true;
        }

        var separator = key.IndexOf('X');
        if (separator > 0 &&
            separator < key.Length - 1 &&
            int.TryParse(key[..separator], out var width) &&
            int.TryParse(key[(separator + 1)..], out var height) &&
            width is >= 320 and <= 8192 &&
            height is >= 240 and <= 4320)
        {
            return true;
        }

        if (!key.EndsWith('P') || key.Length < 2)
        {
            return false;
        }

        return int.TryParse(key[..^1], out var heightP) &&
               heightP is 360 or 480 or 576 or 720 or 1080 or 1440 or 2160 or 4320;
    }

    private static bool IsTechnicalQualifier(string key)
    {
        if (key.EndsWith("AUDIO", StringComparison.Ordinal) &&
            int.TryParse(key[..^"AUDIO".Length], out var audioTracks) &&
            audioTracks is >= 1 and <= 32)
        {
            return true;
        }

        if (key.EndsWith("CH", StringComparison.Ordinal) &&
            int.TryParse(key[..^2], out var channels) &&
            channels is >= 1 and <= 128)
        {
            return true;
        }

        if (key.StartsWith("YUV", StringComparison.Ordinal) &&
            key.Contains('P') &&
            key[3..].All(static c => char.IsDigit(c) || c == 'P'))
        {
            return true;
        }

        if (key.EndsWith("MIX", StringComparison.Ordinal) &&
            int.TryParse(key[..^3], out var mixId) &&
            mixId is >= 1 and <= 9999)
        {
            return true;
        }

        if (key.EndsWith("FPS", StringComparison.Ordinal) &&
            int.TryParse(key[..^3], out var fps) &&
            fps is >= 1 and <= 240)
        {
            return true;
        }

        string[] countedTechnicalTokens =
        [
            "FLAC", "AAC", "AC3", "EAC3", "DTS", "TRUEHD",
            "OPUS", "SRT", "ASS", "PGS"
        ];

        foreach (var token in countedTechnicalTokens)
        {
            if (key.StartsWith(token + "X", StringComparison.Ordinal) &&
                int.TryParse(key[(token.Length + 1)..], out var suffixCount) &&
                suffixCount is >= 1 and <= 32)
            {
                return true;
            }

            if (key.EndsWith(token, StringComparison.Ordinal) &&
                int.TryParse(key[..^token.Length], out var prefixCount) &&
                prefixCount is >= 1 and <= 32)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsBitDepth(string key)
    {
        if (!key.EndsWith("BIT", StringComparison.Ordinal))
        {
            return false;
        }

        return int.TryParse(key[..^3], out var bits) && bits is 8 or 10 or 12 or 16;
    }

    private static bool IsYear(string value)
    {
        if (value.Length != 4 || !int.TryParse(value, out var year))
        {
            return false;
        }

        return year is >= 1900 and <= 2099;
    }

    private static bool IsChecksum(string value)
    {
        var compact = value.Replace(" ", string.Empty, StringComparison.Ordinal);
        if (compact.Length is not (8 or 32 or 40 or 64))
        {
            return false;
        }

        return compact.All(static c =>
            c is >= '0' and <= '9' ||
            c is >= 'a' and <= 'f' ||
            c is >= 'A' and <= 'F');
    }

    private static string ToKey(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value.Normalize(NormalizationForm.FormKC).ToUpperInvariant())
        {
            if (char.IsWhiteSpace(c) || c is '-' or '_' or '.')
            {
                continue;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }
}
