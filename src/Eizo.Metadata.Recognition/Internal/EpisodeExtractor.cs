using System.Globalization;
using System.Text.RegularExpressions;

namespace Eizo.Metadata.Recognition.Internal;

internal static class EpisodeExtractor
{
    private const RegexOptions Options =
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(50);

    private static readonly Regex SeasonEpisodeRegex = new(
        @"(?<![A-Za-z0-9])S(?<season>\d{1,2})E(?<episode>\d{1,3}(?:\.\d+)?)(?![PpIi])(?:\s*[-~]\s*(?:E)?(?<end>\d{1,3}(?:\.\d+)?)(?![PpIi]))?(?!\d)",
        Options,
        RegexTimeout);

    private static readonly Regex OneXEpisodeRegex = new(
        @"(?<![A-Za-z0-9])(?<season>\d{1,2})x(?<episode>\d{1,3}(?:\.\d+)?)(?![PpIi\d])",
        Options,
        RegexTimeout);

    private static readonly Regex PrefixedEpisodeRegex = new(
        @"(?<![A-Za-z0-9])(?:EPISODE|EP|E)\s*[-_. ]?(?<episode>\d{1,3}(?:\.\d+)?)(?![PpIi])(?:\s*[-~]\s*(?:EP|E)?\s*(?<end>\d{1,3}(?:\.\d+)?)(?![PpIi]))?(?!\d)",
        Options,
        RegexTimeout);

    private static readonly Regex JapaneseEpisodeRegex = new(
        @"第\s*(?<episode>\d{1,3}(?:\.\d+)?)\s*(?:話|回)",
        Options,
        RegexTimeout);

    private static readonly Regex DashEpisodeRegex = new(
        @"(?:^|\s)[-–—−]\s*(?<episode>\d{1,3}(?:\.\d+)?)(?:\s*[-~]\s*(?<end>\d{1,3}(?:\.\d+)?))?(?=\s|$|\[|\(|【)",
        Options,
        RegexTimeout);

    private static readonly Regex PureEpisodeRegex = new(
        @"^\s*(?<episode>\d{1,3}(?:\.\d+)?)\s*$",
        Options,
        RegexTimeout);

    private static readonly Regex SeasonDirectoryRegex = new(
        @"(?:^|[\s._-])(?:SEASON|S)\s*0?(?<season>\d{1,2})(?:$|[\s._-])",
        Options,
        RegexTimeout);

    private static readonly Regex JapaneseSeasonDirectoryRegex = new(
        @"第\s*0?(?<season>\d{1,2})\s*(?:期|シーズン|シリーズ)",
        Options,
        RegexTimeout);

    private static readonly Regex OrdinalSeasonDirectoryRegex = new(
        @"(?<!\d)(?<season>\d{1,2})(?:ST|ND|RD|TH)\s+SEASON",
        Options,
        RegexTimeout);

    private static readonly Regex CourDirectoryRegex = new(
        @"(?:COUR\s*0?(?<cour>\d{1,2})|第\s*0?(?<courjp>\d{1,2})\s*クール|(?<courord>\d{1,2})(?:ST|ND|RD|TH)\s+COUR)",
        Options,
        RegexTimeout);

    private static readonly HashSet<int> ResolutionCollisions = new()
    {
        360, 480, 576, 720, 1080, 1440, 2160, 4320,
    };

    internal static EpisodeExtractionResult Extract(NormalizedMediaPath path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var stem = path.NormalizedStem;

        var result =
            MatchPattern(SeasonEpisodeRegex, stem, "episode.sxxexx", 0.98) ??
            MatchPattern(OneXEpisodeRegex, stem, "episode.onex", 0.96) ??
            MatchPattern(PrefixedEpisodeRegex, stem, "episode.prefixed", 0.95) ??
            MatchPattern(JapaneseEpisodeRegex, stem, "episode.japanese-numbered", 0.95) ??
            MatchAllBracketSequence(path) ??
            MatchTrailingBareWithReleaseTags(path) ??
            MatchBare(DashEpisodeRegex, stem, "episode.bare-delimited", 0.80) ??
            MatchBare(PureEpisodeRegex, stem, "episode.bare-filename", 0.66) ??
            EpisodeExtractionResult.Empty;

        return ApplyDirectoryContext(path, result);
    }

    private static EpisodeExtractionResult? MatchPattern(
        Regex regex,
        string input,
        string evidenceCode,
        double confidence)
    {
        var match = regex.Match(input);
        if (!match.Success)
        {
            return null;
        }

        var episode = ParseDecimal(GetGroupValue(match, "episode"));
        if (episode is null)
        {
            return null;
        }

        var season = ParseInt(GetGroupValue(match, "season"));
        var end = ParseDecimal(GetGroupValue(match, "end"));

        var evidence = new List<RecognitionEvidence>
        {
            new(evidenceCode, match.Value, confidence),
        };

        return new EpisodeExtractionResult(
            season,
            episode,
            end,
            CourNumber: null,
            confidence,
            evidence);
    }

    private static EpisodeExtractionResult? MatchAllBracketSequence(
        NormalizedMediaPath path)
    {
        if (!BracketSequenceAnalyzer.TryAnalyze(path, out var sequence) ||
            !BracketSequenceAnalyzer.TryParseEpisode(sequence.EpisodeToken, out var episode))
        {
            return null;
        }

        return new EpisodeExtractionResult(
            SeasonNumber: null,
            episode,
            EpisodeEndNumber: null,
            CourNumber: null,
            sequence.Confidence,
            new[]
            {
                new RecognitionEvidence(
                    "episode.bracket-sequence",
                    sequence.EpisodeToken.NormalizedValue,
                    sequence.Confidence),
            });
    }

