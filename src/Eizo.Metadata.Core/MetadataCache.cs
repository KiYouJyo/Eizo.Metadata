using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Eizo.Metadata.Core;

public interface IMetadataCache
{
    ValueTask<string?> GetAsync(
        string key,
        CancellationToken cancellationToken = default);

    ValueTask SetAsync(
        string key,
        string payload,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default);
}

public sealed class MemoryMetadataCache : IMetadataCache
{
    private readonly ConcurrentDictionary<string, CacheEntry> _entries =
        new(StringComparer.Ordinal);

    public ValueTask<string?> GetAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_entries.TryGetValue(key, out var entry))
        {
            return ValueTask.FromResult<string?>(null);
        }

        if (entry.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            _entries.TryRemove(key, out _);
            return ValueTask.FromResult<string?>(null);
        }

        return ValueTask.FromResult<string?>(entry.Payload);
    }

    public ValueTask SetAsync(
        string key,
        string payload,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _entries[key] = new CacheEntry(payload, expiresAt);
        return ValueTask.CompletedTask;
    }

    private sealed record CacheEntry(string Payload, DateTimeOffset ExpiresAt);
}

public sealed class FileMetadataCache : IMetadataCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _rootDirectory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileMetadataCache(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _rootDirectory = Path.GetFullPath(rootDirectory);
    }

    public async ValueTask<string?> GetAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        var path = GetPath(key);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
                var envelope = JsonSerializer.Deserialize<CacheEnvelope>(json, JsonOptions);
                if (envelope is null || envelope.ExpiresAt <= DateTimeOffset.UtcNow)
                {
                    TryDelete(path);
                    return null;
                }

                return envelope.Payload;
            }
            catch (JsonException)
            {
                TryDelete(path);
                return null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask SetAsync(
        string key,
        string payload,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        var path = GetPath(key);
        var directory = Path.GetDirectoryName(path)!;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(directory);

            var envelope = new CacheEnvelope(payload, expiresAt);
            var json = JsonSerializer.Serialize(envelope, JsonOptions);
            var temp = path + ".tmp";

            await File.WriteAllTextAsync(temp, json, Encoding.UTF8, cancellationToken)
                .ConfigureAwait(false);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    private string GetPath(string key)
    {
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();

        return Path.Combine(_rootDirectory, hash[..2], hash + ".json");
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record CacheEnvelope(string Payload, DateTimeOffset ExpiresAt);
}

public sealed class CachedMetadataProvider : IMetadataProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IMetadataProvider _inner;
    private readonly IMetadataCache _cache;
    private readonly MetadataCachePolicy _policy;

    public CachedMetadataProvider(
        IMetadataProvider inner,
        IMetadataCache cache,
        MetadataCachePolicy? policy = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _policy = policy ?? MetadataCachePolicy.Default;
    }

    public string Name => _inner.Name;

    public async Task<IReadOnlyList<MetadataSearchCandidate>> SearchAsync(
        MetadataSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var key = BuildSearchKey(request);
        var cached = await TryReadAsync<MetadataSearchCandidate[]>(key, cancellationToken)
            .ConfigureAwait(false);
        if (cached is not null)
        {
            return cached;
        }

        var result = (await _inner.SearchAsync(request, cancellationToken)
            .ConfigureAwait(false)).ToArray();

        await WriteAsync(key, result, _policy.SearchTtl, cancellationToken)
            .ConfigureAwait(false);
        return result;
    }

    public async Task<MetadataSubject?> GetSubjectAsync(
        MetadataProviderItemId id,
        CancellationToken cancellationToken = default)
    {
        var key = $"subject|{Name}|{id.Kind}|{id.Value}";
        var cached = await TryReadAsync<MetadataSubject>(key, cancellationToken)
            .ConfigureAwait(false);
        if (cached is not null)
        {
            return cached;
        }

        var result = await _inner.GetSubjectAsync(id, cancellationToken).ConfigureAwait(false);
        if (result is not null)
        {
            await WriteAsync(key, result, _policy.SubjectTtl, cancellationToken)
                .ConfigureAwait(false);
        }

        return result;
    }

    public async Task<IReadOnlyList<MetadataEpisode>> GetEpisodesAsync(
        MetadataProviderItemId id,
        int? seasonNumber = null,
        CancellationToken cancellationToken = default)
    {
        var key = $"episodes|{Name}|{id.Kind}|{id.Value}|{seasonNumber?.ToString() ?? "-"}";
        var cached = await TryReadAsync<MetadataEpisode[]>(key, cancellationToken)
            .ConfigureAwait(false);
        if (cached is not null)
        {
            return cached;
        }

        var result = (await _inner.GetEpisodesAsync(id, seasonNumber, cancellationToken)
            .ConfigureAwait(false)).ToArray();

        await WriteAsync(key, result, _policy.EpisodesTtl, cancellationToken)
            .ConfigureAwait(false);
        return result;
    }

    private async ValueTask<T?> TryReadAsync<T>(
        string key,
        CancellationToken cancellationToken)
    {
        var payload = await _cache.GetAsync(key, cancellationToken).ConfigureAwait(false);
        if (payload is null)
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(payload, JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private ValueTask WriteAsync<T>(
        string key,
        T value,
        TimeSpan ttl,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(value, JsonOptions);
        return _cache.SetAsync(
            key,
            payload,
            DateTimeOffset.UtcNow.Add(ttl),
            cancellationToken);
    }

    private string BuildSearchKey(MetadataSearchRequest request)
    {
        var titles = string.Join("\u001f", request.Titles);
        // Provider subject search is work-level, not episode-level. Keeping
        // season/episode out of this key lets every episode of the same title reuse
        // the same remote search response; episode lists have their own cache key.
        return $"search|{Name}|{request.RecognitionMediaKind}|{request.Year}|{request.PreferredLanguage}|{request.Limit}|{titles}";
    }
}
