using System.Text.RegularExpressions;

namespace Eizo.Metadata.Recognition.Internal;

internal static class EpisodeTitleExtractor
{
    private const RegexOptions Options =
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(50);

    private static readonly Regex DelimitedSeasonEpisodeRegex = new(
        @"^(?<series>.+?)\s*[-–—−]\s*S\d{1,2}E\d{1,3}(?:\.\d+)?(?:\s*[-~]\s*(?:E)?\d{1,3}(?:\.\d+)?)?\s*[-–—−]\s*(?<title>.+?)\s*$",
        Options,
        Timeout);

    private static readonly Regex DelimitedOneXRegex = new(
        @"^(?<series>.+?)\s*[-–—−]\s*\d{1,2}x\d{1,3}(?:\.\d+)?\s*[-–—−]\s*(?<title>.+?)\s*$",
        Options,
        Timeout);

    private static readonly Regex DelimitedPrefixedRegex = new(
        @"^(?<series>.+?)\s*[-–—−]\s*(?:EPISODE|EP|E)\s*[-_. ]?\d{1,3}(?:\.\d+)?\s*[-–—−]\s*(?<title>.+?)\s*$",
        Options,
        Timeout);

    private static readonly Regex JapaneseQuotedRegex = new(
        @"^(?<series>.+?)\s+第\s*\d{1,3}(?:\.\d+)?\s*(?:話|回)\s*[「『](?<title>.+?)[」』]\s*$",
        Options,
        Timeout);

    internal static EpisodeTitleExtractionResult Extract(
        NormalizedMediaPath path,
        EpisodeExtractionResult episode)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(episode);

        if (episode.EpisodeNumber is null)
        {
            return EpisodeTitleExtractionResult.Empty;
        }

        if (NamedOrdinalAnalyzer.TryAnalyze(path, out var named) &&
            named.Number == episode.EpisodeNumber)
        {
            return new EpisodeTitleExtractionResult(
                named.EpisodeTitle,
                named.SeriesTitle,
                named.Confidence,
                named.EpisodeTitle is null
                    ? Array.Empty<RecognitionEvidence>()
                    : new[]
                    {
                        new RecognitionEvidence(
                            "episode-title.named-ordinal." +
                            named.Marker.ToLowerInvariant(),
                            named.EpisodeTitle,
                            named.Confidence),
                    });
        }

        foreach (var (regex, code, confidence) in Patterns)
        {
            Match match;
            try
            {
                match = regex.Match(path.Stem);
            }
            catch (RegexMatchTimeoutException)
            {
                // Episode-title extraction is optional enrichment. Pathological
                // Unicode/repetition must never make the recognition pipeline
                // fail merely because one bounded regex could not decide in time.
                continue;
            }

            if (!match.Success)
            {
                continue;
            }

            var series = CleanSegment(match.Groups["series"].Value);
            var title = CleanSegment(match.Groups["title"].Value);

            if (!IsUseful(series) || !IsUseful(title))
            {
                continue;
            }

            return new EpisodeTitleExtractionResult(
                title,
                series,
                confidence,
                new[]
                {
                    new RecognitionEvidence(
                        code,
                        title,
                        confidence),
                });
        }

        return EpisodeTitleExtractionResult.Empty;
    }

    private static IEnumerable<(Regex Regex, string Code, double Confidence)> Patterns
    {
        get
        {
            yield return (DelimitedSeasonEpisodeRegex, "episode-title.delimited-sxxexx", 0.96);
            yield return (DelimitedOneXRegex, "episode-title.delimited-onex", 0.94);
            yield return (DelimitedPrefixedRegex, "episode-title.delimited-prefixed", 0.93);
            yield return (JapaneseQuotedRegex, "episode-title.japanese-quoted", 0.95);
        }
    }

    private static string CleanSegment(string value)
    {
        var path = PathPreprocessor.Preprocess(value.Trim() + ".mkv");
        var removal = new bool[path.Stem.Length];

        foreach (var token in path.Tokens)
        {
            if (token.Kind is
                TokenKind.ReleaseGroup or
                TokenKind.Resolution or
                TokenKind.Source or
                TokenKind.VideoCodec or
                TokenKind.AudioCodec or
                TokenKind.BitDepth or
                TokenKind.Language or
                TokenKind.Checksum or
                TokenKind.TechnicalGroup ||
                ReleaseNoiseClassifier.IsProviderMetadataTag(token))
            {
                Mark(removal, token.Start, token.Length);
            }
        }

        return Rebuild(path.Stem, removal);
    }

    private static string Rebuild(
        string source,
        IReadOnlyList<bool> removal)
    {
        var chars = new List<char>(source.Length);
        var pendingSpace = false;

        for (var i = 0; i < source.Length; i++)
        {
            if (removal[i])
            {
                pendingSpace = chars.Count > 0;
                continue;
            }

            var c = source[i];
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = chars.Count > 0;
                continue;
            }

            if (pendingSpace &&
                chars.Count > 0 &&
                chars[^1] is not '[' and not '(' and not '【')
            {
                chars.Add(' ');
            }

            pendingSpace = false;
            chars.Add(c);
        }

        return new string(chars.ToArray())
            .Trim(' ', '-', '–', '—', '−', '_', '.');
    }

    private static bool IsUseful(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Any(static c => char.IsLetter(c) || char.IsDigit(c));

    private static void Mark(bool[] removal, int start, int length)
    {
        var end = Math.Min(removal.Length, start + length);
        for (var i = Math.Max(0, start); i < end; i++)
        {
            removal[i] = true;
        }
    }
}
