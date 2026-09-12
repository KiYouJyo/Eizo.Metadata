using Eizo.Metadata.Core;
using Eizo.Metadata.Recognition;

namespace Eizo.Metadata.Core.Tests;

public sealed class MetadataResolverTests
{
    [Fact]
    public void SearchRequest_FromRecognitionPreservesRankedTitlesAndStructure()
    {
        var recognition = new RecognitionResult(
            MediaKind.SeriesEpisode,
            SpecialKind.None,
            EpisodePart.None,
            IsFinalEpisode: false,
            Title: "攻殻機動隊",
            TitleCandidates:
            [
                new RecognitionTitleCandidate("攻殻機動隊", 0.95, "filename", true),
                new RecognitionTitleCandidate("Ghost in the Shell", 0.82, "parent-directory", false),
            ],
            SeasonNumber: 1,
            CourNumber: null,
            EpisodeNumber: 2m,
            EpisodeEndNumber: null,
            SpecialNumber: null,
            Year: 2002,
            Confidence: 0.95,
            RecognitionConfidenceLevel.High,
            IsAmbiguous: false,
            Evidence: Array.Empty<RecognitionEvidence>());

        var request = MetadataSearchRequest.FromRecognition(recognition, "zh-CN");

        Assert.Equal(["攻殻機動隊", "Ghost in the Shell"], request.Titles);
        Assert.Equal(2002, request.Year);
        Assert.Equal(1, request.SeasonNumber);
        Assert.Equal(2m, request.EpisodeNumber);
        Assert.Equal(MediaKind.SeriesEpisode, request.RecognitionMediaKind);
    }

    [Fact]
    public async Task Resolver_NormalizesDirectHostSearchRequestBeforeProviderCall()
    {
        var provider = new CapturingProvider("capture");
        var resolver = new MetadataResolver([provider]);

        _ = await resolver.ResolveAsync(
            new MetadataSearchRequest(
                ["S01 攻壳机动队 STAND ALONE COMPLEX"],
                2002,
                MediaKind.SeriesEpisode,
                1,
                1,
                "zh-CN",
                10),
            TestContext.Current.CancellationToken);

        Assert.NotNull(provider.LastTitles);
        Assert.Equal(
            [
                "S01 攻壳机动队 STAND ALONE COMPLEX",
                "攻壳机动队 STAND ALONE COMPLEX",
            ],
            provider.LastTitles);
    }

