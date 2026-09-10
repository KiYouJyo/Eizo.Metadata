using Eizo.Metadata.Recognition.Internal;

namespace Eizo.Metadata.Recognition.Tests;

public sealed class AdvancedIrregularFilenameTests
{
    private readonly RecognitionEngine _engine = new();

    [Fact]
    public void Recognize_SeparatesSeriesAndEpisodeTitle()
    {
        var result = _engine.Recognize(
            new RecognitionRequest(
                "Re：从零开始的异世界生活 - S01E24 - 自称骑士与最优的骑士.mkv"));

        Assert.Equal(MediaKind.SeriesEpisode, result.MediaKind);
        Assert.Equal("Re：从零开始的异世界生活", result.Title);
        Assert.Equal("自称骑士与最优的骑士", result.EpisodeTitle);
        Assert.Equal(1, result.SeasonNumber);
        Assert.Equal(24m, result.EpisodeNumber);
        Assert.Contains(result.Evidence, static item =>
            item.Code == "episode-title.delimited-sxxexx");
    }

    [Theory]
    [InlineData("Show - S02E03 - The Long Night [1080P].mkv", "Show", "The Long Night", 2, 3)]
    [InlineData("Show - 2x03 - Another Day.mkv", "Show", "Another Day", 2, 3)]
    [InlineData("Show - EP03 - Beginning.mkv", "Show", "Beginning", null, 3)]
    public void Recognize_ExtractsDelimitedEpisodeTitles(
        string path,
        string expectedSeries,
        string expectedEpisodeTitle,
        int? expectedSeason,
        int expectedEpisode)
    {
        var result = _engine.Recognize(new RecognitionRequest(path));

        Assert.Equal(expectedSeries, result.Title);
        Assert.Equal(expectedEpisodeTitle, result.EpisodeTitle);
        Assert.Equal(expectedSeason, result.SeasonNumber);
        Assert.Equal((decimal)expectedEpisode, result.EpisodeNumber);
    }

    [Fact]
    public void Recognize_ExtractsJapaneseQuotedEpisodeTitle()
    {
        var result = _engine.Recognize(
            new RecognitionRequest("ドラマ 第12回 「決着」.mkv"));

        Assert.Equal("ドラマ", result.Title);
        Assert.Equal("決着", result.EpisodeTitle);
        Assert.Equal(12m, result.EpisodeNumber);
    }

    [Fact]
    public void Recognize_DoesNotTreatTechnicalSuffixAsEpisodeTitle()
    {
        var result = _engine.Recognize(
            new RecognitionRequest("Show.S01E03.1080p.WEB-DL.mkv"));

        Assert.Equal("Show", result.Title);
        Assert.Null(result.EpisodeTitle);
        Assert.Equal(3m, result.EpisodeNumber);
    }

    [Fact]
    public void Recognize_AllBracketDeathNoteExample()
    {
        var result = _engine.Recognize(
            new RecognitionRequest(
                "[DBD-Raws][死亡笔记][34][1080P][BDRip][HEVC-10bit][FLAC].mkv"));

        Assert.Equal(MediaKind.SeriesEpisode, result.MediaKind);
        Assert.Equal("死亡笔记", result.Title);
        Assert.Equal(34m, result.EpisodeNumber);
        Assert.Null(result.EpisodeTitle);
        Assert.Contains(result.Evidence, static item =>
            item.Code == "episode.bracket-sequence");
        Assert.Contains(result.Evidence, static item =>
            item.Code == "title.bracket-sequence");
    }

