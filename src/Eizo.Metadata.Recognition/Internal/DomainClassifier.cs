using System.Globalization;
using System.Text.RegularExpressions;

namespace Eizo.Metadata.Recognition.Internal;

internal static class DomainClassifier
{
    private const RegexOptions Options =
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(50);

    private static readonly Regex SpecialMarkerRegex = new(
        @"(?<![\p{L}\p{N}])(?<kind>OVA|OAD|ONA|SP|SPECIALS?|NCOP|NCED)(?:\s*[-_. ]?\s*(?<number>\d{1,3}(?:\.\d+)?))?(?=\s*(?:$|\[|\(|【|\]|\)|】))",
        Options,
        RegexTimeout);

    private static readonly Regex JapaneseSpecialRegex = new(
        @"(?:^|[\s._-])(?<kind>スペシャル|特別編|総集編)(?=\s*(?:$|\[|\(|【))",
        Options,
        RegexTimeout);

    private static readonly Regex JapaneseMovieRegex = new(
        @"(?:^(?:劇場版|剧场版)|^(?:映画|电影|電影)[\s　]+|[\s._-](?:劇場版|剧场版|映画|电影|電影)(?=\s*(?:$|\[|\(|【)))",
        Options,
        RegexTimeout);

    private static readonly Regex EnglishMovieRegex = new(
        @"(?:^MOVIE(?=\s*[-:])|[\s._-]MOVIE(?=\s*(?:$|\[|\(|【)))",
        Options,
        RegexTimeout);

    private static readonly Regex FinaleRegex = new(
        @"(?:最終話|最終回)(?![\p{L}\p{N}])",
        Options,
        RegexTimeout);

    private static readonly Regex FirstPartRegex = new(
        @"(?:前編|前篇|前编)(?![\p{L}\p{N}])",
        Options,
        RegexTimeout);

    private static readonly Regex SecondPartRegex = new(
        @"(?:後編|後篇|后编|后篇)(?![\p{L}\p{N}])",
        Options,
        RegexTimeout);

    private static readonly Regex MovieDirectoryHintRegex = new(
        @"(?:劇場版|剧场版|映画|电影|電影|真人版|LIVE[\s._-]*ACTION)",
        Options,
        RegexTimeout);

    private static readonly Regex SeriesDirectoryHintRegex = new(
        @"(?:SEASON\s*0?\d{1,2}|(?<![A-Za-z0-9])S\s*0?\d{1,2}(?![A-Za-z0-9])|(?<![A-Za-z])PART\s*0?\d{1,2}(?!\d)|COUR\s*0?\d{1,2}|第\s*0?\d{1,2}\s*(?:季|期|クール|シーズン|シリーズ)|(?:^|[\s._-])TV(?:版|\s*SERIES)?(?:$|[\s._-])|^番(?:劇|剧)$)",
        Options,
        RegexTimeout);

