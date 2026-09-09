namespace Eizo.Metadata.Recognition.Tests;

public sealed class Stage3TitleCorpusTests
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
        "{0}/Season 01/05.mkv",
        "{0}/第1シリーズ/06.mkv",
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
    public void Recognize_ExtractsTitleAcrossSanitizedCorpus(
        string path,
        string expectedTitle)
    {
        var result = new RecognitionEngine().Recognize(
            new RecognitionRequest(path));

        Assert.Equal(expectedTitle, result.Title);
        Assert.NotEmpty(result.Evidence);
    }
}
