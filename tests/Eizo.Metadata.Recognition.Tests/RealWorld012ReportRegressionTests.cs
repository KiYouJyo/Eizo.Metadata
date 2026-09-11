using Eizo.Metadata.Recognition.Internal;

namespace Eizo.Metadata.Recognition.Tests;

public sealed class RealWorld012ReportRegressionTests
{
    private readonly RecognitionEngine _engine = new();

    [Fact]
    public void Recognize_DuplicateMediaExtensionDoesNotLeakIntoTitle()
    {
        var result = _engine.Recognize(
            new RecognitionRequest(
                "番剧/W.无职转生 第三季.Mushoku Tensei Jobless Reincarnation S03/Mushoku Tensei Jobless Reincarnation S03E02 [IQIYI WebRip 2160p NVENC AAC Multi-Subs].mkv.mkv"));

        Assert.Equal(MediaKind.SeriesEpisode, result.MediaKind);
        Assert.Equal("Mushoku Tensei Jobless Reincarnation", result.Title);
        Assert.Equal(3, result.SeasonNumber);
        Assert.Equal(2m, result.EpisodeNumber);
        Assert.DoesNotContain(".mkv", result.Title!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Recognize_DotLanguageSuffixDoesNotLeakIntoTitle()
    {
        var result = _engine.Recognize(
            new RecognitionRequest(
                "番剧/黑礁/资源/[4K_NW] 黑礁 OVA 01【Bilibili_AYWDXNH】.zh.mkv"));

        Assert.Equal(MediaKind.Special, result.MediaKind);
        Assert.Equal(SpecialKind.Ova, result.SpecialKind);
        Assert.Equal(1m, result.SpecialNumber);
        Assert.Equal("黑礁", result.Title);
        Assert.DoesNotContain(".zh", result.Title!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Recognize_RemovedDomainMarkerDoesNotLeaveEmptyBrackets()
    {
        var result = _engine.Recognize(
            new RecognitionRequest(
                "movie/D-电锯人（2022）/EDOP/[MAI] Chainsawman [NCED01][Ma10p_2160p][x265_flac].mkv"));

        Assert.Equal("Chainsawman", result.Title);
        Assert.DoesNotContain("[]", result.Title!, StringComparison.Ordinal);
    }

    [Fact]
    public void Recognize_ParenthesizedBdTechnicalTailDoesNotPolluteTitle()
    {
        var result = _engine.Recognize(
            new RecognitionRequest(
                "movie/W 我们仍未知道那天所看见的花的名字/[Kamigami] Ano Hi Mita Hana no Namae o Bokutachi wa Mada Shiranai - 01 (BD 1920x1080 x264 FLAC).mkv"));

        Assert.Equal(MediaKind.SeriesEpisode, result.MediaKind);
        Assert.Equal(1m, result.EpisodeNumber);
        Assert.Equal("Ano Hi Mita Hana no Namae o Bokutachi wa Mada Shiranai", result.Title);
        Assert.DoesNotContain("1920x1080", result.Title!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FLAC", result.Title!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Recognize_AriseBorderEpisodeOverridesMovieDirectoryHint()
    {
        var result = _engine.Recognize(
            new RecognitionRequest(
                "番剧/攻壳机动队/重启设定世界观（ARISE系列宇宙）/OVA剧场版-攻壳机动队崛起 1-4合集 蓝光原盘REMUX (2014-2015)/攻殻機動隊ARISE border-4 Ghost Stands Alone.2014.1080p.BluRay.Remux.AVC.Dolby TrueHD.7.1 -WuKe.mkv"));

        Assert.Equal(MediaKind.SeriesEpisode, result.MediaKind);
        Assert.Equal("攻殻機動隊ARISE", result.Title);
        Assert.Equal("Ghost Stands Alone", result.EpisodeTitle);
        Assert.Equal(4m, result.EpisodeNumber);
        Assert.False(result.IsAmbiguous);
        Assert.Contains(result.Evidence, static item =>
            item.Code == "media-kind.episode-overrides-movie-directory");
    }

    [Fact]
    public void Recognize_ExplicitOvaNumberIsNotAConflictingEpisode()
    {
        var result = _engine.Recognize(
            new RecognitionRequest(
                "番剧/黑礁/资源/[4K_NW] 黑礁 OVA 02【Bilibili_AYWDXNH】.mkv"));

        Assert.Equal(MediaKind.Special, result.MediaKind);
        Assert.Equal(SpecialKind.Ova, result.SpecialKind);
        Assert.Equal(2m, result.SpecialNumber);
        Assert.Equal("黑礁", result.Title);
        Assert.False(result.IsAmbiguous);
    }

    [Fact]
    public void Recognize_S00EpisodeInsideSpecialsIsNotAConflict()
    {
        var result = _engine.Recognize(
            new RecognitionRequest(
                "movie/CLANNAD (2007)/Specials/CLANNAD - S00E01 - 番外篇 暑假发生的事.mkv"));

        Assert.Equal(MediaKind.Special, result.MediaKind);
        Assert.Equal("CLANNAD", result.Title);
        Assert.False(result.IsAmbiguous);
    }

    [Fact]
    public void Recognize_OvaSuffixAttachedToCjkSeriesNameRemainsSeriesTitle()
    {
        var result = _engine.Recognize(
            new RecognitionRequest(
                "movie/JOJO的奇妙冒险/JOJO的奇妙冒险OVA (1993)/JOJO的奇妙冒险OVA (1993) S01E01.愚者的伊奇与盖布神的恩多尔·前篇.mkv"));

        Assert.Equal(MediaKind.SeriesEpisode, result.MediaKind);
        Assert.Equal(1, result.SeasonNumber);
        Assert.Equal(1m, result.EpisodeNumber);
        Assert.NotNull(result.Title);
        Assert.Contains("OVA", result.Title!, StringComparison.Ordinal);
        Assert.False(result.IsAmbiguous);
    }

    [Theory]
    [InlineData("BD 1920x1080 x.264 FLAC")]
    [InlineData("DVD 1024x576 x.264 AAC")]
    [InlineData("X265_fla_aac")]
    public void TokenClassifier_RecognizesFieldReportTechnicalGroups(string value)
    {
        Assert.Equal(
            TokenKind.TechnicalGroup,
            TokenClassifier.Classify(value, bracketed: true));
    }
}
