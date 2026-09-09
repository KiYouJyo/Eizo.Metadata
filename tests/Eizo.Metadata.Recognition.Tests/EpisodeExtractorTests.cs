using System.Globalization;
using Eizo.Metadata.Recognition.Internal;

namespace Eizo.Metadata.Recognition.Tests;

public sealed class EpisodeExtractorTests
{
    public static TheoryData<string, int?, string, string?> StrongCases => new()
    {
        { "Show.S01E03.mkv", 1, "3", null },
        { "Show.S1E3.mkv", 1, "3", null },
        { "Show.S02E12.mkv", 2, "12", null },
        { "Show.S01E03-E04.mkv", 1, "3", "4" },
        { "Show.S01E03-04.mkv", 1, "3", "4" },
        { "Show.1x03.mkv", 1, "3", null },
        { "Show.2x11.mkv", 2, "11", null },
        { "Show EP03.mkv", null, "3", null },
        { "Show Episode 07.mkv", null, "7", null },
        { "Show E03.mkv", null, "3", null },
        { "Show E01-E02.mkv", null, "1", "2" },
        { "Show EP01-EP02.mkv", null, "1", "2" },
        { "ドラマ 第3話.mp4", null, "3", null },
        { "ドラマ 第03話.mp4", null, "3", null },
        { "アニメ 第12.5話.mkv", null, "12.5", null },
        { "Anime Title - 14 [1080P].mkv", null, "14", null },
        { "Anime Title – 7 [WEB-DL].mkv", null, "7", null },
        { "Anime Title — 22.5 [AAC].mkv", null, "22.5", null },
        { "01.mkv", null, "1", null },
        { "12.5.mkv", null, "12.5", null },
        { "Series/Season 02/03.mkv", 2, "3", null },
        { "Series/第2期/03.mkv", 2, "3", null },
        { "Series/2nd Season/03.mkv", 2, "3", null },
        { "Series/Season 2/Show.S02E03.mkv", 2, "3", null },
    };

    [Theory]
    [MemberData(nameof(StrongCases))]
    public void Extract_ParsesSupportedEpisodeSyntax(
        string path,
        int? expectedSeason,
        string expectedEpisode,
        string? expectedEnd)
    {
        var result = EpisodeExtractor.Extract(PathPreprocessor.Preprocess(path));

        Assert.Equal(expectedSeason, result.SeasonNumber);
        Assert.Equal(expectedEpisode, Format(result.EpisodeNumber));
        Assert.Equal(expectedEnd, Format(result.EpisodeEndNumber));
        Assert.NotEmpty(result.Evidence);
    }

    public static TheoryData<string> CollisionCases => new()
    {
        { "Movie.2026.1080p.WEB-DL.mkv" },
        { "Title - 1080 [WEB-DL].mkv" },
        { "Title - 720.mkv" },
        { "AAC 2.0.mkv" },
        { "Title.x265.10bit.AAC.mkv" },
        { "H264.mkv" },
        { "EAC3.mkv" },
        { "1080.mkv" },
        { "720.mkv" },
        { "2026.mkv" },
        { "Title.2026.mkv" },
        { "Title 01.mkv" },
        { "Season 01.mkv" },
    };

    [Theory]
    [MemberData(nameof(CollisionCases))]
    public void Extract_DoesNotTreatTechnicalOrYearNumbersAsEpisodes(string path)
    {
        var result = EpisodeExtractor.Extract(PathPreprocessor.Preprocess(path));

        Assert.Null(result.EpisodeNumber);
        Assert.Null(result.EpisodeEndNumber);
    }

    [Fact]
    public void Extract_FileNameSeasonWinsOverConflictingDirectorySeason()
    {
        var result = EpisodeExtractor.Extract(
            PathPreprocessor.Preprocess("Series/Season 01/Show.S02E03.mkv"));

        Assert.Equal(2, result.SeasonNumber);
        Assert.Equal("3", Format(result.EpisodeNumber));
        Assert.Contains(result.Evidence, static item =>
            item.Code == "season.directory.conflict");
    }

    [Theory]
    [InlineData("Series/Cour 2/03.mkv", 2)]
    [InlineData("Series/第2クール/03.mkv", 2)]
    [InlineData("Series/2nd Cour/03.mkv", 2)]
    public void Extract_RecordsCourFolderContext(string path, int expectedCour)
    {
        var result = EpisodeExtractor.Extract(PathPreprocessor.Preprocess(path));

        Assert.Equal(expectedCour, result.CourNumber);
        Assert.Equal("3", Format(result.EpisodeNumber));
        Assert.Contains(result.Evidence, static item => item.Code == "cour.directory");
    }

    [Fact]
    public void Extract_VeryLongMalformedNameDoesNotThrow()
    {
        var input = new string('[', 10_000) + "Title - 01.mkv";

        var result = EpisodeExtractor.Extract(PathPreprocessor.Preprocess(input));

        Assert.NotNull(result);
    }

    private static string? Format(decimal? value) =>
        value?.ToString(CultureInfo.InvariantCulture);
}