    [Theory]
    [InlineData("[LoliHouse][葬送のフリーレン][01][1080P][WEBRip][x265-10bit][AAC].mkv", "葬送のフリーレン", 1)]
    [InlineData("[DBD-Raws][86 - Eighty Six][03][1080P][BDRip][AVC-8bit][FLAC].mkv", "86 - Eighty Six", 3)]
    public void Recognize_AllBracketReleasePattern(
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
    [InlineData("[Project][Chapter][34][Notes][Draft].mkv")]
    [InlineData("[DBD-Raws][死亡笔记][2026][1080P][BDRip].mkv")]
    [InlineData("[DBD-Raws][死亡笔记][1080][BDRip][HEVC].mkv")]
    public void Recognize_DoesNotOverApplyAllBracketPattern(string path)
    {
        var result = _engine.Recognize(new RecognitionRequest(path));

        Assert.Null(result.EpisodeNumber);
        Assert.NotEqual(MediaKind.SeriesEpisode, result.MediaKind);
    }

    [Fact]
    public void Recognize_GhostInTheShellAriseRemuxExample()
    {
        var result = _engine.Recognize(
            new RecognitionRequest(
                "攻殻機動隊ARISE border-4 Ghost Stands Alone.2014.1080p.BluRay.Remux.AVC.Dolby TrueHD.7.1 -WuKe.mkv"));

        Assert.Equal(MediaKind.SeriesEpisode, result.MediaKind);
        Assert.Equal("攻殻機動隊ARISE", result.Title);
        Assert.Equal("Ghost Stands Alone", result.EpisodeTitle);
        Assert.Equal(4m, result.EpisodeNumber);
        Assert.Equal(2014, result.Year);
        Assert.Contains(result.Evidence, static item =>
            item.Code == "episode.named-ordinal.border");
        Assert.Contains(result.Evidence, static item =>
            item.Code == "release-group.trailing" &&
            item.Value == "WuKe");
    }

    [Theory]
    [InlineData("Series border-1 First Ghost.2013.1080p.BluRay.Remux.AVC.TrueHD.5.1-GRP.mkv", "Series", "First Ghost", 1)]
    [InlineData("Series BORDER 12 Final Border.2024.2160p.BluRay.Remux.HEVC.DTS.5.1-Team.mkv", "Series", "Final Border", 12)]
    public void Recognize_NamedBorderInstallments(
        string path,
        string expectedTitle,
        string expectedEpisodeTitle,
        int expectedEpisode)
    {
        var result = _engine.Recognize(new RecognitionRequest(path));

        Assert.Equal(expectedTitle, result.Title);
        Assert.Equal(expectedEpisodeTitle, result.EpisodeTitle);
        Assert.Equal((decimal)expectedEpisode, result.EpisodeNumber);
    }

    [Theory]
    [InlineData("Border Security 4.2014.1080p.BluRay.Remux.AVC.mkv")]
    [InlineData("Research border notes 4.2014.1080p.BluRay.Remux.AVC.mkv")]
    [InlineData("Movie Part-2 The Return.2014.1080p.BluRay.Remux.AVC.mkv")]
    [InlineData("Archive Disc-2.2014.1080p.BluRay.Remux.AVC.mkv")]
    public void Recognize_DoesNotOverApplyNamedOrdinalRule(string path)
    {
        var result = _engine.Recognize(new RecognitionRequest(path));

        Assert.DoesNotContain(result.Evidence, static item =>
            item.Code.StartsWith("episode.named-ordinal.", StringComparison.Ordinal));
    }

    [Fact]
    public void Recognize_TechnicalSuffixDoesNotPollutePlainMovieTitle()
    {
        var result = _engine.Recognize(
            new RecognitionRequest(
                "Some Movie.2019.1080p.BluRay.Remux.AVC.Dolby TrueHD.7.1 -WuKe.mkv"));

        Assert.Equal("Some Movie", result.Title);
        Assert.Equal(2019, result.Year);
        Assert.Null(result.EpisodeNumber);
        Assert.Contains(result.Evidence, static item =>
            item.Code == "release-group.trailing" &&
            item.Value == "WuKe");
    }

    [Fact]
    public void Recognize_TitleYearCollisionUsesTechnicalSuffixYear()
    {
        var result = _engine.Recognize(
            new RecognitionRequest(
                "2001 A Space Odyssey.1968.1080p.BluRay.Remux.AVC.TrueHD.5.1-GRP.mkv"));

        Assert.Equal("2001 A Space Odyssey", result.Title);
        Assert.Equal(1968, result.Year);
    }

    [Theory]
    [InlineData("HEVC-10bit")]
    [InlineData("x265-10bit")]
    [InlineData("AVC-8bit")]
    public void TokenClassifier_RecognizesCompositeTechnicalBracket(string value)
    {
        Assert.Equal(
            TokenKind.TechnicalGroup,
            TokenClassifier.Classify(value, bracketed: true));
    }
}
