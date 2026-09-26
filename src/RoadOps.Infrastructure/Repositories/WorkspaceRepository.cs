using Microsoft.EntityFrameworkCore;
using RoadOps.Application.Common;
using RoadOps.Application.Repositories;
using RoadOps.Domain.Entities;
using RoadOps.Infrastructure.Data;

namespace RoadOps.Infrastructure.Repositories;

public class WorkspaceRepository : IWorkspaceRepository
{
    private readonly RoadOpsDbContext _context;

    public WorkspaceRepository(RoadOpsDbContext context)
    {
        _context = context;
    }

    public async Task<Workspace?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _context.Workspaces
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == id, cancellationToken);
    }

    public Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default)
    {
        return _context.Workspaces.AnyAsync(w => w.Id == id, cancellationToken);
    }

    public Task<PagedResult<Workspace>> GetPageAsync(PageRequest page, CancellationToken cancellationToken = default)
    {
        return _context.Workspaces
            .AsNoTracking()
            .OrderBy(w => w.CreatedAt)
            .ThenBy(w => w.Id)
            .ToPagedResultAsync(page, cancellationToken);
    }

    public async Task AddAsync(Workspace workspace, CancellationToken cancellationToken = default)
    {
        await _context.Workspaces.AddAsync(workspace, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Workspace workspace, CancellationToken cancellationToken = default)
    {
        _context.Workspaces.Update(workspace);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var workspace = await _context.Workspaces
            .FirstOrDefaultAsync(w => w.Id == id, cancellationToken);

        if (workspace != null)
        {
            _context.Workspaces.Remove(workspace);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
