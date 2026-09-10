namespace Eizo.Metadata.Recognition.Tests;

public sealed class Stage6GoldenCorpusTests
{
    [Fact]
    public void GoldenCorpus_HasAtLeastTwoThousandCases()
    {
        var cases = Stage6CorpusSupport.LoadGolden();

        Assert.True(cases.Count >= 2_000, $"Expected >= 2000 cases, found {cases.Count}.");
    }

    [Fact]
    public void GoldenCorpus_MatchesExpectedStructuredResults()
    {
        var engine = new RecognitionEngine();
        var cases = Stage6CorpusSupport.LoadGolden();
        var failures = new List<string>();
        var mismatchCount = 0;

        foreach (var item in cases)
        {
            var result = engine.Recognize(new RecognitionRequest(item.Path));

            if (result.Title != item.Title ||
                result.MediaKind != item.MediaKind ||
                result.SpecialKind != item.SpecialKind ||
                result.EpisodePart != item.EpisodePart ||
                result.IsFinalEpisode != item.IsFinalEpisode ||
                result.SeasonNumber != item.SeasonNumber ||
                result.CourNumber != item.CourNumber ||
                result.EpisodeNumber != item.EpisodeNumber ||
                result.EpisodeEndNumber != item.EpisodeEndNumber ||
                result.SpecialNumber != item.SpecialNumber ||
                result.Year != item.Year)
            {
                mismatchCount++;

                if (failures.Count < 20)
                {
                    failures.Add(
                        $"{item.Path} => title={result.Title}, kind={result.MediaKind}, " +
                        $"special={result.SpecialKind}, part={result.EpisodePart}, final={result.IsFinalEpisode}, " +
                        $"season={result.SeasonNumber}, cour={result.CourNumber}, ep={result.EpisodeNumber}, " +
                        $"end={result.EpisodeEndNumber}, specialNo={result.SpecialNumber}, year={result.Year}");
                }
            }
        }

        var coverage = (double)(cases.Count - mismatchCount) / cases.Count;
        Console.WriteLine(
            $"Stage6 golden extraction coverage: {cases.Count - mismatchCount}/{cases.Count} ({coverage:P3})");

        Assert.True(
            mismatchCount == 0,
            $"Stage 6 golden corpus mismatches: {mismatchCount}/{cases.Count}\n" +
            string.Join("\n", failures));
    }
}
