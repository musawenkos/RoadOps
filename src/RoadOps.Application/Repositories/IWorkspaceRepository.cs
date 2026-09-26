using RoadOps.Application.Common;
using RoadOps.Domain.Entities;

namespace RoadOps.Application.Repositories;

public interface IWorkspaceRepository
{
    Task<Workspace?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default);
    Task<PagedResult<Workspace>> GetPageAsync(PageRequest page, CancellationToken cancellationToken = default);
    Task AddAsync(Workspace workspace, CancellationToken cancellationToken = default);
    Task UpdateAsync(Workspace workspace, CancellationToken cancellationToken = default);
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}
