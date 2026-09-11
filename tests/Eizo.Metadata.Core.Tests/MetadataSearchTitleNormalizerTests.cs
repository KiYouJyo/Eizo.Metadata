using Eizo.Metadata.Core;
using Eizo.Metadata.Recognition;

namespace Eizo.Metadata.Core.Tests;

public sealed class MetadataSearchTitleNormalizerTests
{
    [Theory]
    [InlineData("02.[1977-1980]鲁邦三世part2", "鲁邦三世part2")]
    [InlineData("S01 攻壳机动队 STAND ALONE COMPLEX", "攻壳机动队 STAND ALONE COMPLEX")]
    [InlineData("纸牌屋.2013", "纸牌屋")]
    [InlineData("Fate.Grand Order Absolute Demonic Front. Babylonia", "Fate Grand Order Absolute Demonic Front Babylonia")]
    [InlineData("Fullmetal Alchemist꞉ Brotherhood", "Fullmetal Alchemist: Brotherhood")]
    public void FromRecognition_AddsSafeProviderSearchVariant(
        string recognizedTitle,
        string expectedVariant)
    {
        var request = MetadataSearchRequest.FromRecognition(
            CreateRecognition(recognizedTitle),
            "zh-CN");

        Assert.Equal(recognizedTitle, request.Titles[0]);
        Assert.Contains(expectedVariant, request.Titles, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void FromRecognition_PreservesSeasonNameWhenItIsPartOfTitle()
    {
        var request = MetadataSearchRequest.FromRecognition(
            CreateRecognition("Date A Live II"),
            "zh-CN");

        Assert.Contains("Date A Live II", request.Titles, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void FromRecognition_DoesNotPromoteParentDirectoryYearIntoCertainYear()
    {
        var recognition = CreateRecognition("Date A Live II");

        var request = MetadataSearchRequest.FromRecognition(recognition, "zh-CN");

        Assert.Null(request.Year);
    }

    private static RecognitionResult CreateRecognition(string title) =>
        new(
            MediaKind.SeriesEpisode,
            SpecialKind.None,
            EpisodePart.None,
            IsFinalEpisode: false,
            Title: title,
            TitleCandidates:
            [
                new RecognitionTitleCandidate(title, 0.94, "field-report", true),
            ],
            SeasonNumber: null,
            CourNumber: null,
            EpisodeNumber: 1m,
            EpisodeEndNumber: null,
            SpecialNumber: null,
            Year: null,
            Confidence: 0.94,
            RecognitionConfidenceLevel.High,
            IsAmbiguous: false,
            Evidence: Array.Empty<RecognitionEvidence>());
}
