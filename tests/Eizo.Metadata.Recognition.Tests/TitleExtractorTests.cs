using Eizo.Metadata.Recognition.Internal;

namespace Eizo.Metadata.Recognition.Tests;

public sealed class TitleExtractorTests
{
    public static TheoryData<string, string> FileNameCases => new()
    {
        {
            "[ANi] 葬送のフリーレン - 14 [1080P][Baha][WEB-DL][AAC AVC][CHT].mp4",
            "葬送のフリーレン"
        },
        {
            "[SubsPlease] Frieren Beyond Journey's End - 14 [1080p].mkv",
            "Frieren Beyond Journey's End"
        },
        {
            "VIVANT.S01E03.1080p.WEB-DL.x265.AAC.mkv",
            "VIVANT"
        },
        {
            "ドラゴン桜 2021 第03話 1080p HDTV.mp4",
            "ドラゴン桜"
        },
        {
            "86 - Eighty Six - 03 [1080P].mkv",
            "86 - Eighty Six"
        },
        {
            "Dr.STONE.S03E04.1080p.WEB-DL.mkv",
            "Dr.STONE"
        },
        {
            "3年A組 第01話.mp4",
            "3年A組"
        },
        {
            "24 JAPAN S01E01.mkv",
            "24 JAPAN"
        },
        {
            "Fate stay night [Unlimited Blade Works] - 01.mkv",
            "Fate stay night [Unlimited Blade Works]"
        },
        {
            "[Oshi no Ko] - 01.mkv",
            "[Oshi no Ko]"
        },
        {
            "Episode 03 - The Long Night.mkv",
            "The Long Night"
        },
        {
            "2025 - 01.mkv",
            "2025"
        },
    };

    [Theory]
    [MemberData(nameof(FileNameCases))]
    public void Extract_ReturnsExpectedFilenameTitle(string path, string expected)
    {
        var preprocessed = PathPreprocessor.Preprocess(path);
        var episode = EpisodeExtractor.Extract(preprocessed);

        var result = TitleExtractor.Extract(preprocessed, episode);

        Assert.Equal(expected, result.Title);
        Assert.Contains(result.Evidence, static item =>
            item.Code == "title.filename");
    }

    [Theory]
    [InlineData("葬送のフリーレン/01.mkv", "葬送のフリーレン")]
    [InlineData("葬送のフリーレン/Season 01/01.mkv", "葬送のフリーレン")]
    [InlineData("ドラゴン桜 (2021)/第1シリーズ/03.mkv", "ドラゴン桜")]
    [InlineData("Library/Anime/VIVANT/Season 01/03.mkv", "VIVANT")]
    [InlineData("Fate stay night [Unlimited Blade Works]/01.mkv", "Fate stay night [Unlimited Blade Works]")]
    public void Extract_FallsBackToNearestMeaningfulParent(
        string path,
        string expected)
    {
        var preprocessed = PathPreprocessor.Preprocess(path);
        var episode = EpisodeExtractor.Extract(preprocessed);

        var result = TitleExtractor.Extract(preprocessed, episode);

        Assert.Equal(expected, result.Title);
        Assert.Contains(result.Evidence, static item =>
            item.Code == "title.parent-directory");
    }

    [Fact]
    public void Extract_PrefersFilenameOverDifferentParentCandidate()
    {
        var preprocessed = PathPreprocessor.Preprocess(
            "Library/Folder Alias/[ANi] Canonical Looking Title - 03 [1080P].mkv");
        var episode = EpisodeExtractor.Extract(preprocessed);

        var result = TitleExtractor.Extract(preprocessed, episode);

        Assert.Equal("Canonical Looking Title", result.Title);
        Assert.Equal("filename", result.Candidates[0].Source);
        Assert.True(result.Candidates.Count >= 2);
    }

    [Fact]
    public void Extract_DoesNotInventTitleFromGenericDirectories()
    {
        var preprocessed = PathPreprocessor.Preprocess(
            "Media/Anime/Season 01/03.mkv");
        var episode = EpisodeExtractor.Extract(preprocessed);

        var result = TitleExtractor.Extract(preprocessed, episode);

        Assert.Null(result.Title);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void Extract_IsDeterministic()
    {
        var preprocessed = PathPreprocessor.Preprocess(
            "Anime/Frieren/[SubsPlease] Frieren - 14 [1080p].mkv");
        var episode = EpisodeExtractor.Extract(preprocessed);

        var first = TitleExtractor.Extract(preprocessed, episode);
        var second = TitleExtractor.Extract(preprocessed, episode);

        Assert.Equal(first.Title, second.Title);
        Assert.Equal(first.Confidence, second.Confidence);
        Assert.Equal(first.Candidates, second.Candidates);
        Assert.Equal(first.Evidence, second.Evidence);
    }
}
