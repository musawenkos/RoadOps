using Microsoft.EntityFrameworkCore;
using RoadOps.Application.Repositories;
using RoadOps.Domain.Entities;
using RoadOps.Infrastructure.Data;

namespace RoadOps.Infrastructure.Repositories;

public class InspectorSessionRepository : IInspectorSessionRepository
{
    private readonly RoadOpsDbContext _context;

    public InspectorSessionRepository(RoadOpsDbContext context)
    {
        _context = context;
    }

    public async Task<InspectorSession?> GetAsync(string inspector, CancellationToken cancellationToken = default)
    {
        return await _context.InspectorSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Inspector == inspector, cancellationToken);
    }

    public async Task SaveAsync(InspectorSession session, CancellationToken cancellationToken = default)
    {
        var exists = await _context.InspectorSessions.AnyAsync(s => s.Inspector == session.Inspector, cancellationToken);
        if (exists)
        {
            _context.InspectorSessions.Update(session);
        }
        else
        {
            await _context.InspectorSessions.AddAsync(session, cancellationToken);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}
