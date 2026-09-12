using Eizo.Metadata.Core;
using Eizo.Metadata.Recognition;

namespace Eizo.Metadata.Core.Tests;

public sealed class MetadataSearchClosureTests
{
    [Fact]
    public async Task Resolver_SplitsParenthesizedBilingualLibraryTitle()
    {
        var provider = new CapturingProvider();
        var resolver = new MetadataResolver([provider]);

        _ = await resolver.ResolveAsync(
            new MetadataSearchRequest(
                ["龙樱 (Dragon Sakura) {tmdbid-31816}"],
                2021,
                MediaKind.SeriesEpisode,
                2,
                1,
                "zh-CN",
                10),
            TestContext.Current.CancellationToken);

        Assert.Contains("龙樱", provider.LastTitles, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Dragon Sakura", provider.LastTitles, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Resolver_AddsPunctuationFoldedProviderQuery()
    {
        var provider = new CapturingProvider();
        var resolver = new MetadataResolver([provider]);

        _ = await resolver.ResolveAsync(
            new MetadataSearchRequest(
                ["LoveLive! Sunshine!!"],
                2016,
                MediaKind.SeriesEpisode,
                1,
                1,
                "ja",
                10),
            TestContext.Current.CancellationToken);

        Assert.Contains("LoveLive Sunshine", provider.LastTitles, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Resolver_AddsYearPinnedBaseTitleForLibraryPartFolders()
    {
        var provider = new CapturingProvider();
        var resolver = new MetadataResolver([provider]);

        _ = await resolver.ResolveAsync(
            new MetadataSearchRequest(
                ["04. 鲁邦三世part4"],
                2015,
                MediaKind.SeriesEpisode,
                4,
                1,
                "zh-CN",
                10),
            TestContext.Current.CancellationToken);

        Assert.Contains("鲁邦三世 2015", provider.LastTitles, StringComparer.OrdinalIgnoreCase);
    }

    private sealed class CapturingProvider : IMetadataProvider
    {
        public string Name => "capture";

        public IReadOnlyList<string> LastTitles { get; private set; } = Array.Empty<string>();

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
}
