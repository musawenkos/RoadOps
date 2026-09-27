using Microsoft.EntityFrameworkCore;
using RoadOps.Application.Repositories;
using RoadOps.Domain.Entities;
using RoadOps.Infrastructure.Data;

namespace RoadOps.Infrastructure.Repositories;

public class PhotoRepository : IPhotoRepository
{
    private readonly RoadOpsDbContext _context;

    public PhotoRepository(RoadOpsDbContext context)
    {
        _context = context;
    }

    public async Task<Photo?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _context.Photos.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task AddAsync(Photo photo, CancellationToken cancellationToken = default)
    {
        await _context.Photos.AddAsync(photo, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Photo photo, CancellationToken cancellationToken = default)
    {
        _context.Photos.Update(photo);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
