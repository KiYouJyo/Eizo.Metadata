using Eizo.Metadata.Recognition.ShadowCompare;

namespace Eizo.Metadata.Recognition.Shadow.Tests;

public sealed class ShadowComparisonSmokeTests
{
    [Fact]
    public void CompareSingle_LoadsAnitomyNativeAndReturnsStructuredSignals()
    {
        var engine = new ShadowComparisonEngine();

        var result = engine.CompareSingle(
            "[DBD-Raws][死亡笔记][34][1080P][BDRip][HEVC-10bit][FLAC].mkv");

        Assert.NotNull(result.Eizo);
        Assert.False(string.IsNullOrWhiteSpace(result.Anitomy.Title));
        Assert.NotEmpty(result.Anitomy.Elements);
    }

    [Fact]
    public void CompareTogether_PreservesInputCardinality()
    {
        var engine = new ShadowComparisonEngine();
        var paths = new[]
        {
            "Example Show/Example Show - 01 [1080p].mkv",
            "Example Show/Example Show - 02 [1080p].mkv",
        };

        var results = engine.CompareTogether(paths);

        Assert.Equal(paths.Length, results.Count);
        Assert.Equal(paths[0], results[0].Path);
        Assert.Equal(paths[1], results[1].Path);
        Assert.All(
            results,
            static result => Assert.NotEmpty(result.Anitomy.Elements));
    }
}
