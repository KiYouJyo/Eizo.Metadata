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

        var evidence = new List<RecognitionEvidence>(episode.Evidence);
        var year = TryGetYear(path, evidence);

        return new RecognitionResult(
            episode.EpisodeNumber is null ? MediaKind.Unknown : MediaKind.SeriesEpisode,
            Title: null,
            episode.SeasonNumber,
            episode.EpisodeNumber,
            episode.EpisodeEndNumber,
            year,
            episode.Confidence,
            evidence);
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
