using System.Text;

namespace Eizo.Metadata.Recognition.Internal;

internal static class PathPreprocessor
{
    private static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".m4v", ".avi", ".mov", ".wmv", ".webm", ".flv",
        ".ts", ".m2ts", ".mts", ".mpg", ".mpeg",
    };

    private static readonly Dictionary<char, char> Brackets = new()
    {
        ['['] = ']',
        ['('] = ')',
        ['{'] = '}',
        ['【'] = '】',
        ['「'] = '」',
        ['『'] = '』',
    };

    internal static NormalizedMediaPath Preprocess(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var segments = path.Split(
            new[] { '/', '\\' },
            StringSplitOptions.RemoveEmptyEntries);

        var fileName = segments.Length == 0 ? path : segments[^1];
        var directories = segments.Length <= 1 ? Array.Empty<string>() : segments[..^1];
        var normalizedDirectories = directories.Select(Normalize).ToArray();

        var (stem, extension) = StripKnownExtension(fileName);
        var normalizedStem = Normalize(stem);
        var tokens = Tokenize(stem);

        return new NormalizedMediaPath(
            path,
            directories,
            normalizedDirectories,
            fileName,
            stem,
            normalizedStem,
            extension,
            tokens);
    }

    private static (string Stem, string? Extension) StripKnownExtension(string fileName)
    {
        var dot = fileName.LastIndexOf('.');
        if (dot <= 0 || dot == fileName.Length - 1)
        {
            return (fileName, null);
        }

        var extension = fileName[dot..];
        if (!MediaExtensions.Contains(extension))
        {
            return (fileName, null);
        }

        return (fileName[..dot], extension.ToLowerInvariant());
    }

    private static IReadOnlyList<RecognitionToken> Tokenize(string stem)
    {
        var tokens = new List<RecognitionToken>();
        var index = 0;

        while (index < stem.Length)
        {
            if (Brackets.TryGetValue(stem[index], out var closing))
            {
                var closeIndex = FindBalancedClose(stem, index, stem[index], closing);
                if (closeIndex >= 0)
                {
                    var raw = stem[index..(closeIndex + 1)];
                    var inner = stem[(index + 1)..closeIndex];
                    tokens.Add(new RecognitionToken(
                        TokenClassifier.Classify(inner, bracketed: true),
                        raw,
                        TokenClassifier.Normalize(inner),
                        index,
                        raw.Length,
                        IsBracketed: true));

                    index = closeIndex + 1;
                    continue;
                }
            }

            if (IsHardSeparator(stem[index]))
            {
                var start = index;
                while (index < stem.Length && IsHardSeparator(stem[index]))
                {
                    index++;
                }

                var raw = stem[start..index];
                tokens.Add(new RecognitionToken(
                    TokenKind.Separator,
                    raw,
                    NormalizeSeparator(raw),
                    start,
                    raw.Length,
                    IsBracketed: false));
                continue;
            }

            var chunkStart = index;
            while (index < stem.Length &&
                   !IsHardSeparator(stem[index]) &&
                   !Brackets.ContainsKey(stem[index]))
            {
                index++;
            }

            AddChunkTokens(tokens, stem, chunkStart, index);
        }

        return tokens;
    }

    private static void AddChunkTokens(
        ICollection<RecognitionToken> tokens,
        string stem,
        int start,
        int end)
    {
        if (end <= start)
        {
            return;
        }

        var raw = stem[start..end];
        var kind = TokenClassifier.Classify(raw, bracketed: false);
        if (kind != TokenKind.Text || !ContainsDash(raw))
        {
            tokens.Add(CreateToken(kind, raw, start));
            return;
        }

        var segmentStart = 0;
        for (var i = 0; i < raw.Length; i++)
        {
            if (!IsDash(raw[i]))
            {
                continue;
            }

            if (i > segmentStart)
            {
                var part = raw[segmentStart..i];
                tokens.Add(CreateToken(
                    TokenClassifier.Classify(part, bracketed: false),
                    part,
                    start + segmentStart));
            }

            tokens.Add(new RecognitionToken(
                TokenKind.Separator,
                raw[i].ToString(),
                "-",
                start + i,
                1,
                IsBracketed: false));

            segmentStart = i + 1;
        }

        if (segmentStart < raw.Length)
        {
            var part = raw[segmentStart..];
            tokens.Add(CreateToken(
                TokenClassifier.Classify(part, bracketed: false),
                part,
                start + segmentStart));
        }
    }

    private static RecognitionToken CreateToken(TokenKind kind, string raw, int start) =>
        new(
            kind,
            raw,
            TokenClassifier.Normalize(raw),
            start,
            raw.Length,
            IsBracketed: false);

    private static int FindBalancedClose(
        string value,
        int openingIndex,
        char opening,
        char closing)
    {
        var depth = 0;
        for (var i = openingIndex; i < value.Length; i++)
        {
            if (value[i] == opening)
            {
                depth++;
            }
            else if (value[i] == closing)
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private static bool IsHardSeparator(char c) =>
        char.IsWhiteSpace(c) || c is '.' or '_';

    private static bool ContainsDash(string value) => value.Any(IsDash);

    private static bool IsDash(char c) => c is '-' or '–' or '—' or '−';

    private static string NormalizeSeparator(string value)
    {
        if (value.Any(IsDash))
        {
            return "-";
        }

        if (value.Any(char.IsWhiteSpace))
        {
            return " ";
        }

        return value[0].ToString();
    }

    private static string Normalize(string value) =>
        value.Normalize(NormalizationForm.FormKC);
}
