namespace Eizo.Metadata.Recognition.Tests;

public sealed class RealWorld014ReportRegressionTests
{
    private readonly RecognitionEngine _engine = new();

    [Fact]
    public void Recognize_ReleaseShapedMovieFilenameOverridesLocalizedParentAlias()
    {
        var result = _engine.Recognize(
            new RecognitionRequest(
                "番剧/攻壳机动队/S.A.C.系列世界观（神山健治宇宙）/攻壳机动队SAC：笑面男 蓝光原盘REMUX (2005)/攻殻機動隊 Stand Alone Complex The Laughing Man.2005.1080p.BluRay.Remux.AVC.DTS-HD.MA.5.1 -WuKe.mkv"));

        Assert.Equal("攻殻機動隊 Stand Alone Complex The Laughing Man", result.Title);
        Assert.False(result.IsAmbiguous);
        Assert.Contains(result.Evidence, static item =>
            item.Code == "confidence.title-cross-source-alias");
    }

    [Fact]
    public void Recognize_Sac2045ReleaseFilenameOverridesLocalizedParentAlias()
    {
        var result = _engine.Recognize(
            new RecognitionRequest(
                "番剧/攻壳机动队/SAC_2045世界观（Netflix）/攻壳机动队SAC_2045 持续可能战争/Ghost.in.the.Shell.SAC_2045.Sustainable.War.2021.1080p.BluRay.x265.10bit-CTRLHD.mkv"));

        Assert.Equal("Ghost.in.the.Shell.SAC_2045.Sustainable.War", result.Title);
        Assert.False(result.IsAmbiguous);
    }

    [Fact]
    public void Recognize_NonReleaseTutorialStillKeepsTitleAmbiguity()
    {
        var result = _engine.Recognize(
            new RecognitionRequest(
                "番剧/序号005.【AYWDXNH】[4K_NW] 黑礁 [外挂字幕]/【如何切换音频和导入字幕】/将字幕文件拖入视频下方位置.mkv"));

        Assert.Equal("将字幕文件拖入视频下方位置", result.Title);
        Assert.True(result.IsAmbiguous);
        Assert.Contains(result.Evidence, static item =>
            item.Code == "confidence.title-ambiguity");
    }

    [Fact]
    public void Recognize_StripsLastObservedMovieTechnicalTail()
    {
        var result = _engine.Recognize(
            new RecognitionRequest(
                "movie/飞驰人生2 {tmdb-1228891}/飞驰人生2 (2024) - 2160p.H.265.24fps.DDP.5.1.mkv"));

        Assert.Equal("飞驰人生2", result.Title);
        Assert.DoesNotContain("H.265", result.Title!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("24fps", result.Title!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DDP", result.Title!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("24fps")]
    [InlineData("60FPS")]
    [InlineData("DDP")]
    public void TokenClassifier_Recognizes014FieldReportTechnicalSignals(string value)
    {
        var expected = value.Contains("DDP", StringComparison.OrdinalIgnoreCase)
            ? TokenKind.AudioCodec
            : TokenKind.TechnicalGroup;

        Assert.Equal(expected, TokenClassifier.Classify(value, bracketed: true));
    }

    [Fact]
    public void Recognize_RealSeasonConflictStillRequiresReview()
    {
        var result = _engine.Recognize(
            new RecognitionRequest("Show/Season 2/Show S01E13.mkv"));

        Assert.True(result.IsAmbiguous);
        Assert.Contains(result.Evidence, static item =>
            item.Code == "confidence.season-conflict");
    }
}
