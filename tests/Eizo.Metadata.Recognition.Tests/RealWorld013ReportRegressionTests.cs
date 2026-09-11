using Eizo.Metadata.Recognition.Internal;

namespace Eizo.Metadata.Recognition.Tests;

public sealed class RealWorld013ReportRegressionTests
{
    private readonly RecognitionEngine _engine = new();

    [Theory]
    [InlineData("WebRip 1080p HEVC-10bit AAC SRTx2")]
    [InlineData("BD 1920x1080 HEVC-YUV420P10 FLAC")]
    [InlineData("BD 1920x1080 x.264 FLACx2")]
    [InlineData("x265_DTS-HDMA_ass")]
    [InlineData("JP.BD.Remux")]
    [InlineData("788-mix")]
    [InlineData("Hi10p_1080p")]
    [InlineData("x265_2flac")]
    public void TokenClassifier_Recognizes013FieldReportTechnicalGroups(string value)
    {
        Assert.Equal(
            TokenKind.TechnicalGroup,
            TokenClassifier.Classify(value, bracketed: true));
    }

    [Theory]
    [InlineData(
        "[LoliHouse] Bungou Stray Dogs - 38 [WebRip 1080p HEVC-10bit AAC SRTx2].mkv",
        "Bungou Stray Dogs")]
    [InlineData(
        "文豪ストレイドッグス - 38 (BD 1920x1080 HEVC-YUV420P10 FLAC).mkv",
        "文豪ストレイドッグス")]
    [InlineData(
        "Sword Art Online Alternative Gun Gale Online - 03 (BD 1920x1080 x.264 FLACx2).mkv",
        "Sword Art Online Alternative Gun Gale Online")]
    [InlineData(
        "Steins;Gate 0 S01E01-[1080p][JP.BD.Remux][788-mix].mkv",
        "Steins;Gate 0")]
    public void Recognize_Strips013FieldReportTechnicalTails(
        string path,
        string expectedTitle)
    {
        var result = _engine.Recognize(new RecognitionRequest(path));

        Assert.Equal(expectedTitle, result.Title);
        Assert.DoesNotContain("1080", result.Title!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FLAC", result.Title!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Remux", result.Title!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SRTx", result.Title!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Recognize_EarlySemanticDvdDoesNotStartTechnicalSuffix()
    {
        var result = _engine.Recognize(
            new RecognitionRequest(
                "movie/EXTRA/Tokuten DVD [SP02] Theatrical Trailer - 03 (DVD 1024x576 x.264 AAC).mkv"));

        Assert.Equal(MediaKind.Special, result.MediaKind);
        Assert.NotNull(result.Title);
        Assert.Contains("Tokuten", result.Title!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Theatrical Trailer", result.Title!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("1024x576", result.Title!, StringComparison.OrdinalIgnoreCase);
        Assert.False(result.IsAmbiguous);
    }

    [Fact]
    public void Recognize_LeadingCollectionIndexBeforeOvaIsCompatible()
    {
        var result = _engine.Recognize(
            new RecognitionRequest(
                "movie/鲁邦三世/04. 鲁邦三世part4/25_OVA1.mp4"));

        Assert.Equal(MediaKind.Special, result.MediaKind);
        Assert.Equal(SpecialKind.Ova, result.SpecialKind);
        Assert.Equal(1m, result.SpecialNumber);
        Assert.False(result.IsAmbiguous);
    }

    [Fact]
    public void Recognize_BroadcastOrdinalWithExplicitOvaIsCompatible()
    {
        var result = _engine.Recognize(
            new RecognitionRequest(
                "movie/文豪野犬/第二季/[Kamigami] Bungo Stray Dogs - 25 [OVA][BD 1080p x265 Ma10p AAC].mkv"));

        Assert.Equal(MediaKind.Special, result.MediaKind);
        Assert.Equal(SpecialKind.Ova, result.SpecialKind);
        Assert.False(result.IsAmbiguous);
    }

    [Fact]
    public void Recognize_SpMenuChildIndexIsCompatible()
    {
        var result = _engine.Recognize(
            new RecognitionRequest(
                "movie/EXTRA/[Moozzi2] Gun Gale [SP00] Menu - 04 (BD 1920x1080 x.264 Flac).mkv"));

        Assert.Equal(MediaKind.Special, result.MediaKind);
        Assert.Equal(0m, result.SpecialNumber);
        Assert.False(result.IsAmbiguous);
    }

    [Fact]
    public void Recognize_ExplicitSeasonEpisodeAndSpecialMarkerRemainAmbiguous()
    {
        var result = _engine.Recognize(
            new RecognitionRequest(
                "Show S01E05 [SP02] 1080p WEB-DL.mkv"));

        Assert.Equal(MediaKind.Special, result.MediaKind);
        Assert.True(result.IsAmbiguous);
        Assert.Contains(result.Evidence, static item =>
            item.Code == "confidence.domain-episode-conflict");
    }

    [Fact]
    public void Recognize_RealSeasonDirectoryConflictRemainsAmbiguous()
    {
        var result = _engine.Recognize(
            new RecognitionRequest(
                "Show/Season 2/Show S01E13.mkv"));

        Assert.Equal(1, result.SeasonNumber);
        Assert.Equal(13m, result.EpisodeNumber);
        Assert.True(result.IsAmbiguous);
        Assert.Contains(result.Evidence, static item =>
            item.Code == "confidence.season-conflict");
    }
}
