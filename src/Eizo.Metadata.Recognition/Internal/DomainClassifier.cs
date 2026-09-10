using System.Globalization;
using System.Text.RegularExpressions;

namespace Eizo.Metadata.Recognition.Internal;

internal static class DomainClassifier
{
    private const RegexOptions Options =
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(50);

    private static readonly Regex SpecialMarkerRegex = new(
        @"(?<![A-Za-z0-9])(?<kind>OVA|OAD|ONA|SP|SPECIALS?|NCOP|NCED)(?:\s*[-_. ]?\s*(?<number>\d{1,3}(?:\.\d+)?))?(?=\s*(?:$|\[|\(|【|\]|\)|】))",
        Options,
        RegexTimeout);

    private static readonly Regex JapaneseSpecialRegex = new(
        @"(?:^|[\s._-])(?<kind>スペシャル|特別編|総集編)(?=\s*(?:$|\[|\(|【))",
        Options,
        RegexTimeout);

    private static readonly Regex JapaneseMovieRegex = new(
        @"(?:^劇場版|^映画[\s　]+|[\s._-](?:劇場版|映画)(?=\s*(?:$|\[|\(|【)))",
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
        @"(?:前編|前篇)(?![\p{L}\p{N}])",
        Options,
        RegexTimeout);

    private static readonly Regex SecondPartRegex = new(
        @"(?:後編|後篇)(?![\p{L}\p{N}])",
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
