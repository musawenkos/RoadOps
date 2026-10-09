using RoadOps.Domain.Entities;

namespace RoadOps.Application.Repositories;

public interface IInspectorSessionRepository
{
    Task<InspectorSession?> GetAsync(string inspector, CancellationToken cancellationToken = default);

    /// <summary>Inserts the inspector's summary, or replaces the one they have.</summary>
    Task SaveAsync(InspectorSession session, CancellationToken cancellationToken = default);
}
