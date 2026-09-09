using Eizo.Metadata.Recognition.Internal;

namespace Eizo.Metadata.Recognition.Tests;

public sealed class PathPreprocessorTests
{
    [Fact]
    public void Preprocess_SplitsWindowsAndUnixSeparatorsWithoutDiskAccess()
    {
        var result = PathPreprocessor.Preprocess(
            @"Z:\does-not-exist\Anime/葬送のフリーレン\Season 01/[ANi] 01.mkv");

        Assert.Equal("[ANi] 01.mkv", result.FileName);
        Assert.Equal("[ANi] 01", result.Stem);
        Assert.Equal(".mkv", result.Extension);
        Assert.Contains("葬送のフリーレン", result.DirectorySegments);
    }

    [Theory]
    [InlineData("episode.MKV", "episode", ".mkv")]
    [InlineData("episode.mp4", "episode", ".mp4")]
    [InlineData("episode.M2TS", "episode", ".m2ts")]
    [InlineData("episode.webm", "episode", ".webm")]
    public void Preprocess_StripsKnownMediaExtensions(
        string fileName,
        string expectedStem,
        string expectedExtension)
    {
        var result = PathPreprocessor.Preprocess(fileName);

        Assert.Equal(expectedStem, result.Stem);
        Assert.Equal(expectedExtension, result.Extension);
    }

    [Fact]
    public void Preprocess_DoesNotStripUnknownSuffix()
    {
        var result = PathPreprocessor.Preprocess("show.part1");

        Assert.Equal("show.part1", result.Stem);
        Assert.Null(result.Extension);
    }

    [Fact]
    public void Preprocess_NormalizesCompatibilityCharactersButPreservesCjkText()
    {
        var result = PathPreprocessor.Preprocess("ＴＥＳＴ 葬送のフリーレン ０１.mkv");

        Assert.Equal("TEST 葬送のフリーレン 01", result.NormalizedStem);
        Assert.Contains("葬送のフリーレン", result.NormalizedStem, StringComparison.Ordinal);
    }

    [Fact]
    public void Preprocess_ExtractsBalancedBracketGroupsWithOriginalSpan()
    {
        const string input = "[ANi] 葬送のフリーレン [1080P][AAC AVC].mkv";

        var result = PathPreprocessor.Preprocess(input);
        var bracketTokens = result.Tokens.Where(static token => token.IsBracketed).ToArray();

        Assert.Equal(3, bracketTokens.Length);
        foreach (var token in bracketTokens)
        {
            Assert.Equal(
                result.Stem.Substring(token.Start, token.Length),
                token.RawValue);
        }
    }

    [Fact]
    public void Preprocess_MalformedBracketDoesNotThrowOrConsumeRemainder()
    {
        var result = PathPreprocessor.Preprocess("[Fansub 葬送のフリーレン - 01.mkv");

        Assert.Equal("[Fansub 葬送のフリーレン - 01", result.Stem);
        Assert.NotEmpty(result.Tokens);
    }

    [Fact]
    public void Preprocess_RepresentativeAnimeNameClassifiesTechnicalNoise()
    {
        var result = PathPreprocessor.Preprocess(
            "[ANi] 葬送のフリーレン - 14 [1080P][Baha][WEB-DL][AAC AVC][CHT].mp4");

        Assert.Contains(result.Tokens, static token => token.Kind == TokenKind.ReleaseGroup);
        Assert.Contains(result.Tokens, static token => token.Kind == TokenKind.Resolution);
        Assert.Contains(result.Tokens, static token => token.Kind == TokenKind.Source);
        Assert.Contains(result.Tokens, static token => token.Kind == TokenKind.TechnicalGroup);
        Assert.Contains(result.Tokens, static token => token.Kind == TokenKind.Language);
        Assert.Contains(result.Tokens, static token =>
            token.Kind == TokenKind.Number && token.NormalizedValue == "14");
    }

    [Fact]
    public void Preprocess_RepresentativeDramaNameSeparatesEpisodeSyntaxFromNoise()
    {
        var result = PathPreprocessor.Preprocess("VIVANT.S01E03.1080p.WEB-DL.mkv");

        Assert.Contains(result.Tokens, static token =>
            token.Kind == TokenKind.Text && token.NormalizedValue == "S01E03");
        Assert.Contains(result.Tokens, static token => token.Kind == TokenKind.Resolution);
        Assert.Contains(result.Tokens, static token => token.Kind == TokenKind.Source);
    }

    [Fact]
    public void Preprocess_JapaneseDramaYearIsClassifiedWithoutConsumingEpisodeText()
    {
        var result = PathPreprocessor.Preprocess("ドラゴン桜 2021 第03話.mp4");

        Assert.Contains(result.Tokens, static token =>
            token.Kind == TokenKind.Year && token.NormalizedValue == "2021");
        Assert.Contains(result.Tokens, static token =>
            token.Kind == TokenKind.Text && token.NormalizedValue == "第03話");
    }

    [Fact]
    public void Preprocess_RepeatedSeparatorsAreStable()
    {
        var result = PathPreprocessor.Preprocess("Title...___   01.mkv");

        Assert.NotEmpty(result.Tokens);
        Assert.All(result.Tokens, token =>
        {
            Assert.True(token.Start >= 0);
            Assert.True(token.Length > 0);
            Assert.True(token.Start + token.Length <= result.Stem.Length);
        });
    }
}
