using System.Text;
using System.Text.RegularExpressions;

namespace Eizo.Metadata.Recognition.Internal;

internal static class TitleExtractor
{
    private const RegexOptions Options =
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(50);

    private static readonly Regex[] EpisodePatterns =
    {
        new(
            @"(?<![A-Za-z0-9])S\d{1,2}E\d{1,3}(?:\.\d+)?(?:\s*[-~]\s*(?:E)?\d{1,3}(?:\.\d+)?)?(?!\d)",
            Options,
            RegexTimeout),
        new(
            @"(?<![A-Za-z0-9])\d{1,2}x\d{1,3}(?:\.\d+)?(?!\d)",
            Options,
            RegexTimeout),
        new(
            @"(?<![A-Za-z0-9])(?:EPISODE|EP|E)\s*[-_. ]?\d{1,3}(?:\.\d+)?(?:\s*[-~]\s*(?:EP|E)?\s*\d{1,3}(?:\.\d+)?)?(?!\d)",
            Options,
            RegexTimeout),
        new(
            @"第\s*\d{1,3}(?:\.\d+)?\s*話",
            Options,
            RegexTimeout),
        new(
            @"(?:^|\s)[-–—−]\s*\d{1,3}(?:\.\d+)?(?:\s*[-~]\s*\d{1,3}(?:\.\d+)?)?(?=\s|$|\[|\(|【)",
            Options,
            RegexTimeout),
    };

    private static readonly Regex PureEpisodeRegex = new(
        @"^\s*\d{1,3}(?:\.\d+)?\s*$",
        Options,
        RegexTimeout);

    private static readonly Regex SeasonDirectoryRegex = new(
        @"^(?:SEASON|S)\s*0?\d{1,2}$",
        Options,
        RegexTimeout);

    private static readonly Regex JapaneseSeasonDirectoryRegex = new(
        @"^第\s*0?\d{1,2}\s*(?:期|シーズン|シリーズ)$",
        Options,
        RegexTimeout);

    private static readonly Regex OrdinalSeasonDirectoryRegex = new(
        @"^\d{1,2}(?:ST|ND|RD|TH)\s+SEASON$",
        Options,
        RegexTimeout);

    private static readonly Regex CourDirectoryRegex = new(
        @"^(?:COUR\s*0?\d{1,2}|第\s*0?\d{1,2}\s*クール|\d{1,2}(?:ST|ND|RD|TH)\s+COUR)$",
        Options,
        RegexTimeout);

