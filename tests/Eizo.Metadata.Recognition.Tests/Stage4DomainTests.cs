namespace Eizo.Metadata.Recognition.Tests;

public sealed class Stage4DomainTests
{
    private readonly RecognitionEngine _engine = new();

    public static TheoryData<string, SpecialKind, string> AnimeSpecialCases => new()
    {
        { "Example - OVA 01 [1080P].mkv", SpecialKind.Ova, "Example" },
        { "Example - OAD 02 [1080P].mkv", SpecialKind.Oad, "Example" },
        { "Example - ONA 03 [1080P].mkv", SpecialKind.Ona, "Example" },
        { "Example - SP 01 [1080P].mkv", SpecialKind.Special, "Example" },
        { "Example - Special 02 [1080P].mkv", SpecialKind.Special, "Example" },
        { "Example/OVA/01.mkv", SpecialKind.Ova, "Example" },
        { "Example/Specials/01.mkv", SpecialKind.Special, "Example" },
        { "Example/NCOP1.mkv", SpecialKind.NcOp, "Example" },
        { "Example/NCED2.mkv", SpecialKind.NcEd, "Example" },
    };

    [Theory]
    [MemberData(nameof(AnimeSpecialCases))]
    public void Recognize_ClassifiesAnimeSpecials(
        string path,
        SpecialKind expectedKind,
        string expectedTitle)
    {
        var result = _engine.Recognize(new RecognitionRequest(path));

        Assert.Equal(MediaKind.Special, result.MediaKind);
        Assert.Equal(expectedKind, result.SpecialKind);
        Assert.Equal(expectedTitle, result.Title);
        Assert.NotNull(result.SpecialNumber);
        Assert.Null(result.EpisodeNumber);
    }

    [Theory]
    [InlineData("劇場版 Example [1080P].mkv", "Example")]
    [InlineData("Example - Movie [1080P].mkv", "Example")]
    [InlineData("Example/Movies/Example [1080P].mkv", "Example")]
    public void Recognize_ClassifiesMovieMarkers(string path, string expectedTitle)
    {
        var result = _engine.Recognize(new RecognitionRequest(path));

        Assert.Equal(MediaKind.Movie, result.MediaKind);
        Assert.Equal(expectedTitle, result.Title);
        Assert.Equal(SpecialKind.None, result.SpecialKind);
        Assert.Null(result.EpisodeNumber);
    }

    [Theory]
    [InlineData("ドラマ 第03回.mp4", 3)]
    [InlineData("ドラマ 第12回 「決着」.mp4", 12)]
    public void Recognize_ParsesJapaneseEpisodeCounter(string path, int expectedEpisode)
    {
        var result = _engine.Recognize(new RecognitionRequest(path));

        Assert.Equal(MediaKind.SeriesEpisode, result.MediaKind);
        Assert.Equal("ドラマ", result.Title);
        Assert.Equal((decimal)expectedEpisode, result.EpisodeNumber);
    }

    [Fact]
    public void Recognize_ParsesFinalEpisodeWithoutInventingNumber()
    {
        var result = _engine.Recognize(
            new RecognitionRequest("ドラマ 最終話 「終幕」.mp4"));

        Assert.Equal(MediaKind.SeriesEpisode, result.MediaKind);
        Assert.True(result.IsFinalEpisode);
        Assert.Null(result.EpisodeNumber);
        Assert.Equal("ドラマ", result.Title);
    }

    [Theory]
    [InlineData("ドラマ 前編.mp4", EpisodePart.First)]
    [InlineData("ドラマ 後編.mp4", EpisodePart.Second)]
    public void Recognize_ParsesJapaneseEpisodeParts(
        string path,
        EpisodePart expectedPart)
    {
        var result = _engine.Recognize(new RecognitionRequest(path));

        Assert.Equal(MediaKind.SeriesEpisode, result.MediaKind);
        Assert.Equal(expectedPart, result.EpisodePart);
        Assert.Equal("ドラマ", result.Title);
    }

    [Fact]
    public void Recognize_ParsesJapaneseSpecial()
    {
        var result = _engine.Recognize(
            new RecognitionRequest("ドラマ スペシャル.mp4"));

        Assert.Equal(MediaKind.Special, result.MediaKind);
        Assert.Equal(SpecialKind.Special, result.SpecialKind);
        Assert.Equal("ドラマ", result.Title);
    }


    [Theory]
    [InlineData("前編資料.2026.1080p.WEB-DL.mkv")]
    [InlineData("後編資料 1080p.mkv")]
    [InlineData("最終話資料 2026.mkv")]
    [InlineData("最終回顧録 2026.mkv")]
    public void Recognize_DoesNotPromoteEmbeddedJapaneseEpisodeMarkers(string path)
    {
        var result = _engine.Recognize(new RecognitionRequest(path));

        Assert.Equal(MediaKind.Unknown, result.MediaKind);
        Assert.Equal(EpisodePart.None, result.EpisodePart);
        Assert.False(result.IsFinalEpisode);
        Assert.Null(result.EpisodeNumber);
    }

    [Theory]
    [InlineData("SPY x FAMILY - 01 [1080P].mkv")]
    [InlineData("Special Ops S01E01.mkv")]
    [InlineData("Movie Night S01E01.mkv")]
    [InlineData("OVA Project S01E01.mkv")]
    public void Recognize_DoesNotPromoteTitleWordsToDomainMarkers(string path)
    {
        var result = _engine.Recognize(new RecognitionRequest(path));

        Assert.Equal(MediaKind.SeriesEpisode, result.MediaKind);
        Assert.Equal(SpecialKind.None, result.SpecialKind);
    }
}
