namespace Eizo.Metadata.Core;

public interface IMetadataProvider
{
    string Name { get; }

    Task<IReadOnlyList<MetadataSearchCandidate>> SearchAsync(
        MetadataSearchRequest request,
        CancellationToken cancellationToken = default);

    Task<MetadataSubject?> GetSubjectAsync(
        MetadataProviderItemId id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MetadataEpisode>> GetEpisodesAsync(
        MetadataProviderItemId id,
        int? seasonNumber = null,
        CancellationToken cancellationToken = default);
}


public sealed record MetadataSubjectRelation(
    MetadataProviderItemId SubjectId,
    string Relation,
    MetadataTitles Titles);

public interface IMetadataRelationProvider
{
    Task<IReadOnlyList<MetadataSubjectRelation>> GetRelatedSubjectsAsync(
        MetadataProviderItemId id,
        CancellationToken cancellationToken = default);
}
