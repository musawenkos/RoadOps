using RoadOps.Domain.Entities;

namespace RoadOps.Application.Repositories;

public interface IPhotoRepository
{
    Task<Photo?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task AddAsync(Photo photo, CancellationToken cancellationToken = default);
    Task UpdateAsync(Photo photo, CancellationToken cancellationToken = default);
}
