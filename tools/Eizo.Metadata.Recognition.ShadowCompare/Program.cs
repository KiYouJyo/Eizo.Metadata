using System.Text.Json;
using System.Text.Json.Serialization;
using Eizo.Metadata.Recognition.ShadowCompare;

var parsed = CliArguments.Parse(args);
if (parsed.ShowHelp)
{
    CliArguments.PrintHelp();
    return 0;
}

var paths = new List<string>();
if (!string.IsNullOrWhiteSpace(parsed.InputFile))
{
    paths.AddRange(
        File.ReadLines(parsed.InputFile)
            .Select(static line => line.Trim())
            .Where(static line =>
                line.Length > 0 &&
                !line.StartsWith('#')));
}

paths.AddRange(parsed.Paths);
paths = paths
    .Where(static path => !string.IsNullOrWhiteSpace(path))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToList();

if (paths.Count == 0)
{
    Console.Error.WriteLine("No media paths supplied. Use --input <paths.txt> or pass paths directly.");
    return 2;
}

var engine = new ShadowComparisonEngine();
var results = new List<ShadowComparisonResult>(paths.Count);

if (parsed.SingleMode)
{
    results.AddRange(paths.Select(engine.CompareSingle));
}
else
{
    foreach (var group in paths.GroupBy(
                 GetLogicalParent,
                 StringComparer.OrdinalIgnoreCase))
    {
        var batch = group.ToArray();
        results.AddRange(
            batch.Length > 1
                ? engine.CompareTogether(batch)
                : [engine.CompareSingle(batch[0])]);
    }
}

var options = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
};
options.Converters.Add(new JsonStringEnumConverter());

TextWriter writer = Console.Out;
StreamWriter? fileWriter = null;
try
{
    if (!string.IsNullOrWhiteSpace(parsed.OutputFile))
    {
        var fullPath = Path.GetFullPath(parsed.OutputFile);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        fileWriter = new StreamWriter(fullPath, append: false);
        writer = fileWriter;
    }

    foreach (var result in results.OrderBy(static item => item.Path, StringComparer.OrdinalIgnoreCase))
    {
        writer.WriteLine(JsonSerializer.Serialize(result, options));
    }
}
finally
{
    fileWriter?.Dispose();
}

var conflictCount = results.Count(static item => item.Agreement == ShadowAgreement.Conflict);
var compatibleCount = results.Count(static item => item.Agreement == ShadowAgreement.Compatible);
var exactCount = results.Count(static item => item.Agreement == ShadowAgreement.Exact);

Console.Error.WriteLine(
    $"Shadow comparison complete: total={results.Count}, exact={exactCount}, compatible={compatibleCount}, conflict={conflictCount}.");

return 0;

static string GetLogicalParent(string path)
{
    var normalized = path.Replace('\\', '/').TrimEnd('/');
    var separator = normalized.LastIndexOf('/');
    return separator <= 0 ? string.Empty : normalized[..separator];
}

internal sealed record CliArguments(
    string? InputFile,
    string? OutputFile,
    bool SingleMode,
    bool ShowHelp,
    IReadOnlyList<string> Paths)
{
    internal static CliArguments Parse(IReadOnlyList<string> args)
    {
        string? input = null;
        string? output = null;
        var single = false;
        var help = false;
        var paths = new List<string>();

        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--input":
                case "-i":
                    input = RequireValue(args, ref i);
                    break;

                case "--output":
                case "-o":
                    output = RequireValue(args, ref i);
                    break;

                case "--single":
                    single = true;
                    break;

                case "--help":
                case "-h":
                    help = true;
                    break;

                default:
                    paths.Add(args[i]);
                    break;
            }
        }

        return new CliArguments(input, output, single, help, paths);
    }

    internal static void PrintHelp()
    {
        Console.WriteLine(
            """
            Eizo.Metadata Recognition Shadow Compare

            Usage:
              dotnet run --project tools/Eizo.Metadata.Recognition.ShadowCompare -- [options] [media-path ...]

            Options:
              -i, --input <file>   Read one logical media path per line.
              -o, --output <file>  Write JSONL results to a file (stdout by default).
                  --single         Disable parent-folder batching / Anitomy ParseTogether.
              -h, --help           Show this help.

            Default behavior groups paths by logical parent folder and uses Anitomy ParseTogether
            for groups with more than one file. Production Eizo recognition decisions are unchanged.
            """);
    }

    private static string RequireValue(IReadOnlyList<string> args, ref int index)
    {
        if (index + 1 >= args.Count)
        {
            throw new ArgumentException($"Missing value after {args[index]}.");
        }

        index++;
        return args[index];
    }
}
