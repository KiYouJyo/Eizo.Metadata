namespace Eizo.Metadata.Recognition.Tests;

public sealed class IrregularFilenameTests
{
    private readonly RecognitionEngine _engine = new();

    [Fact]
    public void Recognize_BilibiliTaggedBareEpisodeExample()
    {
        var result = _engine.Recognize(
            new RecognitionRequest("[4K_NW] 黑礁 22【Bilibili_AYWDXNH】.mkv"));

        Assert.Equal(MediaKind.SeriesEpisode, result.MediaKind);
        Assert.Equal("黑礁", result.Title);
        Assert.Equal(22m, result.EpisodeNumber);
        Assert.Contains(result.Evidence, static item =>
            item.Code == "episode.bare-trailing-release-tag");
    }

    [Theory]
    [InlineData("[4K_NW] BLACK LAGOON 22【Bilibili_ABCDE】.mkv", "BLACK LAGOON", 22)]
    [InlineData("葬送のフリーレン 14 [1080P][Baha].mkv", "葬送のフリーレン", 14)]
    [InlineData("ドラマ 03【Bilibili_WEB】.mp4", "ドラマ", 3)]
    [InlineData("作品名 7 [ANi][1080P].mkv", "作品名", 7)]
    public void Recognize_TrailingBareEpisodeRequiresReleaseIdentity(
        string path,
        string expectedTitle,
        int expectedEpisode)
    {
        var result = _engine.Recognize(new RecognitionRequest(path));

        Assert.Equal(MediaKind.SeriesEpisode, result.MediaKind);
        Assert.Equal(expectedTitle, result.Title);
        Assert.Equal((decimal)expectedEpisode, result.EpisodeNumber);
    }

    [Theory]
    [InlineData("黑礁 22【1080P】.mkv")]
    [InlineData("Movie 22 [1080P].mkv")]
    [InlineData("Area 51【Final】.mkv")]
    [InlineData("24 JAPAN【1080P】.mkv")]
    [InlineData("Archive Notes 22【Final】.mkv")]
    [InlineData("作品名 2026【Bilibili_X】.mkv")]
    [InlineData("作品名 1080【Bilibili_X】.mkv")]
    public void Recognize_DoesNotOpenUnboundedTitlePlusNumberRule(string path)
    {
        var result = _engine.Recognize(new RecognitionRequest(path));

        Assert.Null(result.EpisodeNumber);
        Assert.NotEqual(MediaKind.SeriesEpisode, result.MediaKind);
    }

    [Fact]
    public void Recognize_StripsProviderMetadataEvenWithoutEpisode()
    {
        var result = _engine.Recognize(
            new RecognitionRequest("[4K_NW] 黑礁【Bilibili_AYWDXNH】.mkv"));

        Assert.Equal("黑礁", result.Title);
        Assert.Equal(MediaKind.Unknown, result.MediaKind);
        Assert.Null(result.EpisodeNumber);
    }

    [Fact]
    public void Recognize_DoesNotStripMeaningfulBracketedTitleSuffix()
    {
        var result = _engine.Recognize(
            new RecognitionRequest("Fate stay night [Unlimited Blade Works] - 01.mkv"));

        Assert.Equal("Fate stay night [Unlimited Blade Works]", result.Title);
        Assert.Equal(1m, result.EpisodeNumber);
    }
}
