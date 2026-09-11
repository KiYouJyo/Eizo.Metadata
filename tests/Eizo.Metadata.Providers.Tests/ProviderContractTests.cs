using System.Net;
using System.Text;
using Eizo.Metadata.Core;
using Eizo.Metadata.Providers;
using Eizo.Metadata.Recognition;

namespace Eizo.Metadata.Providers.Tests;

public sealed class ProviderContractTests
{
    [Fact]
    public async Task Bangumi_SearchUsesPublicV0ContractAndMapsCandidate()
    {
        var handler = new RecordingHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Contains("/v0/search/subjects", request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
            Assert.Contains("KiYouJyo/Eizo", request.Headers.UserAgent.ToString(), StringComparison.Ordinal);

            return Json("""
                {
                  "data": [
                    {
                      "id": 253,
                      "name": "攻殻機動隊 STAND ALONE COMPLEX",
                      "name_cn": "攻壳机动队 STAND ALONE COMPLEX",
                      "date": "2002-10-01",
                      "platform": "TV",
                      "rating": { "score": 8.8 }
                    }
                  ],
                  "total": 1,
                  "limit": 10,
                  "offset": 0
                }
                """);
        });

        var provider = new BangumiMetadataProvider(
            new HttpClient(handler),
            new BangumiMetadataProviderOptions(
                "KiYouJyo/Eizo/0.3.6 (https://github.com/KiYouJyo/Eizo)"));

        var result = await provider.SearchAsync(
            new MetadataSearchRequest(
                ["攻殻機動隊"],
                2002,
                MediaKind.SeriesEpisode,
                1,
                1,
                "zh-CN",
                10), TestContext.Current.CancellationToken);

        var candidate = Assert.Single(result);
        Assert.Equal("253", candidate.Id.Value);
        Assert.Equal(MetadataSubjectKind.Series, candidate.Id.Kind);
        Assert.Equal("攻壳机动队 STAND ALONE COMPLEX", candidate.Titles.Primary);
        Assert.Equal(2002, candidate.Year);
    }

    [Fact]
    public async Task Tmdb_SearchUsesBearerTokenAndMapsTvCandidate()
    {
        var handler = new RecordingHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("secret-token", request.Headers.Authorization?.Parameter);
            Assert.Contains("/3/search/tv", request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
            Assert.Contains("first_air_date_year=2011", request.RequestUri.Query, StringComparison.Ordinal);

            return Json("""
                {
                  "results": [
                    {
                      "id": 42509,
                      "name": "Steins;Gate",
                      "original_name": "STEINS;GATE",
                      "first_air_date": "2011-04-06",
                      "poster_path": "/poster.jpg",
                      "backdrop_path": "/backdrop.jpg",
                      "popularity": 42.0
                    }
                  ]
                }
                """);
        });

        var provider = new TmdbMetadataProvider(
            new HttpClient(handler),
            new TmdbMetadataProviderOptions("secret-token", "en-US"));

        var result = await provider.SearchAsync(
            new MetadataSearchRequest(
                ["Steins;Gate"],
                2011,
                MediaKind.SeriesEpisode,
                1,
                1,
                "en",
                10), TestContext.Current.CancellationToken);

        var candidate = Assert.Single(result);
        Assert.Equal("42509", candidate.Id.Value);
        Assert.Equal(MetadataSubjectKind.Series, candidate.Id.Kind);
        Assert.Equal(2011, candidate.Year);
    }

    [Fact]
    public async Task Tmdb_GetSubjectMapsExternalIdsAndArtwork()
    {
        var handler = new RecordingHandler(request =>
        {
            Assert.Contains("/3/movie/129", request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
            Assert.Contains("append_to_response=external_ids", request.RequestUri.Query, StringComparison.Ordinal);

            return Json("""
                {
                  "id": 129,
                  "title": "千与千寻",
                  "original_title": "千と千尋の神隠し",
                  "overview": "A story.",
                  "release_date": "2001-07-20",
                  "poster_path": "/poster.jpg",
                  "backdrop_path": "/backdrop.jpg",
                  "external_ids": {
                    "imdb_id": "tt0245429"
                  }
                }
                """);
        });

        var provider = new TmdbMetadataProvider(
            new HttpClient(handler),
            new TmdbMetadataProviderOptions("secret-token", "zh-CN"));

        var subject = await provider.GetSubjectAsync(
            new MetadataProviderItemId("tmdb", "129", MetadataSubjectKind.Movie),
            TestContext.Current.CancellationToken);

        Assert.NotNull(subject);
        Assert.Equal("千与千寻", subject.Titles.Primary);
        Assert.Equal("tt0245429", subject.ExternalIds["imdb"]);
        Assert.Contains("/poster.jpg", subject.Artwork.PosterUrl, StringComparison.Ordinal);
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
