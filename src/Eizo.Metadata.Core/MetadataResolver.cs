using System.Globalization;
using System.Text;
using Eizo.Metadata.Recognition;

namespace Eizo.Metadata.Core;

public sealed class MetadataResolver
{
    private readonly IReadOnlyList<IMetadataProvider> _providers;
    private readonly MetadataResolverOptions _options;

    public MetadataResolver(
        IEnumerable<IMetadataProvider> providers,
        MetadataResolverOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(providers);

        _providers = providers
            .GroupBy(static provider => provider.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();

        if (_providers.Count == 0)
        {
            throw new ArgumentException("At least one metadata provider is required.", nameof(providers));
        }

        _options = options ?? new MetadataResolverOptions();
    }

    public async Task<MetadataResolution> ResolveAsync(
        MetadataSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Titles.Count == 0 ||
            request.Titles.All(string.IsNullOrWhiteSpace))
        {
            return new MetadataResolution(
                Best: null,
                IsResolved: false,
                Confidence: 0.0,
                Candidates: Array.Empty<MetadataResolutionCandidate>(),
                ProviderErrors: Array.Empty<MetadataProviderError>());
        }

        var tasks = _providers.Select(provider =>
            SearchProviderAsync(provider, request, cancellationToken)).ToArray();

        var outcomes = await Task.WhenAll(tasks).ConfigureAwait(false);

        var errors = outcomes
            .Where(static outcome => outcome.Error is not null)
            .Select(static outcome => outcome.Error!)
            .ToArray();

        var scored = outcomes
            .SelectMany(static outcome => outcome.Candidates)
            .GroupBy(static candidate =>
                $"{candidate.Id.Provider}\u001f{candidate.Id.Value}\u001f{candidate.Id.Kind}",
                StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.OrderBy(static item => item.ProviderRank).First())
            .Select(candidate => MetadataMatchScorer.Score(request, candidate))
            .OrderByDescending(static candidate => candidate.Score)
            .ThenBy(static candidate => candidate.Candidate.ProviderRank)
            .ThenBy(static candidate => candidate.Candidate.Id.Provider, StringComparer.Ordinal)
            .Take(Math.Max(1, _options.MaxCandidates))
            .ToArray();

        var best = scored.FirstOrDefault();
        var second = scored.Skip(1).FirstOrDefault();
        var lead = best is null
            ? 0.0
            : second is null
                ? 1.0
                : best.Score - second.Score;

        var resolved = best is not null &&
                       best.Score >= _options.AutoResolveThreshold &&
                       lead >= _options.MinimumLead;

        return new MetadataResolution(
            best,
            resolved,
            best?.Score ?? 0.0,
            scored,
            errors);
    }

