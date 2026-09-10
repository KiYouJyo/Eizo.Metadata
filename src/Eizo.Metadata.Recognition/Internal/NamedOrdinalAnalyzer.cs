using System.Globalization;
using System.Text.RegularExpressions;

namespace Eizo.Metadata.Recognition.Internal;

internal static class NamedOrdinalAnalyzer
{
    private const RegexOptions Options =
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(50);

    private static readonly Regex BorderRegex = new(
        @"^(?<series>.+?)\s+BORDER\s*[-_.:#]?\s*(?<number>\d{1,3})(?:\s+(?<title>.+?))?\s*$",
        Options,
        Timeout);

    internal static bool TryAnalyze(
        NormalizedMediaPath path,
        out NamedOrdinalResult result)
    {
        ArgumentNullException.ThrowIfNull(path);

        result = null!;

        var suffix = TechnicalSuffixAnalyzer.Analyze(path);
        var semanticEnd = suffix?.StartIndex ?? path.Stem.Length;
        if (semanticEnd <= 0)
        {
            return false;
        }

        var semantic = path.Stem[..semanticEnd]
            .Trim(' ', '.', '_', '-', '–', '—', '−');

        var match = BorderRegex.Match(semantic);
        if (!match.Success ||
            !decimal.TryParse(
                match.Groups["number"].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var number) ||
            number is < 0 or > 999)
        {
            return false;
        }

        var series = match.Groups["series"].Value.Trim();
        var title = match.Groups["title"].Success
            ? match.Groups["title"].Value.Trim()
            : null;

        if (!IsUsefulTitle(series) ||
            title is not null && !IsUsefulTitle(title))
        {
            return false;
        }

        result = new NamedOrdinalResult(
            "BORDER",
            number,
            series,
            title,
            Confidence: title is null ? 0.88 : 0.94);
        return true;
    }

    private static bool IsUsefulTitle(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Any(static c =>
            char.IsLetter(c) ||
            c is >= '\u3040' and <= '\u30ff' ||
            c is >= '\u3400' and <= '\u4dbf' ||
            c is >= '\u4e00' and <= '\u9fff' ||
            c is >= '\uac00' and <= '\ud7af');
}
