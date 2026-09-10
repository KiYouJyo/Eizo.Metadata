namespace Eizo.Metadata.Recognition.Tests;

public sealed class TitleCandidateContractTests
{
    private readonly RecognitionEngine _engine = new();

    [Fact]
    public void Recognition_ExposesRankedDistinctTitleCandidates()
    {
        var result = _engine.Recognize(
            new RecognitionRequest(
                "Folder Alias/Canonical Title - 03 [1080P].mkv"));

        Assert.Equal("Canonical Title", result.Title);
        Assert.True(result.TitleCandidates.Count >= 2);
        Assert.True(result.TitleCandidates[0].IsPrimary);
        Assert.Equal("filename", result.TitleCandidates[0].Source);
        Assert.Equal(
            result.TitleCandidates.Count,
            result.TitleCandidates.Select(static candidate => candidate.Title).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Recognition_DeduplicatesSameTitleFromMultipleSources()
    {
        var result = _engine.Recognize(
            new RecognitionRequest("Example/Example - 03.mkv"));

        Assert.Equal("Example", result.Title);
        Assert.Single(result.TitleCandidates);
        Assert.True(result.TitleCandidates[0].IsPrimary);
    }

    [Fact]
    public void PureEpisodeFallbackExposesParentSource()
    {
        var result = _engine.Recognize(
            new RecognitionRequest("Anime/Example/Season 01/03.mkv"));

        Assert.Single(result.TitleCandidates);
        Assert.Equal("parent-directory", result.TitleCandidates[0].Source);
        Assert.True(result.TitleCandidates[0].IsPrimary);
    }
}