    private static readonly Dictionary<string, (MediaKind Kind, SpecialKind Special)> DirectoryKinds =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["OVA"] = (MediaKind.Special, SpecialKind.Ova),
            ["OAD"] = (MediaKind.Special, SpecialKind.Oad),
            ["ONA"] = (MediaKind.Special, SpecialKind.Ona),
            ["SP"] = (MediaKind.Special, SpecialKind.Special),
            ["SPECIAL"] = (MediaKind.Special, SpecialKind.Special),
            ["SPECIALS"] = (MediaKind.Special, SpecialKind.Special),
            ["NCOP"] = (MediaKind.Special, SpecialKind.NcOp),
            ["NCED"] = (MediaKind.Special, SpecialKind.NcEd),
            ["MOVIE"] = (MediaKind.Movie, SpecialKind.None),
            ["MOVIES"] = (MediaKind.Movie, SpecialKind.None),
            ["劇場版"] = (MediaKind.Movie, SpecialKind.None),
        };

    internal static DomainClassificationResult Classify(NormalizedMediaPath path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var evidence = new List<RecognitionEvidence>();
        var mediaKind = MediaKind.Unknown;
        var specialKind = SpecialKind.None;
        var episodePart = EpisodePart.None;
        var isFinal = false;
        decimal? specialNumber = null;
        var confidence = 0.0;

        var specialMatch = SpecialMarkerRegex.Match(path.NormalizedStem);
        if (specialMatch.Success)
        {
            specialKind = ParseSpecialKind(specialMatch.Groups["kind"].Value);
            mediaKind = MediaKind.Special;
            specialNumber = ParseDecimal(specialMatch.Groups["number"].Value);
            confidence = 0.96;

            evidence.Add(new RecognitionEvidence(
                $"special.{ToEvidenceName(specialKind)}",
                specialMatch.Value.Trim(),
                0.96));
        }
        else
        {
            var japaneseSpecial = JapaneseSpecialRegex.Match(path.NormalizedStem);
            if (japaneseSpecial.Success)
            {
                specialKind = SpecialKind.Special;
                mediaKind = MediaKind.Special;
                confidence = 0.94;
                evidence.Add(new RecognitionEvidence(
                    "special.japanese",
                    japaneseSpecial.Value.Trim(),
                    0.94));
            }
        }

        if (mediaKind == MediaKind.Unknown)
        {
            if (JapaneseMovieRegex.IsMatch(path.NormalizedStem))
            {
                mediaKind = MediaKind.Movie;
                confidence = 0.95;
                evidence.Add(new RecognitionEvidence(
                    "movie.japanese-marker",
                    JapaneseMovieRegex.Match(path.NormalizedStem).Value.Trim(),
                    0.95));
            }
            else if (EnglishMovieRegex.IsMatch(path.NormalizedStem))
            {
                mediaKind = MediaKind.Movie;
                confidence = 0.88;
                evidence.Add(new RecognitionEvidence(
                    "movie.english-marker",
                    EnglishMovieRegex.Match(path.NormalizedStem).Value.Trim(),
                    0.88));
            }
        }

        ApplyDirectoryContext(
            path,
            ref mediaKind,
            ref specialKind,
            ref confidence,
            evidence);

        if (mediaKind == MediaKind.Unknown)
        {
            ApplyMovieDirectoryHint(
                path,
                ref mediaKind,
                ref confidence,
                evidence);
        }

        var finaleMatch = FinaleRegex.Match(path.NormalizedStem);
        if (finaleMatch.Success)
        {
            isFinal = true;
            if (mediaKind == MediaKind.Unknown)
            {
                mediaKind = MediaKind.SeriesEpisode;
            }

            confidence = Math.Max(confidence, 0.93);
            evidence.Add(new RecognitionEvidence(
                "episode.final",
                finaleMatch.Value,
                0.93));
        }

        var firstPart = FirstPartRegex.Match(path.NormalizedStem);
        var secondPart = SecondPartRegex.Match(path.NormalizedStem);
        if (firstPart.Success || secondPart.Success)
        {
            episodePart = secondPart.Success ? EpisodePart.Second : EpisodePart.First;
            if (mediaKind == MediaKind.Unknown)
            {
                mediaKind = MediaKind.SeriesEpisode;
            }

            var value = secondPart.Success ? secondPart.Value : firstPart.Value;
            confidence = Math.Max(confidence, 0.88);
            evidence.Add(new RecognitionEvidence(
                episodePart == EpisodePart.First
                    ? "episode.part.first"
                    : "episode.part.second",
                value,
                0.88));
        }

        if (mediaKind == MediaKind.Unknown &&
            specialKind == SpecialKind.None &&
            episodePart == EpisodePart.None &&
            !isFinal)
        {
            return DomainClassificationResult.Empty;
        }

        return new DomainClassificationResult(
            mediaKind,
            specialKind,
            episodePart,
            isFinal,
            specialNumber,
            confidence,
            evidence);
    }

    private static void ApplyDirectoryContext(
        NormalizedMediaPath path,
        ref MediaKind mediaKind,
        ref SpecialKind specialKind,
        ref double confidence,
        ICollection<RecognitionEvidence> evidence)
    {
        for (var i = path.NormalizedDirectorySegments.Count - 1; i >= 0; i--)
        {
            var directory = path.NormalizedDirectorySegments[i].Trim();
            if (!DirectoryKinds.TryGetValue(directory, out var value))
            {
                continue;
            }

            if (mediaKind == MediaKind.Unknown)
            {
                mediaKind = value.Kind;
                specialKind = value.Special;
                confidence = Math.Max(confidence, 0.90);
                evidence.Add(new RecognitionEvidence(
                    value.Kind == MediaKind.Movie
                        ? "movie.directory"
                        : $"special.{ToEvidenceName(value.Special)}.directory",
                    directory,
                    0.90));
            }
            else
            {
                evidence.Add(new RecognitionEvidence(
                    "domain.directory.confirm",
                    directory,
                    0.30));
            }

            break;
        }
    }

    private static void ApplyMovieDirectoryHint(
        NormalizedMediaPath path,
        ref MediaKind mediaKind,
        ref double confidence,
        ICollection<RecognitionEvidence> evidence)
    {
        if (path.NormalizedDirectorySegments.Count == 0)
        {
            return;
        }

        var start = Math.Max(0, path.NormalizedDirectorySegments.Count - 2);
        var nearby = path.NormalizedDirectorySegments
            .Skip(start)
            .Select(static directory => directory.Trim())
            .Where(static directory => directory.Length > 0)
            .ToArray();

        // A collection directory such as "...1-6季+OVA+剧场版..." must not
        // turn ordinary numbered TV episodes into movies. Nearby explicit
        // season/part/TV context wins over a movie word in an ancestor.
        if (nearby.Any(static directory =>
                SeriesDirectoryHintRegex.IsMatch(directory)))
        {
            return;
        }

        for (var i = nearby.Length - 1; i >= 0; i--)
        {
            var match = MovieDirectoryHintRegex.Match(nearby[i]);
            if (!match.Success)
            {
                continue;
            }

            mediaKind = MediaKind.Movie;
            confidence = Math.Max(confidence, 0.88);
            evidence.Add(new RecognitionEvidence(
                "movie.directory-hint",
                nearby[i],
                0.88));
            return;
        }
    }

    private static SpecialKind ParseSpecialKind(string value) =>
        value.ToUpperInvariant() switch
        {
            "OVA" => SpecialKind.Ova,
            "OAD" => SpecialKind.Oad,
            "ONA" => SpecialKind.Ona,
            "NCOP" => SpecialKind.NcOp,
            "NCED" => SpecialKind.NcEd,
            _ => SpecialKind.Special,
        };

    private static string ToEvidenceName(SpecialKind kind) =>
        kind switch
        {
            SpecialKind.Ova => "ova",
            SpecialKind.Oad => "oad",
            SpecialKind.Ona => "ona",
            SpecialKind.NcOp => "ncop",
            SpecialKind.NcEd => "nced",
            _ => "special",
        };

    private static decimal? ParseDecimal(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return decimal.TryParse(
            value,
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out var result)
            ? result
            : null;
    }
}
