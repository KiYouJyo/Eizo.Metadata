namespace Eizo.Metadata.Recognition.Tests;

public sealed class Stage6DeterministicFuzzTests
{
    [Fact]
    public void OneThousandSeededUnicodePaths_DoNotThrowAndRemainDeterministic()
    {
        var engine = new RecognitionEngine();
        var atoms = new[]
        {
            "A", "b", "7", " ", ".", "_", "-", "—", "[", "]", "(", ")", "【", "】",
            "東", "京", "話", "回", "期", "前", "後", "終", "ア", "メ", "한", "글",
            "Д", "я", "م", "ر", "ह", "ि", "😀", "\u0301", "\u200D",
            "1080p", "x265", "AAC", "S01E03", "OVA", "WEB-DL",
        };

        uint state = 0xE1202026u;

        for (var caseIndex = 0; caseIndex < 1_000; caseIndex++)
        {
            var atomCount = 8 + (int)(Next(ref state) % 48);
            var builder = new System.Text.StringBuilder();

            for (var i = 0; i < atomCount; i++)
            {
                builder.Append(atoms[Next(ref state) % (uint)atoms.Length]);
            }

            builder.Append((caseIndex % 3) switch
            {
                0 => ".mkv",
                1 => ".mp4",
                _ => ".webm",
            });

            var path = builder.ToString();

            var firstException = Record.Exception(() =>
                engine.Recognize(new RecognitionRequest(path)));
            Assert.Null(firstException);

            var first = engine.Recognize(new RecognitionRequest(path));
            var second = engine.Recognize(new RecognitionRequest(path));

            Assert.Equal(first.MediaKind, second.MediaKind);
            Assert.Equal(first.SpecialKind, second.SpecialKind);
            Assert.Equal(first.EpisodePart, second.EpisodePart);
            Assert.Equal(first.IsFinalEpisode, second.IsFinalEpisode);
            Assert.Equal(first.Title, second.Title);
            Assert.Equal(first.SeasonNumber, second.SeasonNumber);
            Assert.Equal(first.CourNumber, second.CourNumber);
            Assert.Equal(first.EpisodeNumber, second.EpisodeNumber);
            Assert.Equal(first.EpisodeEndNumber, second.EpisodeEndNumber);
            Assert.Equal(first.SpecialNumber, second.SpecialNumber);
            Assert.Equal(first.Year, second.Year);
            Assert.Equal(first.Confidence, second.Confidence);
            Assert.Equal(first.ConfidenceLevel, second.ConfidenceLevel);
            Assert.Equal(first.IsAmbiguous, second.IsAmbiguous);
        }
    }

    private static uint Next(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return state;
    }
}