    private static readonly HashSet<string> GenericDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "ANIME", "ANIMES", "TV", "TV SERIES", "SERIES", "DRAMA", "DRAMAS",
        "JDRAMA", "J-DRAMA", "MOVIES", "VIDEO", "VIDEOS", "MEDIA",
        "DOWNLOAD", "DOWNLOADS", "WEBDAV",
    };

    internal static TitleExtractionResult Extract(
        NormalizedMediaPath path,
        EpisodeExtractionResult episode)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(episode);

        var candidates = new List<TitleCandidate>();
        var evidence = new List<RecognitionEvidence>();

        var fileCandidate = ExtractFileCandidate(path, episode);
        if (fileCandidate is not null)
        {
            candidates.Add(fileCandidate);
        }

        AddDirectoryCandidates(path, candidates);

        if (candidates.Count == 0)
        {
            return TitleExtractionResult.Empty;
        }

        var ordered = candidates
            .OrderByDescending(static candidate => candidate.Confidence)
            .ThenBy(static candidate => candidate.SourcePriority)
            .ThenBy(static candidate => candidate.Title, StringComparer.Ordinal)
            .ToArray();

        var primary = ordered[0];
        evidence.Add(new RecognitionEvidence(
            $"title.{primary.Source}",
            primary.Title,
            primary.Confidence));

        foreach (var alternate in ordered.Skip(1))
        {
            evidence.Add(new RecognitionEvidence(
                $"title.candidate.{alternate.Source}",
                alternate.Title,
                Math.Min(0.50, alternate.Confidence)));
        }

        return new TitleExtractionResult(
            primary.Title,
            primary.Confidence,
            ordered,
            evidence);
    }

    private static TitleCandidate? ExtractFileCandidate(
        NormalizedMediaPath path,
        EpisodeExtractionResult episode)
    {
        if (string.IsNullOrWhiteSpace(path.Stem))
        {
            return null;
        }

        if (episode.EpisodeNumber is not null &&
            PureEpisodeRegex.IsMatch(path.NormalizedStem))
        {
            return null;
        }

        var removal = new bool[path.Stem.Length];

        MarkNoiseTokens(path, removal);
        MarkEpisodeSyntax(path, removal);
        MarkLeadingReleaseGroupHeuristic(path, removal);
        MarkOptionalYear(path, removal);

        var title = Rebuild(path.Stem, removal);
        if (!IsUsefulTitle(title))
        {
            return null;
        }

        var confidence = episode.EpisodeNumber is not null ? 0.91 : 0.82;
        return new TitleCandidate(title, "filename", confidence, SourcePriority: 0);
    }

    private static void AddDirectoryCandidates(
        NormalizedMediaPath path,
        ICollection<TitleCandidate> candidates)
    {
        var priority = 1;

        for (var i = path.NormalizedDirectorySegments.Count - 1; i >= 0; i--)
        {
            var normalized = path.NormalizedDirectorySegments[i].Trim();
            if (ShouldSkipDirectory(normalized))
            {
                priority++;
                continue;
            }

            var raw = path.DirectorySegments[i];
            var candidate = CleanDirectoryTitle(raw);
            if (!IsUsefulTitle(candidate))
            {
                priority++;
                continue;
            }

            var confidence = priority == 1 ? 0.78 : 0.68;
            candidates.Add(new TitleCandidate(
                candidate,
                "parent-directory",
                confidence,
                priority));
            priority++;

            // The nearest meaningful parent is the useful fallback. More distant
            // directories are usually library/container names and create noise.
            break;
        }
    }

    private static void MarkNoiseTokens(
        NormalizedMediaPath path,
        bool[] removal)
    {
        foreach (var token in path.Tokens)
        {
            if (!IsRemovableNoise(token.Kind))
            {
                continue;
            }

            Mark(removal, token.Start, token.Length);
        }
    }

    private static void MarkEpisodeSyntax(
        NormalizedMediaPath path,
        bool[] removal)
    {
        foreach (var regex in EpisodePatterns)
        {
            foreach (Match match in regex.Matches(path.NormalizedStem))
            {
                Mark(removal, match.Index, match.Length);
            }
        }
    }

    private static void MarkLeadingReleaseGroupHeuristic(
        NormalizedMediaPath path,
        bool[] removal)
    {
        var first = path.Tokens.FirstOrDefault(static token => token.Length > 0);
        if (first is null ||
            !first.IsBracketed ||
            first.Kind != TokenKind.BracketGroup ||
            first.Start != 0)
        {
            return;
        }

        var inner = first.NormalizedValue;
        if (inner.Length is < 2 or > 40 || ContainsCjk(inner))
        {
            return;
        }

        // Do not strip a bracketed title when it is the only meaningful title text,
        // e.g. "[Oshi no Ko] - 01". Strip only when meaningful lexical material
        // follows the prefix before the episode syntax.
        var remainingStart = first.Start + first.Length;
        if (remainingStart >= path.Stem.Length)
        {
            return;
        }

        var suffix = path.Stem[remainingStart..];
        var cleanedSuffix = RemoveEpisodeAndTechnicalText(suffix);
        if (!HasLetterOrCjk(cleanedSuffix))
        {
            return;
        }

        Mark(removal, first.Start, first.Length);
    }

    private static void MarkOptionalYear(
        NormalizedMediaPath path,
        bool[] removal)
    {
        var yearTokens = path.Tokens
            .Where(static token => token.Kind == TokenKind.Year)
            .ToArray();

        if (yearTokens.Length == 0)
        {
            return;
        }

        var hasOtherLexicalContent = path.Tokens.Any(static token =>
            token.Kind is TokenKind.Text or TokenKind.BracketGroup or TokenKind.Number &&
            token.Kind != TokenKind.Year);

        if (!hasOtherLexicalContent)
        {
            return;
        }

        foreach (var year in yearTokens)
        {
            Mark(removal, year.Start, year.Length);
        }
    }

    private static string CleanDirectoryTitle(string raw)
    {
        var normalized = TokenClassifier.Normalize(raw);
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        var preprocessed = PathPreprocessor.Preprocess(normalized + ".mkv");
        var removal = new bool[preprocessed.Stem.Length];
        MarkNoiseTokens(preprocessed, removal);
        MarkOptionalYear(preprocessed, removal);

        return Rebuild(preprocessed.Stem, removal);
    }

    private static string RemoveEpisodeAndTechnicalText(string value)
    {
        var working = value;
        foreach (var regex in EpisodePatterns)
        {
            working = regex.Replace(working, " ");
        }

        working = Regex.Replace(
            working,
            @"(?i)(?:\b(?:480p|576p|720p|1080p|1440p|2160p|4320p|4k|8k|web-?dl|webrip|bluray|bdrip|hdtv|x264|x265|h264|h265|hevc|avc|aac|flac|10bit|8bit)\b)",
            " ",
            RegexOptions.CultureInvariant,
            RegexTimeout);

        return working;
    }

    private static bool ShouldSkipDirectory(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (GenericDirectories.Contains(value))
        {
            return true;
        }

        return SeasonDirectoryRegex.IsMatch(value) ||
               JapaneseSeasonDirectoryRegex.IsMatch(value) ||
               OrdinalSeasonDirectoryRegex.IsMatch(value) ||
               CourDirectoryRegex.IsMatch(value);
    }

    private static bool IsRemovableNoise(TokenKind kind) =>
        kind is
            TokenKind.ReleaseGroup or
            TokenKind.Resolution or
            TokenKind.Source or
            TokenKind.VideoCodec or
            TokenKind.AudioCodec or
            TokenKind.BitDepth or
            TokenKind.Language or
            TokenKind.Checksum or
            TokenKind.TechnicalGroup;

    private static string Rebuild(
        string source,
        IReadOnlyList<bool> removal)
    {
        var builder = new StringBuilder(source.Length);
        var pendingSpace = false;

        for (var i = 0; i < source.Length; i++)
        {
            if (removal[i])
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            var c = source[i];
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace && ShouldInsertSpace(builder, c))
            {
                builder.Append(' ');
            }

            pendingSpace = false;
            builder.Append(c);
        }

        return TrimDecorativeEdges(builder.ToString());
    }

    private static bool ShouldInsertSpace(StringBuilder builder, char next)
    {
        if (builder.Length == 0)
        {
            return false;
        }

        var previous = builder[^1];
        if (IsOpeningPunctuation(previous) || IsClosingPunctuation(next))
        {
            return false;
        }

        if (IsSeparatorPunctuation(previous) || IsSeparatorPunctuation(next))
        {
            return true;
        }

        return true;
    }

    private static string TrimDecorativeEdges(string value)
    {
        var trimmed = value.Trim();

        while (trimmed.Length > 0 && IsEdgeDecoration(trimmed[0]))
        {
            trimmed = trimmed[1..].TrimStart();
        }

        while (trimmed.Length > 0 && IsEdgeDecoration(trimmed[^1]))
        {
            trimmed = trimmed[..^1].TrimEnd();
        }

        return CollapseWhitespace(trimmed);
    }

    private static string CollapseWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);
        var previousWhitespace = false;

        foreach (var c in value)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!previousWhitespace && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                previousWhitespace = true;
                continue;
            }

            builder.Append(c);
            previousWhitespace = false;
        }

        return builder.ToString().Trim();
    }

    private static bool IsUsefulTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();
        if (PureEpisodeRegex.IsMatch(normalized) ||
            ShouldSkipDirectory(normalized))
        {
            return false;
        }

        return normalized.Any(static c => char.IsLetter(c) || IsCjk(c)) ||
               normalized.Count(char.IsDigit) >= 2;
    }

    private static void Mark(bool[] removal, int start, int length)
    {
        if (start < 0 || length <= 0 || start >= removal.Length)
        {
            return;
        }

        var end = Math.Min(removal.Length, start + length);
        for (var i = start; i < end; i++)
        {
            removal[i] = true;
        }
    }

    private static bool HasLetterOrCjk(string value) =>
        value.Any(static c => char.IsLetter(c) || IsCjk(c));

    private static bool ContainsCjk(string value) => value.Any(IsCjk);

    private static bool IsCjk(char c) =>
        c is >= '\u3040' and <= '\u30ff' ||
        c is >= '\u3400' and <= '\u4dbf' ||
        c is >= '\u4e00' and <= '\u9fff' ||
        c is >= '\uac00' and <= '\ud7af';

    private static bool IsOpeningPunctuation(char c) =>
        c is '(' or '[' or '{' or '【' or '「' or '『';

    private static bool IsClosingPunctuation(char c) =>
        c is ')' or ']' or '}' or '】' or '」' or '』' or ':' or '：' or ',' or '，' or '.' or '。';

    private static bool IsSeparatorPunctuation(char c) =>
        c is '-' or '–' or '—' or '−' or '_' or '.';

    private static bool IsEdgeDecoration(char c) =>
        char.IsWhiteSpace(c) ||
        c is '-' or '–' or '—' or '−' or '_' or '.' or ',' or ';' or ':' or
        '[' or ']' or '(' or ')' or '{' or '}' or '【' or '】';
}
