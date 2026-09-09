using Eizo.Metadata.Recognition.Internal;

namespace Eizo.Metadata.Recognition;

/// <summary>
/// Default deterministic, provider-neutral recognition engine.
/// </summary>
public sealed class RecognitionEngine : IRecognitionEngine
{
    public RecognitionResult Recognize(RecognitionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var path = PathPreprocessor.Preprocess(request.Path);
        var episode = EpisodeExtractor.Extract(path);
        var domain = DomainClassifier.Classify(path);
        var title = TitleExtractor.Extract(path, episode, domain);

        var evidence = new List<RecognitionEvidence>(episode.Evidence);
        evidence.AddRange(domain.Evidence);
        evidence.AddRange(title.Evidence);

        var year = TryGetYear(path, evidence);
        var mediaKind = ResolveMediaKind(episode, domain);

        var episodeNumber = episode.EpisodeNumber;
        var episodeEndNumber = episode.EpisodeEndNumber;
        var specialNumber = domain.SpecialNumber;

        if (mediaKind == MediaKind.Special)
        {
            if (specialNumber is null &&
                episodeNumber is not null &&
                episode.Evidence.Any(static item => item.Code == "episode.bare-filename"))
            {
                specialNumber = episodeNumber;
                evidence.Add(new RecognitionEvidence(
                    "special.number.from-bare-filename",
                    episodeNumber.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    0.70));
            }

            episodeNumber = null;
            episodeEndNumber = null;
        }
        else if (mediaKind == MediaKind.Movie)
        {
            episodeNumber = null;
            episodeEndNumber = null;
        }

        var confidence = CombineConfidence(
            episode,
            title,
            domain,
            mediaKind);

        return new RecognitionResult(
            mediaKind,
            domain.SpecialKind,
            domain.EpisodePart,
            domain.IsFinalEpisode,
            title.Title,
            episode.SeasonNumber,
            episode.CourNumber,
            episodeNumber,
            episodeEndNumber,
            specialNumber,
            year,
            confidence,
            evidence);
    }

    private static MediaKind ResolveMediaKind(
        EpisodeExtractionResult episode,
        DomainClassificationResult domain)
    {
        if (domain.MediaKind != MediaKind.Unknown)
        {
            return domain.MediaKind;
        }

        return episode.EpisodeNumber is null
            ? MediaKind.Unknown
            : MediaKind.SeriesEpisode;
    }

    private static double CombineConfidence(
        EpisodeExtractionResult episode,
        TitleExtractionResult title,
        DomainClassificationResult domain,
        MediaKind mediaKind)
    {
        var values = new List<double>();

        if (title.Title is not null)
        {
            values.Add(title.Confidence);
        }

        if (domain.MediaKind != MediaKind.Unknown)
        {
            values.Add(domain.Confidence);
        }

        if (episode.EpisodeNumber is not null &&
            mediaKind == MediaKind.SeriesEpisode)
        {
            values.Add(episode.Confidence);
        }

        if (values.Count == 0)
        {
            return 0.0;
        }

        return values.Min();
    }

    private static int? TryGetYear(
        NormalizedMediaPath path,
        ICollection<RecognitionEvidence> evidence)
    {
        var token = path.Tokens.FirstOrDefault(static token => token.Kind == TokenKind.Year);
        if (token is null || !int.TryParse(token.NormalizedValue, out var year))
        {
            return null;
        }

        evidence.Add(new RecognitionEvidence("year.token", token.NormalizedValue, 0.80));
        return year;
    }
}