    [Theory]
    [InlineData("04. 鲁邦三世part4", "鲁邦三世 第4期")]
    [InlineData("攻壳机动队:SAC_2045 1-2季", "攻壳机动队:SAC 2045")]
    [InlineData("东京食尸鬼.Tokyo Ghoul", "Tokyo Ghoul")]
    [InlineData("弦音 -风舞高中弓道部", "弦音")]
    public async Task Resolver_ExpandsConservativeProviderSearchVariants(
        string rawTitle,
        string expectedVariant)
    {
        var provider = new CapturingProvider("capture");
        var resolver = new MetadataResolver([provider]);

        _ = await resolver.ResolveAsync(
            new MetadataSearchRequest(
                [rawTitle],
                null,
                MediaKind.SeriesEpisode,
                1,
                1,
                "zh-CN",
                10),
            TestContext.Current.CancellationToken);

        Assert.NotNull(provider.LastTitles);
        Assert.Contains(expectedVariant, provider.LastTitles, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Resolver_PrefersExactTitleYearAndKindMatch()
    {
        var provider = new FakeProvider(
            "fake",
            [
                Candidate("1", "Ghost in the Shell", 1995, MetadataSubjectKind.Movie, 0),
                Candidate("2", "Ghost in the Shell", 2002, MetadataSubjectKind.Series, 1),
            ]);

        var resolver = new MetadataResolver([provider]);
        var request = new MetadataSearchRequest(
            ["Ghost in the Shell"],
            2002,
            MediaKind.SeriesEpisode,
            SeasonNumber: 1,
            EpisodeNumber: 2,
            PreferredLanguage: "en",
            Limit: 10);

        var result = await resolver.ResolveAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.IsResolved);
        Assert.NotNull(result.Best);
        Assert.Equal("2", result.Best.Candidate.Id.Value);
        Assert.True(result.Best.Score >= 0.9);
    }


    [Fact]
    public async Task Resolver_CollectionSeasonRangeDoesNotOverrideCurrentFileSeason()
    {
        var provider = new FakeProvider(
            "fake",
            [
                Candidate("s1", "鬼灭之刃", 2019, MetadataSubjectKind.Series, 0),
                Candidate("s4", "鬼灭之刃 第四季", 2023, MetadataSubjectKind.Series, 1),
            ]);

        var resolver = new MetadataResolver([provider]);
        var result = await resolver.ResolveAsync(
            new MetadataSearchRequest(
                ["鬼灭之刃 S00-S05全", "鬼灭之刃"],
                null,
                MediaKind.SeriesEpisode,
                SeasonNumber: 4,
                EpisodeNumber: 3,
                PreferredLanguage: "zh-CN",
                Limit: 10),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsResolved);
        Assert.Equal("s4", result.Best!.Candidate.Id.Value);
        Assert.Contains(
            result.Best.Evidence,
            static value => value == "installment=request:4,candidate:4");
        Assert.Contains(
            result.Best.Evidence,
            static value => value == "installment-source=recognition-season");
    }

    [Fact]
    public async Task Resolver_CollectionSeasonRangeDoesNotFalseResolveLaterArcToBaseSubject()
    {
        var provider = new FakeProvider(
            "fake",
            [
                Candidate("base", "鬼灭之刃", 2019, MetadataSubjectKind.Series, 0),
                Candidate("arc", "鬼灭之刃 柱训练篇", 2024, MetadataSubjectKind.Series, 1),
            ]);

        var resolver = new MetadataResolver([provider]);
        var result = await resolver.ResolveAsync(
            new MetadataSearchRequest(
                ["鬼灭之刃 S00-S05全", "鬼灭之刃"],
                null,
                MediaKind.SeriesEpisode,
                SeasonNumber: 5,
                EpisodeNumber: 1,
                PreferredLanguage: "zh-CN",
                Limit: 10),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsResolved);
        Assert.NotNull(result.Best);
        Assert.Contains(
            result.Best.Evidence,
            static value => value == "installment=request:5,candidate:-");
        Assert.Contains(
            result.Best.Evidence,
            static value => value == "installment-source=recognition-season");
    }

    [Fact]
    public async Task Resolver_ExplicitSequelTitleCanOverrideGenericSeasonOne()
    {
        var provider = new FakeProvider(
            "fake",
            [
                Candidate("base", "CLANNAD", 2007, MetadataSubjectKind.Series, 0),
                Candidate("after", "CLANNAD 〜AFTER STORY〜", 2008, MetadataSubjectKind.Series, 1),
            ]);

        var resolver = new MetadataResolver([provider]);
        var result = await resolver.ResolveAsync(
            new MetadataSearchRequest(
                ["CLANNAD AFTER STORY"],
                null,
                MediaKind.SeriesEpisode,
                SeasonNumber: 1,
                EpisodeNumber: 1,
                PreferredLanguage: "ja",
                Limit: 10),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsResolved);
        Assert.Equal("after", result.Best!.Candidate.Id.Value);
        Assert.Contains(
            result.Best.Evidence,
            static value => value == "installment-source=title");
    }

    [Fact]
    public async Task Resolver_UsesSeasonAndYearToPreferNamedSecondSeason()
    {
        var provider = new FakeProvider(
            "fake",
            [
                Candidate("1", "夏目友人帐", 2008, MetadataSubjectKind.Series, 0),
                Candidate("2", "续 夏目友人帐", 2009, MetadataSubjectKind.Series, 6),
                Candidate("3", "夏目友人帐 叁", 2011, MetadataSubjectKind.Series, 1),
            ]);

        var resolver = new MetadataResolver([provider]);
        var result = await resolver.ResolveAsync(
            new MetadataSearchRequest(
                ["夏目友人帐"],
                2009,
                MediaKind.SeriesEpisode,
                SeasonNumber: 2,
                EpisodeNumber: 1,
                PreferredLanguage: "zh-CN",
                Limit: 10),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsResolved);
        Assert.Equal("2", result.Best!.Candidate.Id.Value);
        Assert.Contains(result.Best.Evidence, static value => value.Contains("installment=request:2,candidate:2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Resolver_UsesExplicitZeroInstallmentToDisambiguateSteinsGateZero()
    {
        var provider = new FakeProvider(
            "fake",
            [
                CandidateWithAliases(
                    "1",
                    "命运石之门",
                    2011,
                    MetadataSubjectKind.Series,
                    1,
                    ["Steins;Gate"]),
                CandidateWithAliases(
                    "2",
                    "命运石之门 0",
                    2018,
                    MetadataSubjectKind.Series,
                    0,
                    ["Steins;Gate 0"]),
            ]);

        var resolver = new MetadataResolver([provider]);
        var result = await resolver.ResolveAsync(
            new MetadataSearchRequest(
                ["Steins;Gate 0"],
                null,
                MediaKind.SeriesEpisode,
                SeasonNumber: 1,
                EpisodeNumber: 1,
                PreferredLanguage: "en",
                Limit: 10),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsResolved);
        Assert.Equal("2", result.Best!.Candidate.Id.Value);
    }


    [Fact]
    public async Task Resolver_MatchesEquivalentPartAndChinesePeriodMarkers()
    {
        var provider = new FakeProvider(
            "fake",
            [
                Candidate("2", "鲁邦三世 第二期", 1977, MetadataSubjectKind.Series, 0),
                Candidate("1", "鲁邦三世 第一期", 1971, MetadataSubjectKind.Series, 1),
            ]);

        var resolver = new MetadataResolver([provider]);
        var result = await resolver.ResolveAsync(
            new MetadataSearchRequest(
                ["鲁邦三世part2"],
                null,
                MediaKind.SeriesEpisode,
                SeasonNumber: null,
                EpisodeNumber: 1,
                PreferredLanguage: "zh-CN",
                Limit: 10),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsResolved);
        Assert.Equal("2", result.Best!.Candidate.Id.Value);
        Assert.Contains(
            result.Best.Evidence,
            static value => value.Contains(
                "installment=request:2,candidate:2",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task Resolver_RecognizesEnglishOrdinalInstallmentMarkers()
    {
        var provider = new FakeProvider(
            "fake",
            [
                Candidate(
                    "2",
                    "攻壳机动队 S.A.C. 2nd GIG",
                    2004,
                    MetadataSubjectKind.Series,
                    0),
                Candidate(
                    "1",
                    "攻壳机动队 STAND ALONE COMPLEX",
                    2002,
                    MetadataSubjectKind.Series,
                    1),
            ]);

        var resolver = new MetadataResolver([provider]);
        var result = await resolver.ResolveAsync(
            new MetadataSearchRequest(
                [
                    "S02 攻壳机动队 S.A.C. 2nd GIG",
                    "攻壳机动队 S.A.C. 2nd GIG",
                ],
                null,
                MediaKind.SeriesEpisode,
                SeasonNumber: 2,
                EpisodeNumber: 1,
                PreferredLanguage: "zh-CN",
                Limit: 10),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsResolved);
        Assert.Equal("2", result.Best!.Candidate.Id.Value);
    }

    [Fact]
    public async Task Resolver_ExactNamedSeasonBeatsDerivativeThatOnlyContainsSeasonTitle()
    {
        var provider = new FakeProvider(
            "fake",
            [
                Candidate(
                    "gig",
                    "攻壳机动队 S.A.C. 2nd GIG",
                    2004,
                    MetadataSubjectKind.Series,
                    0),
                Candidate(
                    "individual",
                    "攻壳机动队 S.A.C. 2nd GIG 个别的十一人",
                    2006,
                    MetadataSubjectKind.Series,
                    1),
            ]);

        var resolver = new MetadataResolver([provider]);
        var result = await resolver.ResolveAsync(
            new MetadataSearchRequest(
                ["S02 攻壳机动队 S.A.C. 2nd GIG"],
                null,
                MediaKind.SeriesEpisode,
                SeasonNumber: 2,
                EpisodeNumber: 1,
                PreferredLanguage: "zh-CN",
                Limit: 10),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsResolved);
        Assert.Equal("gig", result.Best!.Candidate.Id.Value);
        Assert.True(
            result.Candidates[0].Score - result.Candidates[1].Score >= 0.06);
    }

    [Fact]
    public async Task Resolver_PromotesNearThresholdExactInstallmentWithSafeLead()
    {
        var provider = new FakeProvider(
            "fake",
            [
                Candidate(
                    "s2",
                    "Example 2nd Season",
                    2010,
                    MetadataSubjectKind.Series,
                    20),
                Candidate(
                    "noise",
                    "Example Movie",
                    2010,
                    MetadataSubjectKind.Movie,
                    0),
            ]);

        var resolver = new MetadataResolver(
            [provider],
            new MetadataResolverOptions(
                AutoResolveThreshold: 0.90,
                MinimumLead: 0.06,
                MaxCandidates: 20));
        var result = await resolver.ResolveAsync(
            new MetadataSearchRequest(
                ["Example"],
                2020,
                MediaKind.SeriesEpisode,
                SeasonNumber: 2,
                EpisodeNumber: 1,
                PreferredLanguage: "en",
                Limit: 10),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsResolved);
        Assert.Equal("s2", result.Best!.Candidate.Id.Value);
        Assert.True(result.Best.Score >= 0.90);
        Assert.Contains(
            result.Best.Evidence,
            static value => value.StartsWith(
                "near-threshold=exact-installment:",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task Resolver_PromotesNearThresholdDominantExactTitleWithoutLoweringGlobalGate()
    {
        var provider = new FakeProvider(
            "fake",
            [
                Candidate(
                    "series",
                    "AnoHana",
                    2011,
                    MetadataSubjectKind.Series,
                    0),
                Candidate(
                    "noise",
                    "AnoHana Side Story",
                    2015,
                    MetadataSubjectKind.Movie,
                    20),
            ]);

        var resolver = new MetadataResolver(
            [provider],
            new MetadataResolverOptions(
                AutoResolveThreshold: 0.90,
                MinimumLead: 0.06,
                MaxCandidates: 20));
        var result = await resolver.ResolveAsync(
            new MetadataSearchRequest(
                ["AnoHana"],
                null,
                MediaKind.SeriesEpisode,
                SeasonNumber: null,
                EpisodeNumber: 1,
                PreferredLanguage: "en",
                Limit: 10),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsResolved);
        Assert.Equal("series", result.Best!.Candidate.Id.Value);
        Assert.True(result.Best.Score >= 0.90);
        Assert.Contains(
            result.Best.Evidence,
            static value => value.StartsWith(
                "near-threshold=dominant-title:",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task Resolver_SeasonIdentityOutweighsRepeatedFranchisePremiereYear()
    {
        var provider = new FakeProvider(
            "fake",
            [
                Candidate("1", "轻音少女", 2009, MetadataSubjectKind.Series, 0),
                Candidate("2", "轻音少女 第二季", 2010, MetadataSubjectKind.Series, 1),
            ]);

        var resolver = new MetadataResolver([provider]);
        var result = await resolver.ResolveAsync(
            new MetadataSearchRequest(
                ["轻音少女"],
                2009,
                MediaKind.SeriesEpisode,
                SeasonNumber: 2,
                EpisodeNumber: 1,
                PreferredLanguage: "zh-CN",
                Limit: 10),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsResolved);
        Assert.Equal("2", result.Best!.Candidate.Id.Value);
    }

    [Fact]
    public async Task Resolver_ExactBaseSeriesBeatsProperSupersetSpecial()
    {
        var provider = new FakeProvider(
            "fake",
            [
                Candidate(
                    "base",
                    "Re：从零开始的异世界生活",
                    2016,
                    MetadataSubjectKind.Series,
                    0),
                Candidate(
                    "ova",
                    "Re：从零开始的异世界生活 雪之回忆",
                    2019,
                    MetadataSubjectKind.Series,
                    1),
            ]);

        var resolver = new MetadataResolver([provider]);
        var result = await resolver.ResolveAsync(
            new MetadataSearchRequest(
                ["Re：从零开始的异世界生活"],
                null,
                MediaKind.SeriesEpisode,
                SeasonNumber: 1,
                EpisodeNumber: 1,
                PreferredLanguage: "zh-CN",
                Limit: 10),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsResolved);
        Assert.Equal("base", result.Best!.Candidate.Id.Value);
    }

    [Fact]
    public async Task Resolver_ExactLegacyTitleBeatsBrotherhoodSuperset()
    {
        var provider = new FakeProvider(
            "fake",
            [
                CandidateWithAliases(
                    "2003",
                    "钢之炼金术师",
                    2003,
                    MetadataSubjectKind.Series,
                    1,
                    ["Fullmetal Alchemist"]),
                CandidateWithAliases(
                    "2009",
                    "钢之炼金术师 FULLMETAL ALCHEMIST",
                    2009,
                    MetadataSubjectKind.Series,
                    0,
                    ["Fullmetal Alchemist: Brotherhood"]),
            ]);

        var resolver = new MetadataResolver([provider]);
        var result = await resolver.ResolveAsync(
            new MetadataSearchRequest(
                ["Fullmetal Alchemist", "钢之炼金术师"],
                null,
                MediaKind.SeriesEpisode,
                SeasonNumber: null,
                EpisodeNumber: 1,
                PreferredLanguage: "en",
                Limit: 10),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsResolved);
        Assert.Equal("2003", result.Best!.Candidate.Id.Value);
    }

    [Fact]
    public async Task Resolver_DoesNotAutoResolveDuplicateExactSubjects()
    {
        var provider = new FakeProvider(
            "fake",
            [
                Candidate("a", "刀剑神域", 2012, MetadataSubjectKind.Series, 0),
                Candidate("b", "刀剑神域", 2012, MetadataSubjectKind.Series, 1),
            ]);

        var resolver = new MetadataResolver([provider]);
        var result = await resolver.ResolveAsync(
            new MetadataSearchRequest(
                ["Sword Art Online", "刀剑神域"],
                null,
                MediaKind.SeriesEpisode,
                SeasonNumber: null,
                EpisodeNumber: 1,
                PreferredLanguage: "zh-CN",
                Limit: 10),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsResolved);
        Assert.NotNull(result.Best);
    }

    [Fact]
    public async Task EnrichAsync_PromotesExactLongRunningSeriesWhenLocalSeasonIsOnlyAPartition()
    {
        var provider = new LongRunningSeriesProvider(
            "fake",
            [Candidate("naruto", "火影忍者疾风传", 2007, MetadataSubjectKind.Series, 0)],
            episodeCount: 500);

        var resolver = new MetadataResolver([provider]);
        var result = await resolver.EnrichAsync(
            new MetadataSearchRequest(
                ["火影忍者：疾风传"],
                null,
                MediaKind.SeriesEpisode,
                SeasonNumber: 2,
                EpisodeNumber: 1,
                PreferredLanguage: "zh-CN",
                Limit: 10),
            TestContext.Current.CancellationToken);

        Assert.True(result.Resolution.IsResolved);
        Assert.Equal("naruto", result.Resolution.Best!.Candidate.Id.Value);
        Assert.Contains(
            result.Resolution.Best.Evidence,
            static value => value.StartsWith(
                "continuous-series=episode-count:",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task EnrichAsync_DoesNotPromoteShortSeriesAcrossLocalSeasonBoundary()
    {
        var provider = new LongRunningSeriesProvider(
            "fake",
            [Candidate("clannad", "CLANNAD", 2007, MetadataSubjectKind.Series, 0)],
            episodeCount: 24);

        var resolver = new MetadataResolver([provider]);
        var result = await resolver.EnrichAsync(
            new MetadataSearchRequest(
                ["CLANNAD"],
                null,
                MediaKind.SeriesEpisode,
                SeasonNumber: 2,
                EpisodeNumber: 1,
                PreferredLanguage: "ja",
                Limit: 10),
            TestContext.Current.CancellationToken);

        Assert.False(result.Resolution.IsResolved);
        Assert.Null(result.Subject);
    }

    [Fact]
    public async Task Resolver_SeasonOneKeepsYearStrongEnoughToBeatUnknownYearDuplicate()
    {
        var provider = new FakeProvider(
            "fake",
            [
                CandidateOptionalYear(
                    "2023",
                    "药屋少女的呢喃",
                    2023,
                    MetadataSubjectKind.Series,
                    0),
                CandidateOptionalYear(
                    "unknown",
                    "药屋少女的呢喃",
                    null,
                    MetadataSubjectKind.Series,
                    5),
            ]);

        var resolver = new MetadataResolver([provider]);
        var result = await resolver.ResolveAsync(
            new MetadataSearchRequest(
                ["药屋少女的呢喃"],
                2023,
                MediaKind.SeriesEpisode,
                SeasonNumber: 1,
                EpisodeNumber: 1,
                PreferredLanguage: "zh-CN",
                Limit: 10),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsResolved);
        Assert.Equal("2023", result.Best!.Candidate.Id.Value);
        Assert.True(result.Best.Score >= 0.90);
    }

    [Fact]
    public async Task Resolver_PenalizesOadCandidateForRegularSeriesRequest()
    {
        var provider = new FakeProvider(
            "fake",
            [
                Candidate(
                    "tv",
                    "Fate/kaleid liner 魔法少女☆伊莉雅",
                    2013,
                    MetadataSubjectKind.Series,
                    2),
                Candidate(
                    "oad",
                    "Fate/kaleid liner 魔法少女☆伊莉雅 OAD",
                    2014,
                    MetadataSubjectKind.Series,
                    4),
            ]);

        var resolver = new MetadataResolver([provider]);
        var result = await resolver.ResolveAsync(
            new MetadataSearchRequest(
                ["魔法少女☆伊莉雅"],
                2013,
                MediaKind.SeriesEpisode,
                SeasonNumber: 1,
                EpisodeNumber: 1,
                PreferredLanguage: "zh-CN",
                Limit: 10),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsResolved);
        Assert.Equal("tv", result.Best!.Candidate.Id.Value);
        Assert.Contains(
            result.Candidates.Single(item => item.Candidate.Id.Value == "oad").Evidence,
            static value => value == "special-subject-mismatch=-0.040");
    }

    [Fact]
    public async Task Resolver_MapsAfterStoryToSecondInstallment()
    {
        var provider = new FakeProvider(
            "fake",
            [
                Candidate("base", "CLANNAD", 2007, MetadataSubjectKind.Series, 0),
                Candidate(
                    "after",
                    "CLANNAD 〜AFTER STORY〜",
                    2008,
                    MetadataSubjectKind.Series,
                    1),
                Candidate(
                    "tomoyo",
                    "CLANNAD 另一个世界 智代篇",
                    2008,
                    MetadataSubjectKind.Series,
                    2),
            ]);

        var resolver = new MetadataResolver([provider]);
        var result = await resolver.ResolveAsync(
            new MetadataSearchRequest(
                ["CLANNAD"],
                null,
                MediaKind.SeriesEpisode,
                SeasonNumber: 2,
                EpisodeNumber: 1,
                PreferredLanguage: "ja",
                Limit: 10),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsResolved);
        Assert.Equal("after", result.Best!.Candidate.Id.Value);
        Assert.Contains(
            result.Best.Evidence,
            static value => value.Contains(
                "installment=request:2,candidate:2",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task Resolver_NamedSeasonSemanticBeatsMismatchedFilenameSeasonNumber()
    {
        var provider = new FakeProvider(
            "fake",
            [
                CandidateWithAliases(
                    "diamond",
                    "JOJO的奇妙冒险 不灭钻石",
                    2016,
                    MetadataSubjectKind.Series,
                    0,
                    ["JOJO Part 4"]),
                Candidate(
                    "golden",
                    "JOJO的奇妙冒险 黄金之风",
                    2018,
                    MetadataSubjectKind.Series,
                    1),
            ]);

        var resolver = new MetadataResolver([provider]);
        var result = await resolver.ResolveAsync(
            new MetadataSearchRequest(
                ["JOJO的奇妙冒险", "第五季 黄金之风"],
                null,
                MediaKind.SeriesEpisode,
                SeasonNumber: 4,
                EpisodeNumber: 1,
                PreferredLanguage: "zh-CN",
                Limit: 10),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsResolved);
        Assert.Equal("golden", result.Best!.Candidate.Id.Value);
        Assert.Contains(
            result.Best.Evidence,
            static value => value.StartsWith(
                "named-season-semantic=",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task Resolver_DoesNotTreatMatchingFirstCharacterAsExactShortTitle()
    {
        var provider = new FakeProvider(
            "fake",
            [
                Candidate(
                    "movie",
                    "剧场版 咒术回战 0",
                    2021,
                    MetadataSubjectKind.Movie,
                    0),
                Candidate(
                    "short",
                    "咒",
                    2022,
                    MetadataSubjectKind.Movie,
                    17),
            ]);

        var resolver = new MetadataResolver([provider]);
        var result = await resolver.ResolveAsync(
            new MetadataSearchRequest(
                ["咒术回战0", "咒术回战0 剧场版"],
                2021,
                MediaKind.Movie,
                SeasonNumber: null,
                EpisodeNumber: null,
                PreferredLanguage: "zh-CN",
                Limit: 10),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsResolved);
        Assert.Equal("movie", result.Best!.Candidate.Id.Value);
        var shortCandidate = Assert.Single(
            result.Candidates,
            static item => item.Candidate.Id.Value == "short");
        Assert.Contains(
            shortCandidate.Evidence,
            static value => value == "title=0.000");
    }

    [Theory]
    [InlineData(25, 1)]
    [InlineData(48, 24)]
    public async Task EnrichAsync_ContinuesEpisodeAcrossSameLocalSeasonProviderSubjects(
        int localEpisode,
        int expectedProviderEpisode)
    {
        var provider = new RelationChainProvider(
            "fake",
            [Candidate("base", "JOJO的奇妙冒险", 2012, MetadataSubjectKind.Series, 0)],
            new Dictionary<string, RelationNode>(StringComparer.Ordinal)
            {
                ["base"] = new("JOJO的奇妙冒险", 2012, 26, "stardust"),
                ["stardust"] = new(
                    "JOJO的奇妙冒险 星尘斗士",
                    2014,
                    24,
                    "egypt"),
                ["egypt"] = new(
                    "JOJO的奇妙冒险 星尘斗士 埃及篇",
                    2015,
                    24,
                    null),
            });

        var resolver = new MetadataResolver([provider]);
        var result = await resolver.EnrichAsync(
            new MetadataSearchRequest(
                ["JOJO的奇妙冒险", "第三季 星尘十字军"],
                2012,
                MediaKind.SeriesEpisode,
                SeasonNumber: 2,
                EpisodeNumber: localEpisode,
                PreferredLanguage: "zh-CN",
                Limit: 10),
            TestContext.Current.CancellationToken);

        Assert.True(result.Resolution.IsResolved);
        Assert.NotNull(result.Subject);
        Assert.Equal("egypt", result.Subject.Id.Value);
        Assert.NotNull(result.Episode);
        Assert.Equal(expectedProviderEpisode, result.Episode.EpisodeNumber);
        Assert.Contains(
            result.Resolution.Best!.Evidence,
            static value => value ==
                "episode-subject-span=stardust>egypt");

        var second = result.Resolution.Candidates.Skip(1).FirstOrDefault();
        Assert.True(
            second is null ||
            result.Resolution.Best.Score - second.Score >= 0.06);
        Assert.DoesNotContain(
            result.Resolution.Candidates,
            static item => item.Candidate.Id.Value == "stardust");
    }

    [Theory]
    [InlineData(1, "stardust", 1)]
    [InlineData(24, "stardust", 24)]
    [InlineData(25, "egypt", 1)]
    [InlineData(48, "egypt", 24)]
    public async Task EnrichAsync_ResolvesAmbiguousTwoSubjectLocalSeasonFamily(
        int localEpisode,
        string expectedSubject,
        int expectedEpisode)
    {
        var provider = new RelationChainProvider(
            "fake",
            [
                Candidate(
                    "stardust",
                    "JOJO的奇妙冒险 星尘斗士",
                    2014,
                    MetadataSubjectKind.Series,
                    0),
                Candidate(
                    "egypt",
                    "JOJO的奇妙冒险 星尘斗士 埃及篇",
                    2015,
                    MetadataSubjectKind.Series,
                    1),
            ],
            new Dictionary<string, RelationNode>(StringComparer.Ordinal)
            {
                ["stardust"] = new(
                    "JOJO的奇妙冒险 星尘斗士",
                    2014,
                    24,
                    "egypt"),
                ["egypt"] = new(
                    "JOJO的奇妙冒险 星尘斗士 埃及篇",
                    2015,
                    24,
                    null),
            });

        var resolver = new MetadataResolver([provider]);
        var result = await resolver.EnrichAsync(
            new MetadataSearchRequest(
                ["JOJO的奇妙冒险", "第三季 星尘斗士"],
                null,
                MediaKind.SeriesEpisode,
                SeasonNumber: 2,
                EpisodeNumber: localEpisode,
                PreferredLanguage: "zh-CN",
                Limit: 10),
            TestContext.Current.CancellationToken);

        Assert.True(result.Resolution.IsResolved);
        Assert.NotNull(result.Subject);
        Assert.Equal(expectedSubject, result.Subject.Id.Value);
        Assert.NotNull(result.Episode);
        Assert.Equal(expectedEpisode, result.Episode.EpisodeNumber);
        Assert.Contains(
            result.Resolution.Best!.Evidence,
            static value => value ==
                "local-season-subject-family=stardust>egypt");
    }

    [Theory]
    [InlineData(1, "stone1", 1)]
    [InlineData(12, "stone1", 12)]
    [InlineData(13, "stone2", 1)]
    [InlineData(24, "stone2", 12)]
    [InlineData(25, "stone3", 1)]
    [InlineData(38, "stone3", 14)]
    public async Task EnrichAsync_ResolvesThreePartLocalSeasonFamily(
        int localEpisode,
        string expectedSubject,
        int expectedEpisode)
    {
        var provider = new RelationChainProvider(
            "fake",
            [
                Candidate(
                    "stone1",
                    "JOJO的奇妙冒险 石之海",
                    2021,
                    MetadataSubjectKind.Series,
                    0),
                Candidate(
                    "stone2",
                    "JOJO的奇妙冒险 石之海 第2部分",
                    2022,
                    MetadataSubjectKind.Series,
                    1),
                Candidate(
                    "stone3",
                    "JOJO的奇妙冒险 石之海 第3部分",
                    2022,
                    MetadataSubjectKind.Series,
                    2),
            ],
            new Dictionary<string, RelationNode>(StringComparer.Ordinal)
            {
                ["stone1"] = new(
                    "JOJO的奇妙冒险 石之海",
                    2021,
                    12,
                    "stone2"),
                ["stone2"] = new(
                    "JOJO的奇妙冒险 石之海 第2部分",
                    2022,
                    12,
                    "stone3"),
                ["stone3"] = new(
                    "JOJO的奇妙冒险 石之海 第3部分",
                    2022,
                    14,
                    null),
            });

        var resolver = new MetadataResolver([provider]);
        var result = await resolver.EnrichAsync(
            new MetadataSearchRequest(
                ["JOJO的奇妙冒险", "第六季 石之海"],
                null,
                MediaKind.SeriesEpisode,
                SeasonNumber: 5,
                EpisodeNumber: localEpisode,
                PreferredLanguage: "zh-CN",
                Limit: 10),
            TestContext.Current.CancellationToken);

        Assert.True(result.Resolution.IsResolved);
        Assert.Equal(expectedSubject, result.Subject!.Id.Value);
        Assert.Equal(expectedEpisode, result.Episode!.EpisodeNumber);
        Assert.Contains(
            result.Resolution.Best!.Evidence,
            static value => value ==
                "local-season-subject-family=stone1>stone2>stone3");
    }

    [Fact]
    public async Task EnrichAsync_BlocksRelationChainWhenBestAlreadyMatchesRequestedInstallment()
    {
        var provider = new RelationChainProvider(
            "fake",
            [
                Candidate(
                    "gig",
                    "攻壳机动队 S.A.C. 2nd GIG",
                    2004,
                    MetadataSubjectKind.Series,
                    0),
                Candidate(
                    "individual",
                    "攻壳机动队 S.A.C. 2nd GIG 个别的十一人",
                    2006,
                    MetadataSubjectKind.Series,
                    1),
            ],
            new Dictionary<string, RelationNode>(StringComparer.Ordinal)
            {
                ["gig"] = new(
                    "攻壳机动队 S.A.C. 2nd GIG",
                    2004,
                    26,
                    "sss"),
                ["individual"] = new(
                    "攻壳机动队 S.A.C. 2nd GIG 个别的十一人",
                    2006,
                    1,
                    null),
                ["sss"] = new(
                    "攻壳机动队 S.A.C. Solid State Society",
                    2006,
                    1,
                    null),
            });

        var resolver = new MetadataResolver([provider]);
        var result = await resolver.EnrichAsync(
            new MetadataSearchRequest(
                ["攻壳机动队 S.A.C.", "S02 攻壳机动队 S.A.C. 2nd GIG"],
                2004,
                MediaKind.SeriesEpisode,
                SeasonNumber: 2,
                EpisodeNumber: 1,
                PreferredLanguage: "zh-CN",
                Limit: 10),
            TestContext.Current.CancellationToken);

        Assert.False(result.Resolution.IsResolved);
        Assert.Null(result.Subject);
        Assert.DoesNotContain(
            result.Resolution.Candidates.SelectMany(static item => item.Evidence),
            static value => value.StartsWith(
                "relation-chain=",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task EnrichAsync_ResolvesNamedArcThroughUniqueSeriesSequelChain()
    {
        var provider = new RelationChainProvider(
            "fake",
            [Candidate("s1", "鬼灭之刃", 2019, MetadataSubjectKind.Series, 0)],
            new Dictionary<string, RelationNode>(StringComparer.Ordinal)
            {
                ["s1"] = new("鬼灭之刃", 2019, 26, "s2"),
                ["s2"] = new("鬼灭之刃 无限列车篇", 2021, 7, "s3"),
                ["s3"] = new("鬼灭之刃 游郭篇", 2021, 11, "s4"),
                ["s4"] = new("鬼灭之刃 刀匠村篇", 2023, 11, "s5"),
                ["s5"] = new("鬼灭之刃 柱训练篇", 2024, 8, null),
            });

        var resolver = new MetadataResolver([provider]);
        var result = await resolver.EnrichAsync(
            new MetadataSearchRequest(
                ["鬼灭之刃"],
                2019,
                MediaKind.SeriesEpisode,
                SeasonNumber: 5,
                EpisodeNumber: 1,
                PreferredLanguage: "zh-CN",
                Limit: 10),
            TestContext.Current.CancellationToken);

        Assert.True(result.Resolution.IsResolved);
        Assert.NotNull(result.Subject);
        Assert.Equal("s5", result.Subject.Id.Value);
        Assert.Equal("鬼灭之刃 柱训练篇", result.Subject.Titles.Primary);
        Assert.Contains(
            result.Resolution.Best!.Evidence,
            static value => value == "relation-chain=season:5");
        Assert.Contains(
            result.Resolution.Best.Evidence,
            static value => value == "relation-chain-path=s1>s2>s3>s4>s5");
        Assert.Contains(
            result.Resolution.Best.Evidence,
            static value => value == "relation-chain-season-map=1:s1>2:s2>3:s3>4:s4>5:s5");

        var second = result.Resolution.Candidates.Skip(1).FirstOrDefault();
        Assert.True(
            second is null ||
            result.Resolution.Best.Score - second.Score >= 0.06);
    }

    [Fact]
    public async Task EnrichAsync_NormalizesRawFieldTitleBeforeRelationProbe()
    {
        var provider = new RelationChainProvider(
            "fake",
            [
                CandidateWithAliases(
                    "s1",
                    "鬼灭之刃",
                    2019,
                    MetadataSubjectKind.Series,
                    0,
                    ["Demon Slayer: Kimetsu no Yaiba"]),
            ],
            new Dictionary<string, RelationNode>(StringComparer.Ordinal)
            {
                ["s1"] = new("鬼灭之刃", 2019, 26, "s2"),
                ["s2"] = new("鬼灭之刃 无限列车篇", 2021, 7, "s3"),
                ["s3"] = new("鬼灭之刃 游郭篇", 2021, 11, "s4"),
                ["s4"] = new("鬼灭之刃 刀匠村篇", 2023, 11, "s5"),
                ["s5"] = new("鬼灭之刃 柱训练篇", 2024, 8, null),
            });

        var resolver = new MetadataResolver([provider]);
        var result = await resolver.EnrichAsync(
            new MetadataSearchRequest(
                [
                    "Demon Slayer： Kimetsu no Yaiba.2019",
                    "鬼灭之刃 S00-S05全 4K超分",
                ],
                2019,
                MediaKind.SeriesEpisode,
                SeasonNumber: 5,
                EpisodeNumber: 1,
                PreferredLanguage: "zh-CN",
                Limit: 10),
            TestContext.Current.CancellationToken);

        Assert.True(result.Resolution.IsResolved);
        Assert.Equal("s5", result.Subject!.Id.Value);
        Assert.Contains(
            result.Resolution.Best!.Evidence,
            static value => value == "relation-chain=season:5");
    }

    [Fact]
    public async Task EnrichAsync_DoesNotCountSameSeasonPartAsAnotherLocalSeason()
    {
        var provider = new RelationChainProvider(
            "fake",
            [Candidate("a1", "进击的巨人", 2013, MetadataSubjectKind.Series, 0)],
            new Dictionary<string, RelationNode>(StringComparer.Ordinal)
            {
                ["a1"] = new("进击的巨人", 2013, 25, "a2"),
                ["a2"] = new("进击的巨人 第二季", 2017, 12, "a3"),
                ["a3"] = new("进击的巨人 第三季", 2018, 12, "a3p2"),
                ["a3p2"] = new("进击的巨人 第三季 Part.2", 2019, 10, "a4"),
                ["a4"] = new("进击的巨人 The Final Season", 2020, 16, null),
            });

        var resolver = new MetadataResolver([provider]);
        var result = await resolver.EnrichAsync(
            new MetadataSearchRequest(
                ["进击的巨人"],
                2013,
                MediaKind.SeriesEpisode,
                SeasonNumber: 4,
                EpisodeNumber: 1,
                PreferredLanguage: "zh-CN",
                Limit: 10),
            TestContext.Current.CancellationToken);

        Assert.True(result.Resolution.IsResolved);
        Assert.Equal("a4", result.Subject!.Id.Value);
        Assert.Contains(
            result.Resolution.Best!.Evidence,
            static value => value ==
                "relation-chain-path=a1>a2>a3>a3p2>a4");
        Assert.Contains(
            result.Resolution.Best.Evidence,
            static value => value ==
                "relation-chain-season-map=1:a1>2:a2>3:a3>3:a3p2>4:a4");
    }

    [Fact]
    public async Task EnrichAsync_LeavesLocalSplitCourUnresolvedWithoutStructuralConfirmation()
    {
        var provider = new RelationChainProvider(
            "fake",
            [Candidate("fz", "Fate/Zero", 2011, MetadataSubjectKind.Series, 0)],
            new Dictionary<string, RelationNode>(StringComparer.Ordinal)
            {
                ["fz"] = new("Fate/Zero", 2011, 25, null),
            });

        var resolver = new MetadataResolver([provider]);
        var result = await resolver.EnrichAsync(
            new MetadataSearchRequest(
                ["Fate/Zero"],
                2011,
                MediaKind.SeriesEpisode,
                SeasonNumber: 2,
                EpisodeNumber: 1,
                PreferredLanguage: "ja",
                Limit: 10),
            TestContext.Current.CancellationToken);

        Assert.False(result.Resolution.IsResolved);
        Assert.Null(result.Subject);
    }

    [Fact]
    public async Task EnrichAsync_SelectsNearestChronologicalSeriesFromMultipleSequelBranches()
    {
        var provider = new RelationChainProvider(
            "fake",
            [Candidate("s1", "鬼灭之刃", 2019, MetadataSubjectKind.Series, 0)],
            new Dictionary<string, RelationNode>(StringComparer.Ordinal)
            {
                ["s1"] = new("鬼灭之刃", 2019, 26, "s2"),
                ["s2"] = new(
                    "鬼灭之刃 无限列车篇",
                    2021,
                    7,
                    null,
                    ["s3", "s4"]),
                ["s3"] = new("鬼灭之刃 游郭篇", 2021, 11, null),
                ["s4"] = new("鬼灭之刃 刀匠村篇", 2023, 11, null),
            });

        var resolver = new MetadataResolver([provider]);
        var result = await resolver.EnrichAsync(
            new MetadataSearchRequest(
                ["鬼灭之刃"],
                2019,
                MediaKind.SeriesEpisode,
                SeasonNumber: 3,
                EpisodeNumber: 1,
                PreferredLanguage: "zh-CN",
                Limit: 10),
            TestContext.Current.CancellationToken);

        Assert.True(result.Resolution.IsResolved);
        Assert.Equal("s3", result.Subject!.Id.Value);
        Assert.Contains(
            result.Resolution.Best!.Evidence,
            static value => value.StartsWith(
                "relation-chain-selection=",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task EnrichAsync_PenalizesSideContentWhenChoosingSequelBranch()
    {
        var provider = new RelationChainProvider(
            "fake",
            [Candidate("s1", "Example", 2020, MetadataSubjectKind.Series, 0)],
            new Dictionary<string, RelationNode>(StringComparer.Ordinal)
            {
                ["s1"] = new("Example", 2020, 12, null, ["ova", "s2"]),
                ["ova"] = new("Example OVA", 2021, 1, null),
                ["s2"] = new("Example New Arc", 2021, 12, null),
            });

        var resolver = new MetadataResolver([provider]);
        var result = await resolver.EnrichAsync(
            new MetadataSearchRequest(
                ["Example"],
                2020,
                MediaKind.SeriesEpisode,
                SeasonNumber: 2,
                EpisodeNumber: 1,
                PreferredLanguage: "en",
                Limit: 10),
            TestContext.Current.CancellationToken);

        Assert.True(result.Resolution.IsResolved);
        Assert.Equal("s2", result.Subject!.Id.Value);
    }

    [Fact]
    public async Task EnrichAsync_DoesNotPromoteAmbiguousSeriesSequelBranches()
    {
        var provider = new RelationChainProvider(
            "fake",
            [Candidate("s1", "Example", 2020, MetadataSubjectKind.Series, 0)],
            new Dictionary<string, RelationNode>(StringComparer.Ordinal)
            {
                ["s1"] = new("Example", 2020, 12, null, ["s2a", "s2b"]),
                ["s2a"] = new("Example Arc A", 2021, 12, null),
                ["s2b"] = new("Example Arc B", 2021, 12, null),
            });

        var resolver = new MetadataResolver([provider]);
        var result = await resolver.EnrichAsync(
            new MetadataSearchRequest(
                ["Example"],
                null,
                MediaKind.SeriesEpisode,
                SeasonNumber: 2,
                EpisodeNumber: 1,
                PreferredLanguage: "en",
                Limit: 10),
            TestContext.Current.CancellationToken);

        Assert.False(result.Resolution.IsResolved);
        Assert.Null(result.Subject);
    }

    [Fact]
    public async Task CachedProvider_CachesSubjectRelations()
    {
        var inner = new RelationChainProvider(
            "fake",
            [Candidate("s1", "Example", 2020, MetadataSubjectKind.Series, 0)],
            new Dictionary<string, RelationNode>(StringComparer.Ordinal)
            {
                ["s1"] = new("Example", 2020, 12, "s2"),
                ["s2"] = new("Example Arc", 2021, 12, null),
            });
        var cached = new CachedMetadataProvider(inner, new MemoryMetadataCache());
        var relationProvider = Assert.IsAssignableFrom<IMetadataRelationProvider>(cached);
        var id = new MetadataProviderItemId("fake", "s1", MetadataSubjectKind.Series);

        _ = await relationProvider.GetRelatedSubjectsAsync(
            id,
            TestContext.Current.CancellationToken);
        _ = await relationProvider.GetRelatedSubjectsAsync(
            id,
            TestContext.Current.CancellationToken);

        Assert.Equal(1, inner.RelationCalls);
    }

    [Fact]
    public async Task Resolver_IsolatesProviderFailure()
    {
        var resolver = new MetadataResolver(
        [
            new ThrowingProvider("broken"),
            new FakeProvider(
                "healthy",
                [Candidate("7", "Steins;Gate", 2011, MetadataSubjectKind.Series, 0)]),
        ]);

        var result = await resolver.ResolveAsync(
            new MetadataSearchRequest(
                ["Steins;Gate"],
                2011,
                MediaKind.SeriesEpisode,
                1,
                1,
                "ja",
                10), TestContext.Current.CancellationToken);

        Assert.True(result.IsResolved);
        Assert.Single(result.ProviderErrors);
        Assert.Equal("broken", result.ProviderErrors[0].Provider);
        Assert.Equal("healthy", result.Best!.Candidate.Id.Provider);
    }

    [Fact]
    public async Task CachedProvider_ReusesSubjectSearchAcrossEpisodes()
    {
        var inner = new CountingProvider(
            "counting",
            [Candidate("1", "CLANNAD", 2007, MetadataSubjectKind.Series, 0)]);
        var cached = new CachedMetadataProvider(inner, new MemoryMetadataCache());

        var episode1 = new MetadataSearchRequest(
            ["CLANNAD"], 2007, MediaKind.SeriesEpisode, 1, 1, "ja", 10);
        var episode18 = episode1 with { EpisodeNumber = 18 };

        _ = await cached.SearchAsync(
            episode1,
            TestContext.Current.CancellationToken);
        _ = await cached.SearchAsync(
            episode18,
            TestContext.Current.CancellationToken);

        Assert.Equal(1, inner.SearchCount);
    }

    [Fact]
    public async Task EnrichAsync_FetchesResolvedSubjectAndEpisode()
    {
        var provider = new EnrichmentProvider(
            "catalog",
            [Candidate("42", "CLANNAD", 2007, MetadataSubjectKind.Series, 0)]);

        var resolver = new MetadataResolver([provider]);
        var request = new MetadataSearchRequest(
            ["CLANNAD"], 2007, MediaKind.SeriesEpisode, 1, 3, "ja", 10);

        var result = await resolver.EnrichAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.True(result.Resolution.IsResolved);
        Assert.NotNull(result.Subject);
        Assert.NotNull(result.Episode);
        Assert.Equal("CLANNAD", result.Subject.Titles.Primary);
        Assert.Equal(3m, result.Episode.EpisodeNumber);
        Assert.Empty(result.ProviderErrors);
    }

    [Fact]
    public async Task CachedProvider_AvoidsDuplicateSearchCalls()
    {
        var inner = new CountingProvider(
            "counting",
            [Candidate("1", "CLANNAD", 2007, MetadataSubjectKind.Series, 0)]);
        var cached = new CachedMetadataProvider(inner, new MemoryMetadataCache());
        var request = new MetadataSearchRequest(
            ["CLANNAD"], 2007, MediaKind.SeriesEpisode, 1, 1, "ja", 10);

        var first = await cached.SearchAsync(request, TestContext.Current.CancellationToken);
        var second = await cached.SearchAsync(request, TestContext.Current.CancellationToken);

        Assert.Single(first);
        Assert.Single(second);
        Assert.Equal(1, inner.SearchCount);
    }

    private static MetadataSearchCandidate Candidate(
        string id,
        string title,
        int year,
        MetadataSubjectKind kind,
        int rank) =>
        new(
            new MetadataProviderItemId("fake", id, kind),
            new MetadataTitles(
                title,
                title,
                new Dictionary<string, string>(),
                Array.Empty<string>()),
            year,
            rank);


    private static MetadataSearchCandidate CandidateOptionalYear(
        string id,
        string title,
        int? year,
        MetadataSubjectKind kind,
        int rank) =>
        new(
            new MetadataProviderItemId("fake", id, kind),
            new MetadataTitles(
                title,
                title,
                new Dictionary<string, string>(),
                Array.Empty<string>()),
            year,
            rank);

    private static MetadataSearchCandidate CandidateWithAliases(
        string id,
        string title,
        int year,
        MetadataSubjectKind kind,
        int rank,
        IReadOnlyList<string> aliases) =>
        new(
            new MetadataProviderItemId("fake", id, kind),
            new MetadataTitles(
                title,
                title,
                new Dictionary<string, string>(),
                aliases),
            year,
            rank);

    private class FakeProvider : IMetadataProvider
    {
        private readonly IReadOnlyList<MetadataSearchCandidate> _candidates;

        public FakeProvider(
            string name,
            IReadOnlyList<MetadataSearchCandidate> candidates)
        {
            Name = name;
            _candidates = candidates
                .Select(candidate => candidate with
                {
                    Id = candidate.Id with { Provider = name },
                })
                .ToArray();
        }

        public string Name { get; }

        public virtual Task<IReadOnlyList<MetadataSearchCandidate>> SearchAsync(
            MetadataSearchRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_candidates);

        public virtual Task<MetadataSubject?> GetSubjectAsync(
            MetadataProviderItemId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<MetadataSubject?>(null);

        public virtual Task<IReadOnlyList<MetadataEpisode>> GetEpisodesAsync(
            MetadataProviderItemId id,
            int? seasonNumber = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MetadataEpisode>>(Array.Empty<MetadataEpisode>());
    }

    private sealed class LongRunningSeriesProvider
        : FakeProvider
    {
        private readonly MetadataSearchCandidate _candidate;
        private readonly int _episodeCount;

        public LongRunningSeriesProvider(
            string name,
            IReadOnlyList<MetadataSearchCandidate> candidates,
            int episodeCount)
            : base(name, candidates)
        {
            _candidate = candidates[0];
            _episodeCount = episodeCount;
        }

        public override Task<MetadataSubject?> GetSubjectAsync(
            MetadataProviderItemId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<MetadataSubject?>(
                new MetadataSubject(
                    id,
                    new MetadataTitles(
                        _candidate.Titles.Primary,
                        _candidate.Titles.Original,
                        _candidate.Titles.Localized,
                        _candidate.Titles.Aliases),
                    null,
                    _candidate.Year is { } year
                        ? new DateOnly(year, 1, 1)
                        : null,
                    _episodeCount,
                    new MetadataArtwork(null, null, null),
                    new Dictionary<string, string> { [id.Provider] = id.Value }));
    }

    private sealed record RelationNode(
        string Title,
        int Year,
        int EpisodeCount,
        string? Sequel,
        IReadOnlyList<string>? Sequels = null);

    private sealed class RelationChainProvider
        : FakeProvider, IMetadataRelationProvider
    {
        private readonly IReadOnlyDictionary<string, RelationNode> _nodes;

        public RelationChainProvider(
            string name,
            IReadOnlyList<MetadataSearchCandidate> candidates,
            IReadOnlyDictionary<string, RelationNode> nodes)
            : base(name, candidates)
        {
            _nodes = nodes;
        }

        public int RelationCalls { get; private set; }

        public override Task<MetadataSubject?> GetSubjectAsync(
            MetadataProviderItemId id,
            CancellationToken cancellationToken = default)
        {
            if (!_nodes.TryGetValue(id.Value, out var node))
            {
                return Task.FromResult<MetadataSubject?>(null);
            }

            return Task.FromResult<MetadataSubject?>(
                new MetadataSubject(
                    new MetadataProviderItemId(Name, id.Value, MetadataSubjectKind.Series),
                    new MetadataTitles(
                        node.Title,
                        node.Title,
                        new Dictionary<string, string>(),
                        Array.Empty<string>()),
                    null,
                    new DateOnly(node.Year, 1, 1),
                    node.EpisodeCount,
                    new MetadataArtwork(null, null, null),
                    new Dictionary<string, string> { [Name] = id.Value }));
        }

        public override Task<IReadOnlyList<MetadataEpisode>> GetEpisodesAsync(
            MetadataProviderItemId id,
            int? seasonNumber = null,
            CancellationToken cancellationToken = default)
        {
            if (!_nodes.TryGetValue(id.Value, out var node))
            {
                return Task.FromResult<IReadOnlyList<MetadataEpisode>>(
                    Array.Empty<MetadataEpisode>());
            }

            return Task.FromResult<IReadOnlyList<MetadataEpisode>>(
                Enumerable.Range(1, node.EpisodeCount)
                    .Select(number =>
                        new MetadataEpisode(
                            $"{id.Value}-ep-{number}",
                            new MetadataProviderItemId(
                                Name,
                                id.Value,
                                MetadataSubjectKind.Series),
                            1,
                            number,
                            MetadataEpisodeKind.Regular,
                            new MetadataTitles(
                                $"Episode {number}",
                                null,
                                new Dictionary<string, string>(),
                                Array.Empty<string>()),
                            null,
                            null,
                            null))
                    .ToArray());
        }

        public Task<IReadOnlyList<MetadataSubjectRelation>> GetRelatedSubjectsAsync(
            MetadataProviderItemId id,
            CancellationToken cancellationToken = default)
        {
            RelationCalls++;

            if (!_nodes.TryGetValue(id.Value, out var node))
            {
                return Task.FromResult<IReadOnlyList<MetadataSubjectRelation>>(
                    Array.Empty<MetadataSubjectRelation>());
            }

            IReadOnlyList<string> sequelIds = node.Sequels ??
                (node.Sequel is null
                    ? Array.Empty<string>()
                    : new[] { node.Sequel });

            return Task.FromResult<IReadOnlyList<MetadataSubjectRelation>>(
                sequelIds
                    .Select(sequelId =>
                    {
                        var sequel = _nodes[sequelId];
                        return new MetadataSubjectRelation(
                            new MetadataProviderItemId(
                                Name,
                                sequelId,
                                MetadataSubjectKind.Unknown),
                            "续集",
                            new MetadataTitles(
                                sequel.Title,
                                sequel.Title,
                                new Dictionary<string, string>(),
                                Array.Empty<string>()));
                    })
                    .ToArray());
        }
    }

    private sealed class EnrichmentProvider(
        string name,
        IReadOnlyList<MetadataSearchCandidate> candidates) : FakeProvider(name, candidates)
    {
        public override Task<MetadataSubject?> GetSubjectAsync(
            MetadataProviderItemId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<MetadataSubject?>(
                new MetadataSubject(
                    id,
                    new MetadataTitles(
                        "CLANNAD",
                        "CLANNAD",
                        new Dictionary<string, string>(),
                        Array.Empty<string>()),
                    "Drama",
                    new DateOnly(2007, 10, 4),
                    24,
                    new MetadataArtwork(null, null, null),
                    new Dictionary<string, string> { [id.Provider] = id.Value }));

        public override Task<IReadOnlyList<MetadataEpisode>> GetEpisodesAsync(
            MetadataProviderItemId id,
            int? seasonNumber = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MetadataEpisode>>(
            [
                new MetadataEpisode(
                    "ep-3",
                    id,
                    1,
                    3m,
                    MetadataEpisodeKind.Regular,
                    new MetadataTitles(
                        "涙のあとにもう一度",
                        null,
                        new Dictionary<string, string>(),
                        Array.Empty<string>()),
                    null,
                    null,
                    null),
            ]);
    }

    private sealed class CountingProvider(
        string name,
        IReadOnlyList<MetadataSearchCandidate> candidates) : FakeProvider(name, candidates)
    {
        public int SearchCount { get; private set; }

        public override Task<IReadOnlyList<MetadataSearchCandidate>> SearchAsync(
            MetadataSearchRequest request,
            CancellationToken cancellationToken = default)
        {
            SearchCount++;
            return base.SearchAsync(request, cancellationToken);
        }
    }

    private sealed class CapturingProvider(string name) : IMetadataProvider
    {
        public string Name { get; } = name;

        public IReadOnlyList<string>? LastTitles { get; private set; }

        public Task<IReadOnlyList<MetadataSearchCandidate>> SearchAsync(
            MetadataSearchRequest request,
            CancellationToken cancellationToken = default)
        {
            LastTitles = request.Titles.ToArray();
            return Task.FromResult<IReadOnlyList<MetadataSearchCandidate>>(
                Array.Empty<MetadataSearchCandidate>());
        }

        public Task<MetadataSubject?> GetSubjectAsync(
            MetadataProviderItemId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<MetadataSubject?>(null);

        public Task<IReadOnlyList<MetadataEpisode>> GetEpisodesAsync(
            MetadataProviderItemId id,
            int? seasonNumber = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MetadataEpisode>>(
                Array.Empty<MetadataEpisode>());
    }

    private sealed class ThrowingProvider(string name) : IMetadataProvider
    {
        public string Name { get; } = name;

        public Task<IReadOnlyList<MetadataSearchCandidate>> SearchAsync(
            MetadataSearchRequest request,
            CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("provider unavailable");

        public Task<MetadataSubject?> GetSubjectAsync(
            MetadataProviderItemId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<MetadataSubject?>(null);

        public Task<IReadOnlyList<MetadataEpisode>> GetEpisodesAsync(
            MetadataProviderItemId id,
            int? seasonNumber = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MetadataEpisode>>(Array.Empty<MetadataEpisode>());
    }
}
