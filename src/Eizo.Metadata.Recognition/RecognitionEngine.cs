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
        var title = TitleExtractor.Extract(path, episode);

        var evidence = new List<RecognitionEvidence>(episode.Evidence);
        evidence.AddRange(title.Evidence);

        var year = TryGetYear(path, evidence);
        var confidence = CombineConfidence(episode, title);

        return new RecognitionResult(
            episode.EpisodeNumber is null ? MediaKind.Unknown : MediaKind.SeriesEpisode,
            title.Title,
            episode.SeasonNumber,
            episode.EpisodeNumber,
            episode.EpisodeEndNumber,
            year,
            confidence,
            evidence);
    }

    private static double CombineConfidence(
        EpisodeExtractionResult episode,
        TitleExtractionResult title)
    {
        if (episode.EpisodeNumber is not null && title.Title is not null)
        {
            return Math.Min(episode.Confidence, title.Confidence);
        }

        return Math.Max(episode.Confidence, title.Confidence);
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
