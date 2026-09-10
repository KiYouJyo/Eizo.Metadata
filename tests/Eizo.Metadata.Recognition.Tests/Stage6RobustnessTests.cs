using System.Text;

namespace Eizo.Metadata.Recognition.Tests;

public sealed class Stage6RobustnessTests
{
    private readonly RecognitionEngine _engine = new();

    [Fact]
    public void UnicodeAndSeparatorCorpus_IsDeterministicAndDoesNotThrow()
    {
        var titles = new[]
        {
            "葬送のフリーレン",
            "３年Ａ組",
            "Cafe\u0301 Society",
            "😀 星の記録",
            "Доброе утро",
            "مرحبا بالعالم",
            "नया सफर",
            "한밤의 기록",
            "ＡＢＣ１２３",
            "東京\u200D物語",
        };

        for (var i = 0; i < 250; i++)
        {
            var title = titles[i % titles.Length];
            var separators = i % 4 switch
            {
                0 => " - ",
                1 => " — ",
                2 => "   -   ",
                _ => "\t-\t",
            };

            var path = $"Library/{title}/{title}{separators}{(i % 24) + 1:D2} [1080P].mkv";
            var first = _engine.Recognize(new RecognitionRequest(path));
            var second = _engine.Recognize(new RecognitionRequest(path));

            Assert.Equal(first.MediaKind, second.MediaKind);
            Assert.Equal(first.Title, second.Title);
            Assert.Equal(first.EpisodeNumber, second.EpisodeNumber);
            Assert.Equal(first.Confidence, second.Confidence);
            Assert.Equal(first.IsAmbiguous, second.IsAmbiguous);
        }
    }

    [Fact]
    public void AdversarialButValidUnicodeInputs_DoNotThrow()
    {
        var cases = new[]
        {
            new string('[', 20_000) + " Title - 01.mkv",
            new string(']', 20_000) + " Title - 01.mkv",
            new string('-', 20_000) + " Title - 01.mkv",
            new string('.', 20_000) + " Title.S01E01.mkv",
            string.Concat(Enumerable.Repeat("[1080P]", 1_000)) + " Title - 01.mkv",
            string.Concat(Enumerable.Repeat("第", 10_000)) + " Title.mkv",
            string.Concat(Enumerable.Repeat("S", 20_000)) + "E01.mkv",
            string.Concat(Enumerable.Repeat("（", 10_000)) + "タイトル.mkv",
            "【[[{「『 Title - 01 】)]}」』.mkv",
            "😀😀😀/Season 01/01.mkv",
        };

        foreach (var input in cases)
        {
            var exception = Record.Exception(() =>
                _engine.Recognize(new RecognitionRequest(input)));

            Assert.Null(exception);
        }
    }

    [Fact]
    public void LongCombiningSequence_DoesNotThrow()
    {
        var builder = new StringBuilder("Cafe");
        for (var i = 0; i < 5_000; i++)
        {
            builder.Append('\u0301');
        }

        builder.Append(" - 01.mkv");

        var exception = Record.Exception(() =>
            _engine.Recognize(new RecognitionRequest(builder.ToString())));

        Assert.Null(exception);
    }
}
