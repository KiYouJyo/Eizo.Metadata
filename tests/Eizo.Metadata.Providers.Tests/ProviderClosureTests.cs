using System.Net;
using System.Text;
using Eizo.Metadata.Core;
using Eizo.Metadata.Providers;
using Eizo.Metadata.Recognition;

namespace Eizo.Metadata.Providers.Tests;

public sealed class ProviderClosureTests
{
    [Fact]
    public async Task Bangumi_BridgesEnglishArcTranslationUsingStableLatinAnchors()
    {
        var handler = new RecordingHandler(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                return Json("""
                    {
                      "data": [
                        {
                          "id": 255526,
                          "name": "Fate/Grand Order -絶対魔獣戦線バビロニア-",
                          "name_cn": "命运-冠位指定 绝对魔兽战线 巴比伦尼亚",
                          "date": "2019-10-05",
                          "platform": "TV"
                        }
                      ],
                      "total": 1,
                      "limit": 50,
                      "offset": 0
                    }
                    """);
            }

            return Json("""
                {
                  "id": 255526,
                  "name": "Fate/Grand Order -絶対魔獣戦線バビロニア-",
                  "name_cn": "命运-冠位指定 绝对魔兽战线 巴比伦尼亚",
                  "date": "2019-10-05",
                  "platform": "TV",
                  "eps": 21,
                  "infobox": [
                    {
                      "key": "别名",
                      "value": [
                        { "k": "罗马字", "v": "Fate/Grand Order: Zettai Majuu Sensen Babylonia" }
                      ]
                    }
                  ]
                }
                """);
        });

        var provider = new BangumiMetadataProvider(
            new HttpClient(handler),
            new BangumiMetadataProviderOptions(
                "KiYouJyo/Eizo/0.3.9 (https://github.com/KiYouJyo/Eizo)",
                SearchAliasEnrichmentLimit: 1));
        var resolver = new MetadataResolver([provider]);

        var result = await resolver.ResolveAsync(
            new MetadataSearchRequest(
                ["Fate.Grand Order Absolute Demonic Front. Babylonia"],
                2019,
                MediaKind.SeriesEpisode,
                1,
                1,
                "en",
                10),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsResolved);
        Assert.Equal("255526", result.Best!.Candidate.Id.Value);
        Assert.Contains(
            "Fate.Grand Order Absolute Demonic Front. Babylonia",
            result.Best.Candidate.Titles.Aliases,
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Bangumi_BridgesCjkTitleWithInsertedProviderDescriptors()
    {
        var handler = new RecordingHandler(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                return Json("""
                    {
                      "data": [
                        {
                          "id": 401960,
                          "name": "にじよん あにめーしょん",
                          "name_cn": "虹四格 动画版",
                          "date": "2023-01-06",
                          "platform": "TV"
                        }
                      ],
                      "total": 1,
                      "limit": 50,
                      "offset": 0
                    }
                    """);
            }

            return Json("""
                {
                  "id": 401960,
                  "name": "にじよん あにめーしょん",
                  "name_cn": "虹四格 动画版",
                  "date": "2023-01-06",
                  "platform": "TV",
                  "eps": 12,
                  "infobox": [
                    {
                      "key": "别名",
                      "value": [
                        { "k": "中文", "v": "虹咲学园偶像同好会官方四格漫画 动画版" }
                      ]
                    }
                  ]
                }
                """);
        });

        var provider = new BangumiMetadataProvider(
            new HttpClient(handler),
            new BangumiMetadataProviderOptions(
                "KiYouJyo/Eizo/0.3.9 (https://github.com/KiYouJyo/Eizo)",
                SearchAliasEnrichmentLimit: 1));
        var resolver = new MetadataResolver([provider]);

        var result = await resolver.ResolveAsync(
            new MetadataSearchRequest(
                ["虹咲偶像同好会四格动画"],
                2023,
                MediaKind.SeriesEpisode,
                1,
                1,
                "zh-CN",
                10),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsResolved);
        Assert.Equal("401960", result.Best!.Candidate.Id.Value);
        Assert.Contains(
            "虹咲偶像同好会四格动画",
            result.Best.Candidate.Titles.Aliases,
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Bangumi_DoesNotBridgeBroadcastDerivativeAsCanonicalSeries()
    {
        var handler = new RecordingHandler(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                return Json("""
                    {
                      "data": [
                        {
                          "id": 198872,
                          "name": "Re：ゼロから始める異世界ラジオ生活",
                          "name_cn": "Re：从零开始的异世界广播生活",
                          "date": "2016-03-28",
                          "platform": "WEB"
                        }
                      ],
                      "total": 1,
                      "limit": 50,
                      "offset": 0
                    }
                    """);
            }

            return Json("""
                {
                  "id": 198872,
                  "name": "Re：ゼロから始める異世界ラジオ生活",
                  "name_cn": "Re：从零开始的异世界广播生活",
                  "date": "2016-03-28",
                  "platform": "WEB",
                  "eps": 33
                }
                """);
        });

        var provider = new BangumiMetadataProvider(
            new HttpClient(handler),
            new BangumiMetadataProviderOptions(
                "KiYouJyo/Eizo/0.3.9 (https://github.com/KiYouJyo/Eizo)",
                SearchAliasEnrichmentLimit: 1));

        var results = await provider.SearchAsync(
            new MetadataSearchRequest(
                ["Re：从零开始的异世界生活"],
                2016,
                MediaKind.SeriesEpisode,
                1,
                1,
                "zh-CN",
                10),
            TestContext.Current.CancellationToken);

        var candidate = Assert.Single(results);
        Assert.DoesNotContain(
            "Re：从零开始的异世界生活",
            candidate.Titles.Aliases,
            StringComparer.OrdinalIgnoreCase);
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
