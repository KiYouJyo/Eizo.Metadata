using System.Text.Json;
using System.Text.Json.Serialization;

namespace Eizo.Metadata.Recognition.Tests;

internal static class Stage6CorpusSupport
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    internal static IReadOnlyList<Stage6GoldenCase> LoadGolden() =>
        Load<Stage6GoldenCase>("stage6-golden.jsonl");

    internal static IReadOnlyList<Stage6NegativeCase> LoadNegative() =>
        Load<Stage6NegativeCase>("stage6-negative.jsonl");

    private static IReadOnlyList<T> Load<T>(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Corpus", fileName);
        var result = new List<T>();

        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var item = JsonSerializer.Deserialize<T>(line, Options);
            if (item is null)
            {
                throw new InvalidDataException($"Could not deserialize corpus row in {fileName}.");
            }

            result.Add(item);
        }

        return result;
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

internal sealed record Stage6GoldenCase(
    string Path,
    string Title,
    MediaKind MediaKind,
    SpecialKind SpecialKind,
    EpisodePart EpisodePart,
    bool IsFinalEpisode,
    int? SeasonNumber,
    int? CourNumber,
    decimal? EpisodeNumber,
    decimal? EpisodeEndNumber,
    decimal? SpecialNumber,
    int? Year);

internal sealed record Stage6NegativeCase(string Path);
