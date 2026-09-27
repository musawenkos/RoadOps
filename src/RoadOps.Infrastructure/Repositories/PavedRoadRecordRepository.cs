using Microsoft.EntityFrameworkCore;
using RoadOps.Application.Common;
using RoadOps.Application.Repositories;
using RoadOps.Domain.Entities;
using RoadOps.Infrastructure.Data;

namespace RoadOps.Infrastructure.Repositories;

public class PavedRoadRecordRepository : IPavedRoadRecordRepository
{
    private readonly RoadOpsDbContext _context;

    public PavedRoadRecordRepository(RoadOpsDbContext context)
    {
        _context = context;
    }

    public async Task<PavedRoadRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _context.PavedRoadRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(pr => pr.Id == id, cancellationToken);
    }

    public Task<PagedResult<PavedRoadRecord>> GetPageAsync(PageRequest page, CancellationToken cancellationToken = default)
    {
        return _context.PavedRoadRecords
            .AsNoTracking()
            .OrderBy(pr => pr.CreatedAt)
            .ThenBy(pr => pr.Id)
            .ToPagedResultAsync(page, cancellationToken);
    }

    public Task<PagedResult<PavedRoadRecord>> GetByWorkspaceIdAsync(string workspaceId, PageRequest page, CancellationToken cancellationToken = default)
    {
        return OrderByChainage(_context.PavedRoadRecords.Where(pr => pr.WorkspaceId == workspaceId))
            .ToPagedResultAsync(page, cancellationToken);
    }

    public Task<PagedResult<PavedRoadRecord>> GetBySectionIdAsync(string sectionId, PageRequest page, CancellationToken cancellationToken = default)
    {
        return OrderByChainage(_context.PavedRoadRecords.Where(pr => pr.SectionId == sectionId))
            .ToPagedResultAsync(page, cancellationToken);
    }

    public Task<PagedResult<PavedRoadRecord>> FindByChainageRangeAsync(double chainageFrom, double chainageTo, string? workspaceId, PageRequest page, CancellationToken cancellationToken = default)
    {
        var query = _context.PavedRoadRecords
            .Where(pr => pr.ChainageFrom >= chainageFrom && pr.ChainageTo <= chainageTo);

        if (!string.IsNullOrWhiteSpace(workspaceId))
        {
            query = query.Where(pr => pr.WorkspaceId == workspaceId);
        }

        return OrderByChainage(query).ToPagedResultAsync(page, cancellationToken);
    }

    public async Task<(double From, double To)?> GetChainageExtentBySectionAsync(string sectionId, CancellationToken cancellationToken = default)
    {
        var extent = await _context.PavedRoadRecords
            .Where(pr => pr.SectionId == sectionId)
            .GroupBy(_ => 1)
            .Select(g => new { From = g.Min(pr => pr.ChainageFrom), To = g.Max(pr => pr.ChainageTo) })
            .FirstOrDefaultAsync(cancellationToken);

        return extent is null ? null : (extent.From, extent.To);
    }

    public async Task<PavedRoadRecord?> GetLatestByCreatorAsync(string createdBy, DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        return await _context.PavedRoadRecords
            .AsNoTracking()
            .Where(pr => pr.CreatedBy == createdBy && pr.CreatedAt >= since)
            .OrderByDescending(pr => pr.CreatedAt)
            .ThenByDescending(pr => pr.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    // Several records can share a chainage (one per distress), so Id breaks ties to keep pages stable.
    private static IQueryable<PavedRoadRecord> OrderByChainage(IQueryable<PavedRoadRecord> query) =>
        query.AsNoTracking()
            .OrderBy(pr => pr.ChainageFrom)
            .ThenBy(pr => pr.CreatedAt)
            .ThenBy(pr => pr.Id);

    public async Task AddAsync(PavedRoadRecord record, CancellationToken cancellationToken = default)
    {
        await _context.PavedRoadRecords.AddAsync(record, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(PavedRoadRecord record, CancellationToken cancellationToken = default)
    {
        _context.PavedRoadRecords.Update(record);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        // Hard delete (REST admin) also removes voided records.
        var record = await _context.PavedRoadRecords
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(pr => pr.Id == id, cancellationToken);

        if (record != null)
        {
            _context.PavedRoadRecords.Remove(record);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
