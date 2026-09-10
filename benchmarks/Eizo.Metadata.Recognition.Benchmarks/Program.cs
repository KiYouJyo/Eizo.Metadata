using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Eizo.Metadata.Recognition;

var outputDirectory = GetOutputDirectory(args);
Directory.CreateDirectory(outputDirectory);

var engine = new RecognitionEngine();
var warmup = Enumerable.Range(0, 2_000).Select(CreateInput).ToArray();
foreach (var input in warmup)
{
    _ = engine.Recognize(new RecognitionRequest(input));
}

var sizes = new[] { 1_000, 10_000, 100_000 };
var results = new List<BenchmarkResult>();

foreach (var size in sizes)
{
    var inputs = Enumerable.Range(0, size).Select(CreateInput).ToArray();

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    var beforeAllocated = GC.GetAllocatedBytesForCurrentThread();
    var stopwatch = Stopwatch.StartNew();
    long checksum = 0;

    foreach (var input in inputs)
    {
        var result = engine.Recognize(new RecognitionRequest(input));
        checksum += result.Title?.Length ?? 0;
        checksum += result.EpisodeNumber is null ? 0 : 1;
        checksum += (int)result.MediaKind;
    }

    stopwatch.Stop();
    var allocated = GC.GetAllocatedBytesForCurrentThread() - beforeAllocated;
    var throughput = size / stopwatch.Elapsed.TotalSeconds;

    results.Add(new BenchmarkResult(
        size,
        stopwatch.Elapsed.TotalMilliseconds,
        throughput,
        allocated,
        checksum));

    Console.WriteLine(
        $"{size,7:N0} items | {stopwatch.Elapsed.TotalMilliseconds,10:N1} ms | " +
        $"{throughput,10:N0} items/s | {allocated / (1024.0 * 1024.0),8:N1} MiB allocated | checksum={checksum}");
}

var jsonPath = Path.Combine(outputDirectory, "recognition-benchmark.json");
await File.WriteAllTextAsync(
    jsonPath,
    JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));

var markdown = new StringBuilder();
markdown.AppendLine("# Eizo.Metadata.Recognition benchmark");
markdown.AppendLine();
markdown.AppendLine($"Runtime: {Environment.Version}");
markdown.AppendLine($"OS: {Environment.OSVersion}");
markdown.AppendLine($"Processor count: {Environment.ProcessorCount}");
markdown.AppendLine();
markdown.AppendLine("| Items | Elapsed ms | Throughput items/s | Allocated MiB | Checksum |");
markdown.AppendLine("| ---: | ---: | ---: | ---: | ---: |");

foreach (var result in results)
{
    markdown.AppendLine(
        $"| {result.Items:N0} | {result.ElapsedMilliseconds:F1} | " +
        $"{result.ItemsPerSecond:F0} | {result.AllocatedBytes / (1024.0 * 1024.0):F1} | {result.Checksum} |");
}

await File.WriteAllTextAsync(
    Path.Combine(outputDirectory, "recognition-benchmark.md"),
    markdown.ToString());

static string GetOutputDirectory(string[] arguments)
{
    var index = Array.IndexOf(arguments, "--output");
    if (index >= 0 && index + 1 < arguments.Length)
    {
        return arguments[index + 1];
    }

    return Path.Combine("artifacts", "benchmarks");
}

static string CreateInput(int index)
{
    var title = index % 2 == 0
        ? $"作品記録 {index % 1000:D4}"
        : $"Archive Title {index % 1000:D4}";

    var episode = index % 24 + 1;

    return index % 10 switch
    {
        0 => $"[ANi] {title} - {episode:D2} [1080P][WEB-DL][AAC].mkv",
        1 => $"{title}.S01E{episode:D2}.1080p.WEB-DL.x265.AAC.mkv",
        2 => $"{title} 第{episode:D2}話 1080p HDTV.mp4",
        3 => $"{title}/Season 01/{episode:D2}.mkv",
        4 => $"{title}/Cour 2/{episode:D2}.mkv",
        5 => $"{title} - OVA 01 [1080P].mkv",
        6 => $"劇場版 {title} [1080P].mkv",
        7 => $"{title} 最終話.mp4",
        8 => $"{title} 前編.mp4",
        _ => $"{title}.2026.1080p.WEB-DL.x265.AAC.mkv",
    };
}

internal sealed record BenchmarkResult(
    int Items,
    double ElapsedMilliseconds,
    double ItemsPerSecond,
    long AllocatedBytes,
    long Checksum);
