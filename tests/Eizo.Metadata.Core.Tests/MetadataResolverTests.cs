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
