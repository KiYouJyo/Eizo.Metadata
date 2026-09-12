using Eizo.Metadata.Recognition;

namespace Eizo.Metadata.Core.Tests;

public sealed class MetadataDiagnosticsTests
{
    [Fact]
    public async Task ResolveAsync_NoCandidates_ReportsSearchFailure()
    {
        var resolver = new MetadataResolver([new DiagnosticProvider()]);
        var result = await resolver.ResolveAsync(
            Request("Missing Show"),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsResolved);
        Assert.True(result.NeedsReview);
        Assert.Equal(MetadataFailureStage.Search, result.FailureStage);
        Assert.Equal(MetadataFailureReason.NoCandidates, result.FailureReason);
        Assert.Equal(0.0, result.Lead);
    }

    [Fact]
    public async Task ResolveAsync_TiedCandidates_ReportInsufficientLead()
    {
        var resolver = new MetadataResolver(
        [
            new DiagnosticProvider(
            [
                Candidate("1", "Example", 2024, MetadataSubjectKind.Series, 0),
                Candidate("2", "Example", 2024, MetadataSubjectKind.Series, 1),
            ]),
        ]);

        var result = await resolver.ResolveAsync(
            Request("Example", year: 2024),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsResolved);
        Assert.True(result.NeedsReview);
        Assert.Equal(MetadataFailureStage.CandidateRanking, result.FailureStage);
        Assert.Equal(MetadataFailureReason.InsufficientLead, result.FailureReason);
        Assert.InRange(result.Lead, 0.0, 0.06);
    }

    [Fact]
    public async Task ResolveAsync_LaterSeasonWithoutStructuralEvidence_ReportsInstallmentMapping()
    {
        var resolver = new MetadataResolver(
        [
            new DiagnosticProvider(
            [
                Candidate("1", "Example", 2024, MetadataSubjectKind.Series, 0),
            ]),
        ]);

        var result = await resolver.ResolveAsync(
            Request("Example", year: 2024, season: 2),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsResolved);
        Assert.True(result.NeedsReview);
        Assert.Equal(MetadataFailureStage.InstallmentMapping, result.FailureStage);
        Assert.Equal(
            MetadataFailureReason.StructuralConfirmationRequired,
            result.FailureReason);
        Assert.Equal(1.0, result.Lead);
    }

    [Fact]
    public async Task EnrichAsync_MissingEpisode_ReportsEpisodeMappingFailure()
    {
        var id = new MetadataProviderItemId(
            "fake",
            "1",
            MetadataSubjectKind.Series);
        var subject = new MetadataSubject(
            id,
            Titles("Example"),
            Overview: null,
            ReleaseDate: new DateOnly(2024, 1, 1),
            EpisodeCount: 12,
            Artwork: new MetadataArtwork(null, null, null),
            ExternalIds: new Dictionary<string, string>());

        var resolver = new MetadataResolver(
        [
            new DiagnosticProvider(
                [Candidate("1", "Example", 2024, MetadataSubjectKind.Series, 0)],
                subject,
                Array.Empty<MetadataEpisode>()),
        ]);

        var result = await resolver.EnrichAsync(
            Request("Example", year: 2024, season: 1, episode: 3),
            TestContext.Current.CancellationToken);

        Assert.True(result.Resolution.IsResolved);
        Assert.True(result.NeedsReview);
        Assert.NotNull(result.Subject);
        Assert.Null(result.Episode);
        Assert.Equal(MetadataFailureStage.EpisodeMapping, result.FailureStage);
        Assert.Equal(MetadataFailureReason.EpisodeNotFound, result.FailureReason);
    }

    [Fact]
    public async Task EnrichAsync_ResolvedMovieWithoutEpisode_DoesNotNeedReview()
    {
        var id = new MetadataProviderItemId(
            "fake",
            "movie-1",
            MetadataSubjectKind.Movie);
        var subject = new MetadataSubject(
            id,
            Titles("Example Movie"),
            Overview: null,
            ReleaseDate: new DateOnly(2024, 1, 1),
            EpisodeCount: null,
            Artwork: new MetadataArtwork(null, null, null),
            ExternalIds: new Dictionary<string, string>());

        var resolver = new MetadataResolver(
        [
            new DiagnosticProvider(
                [Candidate("movie-1", "Example Movie", 2024, MetadataSubjectKind.Movie, 0)],
                subject,
                Array.Empty<MetadataEpisode>()),
        ]);

        var result = await resolver.EnrichAsync(
            new MetadataSearchRequest(
                ["Example Movie"],
                2024,
                MediaKind.Movie,
                SeasonNumber: null,
                EpisodeNumber: null,
                PreferredLanguage: "ja",
                Limit: 10),
            TestContext.Current.CancellationToken);

        Assert.True(result.Resolution.IsResolved);
        Assert.False(result.NeedsReview);
        Assert.Equal(MetadataFailureStage.None, result.FailureStage);
        Assert.Equal(MetadataFailureReason.None, result.FailureReason);
    }

    private static MetadataSearchRequest Request(
        string title,
        int? year = null,
        int? season = 1,
        decimal? episode = 1) =>
        new(
            [title],
            year,
            MediaKind.SeriesEpisode,
            season,
            episode,
            "ja",
            10);

    private static MetadataSearchCandidate Candidate(
        string id,
        string title,
        int year,
        MetadataSubjectKind kind,
        int rank) =>
        new(
            new MetadataProviderItemId("fake", id, kind),
            Titles(title),
            year,
            rank);

    private static MetadataTitles Titles(string title) =>
        new(
            title,
            title,
            new Dictionary<string, string>(),
            Array.Empty<string>());

    private sealed class DiagnosticProvider(
        IReadOnlyList<MetadataSearchCandidate>? candidates = null,
        MetadataSubject? subject = null,
        IReadOnlyList<MetadataEpisode>? episodes = null) : IMetadataProvider
    {
        public string Name => "fake";

        public Task<IReadOnlyList<MetadataSearchCandidate>> SearchAsync(
            MetadataSearchRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                candidates ?? (IReadOnlyList<MetadataSearchCandidate>)Array.Empty<MetadataSearchCandidate>());

        public Task<MetadataSubject?> GetSubjectAsync(
            MetadataProviderItemId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(subject);

        public Task<IReadOnlyList<MetadataEpisode>> GetEpisodesAsync(
            MetadataProviderItemId id,
            int? seasonNumber = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                episodes ?? (IReadOnlyList<MetadataEpisode>)Array.Empty<MetadataEpisode>());
    }
}
