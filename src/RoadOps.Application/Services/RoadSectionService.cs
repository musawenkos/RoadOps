using RoadOps.Application.Common;
using RoadOps.Application.DTOs;
using RoadOps.Application.Repositories;
using RoadOps.Domain.Entities;

namespace RoadOps.Application.Services;

public class RoadSectionService
{
    private readonly IRoadSectionRepository _roadSectionRepository;
    private readonly IWorkspaceRepository _workspaceRepository;

    public RoadSectionService(IRoadSectionRepository roadSectionRepository, IWorkspaceRepository workspaceRepository)
    {
        _roadSectionRepository = roadSectionRepository;
        _workspaceRepository = workspaceRepository;
    }

    public async Task<RoadSectionDto> CreateAsync(CreateRoadSectionDto dto, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dto.SectionName))
        {
            throw new ArgumentException("Section name is required.", nameof(dto.SectionName));
        }

        if (string.IsNullOrWhiteSpace(dto.WorkspaceId))
        {
            throw new ArgumentException("Workspace ID is required.", nameof(dto.WorkspaceId));
        }

        if (string.IsNullOrWhiteSpace(dto.CreatedBy))
        {
            throw new ArgumentException("Created by is required.", nameof(dto.CreatedBy));
        }

        if (!await _workspaceRepository.ExistsAsync(dto.WorkspaceId, cancellationToken))
        {
            throw new ArgumentException($"Workspace '{dto.WorkspaceId}' does not exist.", nameof(dto.WorkspaceId));
        }

        var roadSection = new RoadSection
        {
            Id = Guid.NewGuid().ToString(),
            SectionName = dto.SectionName,
            WorkspaceId = dto.WorkspaceId,
            CreatedBy = dto.CreatedBy,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await _roadSectionRepository.AddAsync(roadSection, cancellationToken);

        return MapToDto(roadSection);
    }

    public async Task<RoadSectionDto?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Road section ID is required.", nameof(id));
        }

        var roadSection = await _roadSectionRepository.GetByIdAsync(id, cancellationToken);
        return roadSection != null ? MapToDto(roadSection) : null;
    }

    public async Task<PagedResult<RoadSectionDto>> GetPageAsync(PageRequest page, CancellationToken cancellationToken = default)
    {
        var roadSections = await _roadSectionRepository.GetPageAsync(page.Validate(), cancellationToken);
        return roadSections.Map(MapToDto);
    }

    public async Task<PagedResult<RoadSectionDto>> GetByWorkspaceIdAsync(string workspaceId, PageRequest page, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            throw new ArgumentException("Workspace ID is required.", nameof(workspaceId));
        }

        var roadSections = await _roadSectionRepository.GetByWorkspaceIdAsync(workspaceId, page.Validate(), cancellationToken);
        return roadSections.Map(MapToDto);
    }

    public async Task<RoadSectionDto?> UpdateAsync(string id, UpdateRoadSectionDto dto, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Road section ID is required.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(dto.SectionName))
        {
            throw new ArgumentException("Section name is required.", nameof(dto.SectionName));
        }

        var roadSection = await _roadSectionRepository.GetByIdAsync(id, cancellationToken);
        if (roadSection == null)
        {
            return null;
        }

        roadSection.SectionName = dto.SectionName;
        roadSection.UpdatedAt = DateTimeOffset.UtcNow;

        await _roadSectionRepository.UpdateAsync(roadSection, cancellationToken);

        return MapToDto(roadSection);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Road section ID is required.", nameof(id));
        }

        await _roadSectionRepository.DeleteAsync(id, cancellationToken);
        return true;
    }

    private static RoadSectionDto MapToDto(RoadSection roadSection)
    {
        return new RoadSectionDto
        {
            Id = roadSection.Id,
            SectionName = roadSection.SectionName,
            WorkspaceId = roadSection.WorkspaceId,
            CreatedBy = roadSection.CreatedBy,
            CreatedAt = roadSection.CreatedAt,
            UpdatedAt = roadSection.UpdatedAt
        };
    }
}
