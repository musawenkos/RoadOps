using RoadOps.Application.Common;
using RoadOps.Domain.Entities;

namespace RoadOps.Application.Repositories;

public interface IRoadSectionRepository
{
    Task<RoadSection?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<PagedResult<RoadSection>> GetPageAsync(PageRequest page, CancellationToken cancellationToken = default);
    Task<PagedResult<RoadSection>> GetByWorkspaceIdAsync(string workspaceId, PageRequest page, CancellationToken cancellationToken = default);
    Task AddAsync(RoadSection roadSection, CancellationToken cancellationToken = default);
    Task UpdateAsync(RoadSection roadSection, CancellationToken cancellationToken = default);
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}
