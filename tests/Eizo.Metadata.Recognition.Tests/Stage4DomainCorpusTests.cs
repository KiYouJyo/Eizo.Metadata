namespace Eizo.Metadata.Recognition.Tests;

public sealed class Stage4DomainCorpusTests
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
        "86 Eighty Six",
        "3年A組",
        "SPY x FAMILY",
        "Special Ops",
        "Movie Night",
    };

    private sealed record Pattern(
        string Template,
        MediaKind Kind,
        SpecialKind SpecialKind,
        EpisodePart Part,
        bool Final,
        decimal? Episode,
        decimal? SpecialNumber,
        int? Cour);

    private static readonly Pattern[] Patterns =
    {
        new("{0} - OVA 01 [1080P].mkv", MediaKind.Special, SpecialKind.Ova, EpisodePart.None, false, null, 1m, null),
        new("{0} - OAD 02 [1080P].mkv", MediaKind.Special, SpecialKind.Oad, EpisodePart.None, false, null, 2m, null),
        new("{0} - ONA 03 [1080P].mkv", MediaKind.Special, SpecialKind.Ona, EpisodePart.None, false, null, 3m, null),
        new("{0} - SP 01 [1080P].mkv", MediaKind.Special, SpecialKind.Special, EpisodePart.None, false, null, 1m, null),
        new("{0} - Special 02 [1080P].mkv", MediaKind.Special, SpecialKind.Special, EpisodePart.None, false, null, 2m, null),
        new("{0}/OVA/01.mkv", MediaKind.Special, SpecialKind.Ova, EpisodePart.None, false, null, 1m, null),
        new("{0}/Specials/01.mkv", MediaKind.Special, SpecialKind.Special, EpisodePart.None, false, null, 1m, null),
        new("{0}/NCOP1.mkv", MediaKind.Special, SpecialKind.NcOp, EpisodePart.None, false, null, 1m, null),
        new("{0}/NCED1.mkv", MediaKind.Special, SpecialKind.NcEd, EpisodePart.None, false, null, 1m, null),
        new("劇場版 {0} [1080P].mkv", MediaKind.Movie, SpecialKind.None, EpisodePart.None, false, null, null, null),
        new("{0} - Movie [1080P].mkv", MediaKind.Movie, SpecialKind.None, EpisodePart.None, false, null, null, null),
        new("{0} 第01回.mp4", MediaKind.SeriesEpisode, SpecialKind.None, EpisodePart.None, false, 1m, null, null),
        new("{0} 最終話.mp4", MediaKind.SeriesEpisode, SpecialKind.None, EpisodePart.None, true, null, null, null),
        new("{0} 第10話 最終話.mp4", MediaKind.SeriesEpisode, SpecialKind.None, EpisodePart.None, true, 10m, null, null),
        new("{0} 前編.mp4", MediaKind.SeriesEpisode, SpecialKind.None, EpisodePart.First, false, null, null, null),
        new("{0} 後編.mp4", MediaKind.SeriesEpisode, SpecialKind.None, EpisodePart.Second, false, null, null, null),
        new("{0} スペシャル.mp4", MediaKind.Special, SpecialKind.Special, EpisodePart.None, false, null, null, null),
        new("{0}/Cour 2/03.mkv", MediaKind.SeriesEpisode, SpecialKind.None, EpisodePart.None, false, 3m, null, 2),
        new("[ANi] {0} - 14 [1080P].mkv", MediaKind.SeriesEpisode, SpecialKind.None, EpisodePart.None, false, 14m, null, null),
        new("{0} 第03話 「朝の光」.mp4", MediaKind.SeriesEpisode, SpecialKind.None, EpisodePart.None, false, 3m, null, null),
    };

    public static TheoryData<string, string, MediaKind, SpecialKind, EpisodePart, bool, decimal?, decimal?, int?> Corpus
    {
        get
        {
            var data = new TheoryData<string, string, MediaKind, SpecialKind, EpisodePart, bool, decimal?, decimal?, int?>();
            foreach (var title in Titles)
            {
                foreach (var pattern in Patterns)
                {
                    data.Add(
                        string.Format(pattern.Template, title),
                        title,
                        pattern.Kind,
                        pattern.SpecialKind,
                        pattern.Part,
                        pattern.Final,
                        pattern.Episode,
                        pattern.SpecialNumber,
                        pattern.Cour);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void Recognize_Stage4SanitizedGoldenCorpus(
        string path,
        string expectedTitle,
        MediaKind expectedKind,
        SpecialKind expectedSpecialKind,
        EpisodePart expectedPart,
        bool expectedFinal,
        decimal? expectedEpisode,
        decimal? expectedSpecialNumber,
        int? expectedCour)
    {
        var result = new RecognitionEngine().Recognize(
            new RecognitionRequest(path));

        Assert.Equal(expectedTitle, result.Title);
        Assert.Equal(expectedKind, result.MediaKind);
        Assert.Equal(expectedSpecialKind, result.SpecialKind);
        Assert.Equal(expectedPart, result.EpisodePart);
        Assert.Equal(expectedFinal, result.IsFinalEpisode);
        Assert.Equal(expectedEpisode, result.EpisodeNumber);
        Assert.Equal(expectedSpecialNumber, result.SpecialNumber);
        Assert.Equal(expectedCour, result.CourNumber);
    }
}
