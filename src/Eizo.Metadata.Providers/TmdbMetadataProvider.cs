using System.Net.Http.Headers;
using System.Text.Json;
using Eizo.Metadata.Core;
using Eizo.Metadata.Recognition;

namespace Eizo.Metadata.Providers;

public sealed record TmdbMetadataProviderOptions(
    string ReadAccessToken,
    string Language = "zh-CN",
    string BaseAddress = "https://api.themoviedb.org/3/",
    string ImageBaseAddress = "https://image.tmdb.org/t/p/w780");

public sealed class TmdbMetadataProvider : IMetadataProvider
{
    private const string ProviderName = "tmdb";
    private readonly HttpClient _httpClient;
    private readonly TmdbMetadataProviderOptions _options;

    public TmdbMetadataProvider(
        HttpClient httpClient,
        TmdbMetadataProviderOptions options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));

        ArgumentException.ThrowIfNullOrWhiteSpace(_options.ReadAccessToken);
        if (!Uri.TryCreate(_options.BaseAddress, UriKind.Absolute, out _))
        {
            throw new ArgumentException("TMDB BaseAddress must be an absolute URI.", nameof(options));
        }
    }

    public string Name => ProviderName;

    public async Task<IReadOnlyList<MetadataSearchCandidate>> SearchAsync(
        MetadataSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var kinds = request.RecognitionMediaKind switch
        {
            MediaKind.Movie => new[] { MetadataSubjectKind.Movie },
            MediaKind.SeriesEpisode or MediaKind.Special => new[] { MetadataSubjectKind.Series },
            _ => new[] { MetadataSubjectKind.Series, MetadataSubjectKind.Movie },
        };

        var titles = request.Titles
            .Where(static title => !string.IsNullOrWhiteSpace(title))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToArray();

        var candidates = new Dictionary<string, MetadataSearchCandidate>(StringComparer.Ordinal);
        var rank = 0;

        foreach (var title in titles)
        {
            foreach (var kind in kinds)
            {
                var endpoint = kind == MetadataSubjectKind.Movie ? "search/movie" : "search/tv";
                var yearName = kind == MetadataSubjectKind.Movie
                    ? "primary_release_year"
                    : "first_air_date_year";
                var year = request.Year is null
                    ? string.Empty
                    : $"&{yearName}={request.Year.Value}";

                var relative =
                    $"{endpoint}?query={Uri.EscapeDataString(title)}&include_adult=false&language={Uri.EscapeDataString(_options.Language)}&page=1{year}";

                using var message = CreateRequest(HttpMethod.Get, relative);
                using var response = await _httpClient
                    .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                using var document = await JsonDocument.ParseAsync(
                        await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (!document.RootElement.TryGetProperty("results", out var results) ||
                    results.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var item in results.EnumerateArray())
                {
                    var numericId = item.GetInt32("id");
                    if (numericId is null)
                    {
                        continue;
                    }

                    var value = numericId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    var key = $"{kind}:{value}";
                    if (candidates.ContainsKey(key))
                    {
                        continue;
                    }

                    candidates[key] = MapSearchCandidate(item, value, kind, rank++);
                    if (candidates.Count >= request.Limit)
                    {
                        break;
                    }
                }

                if (candidates.Count >= request.Limit)
                {
                    break;
                }
            }

            if (candidates.Count >= request.Limit)
            {
                break;
            }
        }

        return candidates.Values
            .OrderBy(static item => item.ProviderRank)
            .Take(request.Limit)
            .ToArray();
    }

    public async Task<MetadataSubject?> GetSubjectAsync(
        MetadataProviderItemId id,
        CancellationToken cancellationToken = default)
    {
        ValidateId(id);
        if (id.Kind == MetadataSubjectKind.Unknown)
        {
            throw new ArgumentException("TMDB subject kind must be Series or Movie.", nameof(id));
        }

        var segment = id.Kind == MetadataSubjectKind.Movie ? "movie" : "tv";
        using var message = CreateRequest(
            HttpMethod.Get,
            $"{segment}/{Uri.EscapeDataString(id.Value)}?language={Uri.EscapeDataString(_options.Language)}&append_to_response=external_ids");
        using var response = await _httpClient
            .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var root = document.RootElement;
        var titles = MapTitles(root, id.Kind);
        var releaseDate = id.Kind == MetadataSubjectKind.Movie
            ? root.GetDateOnly("release_date")
            : root.GetDateOnly("first_air_date");

        var externalIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["tmdb"] = id.Value,
        };

        if (root.TryGetProperty("external_ids", out var external) &&
            external.ValueKind == JsonValueKind.Object)
        {
            AddExternalId(externalIds, "imdb", external.GetString("imdb_id"));
            AddExternalId(externalIds, "tvdb", external.GetString("tvdb_id"));
        }

        return new MetadataSubject(
            id,
            titles,
            root.GetString("overview"),
            releaseDate,
            id.Kind == MetadataSubjectKind.Series
                ? root.GetInt32("number_of_episodes")
                : null,
            new MetadataArtwork(
                BuildImageUrl(root.GetString("poster_path")),
                BuildImageUrl(root.GetString("backdrop_path")),
                BuildImageUrl(root.GetString("poster_path"))),
            externalIds);
    }

    public async Task<IReadOnlyList<MetadataEpisode>> GetEpisodesAsync(
        MetadataProviderItemId id,
        int? seasonNumber = null,
        CancellationToken cancellationToken = default)
    {
        ValidateId(id);
        if (id.Kind != MetadataSubjectKind.Series)
        {
            return Array.Empty<MetadataEpisode>();
        }

        var seasons = seasonNumber is not null
            ? new[] { seasonNumber.Value }
            : await GetSeasonNumbersAsync(id.Value, cancellationToken).ConfigureAwait(false);

        var episodes = new List<MetadataEpisode>();
        foreach (var season in seasons)
        {
            using var message = CreateRequest(
                HttpMethod.Get,
                $"tv/{Uri.EscapeDataString(id.Value)}/season/{season}?language={Uri.EscapeDataString(_options.Language)}");
            using var response = await _httpClient
                .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                continue;
            }

            response.EnsureSuccessStatusCode();
            using var document = await JsonDocument.ParseAsync(
                    await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (!document.RootElement.TryGetProperty("episodes", out var items) ||
                items.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var item in items.EnumerateArray())
            {
                var episodeId = item.GetInt32("id")?.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var number = item.GetInt32("episode_number");
                if (episodeId is null || number is null)
                {
                    continue;
                }

                episodes.Add(new MetadataEpisode(
                    episodeId,
                    id,
                    season,
                    number.Value,
                    season == 0 ? MetadataEpisodeKind.Special : MetadataEpisodeKind.Regular,
                    new MetadataTitles(
                        item.GetString("name") ?? $"Episode {number.Value}",
                        Original: null,
                        JsonProviderHelpers.EmptyDictionary(),
                        JsonProviderHelpers.EmptyAliases()),
                    item.GetString("overview"),
                    item.GetDateOnly("air_date"),
                    BuildImageUrl(item.GetString("still_path"))));
            }
        }

        return episodes;
    }

    private async Task<int[]> GetSeasonNumbersAsync(
        string id,
        CancellationToken cancellationToken)
    {
        using var message = CreateRequest(
            HttpMethod.Get,
            $"tv/{Uri.EscapeDataString(id)}?language={Uri.EscapeDataString(_options.Language)}");
        using var response = await _httpClient
            .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!document.RootElement.TryGetProperty("seasons", out var seasons) ||
            seasons.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<int>();
        }

        return seasons.EnumerateArray()
            .Select(static season => season.GetInt32("season_number"))
            .Where(static value => value is not null)
            .Select(static value => value!.Value)
            .Distinct()
            .Order()
            .ToArray();
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativePath)
    {
        var request = new HttpRequestMessage(
            method,
            new Uri(new Uri(_options.BaseAddress, UriKind.Absolute), relativePath));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", _options.ReadAccessToken);
        return request;
    }

    private MetadataSearchCandidate MapSearchCandidate(
        JsonElement item,
        string id,
        MetadataSubjectKind kind,
        int rank) =>
        new(
            new MetadataProviderItemId(ProviderName, id, kind),
            MapTitles(item, kind),
            kind == MetadataSubjectKind.Movie
                ? item.GetYear("release_date")
                : item.GetYear("first_air_date"),
            rank,
            item.GetDouble("popularity"));

    private static MetadataTitles MapTitles(JsonElement item, MetadataSubjectKind kind)
    {
        var primary = kind == MetadataSubjectKind.Movie
            ? item.GetString("title")
            : item.GetString("name");
        var original = kind == MetadataSubjectKind.Movie
            ? item.GetString("original_title")
            : item.GetString("original_name");

        primary = string.IsNullOrWhiteSpace(primary) ? original : primary;
        original = string.IsNullOrWhiteSpace(original) ? primary : original;

        var aliases = new List<string>();
        if (!string.IsNullOrWhiteSpace(original) &&
            !string.Equals(primary, original, StringComparison.OrdinalIgnoreCase))
        {
            aliases.Add(original);
        }

        return new MetadataTitles(
            primary ?? string.Empty,
            original,
            JsonProviderHelpers.EmptyDictionary(),
            aliases);
    }

    private string? BuildImageUrl(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return _options.ImageBaseAddress.TrimEnd('/') + "/" + path.TrimStart('/');
    }

    private static void AddExternalId(
        IDictionary<string, string> target,
        string key,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            target[key] = value;
        }
    }

    private static void ValidateId(MetadataProviderItemId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (!string.Equals(id.Provider, ProviderName, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Provider id '{id.Provider}' cannot be handled by TMDB.",
                nameof(id));
        }
    }
}
