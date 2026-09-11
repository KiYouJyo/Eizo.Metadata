using System.Globalization;
using System.Text.Json;

namespace Eizo.Metadata.Providers;

internal static class JsonProviderHelpers
{
    internal static string? GetString(this JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            _ => null,
        };
    }

    internal static int? GetInt32(this JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number &&
            property.TryGetInt32(out var number))
        {
            return number;
        }

        if (property.ValueKind == JsonValueKind.String &&
            int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
        {
            return number;
        }

        return null;
    }

    internal static decimal? GetDecimal(this JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number &&
            property.TryGetDecimal(out var number))
        {
            return number;
        }

        if (property.ValueKind == JsonValueKind.String &&
            decimal.TryParse(property.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out number))
        {
            return number;
        }

        return null;
    }

    internal static double? GetDouble(this JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number &&
            property.TryGetDouble(out var number))
        {
            return number;
        }

        return null;
    }

    internal static DateOnly? GetDateOnly(this JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            var value = element.GetString(propertyName);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (DateOnly.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces,
                    out var date))
            {
                return date;
            }
        }

        return null;
    }

    internal static int? GetYear(this JsonElement element, params string[] propertyNames) =>
        element.GetDateOnly(propertyNames)?.Year;

    internal static IReadOnlyDictionary<string, string> EmptyDictionary() =>
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    internal static IReadOnlyList<string> EmptyAliases() => Array.Empty<string>();
}
