using Eizo.Metadata.Recognition.Internal;

namespace Eizo.Metadata.Recognition.Tests;

public sealed class Stage1CorpusSmokeTests
{
    private static readonly string[] Titles =
    {
        "星降る街",
        "青い春",
        "旅人の記録",
        "夜の図書館",
        "夏への扉",
        "白い航路",
        "月と珈琲",
        "風の向こう",
        "小さな宇宙",
        "雨上がり",
        "東京の空",
        "京都日和",
        "最後の手紙",
        "深夜食堂前",
        "春の約束",
        "家族の時間",
        "坂の上の家",
        "海辺の記憶",
        "昨日の未来",
        "静かな朝",
    };

    private static readonly string[] Patterns =
    {
        "[ANi] {0} - 01 [1080P][Baha][WEB-DL][AAC AVC][CHT].mp4",
        "[Lilith-Raws] {0} - 02 [Baha][WEB-DL][1080P][AVC AAC][CHT].mkv",
        "{0}.S01E03.1080p.WEB-DL.x265.AAC.mkv",
        "{0} 2021 第04話 1080p HDTV.mp4",
        "{0}/Season 01/{0} - 05 [HEVC 10bit][AAC].mkv",
        "{0}/第1シリーズ/{0} 第06話 [1080P][WEBRip].mp4",
    };

    public static TheoryData<string, string> Corpus
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var title in Titles)
            {
                foreach (var pattern in Patterns)
                {
                    data.Add(string.Format(pattern, title), title);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void Preprocess_FirstSanitizedCorpusIsStable(
        string path,
        string title)
    {
        var result = PathPreprocessor.Preprocess(path);

        Assert.NotEmpty(result.FileName);
        Assert.Contains(title, result.OriginalPath, StringComparison.Ordinal);
        Assert.Contains(title.Normalize(), result.NormalizedStem + string.Join("", result.NormalizedDirectorySegments), StringComparison.Ordinal);
        Assert.All(result.Tokens, token =>
        {
            Assert.True(token.Start >= 0);
            Assert.True(token.Length > 0);
            Assert.True(token.Start + token.Length <= result.Stem.Length);
        });
    }
}
