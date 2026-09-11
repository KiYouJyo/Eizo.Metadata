namespace Eizo.Metadata.Recognition.Tests;

public sealed class RealWorld011ReportRegressionTests
{
    private readonly RecognitionEngine _engine = new();

    [Fact]
    public void Recognize_MovieLibraryDirectoryDoesNotOverrideBracketAnimeEpisode()
    {
        var result = _engine.Recognize(
            new RecognitionRequest(
                "movie/D-Darling in the FRANXX（2018）/[Ygm] Darling In The Franxx [01][Ma10p_2160P][x265_flac_aac].mkv"));

        Assert.Equal(MediaKind.SeriesEpisode, result.MediaKind);
        Assert.Equal(1m, result.EpisodeNumber);
        Assert.Equal("Darling In The Franxx", result.Title);
        Assert.False(result.IsAmbiguous);
        Assert.Contains(result.Evidence, static item =>
            item.Code == "media-kind.episode-overrides-movie-directory");
    }

    [Fact]
    public void Recognize_NumberOnlyEpisodeUsesSeriesPartDirectoryInsteadOfGenericMovieRoot()
    {
        var result = _engine.Recognize(
            new RecognitionRequest(
                "movie/1971.鲁邦三世.1-6季+OVA+剧场版+特别篇/01.[1971-1972]鲁邦三世part1/公众号：SS的笔记/04.MP4"));

        Assert.Equal(MediaKind.SeriesEpisode, result.MediaKind);
        Assert.Equal(4m, result.EpisodeNumber);
        Assert.NotNull(result.Title);
        Assert.DoesNotContain("公众号", result.Title!, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.Confidence >= 0.85);
        Assert.Contains(result.Evidence, static item =>
            item.Code == "episode.bare-series-context");
    }

    [Fact]
    public void Recognize_LeadingNumberedAriseEpisodeUsesParentSeriesAndEpisodeTitle()
    {
        var result = _engine.Recognize(
            new RecognitionRequest(
                "番剧/攻壳机动队/重启设定世界观（ARISE系列宇宙）/TV版-攻壳机动队AAA.2015/02.Ghost Stands Alone 后篇.mp4"));

        Assert.Equal(MediaKind.SeriesEpisode, result.MediaKind);
        Assert.Equal(2m, result.EpisodeNumber);
        Assert.Equal(EpisodePart.Second, result.EpisodePart);
        Assert.NotNull(result.Title);
        Assert.Contains("攻壳机动队AAA", result.Title!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Ghost Stands Alone", result.EpisodeTitle);
        Assert.False(result.IsAmbiguous);
        Assert.Contains(result.Evidence, static item =>
            item.Code == "episode.leading-numbered");
    }

    [Fact]
    public void Recognize_MovieUniverseDirectoryHintClassifiesFeatureFilm()
    {
        var result = _engine.Recognize(
            new RecognitionRequest(
                "番剧/攻壳机动队/剧场版世界观（押井守宇宙）/攻壳机动队1 4K REMUX（1995）/Ghost.in.the.Shell.1995.JAPANESE.2160p.USA.BluRay.REMUX.AVC.DTS-HD.MA.TrueHD.7.1.Atmos-FGT.mkv"));

        Assert.Equal(MediaKind.Movie, result.MediaKind);
        Assert.Equal("Ghost.in.the.Shell", result.Title);
        Assert.Equal(1995, result.Year);
        Assert.False(result.IsAmbiguous);
        Assert.Contains(result.Evidence, static item =>
            item.Code == "movie.directory-hint");
    }

    [Fact]
    public void Recognize_FilenameAndTranslatedParentRemainCandidatesWithoutFalseAmbiguity()
    {
        var result = _engine.Recognize(
            new RecognitionRequest(
                "movie/千与千寻 (2001)/Spirited.Away.2001.1080p.BluRay.x265.10bit-GRP.mkv"));

        Assert.Equal(MediaKind.Movie, result.MediaKind);
        Assert.Equal("Spirited.Away", result.Title);
        Assert.False(result.IsAmbiguous);
        Assert.True(result.TitleCandidates.Count >= 2);
        Assert.Contains(result.Evidence, static item =>
            item.Code == "confidence.title-cross-source-alias");
    }

    [Fact]
    public void Recognize_Sac2045IsPreservedInParentDirectorySeriesTitle()
    {
        var result = _engine.Recognize(
            new RecognitionRequest(
                "番剧/攻壳机动队/SAC_2045世界观（Netflix）/攻壳机动队：SAC_2045 1-2季/S01/S01E01.mp4"));

        Assert.Equal(MediaKind.SeriesEpisode, result.MediaKind);
        Assert.Equal(1, result.SeasonNumber);
        Assert.Equal(1m, result.EpisodeNumber);
        Assert.NotNull(result.Title);
        Assert.Contains("SAC_2045", result.Title!, StringComparison.Ordinal);
    }
}
