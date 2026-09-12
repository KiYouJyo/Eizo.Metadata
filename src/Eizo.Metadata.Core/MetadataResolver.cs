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

        var laterSeasonNeedsStructuralConfirmation =
            request.RecognitionMediaKind == MediaKind.SeriesEpisode &&
            request.SeasonNumber is > 1 &&
            best is not null &&
            best.Candidate.Id.Kind == MetadataSubjectKind.Series &&
            MetadataMatchScorer.GetCandidateInstallment(best.Candidate) is null &&
            !MetadataMatchScorer.HasStrongNamedSeasonSemanticMatch(
                request.Titles,
                best.Candidate.Titles);

        var resolved = best is not null &&
                       best.Score >= _options.AutoResolveThreshold &&
                       lead >= _options.MinimumLead &&
                       !laterSeasonNeedsStructuralConfirmation;

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

        // Keep follow-up structural probes on the same normalized title set
        // used by ResolveAsync. This matters for raw filenames such as
        // "Demon Slayer： Kimetsu no Yaiba.2019", whose provider alias only
        // becomes an exact match after conservative normalization.
        request = MetadataSearchRequestNormalizer.Normalize(request);

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

        if (!resolution.IsResolved &&
            resolution.Best is not null &&
            request.RecognitionMediaKind == MediaKind.SeriesEpisode &&
            request.SeasonNumber is > 1 &&
            resolution.Best.Candidate.Id.Kind == MetadataSubjectKind.Series &&
            resolution.Best.Score >= Math.Max(0.74, _options.AutoResolveThreshold - 0.06) &&
            MetadataMatchScorer.HasExactTitleMatch(
                request.Titles,
                resolution.Best.Candidate.Titles))
        {
            provider ??= _providers.FirstOrDefault(item =>
                string.Equals(
                    item.Name,
                    resolution.Best.Candidate.Id.Provider,
                    StringComparison.OrdinalIgnoreCase));

            if (provider is IMetadataRelationProvider relationProvider)
            {
                try
                {
                    var relationResult = await TryResolveSeasonByRelationChainAsync(
                            provider,
                            relationProvider,
                            resolution.Best.Candidate.Id,
                            request.SeasonNumber.Value,
                            request.Titles,
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (relationResult is not null)
                    {
                        subject = relationResult.Subject;

                        var existingTarget = resolution.Candidates
                            .FirstOrDefault(item => item.Candidate.Id == subject.Id);
                        var remainingCandidates = resolution.Candidates
                            .Where(item => item.Candidate.Id != subject.Id)
                            .ToArray();
                        var strongestCompetitor = remainingCandidates
                            .FirstOrDefault()?.Score ?? 0.0;

                        // A unique provider relation chain is the structural
                        // confirmation that ordinary scoring was missing.
                        // Reflect that in the score as well so downstream
                        // diagnostics do not report "Resolved" together with
                        // "InsufficientLead".
                        var promotedScore = Math.Min(
                            1.0,
                            Math.Max(
                                Math.Max(
                                    _options.AutoResolveThreshold + 0.02,
                                    resolution.Best.Score),
                                strongestCompetitor + _options.MinimumLead + 0.01));

                        var promotedCandidate = new MetadataResolutionCandidate(
                            new MetadataSearchCandidate(
                                subject.Id,
                                subject.Titles,
                                subject.ReleaseDate?.Year,
                                existingTarget?.Candidate.ProviderRank ??
                                resolution.Best.Candidate.ProviderRank),
                            promotedScore,
                            (existingTarget?.Evidence ?? resolution.Best.Evidence)
                                .Concat(
                                [
                                    $"relation-chain=season:{request.SeasonNumber.Value}",
                                    $"relation-chain-path={relationResult.Path}",
                                    $"relation-chain-season-map={relationResult.SeasonMap}",
                                    $"relation-chain-selection={relationResult.SelectionTrace}",
                                ])
                                .Distinct(StringComparer.Ordinal)
                                .ToArray());

                        var candidates = remainingCandidates
                            .Prepend(promotedCandidate)
                            .ToArray();

                        resolution = resolution with
                        {
                            Best = promotedCandidate,
                            IsResolved = true,
                            Confidence = promotedCandidate.Score,
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
                    // Relation traversal is a conservative enhancement only.
                    // Any provider/API failure leaves the ordinary unresolved
                    // result intact.
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
        var episodeRequest = request;
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

        if (subject is not null &&
            id.Kind == MetadataSubjectKind.Series &&
            request.EpisodeNumber is not null &&
            provider is IMetadataRelationProvider episodeRelationProvider)
        {
            try
            {
                var continuation = await TryAdvanceEpisodeOverflowAsync(
                        provider,
                        episodeRelationProvider,
                        subject,
                        request.EpisodeNumber.Value,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (continuation is not null)
                {
                    subject = continuation.Subject;
                    id = subject.Id;
                    episodeRequest = request with
                    {
                        SeasonNumber = null,
                        EpisodeNumber = continuation.EpisodeNumber,
                    };

                    var promotedBest = new MetadataResolutionCandidate(
                        new MetadataSearchCandidate(
                            subject.Id,
                            subject.Titles,
                            subject.ReleaseDate?.Year,
                            resolution.Best.Candidate.ProviderRank),
                        resolution.Best.Score,
                        resolution.Best.Evidence
                            .Concat(
                            [
                                $"episode-subject-span={continuation.Path}",
                                $"episode-offset={request.EpisodeNumber.Value.ToString(CultureInfo.InvariantCulture)}->{continuation.EpisodeNumber.ToString(CultureInfo.InvariantCulture)}",
                            ])
                            .Distinct(StringComparer.Ordinal)
                            .ToArray());

                    resolution = resolution with
                    {
                        Best = promotedBest,
                        Candidates = resolution.Candidates
                            .Where(item => item.Candidate.Id != subject.Id)
                            .Prepend(promotedBest)
                            .ToArray(),
                    };
                }
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Episode overflow continuation is optional enrichment. If a
                // relation/detail request fails, keep the resolved base subject
                // and let episode lookup fall back to the original request.
            }
        }

        MetadataEpisode? episode = null;
        if (subject is not null &&
            id.Kind == MetadataSubjectKind.Series &&
            episodeRequest.EpisodeNumber is not null)
        {
            try
            {
                var episodes = await provider
                    .GetEpisodesAsync(id, episodeRequest.SeasonNumber, cancellationToken)
                    .ConfigureAwait(false);

                episode = SelectEpisode(episodeRequest, episodes);
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

    private static async Task<RelationChainResult?> TryResolveSeasonByRelationChainAsync(
        IMetadataProvider provider,
        IMetadataRelationProvider relationProvider,
        MetadataProviderItemId baseId,
        int targetSeason,
        IReadOnlyList<string> requestedTitles,
        CancellationToken cancellationToken)
    {
        if (targetSeason <= 1)
        {
            return null;
        }

        var visited = new HashSet<string>(StringComparer.Ordinal)
        {
            baseId.Value,
        };
        var current = await provider
            .GetSubjectAsync(baseId, cancellationToken)
            .ConfigureAwait(false);
        if (current is null ||
            current.Id.Kind != MetadataSubjectKind.Series)
        {
            return null;
        }

        var logicalSeason = 1;
        var path = new List<string> { current.Id.Value };
        var seasonMap = new List<string> { $"1:{current.Id.Value}" };
        var selectionTrace = new List<string>();

        // Provider relation graphs are not guaranteed to be linked lists.
        // A subject may expose several later sequel-series at once. Traverse
        // conservatively by ranking only series-level sequel candidates and
        // requiring a meaningful lead whenever more than one branch remains.
        var maxHops = Math.Max(targetSeason * 4, targetSeason + 6);
        for (var hop = 0; hop < maxHops && logicalSeason < targetSeason; hop++)
        {
            var relations = await relationProvider
                .GetRelatedSubjectsAsync(current.Id, cancellationToken)
                .ConfigureAwait(false);

            var sequelRelations = relations
                .Where(static relation => IsSequelRelation(relation.Relation))
                .Where(relation => !visited.Contains(relation.SubjectId.Value))
                .ToArray();

            if (sequelRelations.Length == 0)
            {
                return null;
            }

            var sequelSubjects = new List<MetadataSubject>();
            foreach (var relation in sequelRelations)
            {
                var related = await provider
                    .GetSubjectAsync(relation.SubjectId, cancellationToken)
                    .ConfigureAwait(false);
                if (related is { Id.Kind: MetadataSubjectKind.Series })
                {
                    sequelSubjects.Add(related);
                }
            }

            var distinctSeries = sequelSubjects
                .GroupBy(static subject => subject.Id.Value, StringComparer.Ordinal)
                .Select(static group => group.First())
                .ToArray();

            if (distinctSeries.Length == 0)
            {
                return null;
            }

            var selection = SelectNextSeriesSequel(
                current,
                distinctSeries,
                requestedTitles,
                logicalSeason);

            if (selection is null)
            {
                return null;
            }

            var next = selection.Subject;
            selectionTrace.Add(
                $"{current.Id.Value}>{next.Id.Value}:{selection.Score:0.000}");

            var currentInstallment = MetadataMatchScorer.GetTitleInstallment(
                current.Titles);
            var nextInstallment = MetadataMatchScorer.GetTitleInstallment(
                next.Titles);

            // Adjacent provider subjects can be split cours/parts of one local
            // season. Explicitly equal installment numbers do not consume the
            // next logical Season.
            if (currentInstallment is null ||
                nextInstallment is null ||
                currentInstallment != nextInstallment)
            {
                logicalSeason++;
            }

            current = next;
            visited.Add(current.Id.Value);
            path.Add(current.Id.Value);
            seasonMap.Add($"{logicalSeason}:{current.Id.Value}");
        }

        if (logicalSeason != targetSeason)
        {
            return null;
        }

        return new RelationChainResult(
            current,
            string.Join(">", path),
            string.Join(">", seasonMap),
            string.Join(";", selectionTrace));
    }

    private static RelationSequelSelection? SelectNextSeriesSequel(
        MetadataSubject current,
        IReadOnlyList<MetadataSubject> candidates,
        IReadOnlyList<string> requestedTitles,
        int logicalSeason)
    {
        if (candidates.Count == 1)
        {
            return new RelationSequelSelection(candidates[0], 1.0);
        }

        var currentTitles = current.Titles
            .EnumerateAll()
            .Where(static title => !string.IsNullOrWhiteSpace(title))
            .ToArray();
        var currentInstallment = MetadataMatchScorer.GetTitleInstallment(
            current.Titles);

        var ranked = candidates
            .Select(candidate =>
            {
                var requestAffinity = MetadataMatchScorer.GetFranchiseTitleScore(
                    requestedTitles,
                    candidate.Titles);
                var currentAffinity = MetadataMatchScorer.GetFranchiseTitleScore(
                    currentTitles,
                    candidate.Titles);
                var titleAffinity = Math.Max(requestAffinity, currentAffinity);

                var nextInstallment = MetadataMatchScorer.GetTitleInstallment(
                    candidate.Titles);
                var structureScore =
                    currentInstallment is not null &&
                    nextInstallment == currentInstallment
                        ? 1.0
                        : currentInstallment is not null &&
                          nextInstallment == currentInstallment + 1
                            ? 0.96
                            : nextInstallment == logicalSeason + 1
                                ? 0.94
                                : nextInstallment is null
                                    ? 0.66
                                    : 0.20;

                var chronologyScore = 0.45;
                var chronologyValid = true;
                if (current.ReleaseDate is { } currentDate &&
                    candidate.ReleaseDate is { } candidateDate)
                {
                    var days = candidateDate.DayNumber - currentDate.DayNumber;
                    if (days < -31)
                    {
                        chronologyValid = false;
                    }
                    else if (days >= 0)
                    {
                        chronologyScore = 1.0 /
                            (1.0 + days / 365.0);
                    }
                    else
                    {
                        chronologyScore = 0.30;
                    }
                }

                var sideContentPenalty = IsRelationSideContent(candidate.Titles)
                    ? 0.14
                    : 0.0;

                var score =
                    titleAffinity * 0.48 +
                    chronologyScore * 0.32 +
                    structureScore * 0.20 -
                    sideContentPenalty;

                return new RelationSequelSelection(
                    candidate,
                    Math.Clamp(score, 0.0, 1.0),
                    chronologyValid);
            })
            .Where(static item => item.ChronologyValid)
            .OrderByDescending(static item => item.Score)
            .ThenBy(static item => item.Subject.ReleaseDate)
            .ThenBy(static item => item.Subject.Id.Value, StringComparer.Ordinal)
            .ToArray();

        if (ranked.Length == 0 ||
            ranked[0].Score < 0.56)
        {
            return null;
        }

        if (ranked.Length > 1 &&
            ranked[0].Score - ranked[1].Score < 0.06)
        {
            return null;
        }

        return ranked[0];
    }

    private static bool IsRelationSideContent(MetadataTitles titles) =>
        titles.EnumerateAll().Any(static title =>
            title.Contains("OVA", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("OAD", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("SPECIAL", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("番外", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("特別篇", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("特别篇", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("総集編", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("总集篇", StringComparison.OrdinalIgnoreCase));

    private static bool IsSequelRelation(string value)
    {
        var normalized = value
            .Normalize(NormalizationForm.FormKC)
            .Trim();

        return normalized.Equals("续集", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("續集", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("SEQUEL", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<EpisodeSubjectContinuation?> TryAdvanceEpisodeOverflowAsync(
        IMetadataProvider provider,
        IMetadataRelationProvider relationProvider,
        MetadataSubject initialSubject,
        decimal localEpisodeNumber,
        CancellationToken cancellationToken)
    {
        if (initialSubject.EpisodeCount is not > 0 ||
            localEpisodeNumber <= initialSubject.EpisodeCount.Value)
        {
            return null;
        }

        var current = initialSubject;
        var remainingEpisode = localEpisodeNumber;
        var visited = new HashSet<string>(StringComparer.Ordinal)
        {
            current.Id.Value,
        };
        var path = new List<string> { current.Id.Value };

        for (var hop = 0; hop < 6; hop++)
        {
            if (current.EpisodeCount is not > 0 ||
                remainingEpisode <= current.EpisodeCount.Value)
            {
                break;
            }

            remainingEpisode -= current.EpisodeCount.Value;

            var relations = await relationProvider
                .GetRelatedSubjectsAsync(current.Id, cancellationToken)
                .ConfigureAwait(false);

            var continuationSubjects = new List<MetadataSubject>();
            foreach (var relation in relations
                         .Where(static relation =>
                             IsSequelRelation(relation.Relation)))
            {
                if (visited.Contains(relation.SubjectId.Value))
                {
                    continue;
                }

                var related = await provider
                    .GetSubjectAsync(relation.SubjectId, cancellationToken)
                    .ConfigureAwait(false);

                if (related is { Id.Kind: MetadataSubjectKind.Series } &&
                    IsSameLocalSeasonContinuation(current, related))
                {
                    continuationSubjects.Add(related);
                }
            }

            var candidates = continuationSubjects
                .GroupBy(static subject => subject.Id.Value, StringComparer.Ordinal)
                .Select(static group => group.First())
                .Where(subject =>
                    subject.ReleaseDate is null ||
                    current.ReleaseDate is null ||
                    subject.ReleaseDate.Value >= current.ReleaseDate.Value.AddDays(-31))
                .OrderBy(static subject => subject.ReleaseDate)
                .ThenBy(static subject => subject.Id.Value, StringComparer.Ordinal)
                .ToArray();

            if (candidates.Length == 0)
            {
                return null;
            }

            var next = candidates[0];
            if (candidates.Length > 1 &&
                candidates[0].ReleaseDate == candidates[1].ReleaseDate)
            {
                return null;
            }

            current = next;
            visited.Add(current.Id.Value);
            path.Add(current.Id.Value);
        }

        if (current.Id == initialSubject.Id ||
            current.EpisodeCount is not > 0 ||
            remainingEpisode <= 0 ||
            remainingEpisode > current.EpisodeCount.Value)
        {
            return null;
        }

        return new EpisodeSubjectContinuation(
            current,
            remainingEpisode,
            string.Join(">", path));
    }

    private static bool IsSameLocalSeasonContinuation(
        MetadataSubject current,
        MetadataSubject next)
    {
        var currentInstallment = MetadataMatchScorer.GetTitleInstallment(
            current.Titles);
        var nextInstallment = MetadataMatchScorer.GetTitleInstallment(
            next.Titles);

        if (currentInstallment is not null &&
            nextInstallment == currentInstallment)
        {
            return true;
        }

        var currentTitles = current.Titles
            .EnumerateAll()
            .Select(MetadataMatchScorer.NormalizeTitleForComparison)
            .Where(static value => value.Length >= 4)
            .ToArray();
        var nextTitles = next.Titles
            .EnumerateAll()
            .Select(MetadataMatchScorer.NormalizeTitleForComparison)
            .Where(static value => value.Length >= 4)
            .ToArray();

        foreach (var left in currentTitles)
        {
            foreach (var right in nextTitles)
            {
                if (left.Contains(right, StringComparison.Ordinal) ||
                    right.Contains(left, StringComparison.Ordinal))
                {
                    var ratio =
                        (double)Math.Min(left.Length, right.Length) /
                        Math.Max(left.Length, right.Length);
                    if (ratio >= 0.72)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
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

    private sealed record EpisodeSubjectContinuation(
        MetadataSubject Subject,
        decimal EpisodeNumber,
        string Path);

    private sealed record RelationChainResult(
        MetadataSubject Subject,
        string Path,
        string SeasonMap,
        string SelectionTrace);

    private sealed record RelationSequelSelection(
        MetadataSubject Subject,
        double Score,
        bool ChronologyValid = true);

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
        @"(?:^|[\s._-])S(?:EASON)?\s*0?(?<n>\d{1,2})(?:$|[\s._-])|SEASON\s*0?(?<n2>\d{1,2})|第\s*(?<cn>[一二三四五六七八九十两兩〇零壹贰貳叁參肆伍陆陸柒捌玖拾\d]{1,3})\s*(?:季|期)|PART\s*0?(?<part>\d{1,2})|(?<![A-Z0-9])(?<ord>\d{1,2})(?:ST|ND|RD|TH)\s*(?:SEASON|SERIES|GIG|PART)(?![A-Z0-9])|(?<after>AFTER\s*STORY)",
        RegexOptionsValue,
        RegexTimeout);

    private static readonly Regex FranchiseInstallmentRegex = new(
        @"(?:^|[\s._-])S(?:EASON)?\s*0?\d{1,2}(?=$|[\s._-])|SEASON\s*0?\d{1,2}|第\s*[一二三四五六七八九十两兩〇零壹贰貳叁參肆伍陆陸柒捌玖拾\d]{1,3}\s*(?:季|期)|PART\s*0?\d{1,2}|(?<![A-Z0-9])\d{1,2}(?:ST|ND|RD|TH)\s*(?:SEASON|SERIES|GIG|PART)(?![A-Z0-9])|AFTER\s*STORY",
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

    private static readonly Regex SpecialSubjectMarkerRegex = new(
        @"(?:^|[\s._-])(?:OVA|OAD|ONA|SPECIAL|SP)(?:$|[\s._-])|劇場版|剧场版|映画|电影|電影",
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

    internal static int? GetTitleInstallment(
        MetadataTitles titles) =>
        FindInstallment(titles.EnumerateAll());

    internal static double GetFranchiseTitleScore(
        IReadOnlyList<string> requestedTitles,
        MetadataTitles candidateTitles) =>
        BestFranchiseTitleScore(requestedTitles, candidateTitles);

    internal static bool HasStrongNamedSeasonSemanticMatch(
        IReadOnlyList<string> requestedTitles,
        MetadataTitles candidateTitles) =>
        BestNamedSeasonSemanticScore(requestedTitles, candidateTitles) >= 0.88;

    internal static string NormalizeTitleForComparison(string value) =>
        NormalizeTitle(value);

    internal static MetadataResolutionCandidate Score(
        MetadataSearchRequest request,
        MetadataSearchCandidate candidate)
    {
        var evidence = new List<string>();

        var titleInstallment = FindInstallment(request.Titles);
        var structure = ScoreInstallment(
            request,
            candidate,
            out var requestedInstallment,
            out var candidateInstallment,
            out var requestedInstallmentSource);
        var strongInstallmentEvidence =
            requestedInstallmentSource == "title" ||
            request.SeasonNumber is > 1;

        var titleScore = BestTitleScore(request.Titles, candidate.Titles);
        var namedSeasonSemanticScore = BestNamedSeasonSemanticScore(
            request.Titles,
            candidate.Titles);
        if (namedSeasonSemanticScore >= 0.88)
        {
            titleScore = Math.Max(titleScore, namedSeasonSemanticScore);
            structure = Math.Max(structure, 0.92);
            strongInstallmentEvidence = true;
            evidence.Add($"named-season-semantic={namedSeasonSemanticScore:0.000}");
        }
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
            evidence.Add($"installment-source={requestedInstallmentSource}");
        }

        var rankScore = 1.0 - Math.Min(Math.Max(candidate.ProviderRank, 0), 20) / 25.0;
        evidence.Add($"rank={rankScore:0.000}");

        // Season 1 is commonly just the library's default bucket, so it
        // should retain normal year discrimination. Season 2+ (or an installment
        // explicitly encoded in the title) is strong structural evidence.
        var score = strongInstallmentEvidence
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

        if (request.RecognitionMediaKind == MediaKind.SeriesEpisode &&
            candidate.Id.Kind == MetadataSubjectKind.Series &&
            candidate.Titles.EnumerateAll().Any(static title =>
                SpecialSubjectMarkerRegex.IsMatch(title)))
        {
            score -= 0.04;
            evidence.Add("special-subject-mismatch=-0.040");
        }

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

    private static double BestNamedSeasonSemanticScore(
        IReadOnlyList<string> requestedTitles,
        MetadataTitles candidateTitles)
    {
        var semanticTitles = requestedTitles
            .Select(static title =>
                MetadataSearchTitleNormalizer.TryExtractNamedSeasonSemanticTitle(
                    title,
                    out var semantic)
                        ? semantic
                        : null)
            .Where(static title => !string.IsNullOrWhiteSpace(title))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (semanticTitles.Length == 0)
        {
            return 0.0;
        }

        var candidates = candidateTitles
            .EnumerateAll()
            .Where(static title => !string.IsNullOrWhiteSpace(title))
            .ToArray();

        var best = 0.0;
        foreach (var semanticTitle in semanticTitles)
        {
            var semantic = NormalizeTitle(semanticTitle!);
            if (semantic.Length < 2)
            {
                continue;
            }

            foreach (var candidate in candidates)
            {
                var normalizedCandidate = NormalizeTitle(candidate);
                if (normalizedCandidate.Length == 0)
                {
                    continue;
                }

                double score;
                if (string.Equals(
                        semantic,
                        normalizedCandidate,
                        StringComparison.Ordinal))
                {
                    score = 1.0;
                }
                else if (semantic.Length >= 3 &&
                         normalizedCandidate.Contains(
                             semantic,
                             StringComparison.Ordinal))
                {
                    score = 0.96;
                }
                else
                {
                    score = TitleSimilarity(semanticTitle!, candidate) * 0.94;
                }

                best = Math.Max(best, score);
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
            return string.Equals(left, right, StringComparison.Ordinal)
                ? 1.0
                : 0.0;
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

    private static double ScoreInstallment(
        MetadataSearchRequest request,
        MetadataSearchCandidate candidate,
        out int? requestedInstallment,
        out int? candidateInstallment,
        out string requestedInstallmentSource)
    {
        var titleInstallment = FindInstallment(request.Titles);

        // A file-specific Recognition season is the strongest structural fact.
        // Season 1 is frequently only a default bucket, so an explicit sequel
        // marker in the canonical title may still override that generic value.
        // Season 2+ from SxxEyy / nearest Season directory always wins over
        // aliases and parent-directory collection labels.
        if (request.SeasonNumber is > 1)
        {
            requestedInstallment = request.SeasonNumber;
            requestedInstallmentSource = "recognition-season";
        }
        else if (titleInstallment is not null &&
                 (request.SeasonNumber is null ||
                  request.SeasonNumber <= 1) &&
                 titleInstallment != request.SeasonNumber)
        {
            requestedInstallment = titleInstallment;
            requestedInstallmentSource = "title";
        }
        else if (request.SeasonNumber is > 0)
        {
            requestedInstallment = request.SeasonNumber;
            requestedInstallmentSource = "recognition-season";
        }
        else
        {
            requestedInstallment = titleInstallment;
            requestedInstallmentSource = titleInstallment is null
                ? "none"
                : "title";
        }

        candidateInstallment = FindInstallment(candidate.Titles.EnumerateAll());

        if (requestedInstallment is null)
        {
            return candidateInstallment is null ? 0.60 : 0.45;
        }

        if (candidateInstallment is null)
        {
            return requestedInstallment <= 1 ? 0.65 : 0.20;
        }

        return requestedInstallment == candidateInstallment ? 1.0 : 0.0;
    }

    private static int? FindInstallment(IEnumerable<string> titles)
    {
        foreach (var title in titles)
        {
            var value = title.Normalize(NormalizationForm.FormKC).Trim();
            if (value.Length == 0 ||
                MetadataSearchTitleNormalizer.IsSeasonCoverageRange(value))
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
                if (season.Groups["after"].Success)
                {
                    return 2;
                }

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
