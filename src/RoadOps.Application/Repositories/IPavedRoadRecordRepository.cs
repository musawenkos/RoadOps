using RoadOps.Application.Common;
using RoadOps.Domain.Entities;

namespace RoadOps.Application.Repositories;

public interface IPavedRoadRecordRepository
{
    Task<PavedRoadRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<PagedResult<PavedRoadRecord>> GetPageAsync(PageRequest page, CancellationToken cancellationToken = default);
    Task<PagedResult<PavedRoadRecord>> GetByWorkspaceIdAsync(string workspaceId, PageRequest page, CancellationToken cancellationToken = default);
    Task<PagedResult<PavedRoadRecord>> GetBySectionIdAsync(string sectionId, PageRequest page, CancellationToken cancellationToken = default);

    /// <summary>Records lying fully inside [chainageFrom, chainageTo], optionally limited to one workspace.</summary>
    Task<PagedResult<PavedRoadRecord>> FindByChainageRangeAsync(double chainageFrom, double chainageTo, string? workspaceId, PageRequest page, CancellationToken cancellationToken = default);

    /// <summary>The lowest ChainageFrom and highest ChainageTo of the section's records, or null if it has none.</summary>
    Task<(double From, double To)?> GetChainageExtentBySectionAsync(string sectionId, CancellationToken cancellationToken = default);

    /// <summary>The most recent (non-voided) record created by <paramref name="createdBy"/> since <paramref name="since"/>.</summary>
    Task<PavedRoadRecord?> GetLatestByCreatorAsync(string createdBy, DateTimeOffset since, CancellationToken cancellationToken = default);

    Task AddAsync(PavedRoadRecord record, CancellationToken cancellationToken = default);
    Task UpdateAsync(PavedRoadRecord record, CancellationToken cancellationToken = default);
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}
