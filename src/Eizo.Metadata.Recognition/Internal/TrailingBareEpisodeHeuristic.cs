namespace Eizo.Metadata.Recognition.Internal;

internal static class TrailingBareEpisodeHeuristic
{
    internal static bool TryMatch(
        NormalizedMediaPath path,
        out RecognitionToken episodeToken,
        out IReadOnlyList<RecognitionToken> releaseTags)
    {
        ArgumentNullException.ThrowIfNull(path);

        episodeToken = null!;
        releaseTags = Array.Empty<RecognitionToken>();

        var tokens = path.Tokens;
        if (tokens.Count == 0)
        {
            return false;
        }

        var index = tokens.Count - 1;
        while (index >= 0 && tokens[index].Kind == TokenKind.Separator)
        {
            index--;
        }

        var tags = new List<RecognitionToken>();
        var hasIdentityTag = false;

        while (index >= 0)
        {
            var token = tokens[index];
            if (token.Kind == TokenKind.Separator)
            {
                index--;
                continue;
            }

            if (!ReleaseNoiseClassifier.IsTrailingNoiseTag(token))
            {
                break;
            }

            tags.Add(token);
            hasIdentityTag |= ReleaseNoiseClassifier.IsIdentityTag(token);
            index--;
        }

        if (tags.Count == 0 || !hasIdentityTag)
        {
            return false;
        }

        while (index >= 0 && tokens[index].Kind == TokenKind.Separator)
        {
            index--;
        }

        if (index < 0 || tokens[index].Kind != TokenKind.Number)
        {
            return false;
        }

        var candidate = tokens[index];
        if (!int.TryParse(candidate.NormalizedValue, out var episode) ||
            IsCollision(episode))
        {
            return false;
        }

        if (!HasTitleTextBefore(tokens, index))
        {
            return false;
        }

        episodeToken = candidate;
        tags.Reverse();
        releaseTags = tags;
        return true;
    }

    private static bool HasTitleTextBefore(
        IReadOnlyList<RecognitionToken> tokens,
        int episodeIndex)
    {
        for (var i = episodeIndex - 1; i >= 0; i--)
        {
            var token = tokens[i];
            if (token.Kind == TokenKind.Separator)
            {
                continue;
            }

            if (token.IsBracketed &&
                (ReleaseNoiseClassifier.IsTrailingNoiseTag(token) ||
                 token.Kind == TokenKind.ReleaseGroup))
            {
                continue;
            }

            return token.NormalizedValue.Any(static c =>
                char.IsLetter(c) ||
                IsCjk(c));
        }

        return false;
    }

    private static bool IsCollision(int value) =>
        value is 360 or 480 or 576 or 720 or 1080 or 1440 or 2160 or 4320 ||
        value is >= 1900 and <= 2099;

    private static bool IsCjk(char c) =>
        c is >= '\u3040' and <= '\u30ff' ||
        c is >= '\u3400' and <= '\u4dbf' ||
        c is >= '\u4e00' and <= '\u9fff' ||
        c is >= '\uac00' and <= '\ud7af';
}
