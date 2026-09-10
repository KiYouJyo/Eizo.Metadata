namespace Eizo.Metadata.Recognition.Internal;

internal static class BracketSequenceAnalyzer
{
    internal static bool TryAnalyze(
        NormalizedMediaPath path,
        out BracketSequenceResult result)
    {
        ArgumentNullException.ThrowIfNull(path);

        result = null!;

        var tokens = path.Tokens
            .Where(static token => token.Kind != TokenKind.Separator)
            .ToArray();

        if (tokens.Length < 5 ||
            tokens.Any(static token => !token.IsBracketed))
        {
            return false;
        }

        var release = tokens[0];
        if (!IsLikelyReleaseGroup(release))
        {
            return false;
        }

        var title = tokens[1];
        if (!IsPlausibleTitle(title))
        {
            return false;
        }

        var episode = tokens[2];
        if (!TryParseEpisode(episode, out _))
        {
            return false;
        }

        var technical = tokens[3..];
        if (technical.Length < 2 ||
            technical.Any(static token => !IsTechnicalTailToken(token)))
        {
            return false;
        }

        var hasStrongTechnicalSignal = technical.Any(static token =>
            token.Kind is TokenKind.Resolution or TokenKind.Source or
            TokenKind.VideoCodec or TokenKind.AudioCodec or
            TokenKind.BitDepth or TokenKind.TechnicalGroup);

        if (!hasStrongTechnicalSignal)
        {
            return false;
        }

        result = new BracketSequenceResult(
            release,
            title,
            episode,
            technical,
            Confidence: 0.94);
        return true;
    }

    internal static bool TryParseEpisode(
        RecognitionToken token,
        out decimal episode)
    {
        episode = default;

        if (!token.IsBracketed ||
            token.Kind != TokenKind.Number ||
            !decimal.TryParse(
                token.NormalizedValue,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out episode))
        {
            return false;
        }

        if (episode != decimal.Truncate(episode) ||
            episode is < 0 or > 999)
        {
            return false;
        }

        var value = decimal.ToInt32(episode);
        return value is not (360 or 480 or 576 or 720 or 1080 or 1440 or 2160 or 4320) &&
               value is not (>= 1900 and <= 2099);
    }

    private static bool IsLikelyReleaseGroup(RecognitionToken token)
    {
        if (!token.IsBracketed)
        {
            return false;
        }

        if (token.Kind == TokenKind.ReleaseGroup)
        {
            return true;
        }

        if (token.Kind != TokenKind.BracketGroup)
        {
            return false;
        }

        var value = token.NormalizedValue;
        if (value.Length is < 2 or > 48 ||
            !value.Any(char.IsAsciiLetter) ||
            value.Any(static c => !(char.IsAsciiLetterOrDigit(c) ||
                                    c is '-' or '_' or '.' or '+')))
        {
            return false;
        }

        var key = value.ToUpperInvariant();
        return key.Contains("RAWS", StringComparison.Ordinal) ||
               key.Contains("RIP", StringComparison.Ordinal) ||
               key.Contains("ENCODE", StringComparison.Ordinal) ||
               key.Contains("GROUP", StringComparison.Ordinal) ||
               value.Contains('-') ||
               value.Contains('_');
    }

    private static bool IsPlausibleTitle(RecognitionToken token)
    {
        if (!token.IsBracketed ||
            token.Kind != TokenKind.BracketGroup ||
            ReleaseNoiseClassifier.IsProviderMetadataTag(token))
        {
            return false;
        }

        var value = token.NormalizedValue;
        if (value.Length is < 1 or > 180)
        {
            return false;
        }

        return value.Any(static c =>
            char.IsLetter(c) ||
            c is >= '\u3040' and <= '\u30ff' ||
            c is >= '\u3400' and <= '\u4dbf' ||
            c is >= '\u4e00' and <= '\u9fff' ||
            c is >= '\uac00' and <= '\ud7af');
    }

    private static bool IsTechnicalTailToken(RecognitionToken token)
    {
        if (!token.IsBracketed)
        {
            return false;
        }

        return token.Kind is
            TokenKind.Resolution or
            TokenKind.Source or
            TokenKind.VideoCodec or
            TokenKind.AudioCodec or
            TokenKind.BitDepth or
            TokenKind.Language or
            TokenKind.Checksum or
            TokenKind.TechnicalGroup ||
            ReleaseNoiseClassifier.IsProviderMetadataTag(token);
    }
}
