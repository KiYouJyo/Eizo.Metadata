namespace Eizo.Metadata.Recognition.Tests;

public sealed class Stage6FalsePositiveTests
{
    [Fact]
    public void NegativeCorpus_TracksStructuralFalsePositiveRateSeparately()
    {
        var engine = new RecognitionEngine();
        var cases = Stage6CorpusSupport.LoadNegative();
        var falsePositives = new List<string>();
        var falsePositiveCount = 0;

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
                falsePositiveCount++;

                if (falsePositives.Count < 20)
                {
                    falsePositives.Add(
                        $"{item.Path} => kind={result.MediaKind}, special={result.SpecialKind}, " +
                        $"episode={result.EpisodeNumber}, specialNo={result.SpecialNumber}");
                }
            }
        }

        var falsePositiveRate = (double)falsePositiveCount / cases.Count;
        Console.WriteLine(
            $"Stage6 structural false-positive baseline: {falsePositiveCount}/{cases.Count} " +
            $"({falsePositiveRate:P3})");

        Assert.True(cases.Count >= 400);
        Assert.True(
            falsePositiveCount == 0,
            $"Structural false positives: {falsePositiveCount}/{cases.Count}\n" +
            string.Join("\n", falsePositives));
    }
}
