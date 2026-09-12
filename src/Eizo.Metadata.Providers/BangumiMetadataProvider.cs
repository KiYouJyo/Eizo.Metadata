using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Eizo.Metadata.Core;

namespace Eizo.Metadata.Providers;

public sealed record BangumiMetadataProviderOptions(
    string UserAgent,
    string? AccessToken = null,
    string BaseAddress = "https://api.bgm.tv/",
    int SearchAliasEnrichmentLimit = 5)
{
    // Binary-compatibility bridge for Eizo 0.3.6 and any host compiled against
    // Metadata <= 0.2.3. Optional parameters are substituted by the C# compiler;
    // adding a fourth primary-constructor parameter in 0.2.4 removed the CLR
    // .ctor(string, string, string) method that those hosts call at runtime.
    public BangumiMetadataProviderOptions(
        string UserAgent,
        string? AccessToken,
        string BaseAddress)
        : this(UserAgent, AccessToken, BaseAddress, SearchAliasEnrichmentLimit: 5)
    {
    }
}

public sealed class BangumiMetadataProvider : IMetadataProvider, IMetadataRelationProvider
{
    private const string ProviderName = "bangumi";
    private readonly HttpClient _httpClient;
    private readonly BangumiMetadataProviderOptions _options;

    public BangumiMetadataProvider(
        HttpClient httpClient,
        BangumiMetadataProviderOptions options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));

        ArgumentException.ThrowIfNullOrWhiteSpace(_options.UserAgent);
        if (_options.UserAgent.Contains('\r') || _options.UserAgent.Contains('\n'))
        {
            throw new ArgumentException("Bangumi User-Agent must not contain CR or LF.", nameof(options));
        }

        if (!Uri.TryCreate(_options.BaseAddress, UriKind.Absolute, out _))
        {
            throw new ArgumentException("Bangumi BaseAddress must be an absolute URI.", nameof(options));
        }
    }

    public string Name => ProviderName;

    public async Task<IReadOnlyList<MetadataSearchCandidate>> SearchAsync(
        MetadataSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        request = request.ForProviderSearch();

        var titles = request.Titles
            .Where(static title => !string.IsNullOrWhiteSpace(title))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(7)
            .ToArray();

        var candidates = new Dictionary<string, MetadataSearchCandidate>(StringComparer.Ordinal);

        foreach (var title in titles)
        {
            // Fetch a wider candidate window than the resolver ultimately
            // returns. Bangumi's provider ranking often places the canonical TV
            // subject behind live events, movies or specials for franchise-heavy
            // queries such as LoveLive!, while our local scorer can recover the
            // right subject once it is present in the candidate set.
            var providerLimit = Math.Max(50, Math.Clamp(request.Limit, 1, 50));
            using var message = CreateRequest(
                HttpMethod.Post,
                $"v0/search/subjects?limit={providerLimit}&offset=0");
            message.Content = JsonContent.Create(new
            {
                keyword = title,
                sort = "match",
                filter = new
                {
                    type = new[] { 2, 6 },
                    nsfw = false,
                },
            });

            using var response = await _httpClient
                .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            using var document = await JsonDocument.ParseAsync(
                    await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (!document.RootElement.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var queryRank = 0;
            foreach (var item in data.EnumerateArray())
            {
                var id = item.GetInt32("id");
                if (id is null)
                {
                    continue;
                }

                var key = id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var mapped = MapCandidate(item, key, queryRank++);

                if (candidates.TryGetValue(key, out var existing))
                {
                    candidates[key] = existing with
                    {
                        Titles = MergeTitles(existing.Titles, mapped.Titles),
                        Year = existing.Year ?? mapped.Year,
                        ProviderRank = Math.Min(existing.ProviderRank, mapped.ProviderRank),
                        Popularity = Math.Max(existing.Popularity ?? 0.0, mapped.Popularity ?? 0.0),
                    };
                }
                else
                {
                    candidates[key] = mapped;
                }
            }
        }

        // Query variants are intentionally unioned before truncation. The old
        // early-exit behavior could fill the limit with results from a noisy raw
        // filename and never execute the normalized title query.
        var result = candidates.Values
            .OrderBy(static item => item.ProviderRank)
            .ThenByDescending(static item => item.Popularity ?? 0.0)
            .ThenBy(static item => item.Id.Value, StringComparer.Ordinal)
            .Take(Math.Max(request.Limit, 50))
            .ToArray();

        var enrichmentLimit = Math.Clamp(_options.SearchAliasEnrichmentLimit, 0, 10);
        for (var index = 0; index < Math.Min(enrichmentLimit, result.Length); index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var subject = await GetSubjectAsync(result[index].Id, cancellationToken)
                    .ConfigureAwait(false);
                if (subject is null)
                {
                    continue;
                }

                result[index] = result[index] with
                {
                    Id = subject.Id,
                    Titles = MergeTitles(result[index].Titles, subject.Titles),
                    Year = result[index].Year ?? subject.ReleaseDate?.Year,
                };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Alias enrichment is optional. Search must remain usable if one
                // detail request is rate-limited or a legacy subject is malformed.
            }
        }

        // Bangumi often stores an official romanized alias where the middle arc
        // wording differs from an English library title, or a long Chinese alias
        // with inserted descriptors. Bridge only when the request/candidate pair
        // has a strong structural identity; this improves scoring without turning
        // generic franchise containment into an exact-title match.
        for (var index = 0; index < result.Length; index++)
        {
            var bridge = FindConservativeRequestBridge(request.Titles, result[index].Titles);
            if (bridge is null)
            {
                continue;
            }

            var aliases = result[index].Titles.Aliases
                .Append(bridge)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            result[index] = result[index] with
            {
                Titles = result[index].Titles with { Aliases = aliases },
            };
        }

        return result;
    }

    public async Task<MetadataSubject?> GetSubjectAsync(
        MetadataProviderItemId id,
        CancellationToken cancellationToken = default)
    {
        ValidateId(id);

        using var message = CreateRequest(HttpMethod.Get, $"v0/subjects/{Uri.EscapeDataString(id.Value)}");
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
        var titles = MapTitles(root);
        var date = root.GetDateOnly("date", "air_date");
        var episodeCount = root.GetInt32("total_episodes") ?? root.GetInt32("eps");
        var kind = MapKind(root);
        var images = root.TryGetProperty("images", out var imageElement)
            ? imageElement
            : default;

        var poster = images.ValueKind == JsonValueKind.Object
            ? images.GetString("large") ?? images.GetString("common")
            : null;
        var thumbnail = images.ValueKind == JsonValueKind.Object
            ? images.GetString("medium") ?? images.GetString("small")
            : null;

        var externalIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["bangumi"] = id.Value,
        };

        if (root.TryGetProperty("infobox", out var infoBox) &&
            infoBox.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in infoBox.EnumerateArray())
            {
                var key = entry.GetString("key");
                if (!string.Equals(key, "IMDb", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var imdb = ReadInfoboxValue(entry);
                if (!string.IsNullOrWhiteSpace(imdb))
                {
                    externalIds["imdb"] = imdb;
                }
            }
        }

        return new MetadataSubject(
            new MetadataProviderItemId(ProviderName, id.Value, kind),
            titles,
            root.GetString("summary"),
            date,
            episodeCount,
            new MetadataArtwork(poster, BackdropUrl: null, thumbnail),
            externalIds);
    }

    public async Task<IReadOnlyList<MetadataSubjectRelation>> GetRelatedSubjectsAsync(
        MetadataProviderItemId id,
        CancellationToken cancellationToken = default)
    {
        ValidateId(id);

        using var message = CreateRequest(
            HttpMethod.Get,
            $"v0/subjects/{Uri.EscapeDataString(id.Value)}/subjects");
        using var response = await _httpClient
            .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Array.Empty<MetadataSubjectRelation>();
        }

        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<MetadataSubjectRelation>();
        }

        var result = new List<MetadataSubjectRelation>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var relatedId = item.GetInt32("id");
            var relation = item.GetString("relation")?.Trim();
            if (relatedId is null || string.IsNullOrWhiteSpace(relation))
            {
                continue;
            }

            result.Add(
                new MetadataSubjectRelation(
                    new MetadataProviderItemId(
                        ProviderName,
                        relatedId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        MetadataSubjectKind.Unknown),
                    relation,
                    MapTitles(item)));
        }

        return result;
    }

    public async Task<IReadOnlyList<MetadataEpisode>> GetEpisodesAsync(
        MetadataProviderItemId id,
        int? seasonNumber = null,
        CancellationToken cancellationToken = default)
    {
        ValidateId(id);

        var result = new List<MetadataEpisode>();
        var offset = 0;
        const int limit = 200;

        while (true)
        {
            using var message = CreateRequest(
                HttpMethod.Get,
                $"v0/episodes?subject_id={Uri.EscapeDataString(id.Value)}&limit={limit}&offset={offset}");
            using var response = await _httpClient
                .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();
            using var document = await JsonDocument.ParseAsync(
                    await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (!document.RootElement.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Array)
            {
                break;
            }

            var count = 0;
            foreach (var episode in data.EnumerateArray())
            {
                count++;
                var episodeId = episode.GetInt32("id")?.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (episodeId is null)
                {
                    continue;
                }

                var episodeKind = MapEpisodeKind(episode.GetInt32("type"));
                var number = episode.GetDecimal("ep") ?? episode.GetDecimal("sort");

                result.Add(new MetadataEpisode(
                    episodeId,
                    new MetadataProviderItemId(ProviderName, id.Value, id.Kind),
                    SeasonNumber: null,
                    number,
                    episodeKind,
                    MapTitles(episode),
                    episode.GetString("desc"),
                    episode.GetDateOnly("airdate"),
                    ThumbnailUrl: null));
            }

            var total = document.RootElement.GetInt32("total");
            offset += count;

            if (count == 0 ||
                count < limit ||
                total is not null && offset >= total.Value)
            {
                break;
            }
        }

        return result;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativePath)
    {
        var baseAddress = new Uri(_options.BaseAddress, UriKind.Absolute);
        var request = new HttpRequestMessage(method, new Uri(baseAddress, relativePath));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!request.Headers.TryAddWithoutValidation("User-Agent", _options.UserAgent))
        {
            request.Dispose();
            throw new InvalidOperationException("Bangumi User-Agent could not be added.");
        }

        if (!string.IsNullOrWhiteSpace(_options.AccessToken))
        {
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", _options.AccessToken);
        }

        return request;
    }

    private static MetadataSearchCandidate MapCandidate(
        JsonElement item,
        string id,
        int rank) =>
        new(
            new MetadataProviderItemId(ProviderName, id, MapKind(item)),
            MapTitles(item),
            item.GetYear("date", "air_date"),
            rank,
            item.TryGetProperty("rating", out var rating) &&
            rating.ValueKind == JsonValueKind.Object
                ? rating.GetDouble("score")
                : null);

    private static MetadataTitles MapTitles(JsonElement item)
    {
        var original = item.GetString("name")?.Trim();
        var chinese = item.GetString("name_cn")?.Trim();
        var primary = !string.IsNullOrWhiteSpace(chinese) ? chinese : original ?? string.Empty;

        var localized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(chinese))
        {
            localized["zh-CN"] = chinese;
        }

        var aliases = new List<string>();
        if (!string.IsNullOrWhiteSpace(original) &&
            !string.Equals(original, primary, StringComparison.OrdinalIgnoreCase))
        {
            aliases.Add(original);
        }

        AddInfoboxAliases(item, aliases, localized);

        return new MetadataTitles(
            primary,
            original,
            localized,
            aliases
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    private static void AddInfoboxAliases(
        JsonElement item,
        List<string> aliases,
        Dictionary<string, string> localized)
    {
        if (!item.TryGetProperty("infobox", out var infoBox) ||
            infoBox.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var entry in infoBox.EnumerateArray())
        {
            var key = entry.GetString("key")?.Trim() ?? string.Empty;
            if (key.Length == 0)
            {
                continue;
            }

            var values = ReadInfoboxValues(entry).ToArray();
            if (values.Length == 0)
            {
                continue;
            }

            if (key is "简体中文名" or "簡體中文名")
            {
                localized.TryAdd("zh-CN", values[0]);
                continue;
            }

            if (key.Contains("别名", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("別名", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("ALIAS", StringComparison.OrdinalIgnoreCase) ||
                key is "英文名" or "英语名" or "英語名" or "罗马字" or "羅馬字")
            {
                aliases.AddRange(values);
            }
        }
    }

    private static IEnumerable<string> ReadInfoboxValues(JsonElement entry)
    {
        if (!entry.TryGetProperty("value", out var value))
        {
            yield break;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                yield return text.Trim();
            }

            yield break;
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var text = item.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    yield return text.Trim();
                }

                continue;
            }

            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var textValue = item.GetString("v") ??
                            item.GetString("value") ??
                            item.GetString("name");
            if (!string.IsNullOrWhiteSpace(textValue))
            {
                yield return textValue.Trim();
            }
        }
    }

    private static MetadataTitles MergeTitles(MetadataTitles left, MetadataTitles right)
    {
        var localized = new Dictionary<string, string>(left.Localized, StringComparer.OrdinalIgnoreCase);
        foreach (var (language, title) in right.Localized)
        {
            localized.TryAdd(language, title);
        }

        var primary = !string.IsNullOrWhiteSpace(left.Primary)
            ? left.Primary
            : right.Primary;
        var original = !string.IsNullOrWhiteSpace(left.Original)
            ? left.Original
            : right.Original;

        var aliases = left.EnumerateAll()
            .Concat(right.EnumerateAll())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Where(value =>
                !string.Equals(value, primary, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(value, original, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new MetadataTitles(primary, original, localized, aliases);
    }

    private static string? FindConservativeRequestBridge(
        IReadOnlyList<string> requestTitles,
        MetadataTitles candidateTitles)
    {
        foreach (var requestTitle in requestTitles.Take(8))
        {
            if (string.IsNullOrWhiteSpace(requestTitle))
            {
                continue;
            }

            foreach (var candidateTitle in candidateTitles.EnumerateAll())
            {
                if (string.IsNullOrWhiteSpace(candidateTitle))
                {
                    continue;
                }

                if (HasStrongLatinAnchorIdentity(requestTitle, candidateTitle) ||
                    HasCjkInsertedDescriptorIdentity(requestTitle, candidateTitle))
                {
                    return requestTitle.Trim();
                }
            }
        }

        return null;
    }

    private static bool HasStrongLatinAnchorIdentity(string requestTitle, string candidateTitle)
    {
        var requested = GetLatinTokens(requestTitle);
        var candidate = GetLatinTokens(candidateTitle);
        if (requested.Count < 4 || candidate.Count < 4)
        {
            return false;
        }

        if (!string.Equals(requested[0], candidate[0], StringComparison.Ordinal) ||
            !string.Equals(requested[1], candidate[1], StringComparison.Ordinal) ||
            !string.Equals(requested[2], candidate[2], StringComparison.Ordinal))
        {
            return false;
        }

        var requestedLast = requested[^1];
        var candidateLast = candidate[^1];
        if (requestedLast.Length < 5 ||
            !string.Equals(requestedLast, candidateLast, StringComparison.Ordinal))
        {
            return false;
        }

        return requested[0].Length + requested[1].Length + requested[2].Length >= 10;
    }

    private static IReadOnlyList<string> GetLatinTokens(string value)
    {
        var normalized = value
            .Normalize(System.Text.NormalizationForm.FormKC)
            .ToUpperInvariant();
        var tokens = new List<string>();
        var builder = new System.Text.StringBuilder();

        void Flush()
        {
            if (builder.Length >= 2)
            {
                tokens.Add(builder.ToString());
            }

            builder.Clear();
        }

        foreach (var c in normalized)
        {
            if (c is >= 'A' and <= 'Z' || char.IsDigit(c))
            {
                builder.Append(c);
            }
            else
            {
                Flush();
            }
        }

        Flush();
        return tokens;
    }

    private static bool HasCjkInsertedDescriptorIdentity(string requestTitle, string candidateTitle)
    {
        var requested = NormalizeComparableTitle(requestTitle);
        var candidate = NormalizeComparableTitle(candidateTitle);
        if (requested.Length < 6 ||
            candidate.Length <= requested.Length ||
            CountCjk(requested) < 6 ||
            CountCjk(candidate) < 6 ||
            candidate.Contains(requested, StringComparison.Ordinal))
        {
            return false;
        }

        var ratio = (double)requested.Length / candidate.Length;
        return ratio is >= 0.55 and <= 0.92 &&
               IsSubsequence(requested, candidate);
    }

    private static string NormalizeComparableTitle(string value)
    {
        var normalized = value
            .Normalize(System.Text.NormalizationForm.FormKC)
            .ToUpperInvariant();
        var builder = new System.Text.StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    private static int CountCjk(string value) =>
        value.Count(static c => c is >= '\u3400' and <= '\u9fff');

    private static bool IsSubsequence(string shorter, string longer)
    {
        var index = 0;
        foreach (var c in longer)
        {
            if (index < shorter.Length && shorter[index] == c)
            {
                index++;
            }
        }

        return index == shorter.Length;
    }

    private static MetadataSubjectKind MapKind(JsonElement item)
    {
        var platform = item.GetString("platform") ?? string.Empty;
        if (platform.Contains("剧场", StringComparison.OrdinalIgnoreCase) ||
            platform.Contains("劇場", StringComparison.OrdinalIgnoreCase) ||
            platform.Contains("电影", StringComparison.OrdinalIgnoreCase) ||
            platform.Contains("電影", StringComparison.OrdinalIgnoreCase) ||
            platform.Contains("映画", StringComparison.OrdinalIgnoreCase) ||
            platform.Contains("MOVIE", StringComparison.OrdinalIgnoreCase))
        {
            return MetadataSubjectKind.Movie;
        }

        return MetadataSubjectKind.Series;
    }

    private static MetadataEpisodeKind MapEpisodeKind(int? type) =>
        type switch
        {
            0 => MetadataEpisodeKind.Regular,
            1 => MetadataEpisodeKind.Special,
            2 => MetadataEpisodeKind.Opening,
            3 => MetadataEpisodeKind.Ending,
            4 => MetadataEpisodeKind.Trailer,
            _ => MetadataEpisodeKind.Other,
        };

    private static string? ReadInfoboxValue(JsonElement entry)
    {
        if (!entry.TryGetProperty("value", out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    return item.GetString();
                }

                if (item.ValueKind == JsonValueKind.Object)
                {
                    var text = item.GetString("v");
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }
            }
        }

        return null;
    }

    private static void ValidateId(MetadataProviderItemId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (!string.Equals(id.Provider, ProviderName, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Provider id '{id.Provider}' cannot be handled by Bangumi.",
                nameof(id));
        }
    }
}
