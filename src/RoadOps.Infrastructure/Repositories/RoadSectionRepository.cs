using Microsoft.EntityFrameworkCore;
using RoadOps.Application.Common;
using RoadOps.Application.Repositories;
using RoadOps.Domain.Entities;
using RoadOps.Infrastructure.Data;

namespace RoadOps.Infrastructure.Repositories;

public class RoadSectionRepository : IRoadSectionRepository
{
    private readonly RoadOpsDbContext _context;

    public RoadSectionRepository(RoadOpsDbContext context)
    {
        _context = context;
    }

    public async Task<RoadSection?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _context.RoadSections
            .AsNoTracking()
            .FirstOrDefaultAsync(rs => rs.Id == id, cancellationToken);
    }

    public Task<PagedResult<RoadSection>> GetPageAsync(PageRequest page, CancellationToken cancellationToken = default)
    {
        return _context.RoadSections
            .AsNoTracking()
            .OrderBy(rs => rs.CreatedAt)
            .ThenBy(rs => rs.Id)
            .ToPagedResultAsync(page, cancellationToken);
    }

    public Task<PagedResult<RoadSection>> GetByWorkspaceIdAsync(string workspaceId, PageRequest page, CancellationToken cancellationToken = default)
    {
        return _context.RoadSections
            .AsNoTracking()
            .Where(rs => rs.WorkspaceId == workspaceId)
            .OrderBy(rs => rs.CreatedAt)
            .ThenBy(rs => rs.Id)
            .ToPagedResultAsync(page, cancellationToken);
    }

    public Task<bool> OverlapsAnotherSectionAsync(string workspaceId, double chainageFrom, double chainageTo, string? excludeSectionId, CancellationToken cancellationToken = default)
    {
        return _context.RoadSections.AnyAsync(rs =>
            rs.WorkspaceId == workspaceId &&
            (excludeSectionId == null || rs.Id != excludeSectionId) &&
            rs.ChainageFrom < chainageTo &&
            rs.ChainageTo > chainageFrom, cancellationToken);
    }

    public async Task AddAsync(RoadSection roadSection, CancellationToken cancellationToken = default)
    {
        await _context.RoadSections.AddAsync(roadSection, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(RoadSection roadSection, CancellationToken cancellationToken = default)
    {
        _context.RoadSections.Update(roadSection);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var roadSection = await _context.RoadSections
            .FirstOrDefaultAsync(rs => rs.Id == id, cancellationToken);

        if (roadSection != null)
        {
            _context.RoadSections.Remove(roadSection);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
