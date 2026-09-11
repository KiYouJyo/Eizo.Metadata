using System.Reflection;
using Eizo.Metadata.Providers;

namespace Eizo.Metadata.Providers.Tests;

public sealed class ProviderAbiCompatibilityTests
{
    [Fact]
    public void BangumiOptions_PreservesLegacyThreeArgumentConstructor()
    {
        var signature = new[]
        {
            typeof(string),
            typeof(string),
            typeof(string),
        };

        var constructor = typeof(BangumiMetadataProviderOptions).GetConstructor(signature);

        Assert.NotNull(constructor);

        var options = Assert.IsType<BangumiMetadataProviderOptions>(
            constructor.Invoke(
            [
                "KiYouJyo/Eizo/0.3.6",
                null,
                "https://api.bgm.tv/",
            ]));

        Assert.Equal("KiYouJyo/Eizo/0.3.6", options.UserAgent);
        Assert.Null(options.AccessToken);
        Assert.Equal("https://api.bgm.tv/", options.BaseAddress);
        Assert.Equal(5, options.SearchAliasEnrichmentLimit);
    }

    [Fact]
    public void ProvidersAssembly_KeepsStableHostFacingAssemblyVersion()
    {
        var version = typeof(BangumiMetadataProviderOptions)
            .Assembly
            .GetName()
            .Version;

        Assert.Equal(new Version(0, 1, 0, 0), version);
    }
}
