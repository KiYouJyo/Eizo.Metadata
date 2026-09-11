using System.Globalization;

namespace Eizo.Metadata.Recognition.Internal;

internal static class ConfidenceScorer
{
    private const double HighThreshold = 0.85;
    private const double MediumThreshold = 0.65;

    internal static ConfidenceAssessment Assess(
        MediaKind mediaKind,
        EpisodeExtractionResult episode,
        TitleExtractionResult title,
        DomainClassificationResult domain)
    {
        ArgumentNullException.ThrowIfNull(episode);
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(domain);

        var evidence = new List<RecognitionEvidence>();
        var score = BaseScore(mediaKind, episode, title, domain, evidence);
        var isAmbiguous = false;

        ApplyTitleConsensus(
            mediaKind,
            episode,
            domain,
            title,
            ref score,
            ref isAmbiguous,
            evidence);
        ApplyStructuralConflicts(mediaKind, episode, title, domain, ref score, ref isAmbiguous, evidence);
        ApplyEvidencePenalties(episode, ref score, ref isAmbiguous, evidence);

        score = Math.Clamp(score, 0.0, 0.99);
        var level = ToLevel(score);

        evidence.Add(new RecognitionEvidence(
            "confidence.final",
            score.ToString("0.000", CultureInfo.InvariantCulture),
            score));

        return new ConfidenceAssessment(
            score,
            level,
            isAmbiguous,
            evidence);
    }

    private static double BaseScore(
        MediaKind mediaKind,
        EpisodeExtractionResult episode,
        TitleExtractionResult title,
        DomainClassificationResult domain,
        ICollection<RecognitionEvidence> evidence)
    {
        var titleScore = title.Title is null ? 0.0 : title.Confidence;
        var episodeScore = episode.EpisodeNumber is null ? 0.0 : episode.Confidence;
        var domainScore = domain.MediaKind == MediaKind.Unknown ? 0.0 : domain.Confidence;

        if (titleScore > 0)
        {
            evidence.Add(new RecognitionEvidence(
                "confidence.component.title",
                titleScore.ToString("0.000", CultureInfo.InvariantCulture),
                titleScore));
        }

        if (episodeScore > 0)
        {
            evidence.Add(new RecognitionEvidence(
                "confidence.component.episode",
                episodeScore.ToString("0.000", CultureInfo.InvariantCulture),
                episodeScore));
        }

        if (domainScore > 0)
        {
            evidence.Add(new RecognitionEvidence(
                "confidence.component.domain",
                domainScore.ToString("0.000", CultureInfo.InvariantCulture),
                domainScore));
        }

        return mediaKind switch
        {
            MediaKind.SeriesEpisode => ScoreSeries(
                episodeScore,
                titleScore,
                domainScore,
                domain.IsFinalEpisode || domain.EpisodePart != EpisodePart.None),

            MediaKind.Special => ScoreDomainItem(
                domainScore,
                titleScore),

            MediaKind.Movie => ScoreDomainItem(
                domainScore,
                titleScore),

            _ => titleScore > 0
                ? Math.Min(0.58, titleScore * 0.72)
                : 0.0,
        };
    }

    private static double ScoreSeries(
        double episodeScore,
        double titleScore,
        double domainScore,
        bool hasDomainEpisodeSemantics)
    {
        if (episodeScore > 0 && titleScore > 0)
        {
            return episodeScore * 0.58 + titleScore * 0.42;
        }

        if (hasDomainEpisodeSemantics && domainScore > 0 && titleScore > 0)
        {
            return domainScore * 0.55 + titleScore * 0.45;
        }

        if (episodeScore > 0)
        {
            return episodeScore * 0.76;
        }

        if (hasDomainEpisodeSemantics && domainScore > 0)
        {
            return domainScore * 0.72;
        }

        return 0.0;
    }

    private static double ScoreDomainItem(
        double domainScore,
        double titleScore)
    {
        if (domainScore > 0 && titleScore > 0)
        {
            return domainScore * 0.58 + titleScore * 0.42;
        }

        if (domainScore > 0)
        {
            return domainScore * 0.78;
        }

        return 0.0;
    }

    private static void ApplyTitleConsensus(
        MediaKind mediaKind,
        EpisodeExtractionResult episode,
        DomainClassificationResult domain,
        TitleExtractionResult title,
        ref double score,
        ref bool isAmbiguous,
        ICollection<RecognitionEvidence> evidence)
    {
        if (title.Candidates.Count < 2)
        {
            return;
        }

        var first = title.Candidates[0];
        var second = title.Candidates[1];

        if (string.Equals(first.Title, second.Title, StringComparison.Ordinal))
        {
            score += 0.03;
            evidence.Add(new RecognitionEvidence(
                "confidence.title-consensus",
                first.Title,
                0.03));
            return;
        }

        // A filename-derived title and its parent directory are often aliases,
        // translations, romanizations, or release-folder labels for the same item.
        // The 0.1.1 field report showed this was the sole source of 850 ambiguous
        // results. Keep the parent as a metadata candidate, but do not turn a strong
        // filename parse into a review item merely because the parent text differs.
        if ((HasStructuredMediaIdentity(mediaKind, episode, domain) ||
             string.Equals(first.Source, "filename-release", StringComparison.Ordinal)) &&
            IsAuthoritativeFilenameCandidate(first) &&
            string.Equals(second.Source, "parent-directory", StringComparison.Ordinal) &&
            first.Confidence >= 0.82)
        {
            evidence.Add(new RecognitionEvidence(
                "confidence.title-cross-source-alias",
                $"{first.Title} | {second.Title}",
                0.0));
            return;
        }

        var delta = Math.Abs(first.Confidence - second.Confidence);
        if (delta <= 0.07)
        {
            score -= 0.07;
            isAmbiguous = true;
            evidence.Add(new RecognitionEvidence(
                "confidence.title-ambiguity",
                $"{first.Title} | {second.Title}",
                -0.07));
        }
    }

