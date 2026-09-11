using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
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

        // Normalize at the resolver boundary so every host benefits, including
        // callers that construct MetadataSearchRequest directly instead of using
        // FromRecognition. Preserve originals and add conservative search variants.
        request = MetadataSearchRequestNormalizer.Normalize(request);

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

    public async Task<MetadataEnrichmentResult> EnrichAsync(
        MetadataSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var resolution = await ResolveAsync(request, cancellationToken).ConfigureAwait(false);
        var errors = resolution.ProviderErrors.ToList();

        IMetadataProvider? provider = null;
        MetadataSubject? subject = null;

        if (!resolution.IsResolved &&
            resolution.Best is not null &&
            CanProbeContinuousSeries(request, resolution))
        {
            var probeId = resolution.Best.Candidate.Id;
            provider = _providers.FirstOrDefault(item =>
                string.Equals(
                    item.Name,
                    probeId.Provider,
                    StringComparison.OrdinalIgnoreCase));

            if (provider is not null)
            {
                try
                {
                    subject = await provider
                        .GetSubjectAsync(probeId, cancellationToken)
                        .ConfigureAwait(false);

                    if (subject?.EpisodeCount is >= 48)
                    {
                        var promotedScore = Math.Max(
                            resolution.Best.Score,
                            Math.Min(1.0, _options.AutoResolveThreshold + 0.01));
                        var promotedBest = resolution.Best with
                        {
                            Score = promotedScore,
                            Evidence = resolution.Best.Evidence
                                .Concat(
                                [
                                    $"continuous-series=episode-count:{subject.EpisodeCount}",
                                    "installment=local-partition",
                                ])
                                .ToArray(),
                        };

                        var candidates = resolution.Candidates
                            .Select(candidate =>
                                candidate.Candidate.Id == promotedBest.Candidate.Id
                                    ? promotedBest
                                    : candidate)
                            .ToArray();

                        resolution = resolution with
                        {
                            Best = promotedBest,
                            IsResolved = true,
                            Confidence = promotedScore,
                            Candidates = candidates,
                        };
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    // This is only a conservative confirmation probe. A failed
                    // detail request must not turn an otherwise valid unresolved
                    // result into a provider error or change fallback behavior.
                    subject = null;
                }
            }
        }

        if (!resolution.IsResolved || resolution.Best is null)
        {
            return new MetadataEnrichmentResult(
                resolution,
                Subject: null,
                Episode: null,
                errors);
        }

        var id = resolution.Best.Candidate.Id;
        provider ??= _providers.FirstOrDefault(item =>
            string.Equals(item.Name, id.Provider, StringComparison.OrdinalIgnoreCase));

        if (provider is null)
        {
            errors.Add(new MetadataProviderError(
                id.Provider,
                "ProviderNotRegistered",
                "The resolved provider is no longer registered."));
            return new MetadataEnrichmentResult(resolution, null, null, errors);
        }

        try
        {
            subject ??= await provider
                .GetSubjectAsync(id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            errors.Add(new MetadataProviderError(
                provider.Name,
                exception.GetType().Name,
                exception.Message));
            return new MetadataEnrichmentResult(resolution, null, null, errors);
        }

        MetadataEpisode? episode = null;
        if (subject is not null &&
            id.Kind == MetadataSubjectKind.Series &&
            request.EpisodeNumber is not null)
        {
            try
            {
                var episodes = await provider
                    .GetEpisodesAsync(id, request.SeasonNumber, cancellationToken)
                    .ConfigureAwait(false);

                episode = SelectEpisode(request, episodes);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                errors.Add(new MetadataProviderError(
                    provider.Name,
                    exception.GetType().Name,
                    exception.Message));
            }
        }

        return new MetadataEnrichmentResult(
            resolution,
            subject,
            episode,
            errors);
    }

    private bool CanProbeContinuousSeries(
        MetadataSearchRequest request,
        MetadataResolution resolution)
    {
        if (resolution.Best is null ||
            request.RecognitionMediaKind != MediaKind.SeriesEpisode ||
            request.SeasonNumber is not > 1 ||
            resolution.Best.Candidate.Id.Kind != MetadataSubjectKind.Series ||
            resolution.Best.Score < Math.Max(0.74, _options.AutoResolveThreshold - 0.06) ||
            !MetadataMatchScorer.HasExactTitleMatch(
                request.Titles,
                resolution.Best.Candidate.Titles) ||
            MetadataMatchScorer.GetCandidateInstallment(
                resolution.Best.Candidate) is not null)
        {
            return false;
        }

        var second = resolution.Candidates
            .Skip(1)
            .FirstOrDefault();
        var lead = second is null
            ? 1.0
            : resolution.Best.Score - second.Score;

        return lead >= _options.MinimumLead;
    }

    private static MetadataEpisode? SelectEpisode(
        MetadataSearchRequest request,
        IReadOnlyList<MetadataEpisode> episodes)
    {
        if (request.EpisodeNumber is null)
        {
            return null;
        }

        var expectedKind = request.RecognitionMediaKind == MediaKind.Special
            ? MetadataEpisodeKind.Special
            : MetadataEpisodeKind.Regular;

        return episodes
            .Where(item => item.EpisodeNumber == request.EpisodeNumber)
            .OrderByDescending(item => item.Kind == expectedKind)
            .ThenBy(item =>
                request.SeasonNumber is not null &&
                item.SeasonNumber == request.SeasonNumber
                    ? 0
                    : 1)
            .FirstOrDefault();
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
    private const RegexOptions RegexOptionsValue =
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(50);

    private static readonly Regex SeasonRegex = new(
        @"(?:^|[\s._-])S(?:EASON)?\s*0?(?<n>\d{1,2})(?:$|[\s._-])|SEASON\s*0?(?<n2>\d{1,2})|第\s*(?<cn>[一二三四五六七八九十两兩〇零壹贰貳叁參肆伍陆陸柒捌玖拾\d]{1,3})\s*(?:季|期)|PART\s*0?(?<part>\d{1,2})|(?<![A-Z0-9])(?<ord>\d{1,2})(?:ST|ND|RD|TH)\s*(?:SEASON|SERIES|GIG|PART)(?![A-Z0-9])",
        RegexOptionsValue,
        RegexTimeout);

    private static readonly Regex FranchiseInstallmentRegex = new(
        @"(?:^|[\s._-])S(?:EASON)?\s*0?\d{1,2}(?=$|[\s._-])|SEASON\s*0?\d{1,2}|第\s*[一二三四五六七八九十两兩〇零壹贰貳叁參肆伍陆陸柒捌玖拾\d]{1,3}\s*(?:季|期)|PART\s*0?\d{1,2}|(?<![A-Z0-9])\d{1,2}(?:ST|ND|RD|TH)\s*(?:SEASON|SERIES|GIG|PART)(?![A-Z0-9])",
        RegexOptionsValue,
        RegexTimeout);

    private static readonly Regex RomanSuffixRegex = new(
        @"(?:^|[\s._-])(?<roman>II|III|IV|V|VI|VII|VIII|IX|X|I)$",
        RegexOptionsValue,
        RegexTimeout);

    private static readonly Regex ArabicSuffixRegex = new(
        @"(?:^|[\s._-])(?<n>0|[1-9]|1\d|20)$",
        RegexOptionsValue,
        RegexTimeout);

    private static readonly Regex ChineseSuffixRegex = new(
        @"(?:^|[\s._-])(?<cn>壹|贰|貳|叁|參|肆|伍|陆|陸|柒|捌|玖|拾)$",
        RegexOptionsValue,
        RegexTimeout);

    private static readonly Regex DerivativeMarkerRegex = new(
        @"(?<![A-Z0-9])(?:OVA|OAD|SPECIAL|TRAILER|MENU|PV|CM)(?![A-Z0-9])|(?<![A-Z0-9])LIVE!(?![A-Z0-9])|(?<![A-Z0-9])(?:\d+(?:ST|ND|RD|TH)\s+)?LIVE(?=$|[\s!~～._-])|FAN\s*DISC|FANDISC|劇場版|剧场版|特別篇|特别篇|総集編|总集篇|演唱会|演唱會|音楽会|音樂會|音乐会",
        RegexOptionsValue,
        RegexTimeout);

    internal static bool HasExactTitleMatch(
        IReadOnlyList<string> requestedTitles,
        MetadataTitles candidateTitles)
    {
        var candidateSet = candidateTitles
            .EnumerateAll()
            .Select(NormalizeTitle)
            .Where(static value => value.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        return requestedTitles
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(NormalizeTitle)
            .Any(candidateSet.Contains);
    }

    internal static int? GetCandidateInstallment(
        MetadataSearchCandidate candidate) =>
        FindInstallment(candidate.Titles.EnumerateAll());

    internal static MetadataResolutionCandidate Score(
        MetadataSearchRequest request,
        MetadataSearchCandidate candidate)
    {
        var evidence = new List<string>();

        var structure = ScoreInstallment(
            request,
            candidate,
            out var requestedInstallment,
            out var candidateInstallment);

        var titleScore = BestTitleScore(request.Titles, candidate.Titles);
        if (requestedInstallment is not null &&
            candidateInstallment == requestedInstallment)
        {
            var franchiseTitleScore = BestFranchiseTitleScore(
                request.Titles,
                candidate.Titles);
            if (franchiseTitleScore > titleScore)
            {
                titleScore = franchiseTitleScore;
                evidence.Add($"title-franchise={franchiseTitleScore:0.000}");
            }
        }

        evidence.Add($"title={titleScore:0.000}");

        var yearScore = ScoreYear(request.Year, candidate.Year);
        evidence.Add($"year={yearScore:0.000}");

        var kindScore = ScoreKind(request.RecognitionMediaKind, candidate.Id.Kind);
        evidence.Add($"kind={kindScore:0.000}");

        evidence.Add($"structure={structure:0.000}");
        if (requestedInstallment is not null || candidateInstallment is not null)
        {
            evidence.Add($"installment=request:{requestedInstallment?.ToString(CultureInfo.InvariantCulture) ?? "-"},candidate:{candidateInstallment?.ToString(CultureInfo.InvariantCulture) ?? "-"}");
        }

        var rankScore = 1.0 - Math.Min(Math.Max(candidate.ProviderRank, 0), 20) / 25.0;
        evidence.Add($"rank={rankScore:0.000}");

        // When Recognition carries an explicit season/part identity, that
        // structural evidence is more reliable than a series-level year. Real
        // libraries frequently repeat the franchise premiere year in every
        // season folder, so year must not pull a season-2 request back to season 1.
        var score = requestedInstallment is not null
            ? titleScore * 0.58 +
              yearScore * 0.07 +
              kindScore * 0.10 +
              structure * 0.21 +
              rankScore * 0.04
            : titleScore * 0.60 +
              yearScore * 0.15 +
              kindScore * 0.10 +
              structure * 0.11 +
              rankScore * 0.04;

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
        for (var index = 0; index < requestedTitles.Count; index++)
        {
            var requested = requestedTitles[index];
            if (string.IsNullOrWhiteSpace(requested) ||
                MetadataSearchTitleNormalizer.IsWeakStandaloneTitle(requested))
            {
                continue;
            }

            // Recognition's primary title is most trustworthy. Later title
            // candidates remain useful aliases but cannot dominate solely
            // because they contain a generic fragment.
            var requestWeight = Math.Max(0.82, 1.0 - index * 0.04);

            foreach (var candidate in candidates)
            {
                var value = TitleSimilarity(requested, candidate) * requestWeight;
                best = Math.Max(best, value);
                if (best >= 1.0)
                {
                    return 1.0;
                }
            }
        }

        return best;
    }

    private static double BestFranchiseTitleScore(
        IReadOnlyList<string> requestedTitles,
        MetadataTitles candidateTitles)
    {
        var candidates = candidateTitles
            .EnumerateAll()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var best = 0.0;
        for (var index = 0; index < requestedTitles.Count; index++)
        {
            var requested = requestedTitles[index];
            if (string.IsNullOrWhiteSpace(requested) ||
                MetadataSearchTitleNormalizer.IsWeakStandaloneTitle(requested))
            {
                continue;
            }

            var requestWeight = Math.Max(0.82, 1.0 - index * 0.04);
            var requestedBase = NormalizeFranchiseTitle(requested);
            if (requestedBase.Length < 3)
            {
                continue;
            }

            foreach (var candidate in candidates)
            {
                var candidateBase = NormalizeFranchiseTitle(candidate);
                if (candidateBase.Length < 3)
                {
                    continue;
                }

                var similarity = string.Equals(
                    requestedBase,
                    candidateBase,
                    StringComparison.Ordinal)
                        ? 0.98
                        : TitleSimilarity(requestedBase, candidateBase) * 0.96;

                best = Math.Max(best, similarity * requestWeight);
            }
        }

        return best;
    }

    private static string NormalizeFranchiseTitle(string value)
    {
        var normalized = value
            .Normalize(NormalizationForm.FormKC)
            .ToUpperInvariant();

        normalized = FranchiseInstallmentRegex.Replace(normalized, " ");
        normalized = RomanSuffixRegex.Replace(normalized, " ");
        normalized = ChineseSuffixRegex.Replace(normalized, " ");

        return NormalizeTitle(normalized);
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

        if (a.Length >= 3 &&
            b.Length >= 3 &&
            (a.Contains(b, StringComparison.Ordinal) ||
             b.Contains(a, StringComparison.Ordinal)))
        {
            var ratio = (double)Math.Min(a.Length, b.Length) / Math.Max(a.Length, b.Length);
            // A proper superset is often an OVA, movie, sequel or special
            // sharing the base title. Keep containment useful for retrieval,
            // but leave a meaningful margin behind an exact alias/title match.
            return 0.78 + ratio * 0.12;
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
        if (requested is null)
        {
            return 0.50;
        }

        // Once Recognition has a concrete release year, a provider candidate
        // with no date should not remain almost tied with an otherwise identical
        // exact-year subject. Missing data is still possible, so keep a modest
        // neutral score rather than treating it as a mismatch.
        if (candidate is null)
        {
            return 0.25;
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

    private static double ScoreInstallment(
        MetadataSearchRequest request,
        MetadataSearchCandidate candidate,
        out int? requestedInstallment,
        out int? candidateInstallment)
    {
        requestedInstallment = FindInstallment(request.Titles);
        if (requestedInstallment is null && request.SeasonNumber is > 0)
        {
            requestedInstallment = request.SeasonNumber;
        }

        var candidateTitles = candidate.Titles.EnumerateAll().ToArray();
        candidateInstallment = FindInstallment(candidateTitles);

        var derivativeMismatch =
            HasDerivativeMarker(candidateTitles) &&
            !HasDerivativeMarker(request.Titles);

        double score;
        if (requestedInstallment is null)
        {
            score = candidateInstallment is null ? 0.60 : 0.45;
        }
        else if (candidateInstallment is null)
        {
            // Provider catalogs normally omit an explicit "season 1" marker
            // from the base subject. Treat that as a strong structural match,
            // rather than penalizing it simply for being unlabeled.
            score = requestedInstallment == 1 ? 0.90 : 0.20;
        }
        else
        {
            score = requestedInstallment == candidateInstallment ? 1.0 : 0.0;
        }

        // Bangumi maps OAD/OVA/live-event/special subjects to the broad Series
        // kind, so MediaKind alone cannot separate them from a regular TV
        // request. Penalize an explicit derivative marker only when the request
        // itself did not ask for that derivative.
        return derivativeMismatch
            ? Math.Min(score, 0.15)
            : score;
    }

    private static bool HasDerivativeMarker(IEnumerable<string> titles)
    {
        foreach (var title in titles)
        {
            if (!string.IsNullOrWhiteSpace(title) &&
                DerivativeMarkerRegex.IsMatch(
                    title.Normalize(NormalizationForm.FormKC)))
            {
                return true;
            }
        }

        return false;
    }

    private static int? FindInstallment(IEnumerable<string> titles)
    {
        foreach (var title in titles)
        {
            var value = title.Normalize(NormalizationForm.FormKC).Trim();
            if (value.Length == 0)
            {
                continue;
            }

            if (value.StartsWith("续", StringComparison.Ordinal) ||
                value.StartsWith("続", StringComparison.Ordinal))
            {
                return 2;
            }

            var season = SeasonRegex.Match(value);
            if (season.Success)
            {
                foreach (var groupName in new[] { "n", "n2", "part", "ord" })
                {
                    if (int.TryParse(season.Groups[groupName].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) &&
                        number is >= 0 and <= 20)
                    {
                        return number;
                    }
                }

                var chinese = ParseChineseNumber(season.Groups["cn"].Value);
                if (chinese is not null)
                {
                    return chinese;
                }
            }

            var roman = RomanSuffixRegex.Match(value);
            if (roman.Success)
            {
                return ParseRoman(roman.Groups["roman"].Value);
            }

            var chineseSuffix = ChineseSuffixRegex.Match(value);
            if (chineseSuffix.Success)
            {
                var chinese = ParseChineseNumber(chineseSuffix.Groups["cn"].Value);
                if (chinese is not null)
                {
                    return chinese;
                }
            }

            var arabic = ArabicSuffixRegex.Match(value);
            if (arabic.Success &&
                int.TryParse(arabic.Groups["n"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var suffix))
            {
                return suffix;
            }
        }

        return null;
    }

    private static int? ParseRoman(string value) =>
        value.ToUpperInvariant() switch
        {
            "I" => 1,
            "II" => 2,
            "III" => 3,
            "IV" => 4,
            "V" => 5,
            "VI" => 6,
            "VII" => 7,
            "VIII" => 8,
            "IX" => 9,
            "X" => 10,
            _ => null,
        };

    private static int? ParseChineseNumber(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            return number is >= 0 and <= 20 ? number : null;
        }

        return value switch
        {
            "〇" or "零" => 0,
            "一" or "壹" => 1,
            "二" or "两" or "兩" or "贰" or "貳" => 2,
            "三" or "叁" or "參" => 3,
            "四" or "肆" => 4,
            "五" or "伍" => 5,
            "六" or "陆" or "陸" => 6,
            "七" or "柒" => 7,
            "八" or "捌" => 8,
            "九" or "玖" => 9,
            "十" or "拾" => 10,
            _ when value.StartsWith("十", StringComparison.Ordinal) && value.Length == 2 =>
                ParseChineseDigit(value[1]) is int ones ? 10 + ones : null,
            _ => null,
        };
    }

    private static int? ParseChineseDigit(char value) =>
        value switch
        {
            '一' or '壹' => 1,
            '二' or '两' or '兩' or '贰' or '貳' => 2,
            '三' or '叁' or '參' => 3,
            '四' or '肆' => 4,
            '五' or '伍' => 5,
            '六' or '陆' or '陸' => 6,
            '七' or '柒' => 7,
            '八' or '捌' => 8,
            '九' or '玖' => 9,
            _ => null,
        };
}
