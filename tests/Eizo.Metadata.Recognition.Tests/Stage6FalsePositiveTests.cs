namespace Eizo.Metadata.Recognition.Tests;

public sealed class Stage6FalsePositiveTests
{
    [Fact]
    public void NegativeCorpus_TracksStructuralFalsePositiveRateSeparately()
    {
        var engine = new RecognitionEngine();
        var cases = Stage6CorpusSupport.LoadNegative();
        var falsePositives = new List<string>();

        foreach (var item in cases)
        {
            var result = engine.Recognize(new RecognitionRequest(item.Path));
            var structuralFalsePositive =
                result.MediaKind != MediaKind.Unknown ||
                result.SpecialKind != SpecialKind.None ||
                result.EpisodeNumber is not null ||
                result.EpisodeEndNumber is not null ||
                result.SpecialNumber is not null ||
                result.EpisodePart != EpisodePart.None ||
                result.IsFinalEpisode;

            if (structuralFalsePositive)
            {
                falsePositives.Add(
                    $"{item.Path} => kind={result.MediaKind}, special={result.SpecialKind}, " +
                    $"episode={result.EpisodeNumber}, specialNo={result.SpecialNumber}");

                if (falsePositives.Count >= 20)
                {
                    break;
                }
            }
        }

        var falsePositiveRate = (double)falsePositives.Count / cases.Count;
        Console.WriteLine(
            $"Stage6 structural false-positive baseline: {falsePositives.Count}/{cases.Count} " +
            $"({falsePositiveRate:P3})");

        Assert.True(cases.Count >= 400);
        Assert.True(
            falsePositives.Count == 0,
            "Structural false positives:\n" + string.Join("\n", falsePositives));
    }
}