    private static EpisodeExtractionResult? MatchTrailingBareWithReleaseTags(
        NormalizedMediaPath path)
    {
        if (!TrailingBareEpisodeHeuristic.TryMatch(
                path,
                out var episodeToken,
                out var releaseTags) ||
            !decimal.TryParse(
                episodeToken.NormalizedValue,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var episode))
        {
            return null;
        }

        var evidenceValue =
            episodeToken.NormalizedValue + " " +
            string.Join(
                string.Empty,
                releaseTags.Select(static tag => tag.RawValue));

        return new EpisodeExtractionResult(
            SeasonNumber: null,
            episode,
            EpisodeEndNumber: null,
            CourNumber: null,
            Confidence: 0.78,
            new[]
            {
                new RecognitionEvidence(
                    "episode.bare-trailing-release-tag",
                    evidenceValue,
                    0.78),
            });
    }

    private static EpisodeExtractionResult? MatchBare(
        Regex regex,
        string input,
        string evidenceCode,
        double confidence)
    {
        var match = regex.Match(input);
        if (!match.Success)
        {
            return null;
        }

        var episode = ParseDecimal(GetGroupValue(match, "episode"));
        if (episode is null || IsBareCollision(episode.Value))
        {
            return null;
        }

        var end = ParseDecimal(GetGroupValue(match, "end"));
        if (end is not null && IsBareCollision(end.Value))
        {
            end = null;
        }

        return new EpisodeExtractionResult(
            SeasonNumber: null,
            episode,
            end,
            CourNumber: null,
            confidence,
            new[]
            {
                new RecognitionEvidence(evidenceCode, match.Value, confidence),
            });
    }

    private static EpisodeExtractionResult ApplyDirectoryContext(
        NormalizedMediaPath path,
        EpisodeExtractionResult result)
    {
        int? directorySeason = null;
        int? cour = null;
        string? seasonEvidenceValue = null;
        string? courEvidenceValue = null;

        for (var i = path.NormalizedDirectorySegments.Count - 1; i >= 0; i--)
        {
            var directory = path.NormalizedDirectorySegments[i];

            if (directorySeason is null &&
                TryParseSeasonDirectory(directory, out var parsedSeason))
            {
                directorySeason = parsedSeason;
                seasonEvidenceValue = directory;
            }

            if (cour is null && TryParseCourDirectory(directory, out var parsedCour))
            {
                cour = parsedCour;
                courEvidenceValue = directory;
            }

            if (directorySeason is not null && cour is not null)
            {
                break;
            }
        }

        if (directorySeason is null && cour is null)
        {
            return result;
        }

        var evidence = new List<RecognitionEvidence>(result.Evidence);
        var season = result.SeasonNumber;
        var confidence = result.Confidence;

        if (directorySeason is not null)
        {
            if (season is null)
            {
                season = directorySeason;
                evidence.Add(new RecognitionEvidence(
                    "season.directory",
                    seasonEvidenceValue,
                    0.72));

                if (result.EpisodeNumber is not null)
                {
                    confidence = Math.Min(0.99, confidence + 0.04);
                }
            }
            else if (season == directorySeason)
            {
                evidence.Add(new RecognitionEvidence(
                    "season.directory.confirm",
                    seasonEvidenceValue,
                    0.35));
                confidence = Math.Min(0.99, confidence + 0.01);
            }
            else
            {
                evidence.Add(new RecognitionEvidence(
                    "season.directory.conflict",
                    seasonEvidenceValue,
                    -0.20));
            }
        }

        if (cour is not null)
        {
            evidence.Add(new RecognitionEvidence(
                "cour.directory",
                courEvidenceValue,
                0.55));
        }

        return new EpisodeExtractionResult(
            season,
            result.EpisodeNumber,
            result.EpisodeEndNumber,
            cour,
            confidence,
            evidence);
    }

    private static bool TryParseSeasonDirectory(string directory, out int season)
    {
        foreach (var regex in new[]
                 {
                     SeasonDirectoryRegex,
                     JapaneseSeasonDirectoryRegex,
                     OrdinalSeasonDirectoryRegex,
                 })
        {
            var match = regex.Match(directory);
            if (match.Success &&
                int.TryParse(GetGroupValue(match, "season"), out season) &&
                season is >= 0 and <= 99)
            {
                return true;
            }
        }

        season = default;
        return false;
    }

    private static bool TryParseCourDirectory(string directory, out int cour)
    {
        var match = CourDirectoryRegex.Match(directory);
        if (!match.Success)
        {
            cour = default;
            return false;
        }

        var value =
            GetGroupValue(match, "cour") ??
            GetGroupValue(match, "courjp") ??
            GetGroupValue(match, "courord");

        return int.TryParse(value, out cour) && cour is >= 1 and <= 99;
    }

    private static string? GetGroupValue(Match match, string name)
    {
        var group = match.Groups[name];
        return group.Success ? group.Value : null;
    }

    private static bool IsBareCollision(decimal episode)
    {
        if (episode != decimal.Truncate(episode))
        {
            return false;
        }

        if (episode is < 0 or > 999)
        {
            return true;
        }

        var value = decimal.ToInt32(episode);
        return ResolutionCollisions.Contains(value) ||
               value is >= 1900 and <= 2099;
    }

    private static decimal? ParseDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return decimal.TryParse(
            value,
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : null;
    }

    private static int? ParseInt(string? value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
}
