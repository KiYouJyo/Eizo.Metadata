using System.Text.RegularExpressions;

namespace Eizo.Metadata.Recognition.Internal;

internal static class EpisodeTitleBoundaryResolver
{
    private const RegexOptions Options =
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(50);
    private const int MaxBoundaryProbeLength = 2048;

    private static readonly Regex DotSeasonEpisodeRegex = new(
        @"^(?<series>.+?)(?:\s|[._-])*S\d{1,2}E\d{1,4}[._](?<title>.+?)\s*$",
        Options,
        Timeout);

    private static readonly Regex TrailingParenthesizedYearRegex = new(
        @"[\s._-]*[\(（\[【](?:19|20)\d{2}[\)）\]】]\s*$",
        Options,
        Timeout);

    private static readonly Regex TrailingPartMarkerRegex = new(
        @"\s*(?:前編|前篇|前编|後編|後篇|后编|后篇)\s*$",
        Options,
        Timeout);

    internal static EpisodeTitleExtractionResult Normalize(
        NormalizedMediaPath path,
        EpisodeExtractionResult episode,
        EpisodeTitleExtractionResult result)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(episode);
        ArgumentNullException.ThrowIfNull(result);

        if (episode.EpisodeNumber is null ||
            !string.IsNullOrWhiteSpace(result.EpisodeTitle) ||
            episode.EpisodeNumber.Value != decimal.Truncate(episode.EpisodeNumber.Value) ||
            path.Stem.Length > MaxBoundaryProbeLength)
        {
            return result;
        }

        if (episode.Evidence.Any(static item =>
                item.Code == "episode.leading-numbered") &&
            EpisodeBoundaryNormalizer.TryGetLeadingNumberedEpisode(
                path,
                out _,
                out var leadingTitle))
        {
            var cleaned = TrailingPartMarkerRegex
                .Replace(leadingTitle, string.Empty)
                .Trim(' ', '.', '_', '-', '–', '—', '−');

            if (IsLexicalEpisodeTitle(cleaned))
            {
                return new EpisodeTitleExtractionResult(
                    cleaned,
                    SeriesTitle: null,
                    Confidence: 0.90,
                    new[]
                    {
                        new RecognitionEvidence(
                            "episode-title.leading-numbered",
                            cleaned,
                            0.90),
                    });
            }
        }

        var match = DotSeasonEpisodeRegex.Match(path.Stem);
        if (!match.Success)
        {
            return result;
        }

        var series = CleanSeries(match.Groups["series"].Value);
        var episodeTitle = match.Groups["title"].Value.Trim(' ', '.', '_', '-', '–', '—', '−');
        if (!IsUsefulSeries(series) || !IsLexicalEpisodeTitle(episodeTitle))
        {
            return result;
        }

        return new EpisodeTitleExtractionResult(
            episodeTitle,
            series,
            Confidence: 0.94,
            new[]
            {
                new RecognitionEvidence(
                    "episode-title.dot-sxxexx",
                    episodeTitle,
                    0.94),
            });
    }

    private static string CleanSeries(string value)
    {
        var trimmed = value.Trim(' ', '.', '_', '-', '–', '—', '−');
        return TrailingParenthesizedYearRegex
            .Replace(trimmed, string.Empty)
            .Trim(' ', '.', '_', '-', '–', '—', '−');
    }

    private static bool IsLexicalEpisodeTitle(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var candidate = PathPreprocessor.Preprocess(value + ".mkv");
        return candidate.Tokens.Any(static token =>
            (token.Kind is TokenKind.Text or TokenKind.BracketGroup) &&
            !ReleaseNoiseClassifier.IsProviderMetadataTag(token) &&
            token.NormalizedValue.Any(static c =>
                char.IsLetter(c) ||
                c is >= '\u3040' and <= '\u30ff' ||
                c is >= '\u3400' and <= '\u4dbf' ||
                c is >= '\u4e00' and <= '\u9fff' ||
                c is >= '\uac00' and <= '\ud7af'));
    }

    private static bool IsUsefulSeries(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Any(static c => char.IsLetter(c) || char.IsDigit(c));
}
