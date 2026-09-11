using Eizo.Metadata.Recognition.Internal;

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
    public void Recognize_ViuTvProviderTagDoesNotLeakIntoTitle()
    {
        var result = _engine.Recognize(
            new RecognitionRequest(
                "movie/钢之炼金术师系列 (2003)/钢之炼金术师FA (2009)/[Skymoon-Raws]/[Skymoon-Raws] Fullmetal Alchemist - 01 [ViuTV][WEB-DL][1080p][AVC AAC].mp4"));

        Assert.Equal(MediaKind.SeriesEpisode, result.MediaKind);
        Assert.Equal(1m, result.EpisodeNumber);
        Assert.Equal("Fullmetal Alchemist", result.Title);
    }

    [Fact]
    public void Recognize_NumericOnlyFileUnderTitledParentIsSeriesEpisode()
    {
        var result = _engine.Recognize(
            new RecognitionRequest("movie/夏洛特/01.mkv"));

        Assert.Equal(MediaKind.SeriesEpisode, result.MediaKind);
        Assert.Equal(1m, result.EpisodeNumber);
        Assert.Equal("夏洛特", result.Title);
        Assert.Contains(result.Evidence, static item =>
            item.Code == "media-kind.titled-parent-overrides-movie-directory");
    }

    [Fact]
    public void Recognize_MenuDirectoryIsSpecialAndChildIndexIsCompatible()
    {
        var result = _engine.Recognize(
            new RecognitionRequest(
                "movie/S-死亡笔记（2006）/[DBD-Raws]/Menu/[DBD-Raws][SW笔记][D1][menu][01][1080P][BDRip][HEVC-10bit][FLAC].mkv"));

        Assert.Equal(MediaKind.Special, result.MediaKind);
        Assert.Equal(SpecialKind.Special, result.SpecialKind);
        Assert.False(result.IsAmbiguous);
    }

    [Fact]
    public void Recognize_TrailingEndAfterEpisodeIsFinalMarkerNotTitle()
    {
        var result = _engine.Recognize(
            new RecognitionRequest(
                "movie/D-刀剑神域（2012）/2018.04 刀剑神域外传 暴风之铳 Gun Gale Online/[Moozzi2]/[Moozzi2] Sword Art Online Alternative Gun Gale Online - 12 END (BD 1920x1080 x.264 FLACx3).mkv"));

        Assert.Equal(MediaKind.SeriesEpisode, result.MediaKind);
        Assert.Equal(12m, result.EpisodeNumber);
        Assert.Equal("Sword Art Online Alternative Gun Gale Online", result.Title);
        Assert.True(result.IsFinalEpisode);
    }

    [Fact]
    public void Recognize_DuplicateCopySuffixAfterTechnicalTagDoesNotLeakIntoTitle()
    {
        var result = _engine.Recognize(
            new RecognitionRequest(
                "movie/文豪野犬 (2016)/第三季/[Kamigami] Bungou Stray Dogs - 32 [1080p x265 Ma10p AAC](1).mkv"));

        Assert.Equal(MediaKind.SeriesEpisode, result.MediaKind);
        Assert.Equal(32m, result.EpisodeNumber);
        Assert.Equal("Bungou Stray Dogs", result.Title);
    }

    [Fact]
    public void Recognize_OrdinaryParenthesizedNumberInTitleIsPreserved()
    {
        var result = _engine.Recognize(
            new RecognitionRequest("movie/Room (1)/Room (1).2015.1080p.BluRay.x265.mkv"));

        Assert.Equal("Room (1)", result.Title);
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
