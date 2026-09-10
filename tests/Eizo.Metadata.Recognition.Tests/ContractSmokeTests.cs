namespace Eizo.Metadata.Recognition.Tests;

public sealed class ContractSmokeTests
{
    [Fact]
    public void RecognitionRequest_PreservesUnicodePath()
    {
        const string path = "Anime/葬送のフリーレン/[ANi] 葬送のフリーレン - 14 [1080P].mkv";

        var request = new RecognitionRequest(path);

        Assert.Equal(path, request.Path);
    }

    [Fact]
    public void RecognitionResult_CanRepresentProviderNeutralEpisodeCandidate()
    {
        var titleCandidates = new[]
        {
            new RecognitionTitleCandidate(
                "葬送のフリーレン",
                0.91,
                "filename",
                IsPrimary: true),
        };

        var evidence = new[]
        {
            new RecognitionEvidence("episode.pattern", "14", 0.8),
        };

        var result = new RecognitionResult(
            MediaKind.SeriesEpisode,
            SpecialKind.None,
            EpisodePart.None,
            IsFinalEpisode: false,
            "葬送のフリーレン",
            titleCandidates,
            1,
            CourNumber: null,
            14m,
            EpisodeEndNumber: null,
            SpecialNumber: null,
            2023,
            0.92,
            RecognitionConfidenceLevel.High,
            IsAmbiguous: false,
            evidence)
        {
            EpisodeTitle = "旅立ち",
        };

        Assert.Equal(MediaKind.SeriesEpisode, result.MediaKind);
        Assert.Equal(SpecialKind.None, result.SpecialKind);
        Assert.Equal("葬送のフリーレン", result.Title);
        Assert.Equal("旅立ち", result.EpisodeTitle);
        Assert.Equal(14m, result.EpisodeNumber);
        Assert.Equal(RecognitionConfidenceLevel.High, result.ConfidenceLevel);
        Assert.Single(result.TitleCandidates);
        Assert.Single(result.Evidence);
    }
}
