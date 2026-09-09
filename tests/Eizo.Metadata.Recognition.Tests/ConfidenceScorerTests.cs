using Eizo.Metadata.Recognition.Internal;

namespace Eizo.Metadata.Recognition.Tests;

public sealed class ConfidenceScorerTests
{
    private readonly RecognitionEngine _engine = new();

    [Fact]
    public void StrongFilenameEpisode_IsHighConfidence()
    {
        var result = _engine.Recognize(
            new RecognitionRequest("Anime/Frieren/Frieren.S01E03.1080p.WEB-DL.mkv"));

        Assert.Equal(RecognitionConfidenceLevel.High, result.ConfidenceLevel);
        Assert.False(result.IsAmbiguous);
        Assert.InRange(result.Confidence, 0.85, 0.99);
    }

    [Fact]
    public void NumericEpisodeWithParent_IsMediumConfidence()
    {
        var result = _engine.Recognize(
            new RecognitionRequest("Anime/Frieren/Season 01/03.mkv"));

        Assert.Equal(RecognitionConfidenceLevel.Medium, result.ConfidenceLevel);
        Assert.False(result.IsAmbiguous);
        Assert.InRange(result.Confidence, 0.65, 0.849);
    }

    [Fact]
    public void StandaloneTitleWithoutMediaShape_RemainsLowConfidence()
    {
        var result = _engine.Recognize(
            new RecognitionRequest("Unstructured Title.mkv"));

        Assert.Equal(MediaKind.Unknown, result.MediaKind);
        Assert.Equal(RecognitionConfidenceLevel.Low, result.ConfidenceLevel);
        Assert.False(result.IsAmbiguous);
        Assert.InRange(result.Confidence, 0.01, 0.649);
    }

    [Fact]
    public void SeasonConflict_LowersScoreAndMarksAmbiguous()
    {
        var conflict = _engine.Recognize(
            new RecognitionRequest("Show/Season 01/Show.S02E03.mkv"));

        var clean = _engine.Recognize(
            new RecognitionRequest("Show/Season 02/Show.S02E03.mkv"));

        Assert.True(conflict.IsAmbiguous);
        Assert.True(conflict.Confidence < clean.Confidence);
        Assert.Contains(conflict.Evidence, static item =>
            item.Code == "confidence.season-conflict");
    }

    [Fact]
    public void ExplicitSpecialVsEpisodeConflict_IsAmbiguous()
    {
        var result = _engine.Recognize(
            new RecognitionRequest("Example - OVA S01E03.mkv"));

        Assert.Equal(MediaKind.Special, result.MediaKind);
        Assert.True(result.IsAmbiguous);
        Assert.Contains(result.Evidence, static item =>
            item.Code == "confidence.domain-episode-conflict");
    }

    [Fact]
    public void RepeatedSameTitleAcrossFilenameAndParentAddsConsensus()
    {
        var result = _engine.Recognize(
            new RecognitionRequest("Example/Example - 03.mkv"));

        Assert.False(result.IsAmbiguous);
        Assert.Contains(result.Evidence, static item =>
            item.Code == "confidence.title-consensus");
    }

    [Fact]
    public void CloseDifferentTitleCandidatesAreMarkedAmbiguous()
    {
        var preprocessed = PathPreprocessor.Preprocess(
            "Parent Candidate/Standalone Candidate.mkv");
        var episode = EpisodeExtractor.Extract(preprocessed);
        var domain = DomainClassifier.Classify(preprocessed);
        var title = TitleExtractor.Extract(preprocessed, episode, domain);

        var assessment = ConfidenceScorer.Assess(
            MediaKind.Unknown,
            episode,
            title,
            domain);

        Assert.True(assessment.IsAmbiguous);
        Assert.Contains(assessment.Evidence, static item =>
            item.Code == "confidence.title-ambiguity");
    }

    [Fact]
    public void ConfidenceEvidenceEndsWithFinalScore()
    {
        var result = _engine.Recognize(
            new RecognitionRequest("Example.S01E03.mkv"));

        Assert.Equal("confidence.final", result.Evidence[^1].Code);
        Assert.Equal(
            result.Confidence.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture),
            result.Evidence[^1].Value);
    }
}
