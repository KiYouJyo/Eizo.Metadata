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
        Assert.Equal("ドラマ", result.Title);
        Assert.Equal(1, result.SeasonNumber);
        Assert.Equal(3m, result.EpisodeNumber);
        Assert.Equal(2023, result.Year);
        Assert.InRange(result.Confidence, 0.0, 1.0);
    }

    [Fact]
    public void Recognize_NonEpisodeTechnicalNameKeepsUsefulTitle()
    {
        var result = _engine.Recognize(
            new RecognitionRequest("Movie.2026.1080p.WEB-DL.x265.AAC.mkv"));

        Assert.Equal(MediaKind.Unknown, result.MediaKind);
        Assert.Equal("Movie", result.Title);
        Assert.Null(result.EpisodeNumber);
        Assert.Equal(2026, result.Year);
    }

    [Fact]
    public void Recognize_UsesParentTitleForNumericEpisodeFile()
    {
        var result = _engine.Recognize(
            new RecognitionRequest("Anime/葬送のフリーレン/Season 01/03.mkv"));

        Assert.Equal(MediaKind.SeriesEpisode, result.MediaKind);
        Assert.Equal("葬送のフリーレン", result.Title);
        Assert.Equal(1, result.SeasonNumber);
        Assert.Equal(3m, result.EpisodeNumber);
    }

    [Fact]
    public void Recognize_ExposesCourContext()
    {
        var result = _engine.Recognize(
            new RecognitionRequest("Anime/Example/Cour 2/03.mkv"));

        Assert.Equal(2, result.CourNumber);
        Assert.Equal(3m, result.EpisodeNumber);
    }

    [Fact]
    public void Recognize_IsDeterministic()
    {
        var request = new RecognitionRequest(
            "Anime/Season 02/[ANi] Example - 14 [1080P][WEB-DL][AAC].mkv");

        var first = _engine.Recognize(request);
        var second = _engine.Recognize(request);

        Assert.Equal(first.MediaKind, second.MediaKind);
        Assert.Equal(first.SpecialKind, second.SpecialKind);
        Assert.Equal(first.EpisodePart, second.EpisodePart);
        Assert.Equal(first.IsFinalEpisode, second.IsFinalEpisode);
        Assert.Equal(first.Title, second.Title);
        Assert.Equal(first.EpisodeTitle, second.EpisodeTitle);
        Assert.Equal(first.SeasonNumber, second.SeasonNumber);
        Assert.Equal(first.CourNumber, second.CourNumber);
        Assert.Equal(first.EpisodeNumber, second.EpisodeNumber);
        Assert.Equal(first.EpisodeEndNumber, second.EpisodeEndNumber);
        Assert.Equal(first.SpecialNumber, second.SpecialNumber);
        Assert.Equal(first.Year, second.Year);
        Assert.Equal(first.Confidence, second.Confidence);
        Assert.Equal(first.Evidence.Count, second.Evidence.Count);

        for (var i = 0; i < first.Evidence.Count; i++)
        {
            Assert.Equal(first.Evidence[i], second.Evidence[i]);
        }
    }
}