    private static bool HasStructuredMediaIdentity(
        MediaKind mediaKind,
        EpisodeExtractionResult episode,
        DomainClassificationResult domain) =>
        mediaKind != MediaKind.Unknown ||
        episode.EpisodeNumber is not null ||
        domain.MediaKind != MediaKind.Unknown;

    private static bool IsAuthoritativeFilenameCandidate(TitleCandidate candidate) =>
        candidate.Source is
            "filename" or
            "filename-release" or
            "filename-episode-title" or
            "filename-extended-sxxexx" or
            "bracket-sequence" or
            "bracket-episode";

    private static void ApplyStructuralConflicts(
        MediaKind mediaKind,
        EpisodeExtractionResult episode,
        TitleExtractionResult title,
        DomainClassificationResult domain,
        ref double score,
        ref bool isAmbiguous,
        ICollection<RecognitionEvidence> evidence)
    {
        if (mediaKind is not (MediaKind.Special or MediaKind.Movie) ||
            episode.EpisodeNumber is null)
        {
            return;
        }

        if (mediaKind == MediaKind.Special &&
            IsCompatibleSpecialEpisode(episode, title, domain))
        {
            return;
        }

        var onlyBareEpisode = episode.Evidence.Count > 0 &&
                              episode.Evidence.All(static item =>
                                  item.Code == "episode.bare-filename" ||
                                  item.Code == "episode.bare-series-context" ||
                                  item.Code.StartsWith("season.directory", StringComparison.Ordinal) ||
                                  item.Code == "cour.directory");

        if (onlyBareEpisode)
        {
            return;
        }

        score -= 0.12;
        isAmbiguous = true;
        evidence.Add(new RecognitionEvidence(
            "confidence.domain-episode-conflict",
            mediaKind.ToString(),
            -0.12));
    }

    private static bool IsCompatibleSpecialEpisode(
        EpisodeExtractionResult episode,
        TitleExtractionResult title,
        DomainClassificationResult domain)
    {
        // S00E.. is the de-facto Specials convention used by Plex/Jellyfin-style
        // libraries. It is not a contradiction with a Specials/OVA directory.
        if (episode.SeasonNumber == 0)
        {
            return true;
        }

        // "OVA 02" can be observed by both the domain and episode analyzers.
        // Matching numbers are corroboration, not a structural conflict.
        if (domain.SpecialNumber is { } specialNumber &&
            episode.EpisodeNumber == specialNumber)
        {
            return true;
        }

        var hasLeadingIndex = episode.Evidence.Any(static item =>
            item.Code == "episode.leading-numbered");
        var hasBareDelimitedIndex = episode.Evidence.Any(static item =>
            item.Code == "episode.bare-delimited");

        // Real libraries often prefix an explicit OVA/OAD/ONA with a collection
        // index ("25_OVA1") or use the broadcast ordinal before a trailing [OVA].
        // The explicit special marker is authoritative in those two narrow shapes.
        if (domain.SpecialKind is SpecialKind.Ova or SpecialKind.Oad or SpecialKind.Ona &&
            (hasLeadingIndex || hasBareDelimitedIndex))
        {
            return true;
        }

        // Disc menus and trailer collections commonly use [SPxx] for the group and
        // a trailing "- 01" child index. Keep genuinely conflicting SxxExx + SPxx
        // cases ambiguous; only suppress the bare child-index shape with an
        // auxiliary-content title marker observed in the field report.
        return hasBareDelimitedIndex &&
               IsAuxiliarySpecialTitle(title.Title);
    }

    private static bool IsAuxiliarySpecialTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        return title.Contains("MENU", StringComparison.OrdinalIgnoreCase) ||
               title.Contains("TRAILER", StringComparison.OrdinalIgnoreCase) ||
               title.Contains("TOKUTEN", StringComparison.OrdinalIgnoreCase) ||
               title.Contains("特典", StringComparison.Ordinal);
    }

    private static void ApplyEvidencePenalties(
        EpisodeExtractionResult episode,
        ref double score,
        ref bool isAmbiguous,
        ICollection<RecognitionEvidence> evidence)
    {
        if (episode.Evidence.Any(static item =>
            item.Code == "season.directory.conflict"))
        {
            score -= 0.08;
            isAmbiguous = true;
            evidence.Add(new RecognitionEvidence(
                "confidence.season-conflict",
                null,
                -0.08));
        }
    }

    private static RecognitionConfidenceLevel ToLevel(double score)
    {
        if (score <= 0.0)
        {
            return RecognitionConfidenceLevel.None;
        }

        if (score >= HighThreshold)
        {
            return RecognitionConfidenceLevel.High;
        }

        if (score >= MediumThreshold)
        {
            return RecognitionConfidenceLevel.Medium;
        }

        return RecognitionConfidenceLevel.Low;
    }
}
