namespace Eizo.Metadata.Recognition.Tests;

public sealed class RecognitionEngineTests
{
    private readonly RecognitionEngine _engine = new();

    [Fact]
    public void Recognize_ProducesProviderNeutralEpisodeResult()
    {
        var result = _engine.Recognize(
            new RecognitionRequest("Drama/Season 01/ドラマ 2023 第03話 1080p.mkv"));

        Assert.Equal(MediaKind.SeriesEpisode, result.MediaKind);
        Assert.Equal(1, result.SeasonNumber);
        Assert.Equal(3m, result.EpisodeNumber);
        Assert.Equal(2023, result.Year);
        Assert.Null(result.Title);
        Assert.InRange(result.Confidence, 0.0, 1.0);
    }

    [Fact]
    public void Recognize_NonEpisodeTechnicalNameRemainsUnknown()
    {
        var result = _engine.Recognize(
            new RecognitionRequest("Movie.2026.1080p.WEB-DL.x265.AAC.mkv"));

        Assert.Equal(MediaKind.Unknown, result.MediaKind);
        Assert.Null(result.EpisodeNumber);
        Assert.Equal(2026, result.Year);
    }

    [Fact]
    public void Recognize_IsDeterministic()
    {
        var request = new RecognitionRequest(
            "Anime/Season 02/[ANi] Example - 14 [1080P][WEB-DL][AAC].mkv");

        var first = _engine.Recognize(request);
        var second = _engine.Recognize(request);

        Assert.Equal(first, second);
    }
}
