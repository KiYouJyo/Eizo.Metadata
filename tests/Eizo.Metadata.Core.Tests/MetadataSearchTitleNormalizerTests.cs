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

    [Theory]
    [InlineData("鬼灭之刃 S00-S05全", "鬼灭之刃")]
    [InlineData("鬼灭之刃 Season 1-Season 6", "鬼灭之刃")]
    [InlineData("鬼灭之刃 S01～S06 COMPLETE", "鬼灭之刃")]
    public void FromRecognition_StripsCollectionSeasonCoverageFromProviderVariant(
        string recognizedTitle,
        string expectedVariant)
    {
        var request = MetadataSearchRequest.FromRecognition(
            CreateRecognition(recognizedTitle),
            "zh-CN");

        Assert.Equal(recognizedTitle, request.Titles[0]);
        Assert.Contains(expectedVariant, request.Titles, StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("第五季 黄金之风", "黄金之风")]
    [InlineData("第六季 石之海", "石之海")]
    [InlineData("Season 4 Diamond is Unbreakable", "Diamond is Unbreakable")]
    public void FromRecognition_AddsNamedSeasonSemanticVariant(
        string directoryTitle,
        string expectedSemantic)
    {
        var recognition = CreateRecognition("JOJO的奇妙冒险") with
        {
            TitleCandidates =
            [
                new RecognitionTitleCandidate(
                    "JOJO的奇妙冒险",
                    0.94,
                    "filename",
                    true),
                new RecognitionTitleCandidate(
                    directoryTitle,
                    0.78,
                    "parent-directory",
                    false),
            ],
        };

        var request = MetadataSearchRequest.FromRecognition(
            recognition,
            "zh-CN");

        Assert.Contains(
            directoryTitle,
            request.Titles,
            StringComparer.OrdinalIgnoreCase);
        Assert.Contains(
            expectedSemantic,
            request.Titles,
            StringComparer.OrdinalIgnoreCase);
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


    [Fact]
    public void FromRecognition_ExtractsTrailingYearFromFullWidthParentheses()
    {
        var request = MetadataSearchRequest.FromRecognition(
            CreateRecognition("侧耳倾听（1995）"),
            "zh-CN");

        Assert.Equal(1995, request.Year);
        Assert.Contains("侧耳倾听", request.Titles, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void FromRecognition_DropsGenericSeasonAndReleaseGroupCandidates()
    {
        var recognition = CreateRecognition("Bungo Stray Dogs") with
        {
            TitleCandidates =
            [
                new RecognitionTitleCandidate("Bungo Stray Dogs", 0.94, "filename", true),
                new RecognitionTitleCandidate("第二季", 0.80, "parent-directory", false),
                new RecognitionTitleCandidate("[VCB-Studio]", 0.75, "parent-directory", false),
            ],
        };

        var request = MetadataSearchRequest.FromRecognition(recognition, "zh-CN");

        Assert.Contains("Bungo Stray Dogs", request.Titles, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("第二季", request.Titles, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("[VCB-Studio]", request.Titles, StringComparer.OrdinalIgnoreCase);
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