    public async Task<MetadataSubject?> ResolveSubjectAsync(
        MetadataSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var resolution = await ResolveAsync(request, cancellationToken).ConfigureAwait(false);
        if (!resolution.IsResolved || resolution.Best is null)
        {
            return null;
        }

        var id = resolution.Best.Candidate.Id;
        var provider = _providers.FirstOrDefault(item =>
            string.Equals(item.Name, id.Provider, StringComparison.OrdinalIgnoreCase));

        return provider is null
            ? null
            : await provider.GetSubjectAsync(id, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ProviderSearchOutcome> SearchProviderAsync(
        IMetadataProvider provider,
        MetadataSearchRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var candidates = await provider
                .SearchAsync(request, cancellationToken)
                .ConfigureAwait(false);

            return new ProviderSearchOutcome(
                candidates ?? Array.Empty<MetadataSearchCandidate>(),
                null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new ProviderSearchOutcome(
                Array.Empty<MetadataSearchCandidate>(),
                new MetadataProviderError(
                    provider.Name,
                    exception.GetType().Name,
                    exception.Message));
        }
    }

    private sealed record ProviderSearchOutcome(
        IReadOnlyList<MetadataSearchCandidate> Candidates,
        MetadataProviderError? Error);
}

internal static class MetadataMatchScorer
{
    internal static MetadataResolutionCandidate Score(
        MetadataSearchRequest request,
        MetadataSearchCandidate candidate)
    {
        var evidence = new List<string>();

        var titleScore = BestTitleScore(request.Titles, candidate.Titles);
        evidence.Add($"title={titleScore:0.000}");

        var yearScore = ScoreYear(request.Year, candidate.Year);
        evidence.Add($"year={yearScore:0.000}");

        var kindScore = ScoreKind(request.RecognitionMediaKind, candidate.Id.Kind);
        evidence.Add($"kind={kindScore:0.000}");

        var rankScore = 1.0 - Math.Min(Math.Max(candidate.ProviderRank, 0), 20) / 25.0;
        evidence.Add($"rank={rankScore:0.000}");

        var score =
            titleScore * 0.68 +
            yearScore * 0.14 +
            kindScore * 0.12 +
            rankScore * 0.06;

        return new MetadataResolutionCandidate(
            candidate,
            Math.Clamp(score, 0.0, 1.0),
            evidence);
    }

    private static double BestTitleScore(
        IReadOnlyList<string> requestedTitles,
        MetadataTitles candidateTitles)
    {
        var candidates = candidateTitles
            .EnumerateAll()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var best = 0.0;
        foreach (var requested in requestedTitles)
        {
            if (string.IsNullOrWhiteSpace(requested))
            {
                continue;
            }

            foreach (var candidate in candidates)
            {
                best = Math.Max(best, TitleSimilarity(requested, candidate));
                if (best >= 1.0)
                {
                    return 1.0;
                }
            }
        }

        return best;
    }

    private static double TitleSimilarity(string left, string right)
    {
        var a = NormalizeTitle(left);
        var b = NormalizeTitle(right);
        if (a.Length == 0 || b.Length == 0)
        {
            return 0.0;
        }

        if (string.Equals(a, b, StringComparison.Ordinal))
        {
            return 1.0;
        }

        if (a.Length >= 4 &&
            b.Length >= 4 &&
            (a.Contains(b, StringComparison.Ordinal) ||
             b.Contains(a, StringComparison.Ordinal)))
        {
            var ratio = (double)Math.Min(a.Length, b.Length) / Math.Max(a.Length, b.Length);
            return 0.82 + ratio * 0.12;
        }

        return BigramDice(a, b) * 0.90;
    }

    private static string NormalizeTitle(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormKC).ToUpperInvariant();
        var builder = new StringBuilder(normalized.Length);

        foreach (var c in normalized)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    private static double BigramDice(string left, string right)
    {
        if (left.Length == 1 || right.Length == 1)
        {
            return left[0] == right[0] ? 1.0 : 0.0;
        }

        var leftCounts = BuildBigrams(left);
        var rightCounts = BuildBigrams(right);
        var intersection = 0;

        foreach (var (bigram, leftCount) in leftCounts)
        {
            if (rightCounts.TryGetValue(bigram, out var rightCount))
            {
                intersection += Math.Min(leftCount, rightCount);
            }
        }

        var leftTotal = leftCounts.Values.Sum();
        var rightTotal = rightCounts.Values.Sum();
        return leftTotal + rightTotal == 0
            ? 0.0
            : 2.0 * intersection / (leftTotal + rightTotal);
    }

    private static Dictionary<string, int> BuildBigrams(string value)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < value.Length - 1; i++)
        {
            var bigram = value.Substring(i, 2);
            result.TryGetValue(bigram, out var count);
            result[bigram] = count + 1;
        }

        return result;
    }

    private static double ScoreYear(int? requested, int? candidate)
    {
        if (requested is null || candidate is null)
        {
            return 0.50;
        }

        var delta = Math.Abs(requested.Value - candidate.Value);
        return delta switch
        {
            0 => 1.0,
            1 => 0.80,
            2 => 0.55,
            _ => 0.0,
        };
    }

    private static double ScoreKind(
        MediaKind recognitionKind,
        MetadataSubjectKind candidateKind)
    {
        var expected = recognitionKind switch
        {
            MediaKind.Movie => MetadataSubjectKind.Movie,
            MediaKind.SeriesEpisode or MediaKind.Special => MetadataSubjectKind.Series,
            _ => MetadataSubjectKind.Unknown,
        };

        if (expected == MetadataSubjectKind.Unknown ||
            candidateKind == MetadataSubjectKind.Unknown)
        {
            return 0.50;
        }

        return expected == candidateKind ? 1.0 : 0.0;
    }
}
