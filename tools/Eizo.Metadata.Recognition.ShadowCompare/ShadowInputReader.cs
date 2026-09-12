using Microsoft.VisualBasic.FileIO;

namespace Eizo.Metadata.Recognition.ShadowCompare;

public static class ShadowInputReader
{
    public static IReadOnlyList<string> ReadPaths(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return string.Equals(
                Path.GetExtension(path),
                ".csv",
                StringComparison.OrdinalIgnoreCase)
            ? ReadCsvPaths(path)
            : File.ReadLines(path)
                .Select(static line => line.Trim())
                .Where(static line =>
                    line.Length > 0 &&
                    !line.StartsWith('#'))
                .ToArray();
    }

    private static IReadOnlyList<string> ReadCsvPaths(string path)
    {
        using var parser = new TextFieldParser(path)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false,
        };
        parser.SetDelimiters(",");

        var header = parser.ReadFields();
        if (header is null)
        {
            return Array.Empty<string>();
        }

        var logicalPathIndex = Array.FindIndex(
            header,
            static value => string.Equals(
                value?.Trim(),
                "LogicalPath",
                StringComparison.OrdinalIgnoreCase));

        if (logicalPathIndex < 0)
        {
            throw new InvalidDataException(
                "CSV input must contain a LogicalPath column.");
        }

        var paths = new List<string>();
        while (!parser.EndOfData)
        {
            string[]? row;
            try
            {
                row = parser.ReadFields();
            }
            catch (MalformedLineException exception)
            {
                throw new InvalidDataException(
                    $"Malformed CSV row near line {parser.ErrorLineNumber}.",
                    exception);
            }

            if (row is null || logicalPathIndex >= row.Length)
            {
                continue;
            }

            var logicalPath = row[logicalPathIndex]?.Trim();
            if (!string.IsNullOrWhiteSpace(logicalPath))
            {
                paths.Add(logicalPath);
            }
        }

        return paths;
    }
}
