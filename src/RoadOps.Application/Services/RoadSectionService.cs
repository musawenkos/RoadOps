using RoadOps.Application.Common;
using RoadOps.Application.DTOs;
using RoadOps.Application.Repositories;
using RoadOps.Domain.Entities;

namespace RoadOps.Application.Services;

public class RoadSectionService
{
    private readonly IRoadSectionRepository _roadSectionRepository;
    private readonly IWorkspaceRepository _workspaceRepository;
    private readonly IPavedRoadRecordRepository _pavedRoadRecordRepository;

    public RoadSectionService(
        IRoadSectionRepository roadSectionRepository,
        IWorkspaceRepository workspaceRepository,
        IPavedRoadRecordRepository pavedRoadRecordRepository)
    {
        _roadSectionRepository = roadSectionRepository;
        _workspaceRepository = workspaceRepository;
        _pavedRoadRecordRepository = pavedRoadRecordRepository;
    }

    public async Task<RoadSectionDto> CreateAsync(CreateRoadSectionDto dto, CancellationToken cancellationToken = default)
    {
        FieldRules.RequireText(dto.SectionName, FieldRules.MaxNameLength, "Section name is required.", nameof(dto.SectionName));

        if (string.IsNullOrWhiteSpace(dto.WorkspaceId))
        {
            throw new ArgumentException("Workspace ID is required.", nameof(dto.WorkspaceId));
        }

        FieldRules.RequireChainageRange(dto.ChainageFrom, dto.ChainageTo, allowEmpty: false, nameof(dto.ChainageFrom), nameof(dto.ChainageTo));
        FieldRules.RequireText(dto.CreatedBy, FieldRules.MaxNameLength, "Created by is required.", nameof(dto.CreatedBy));

        if (!await _workspaceRepository.ExistsAsync(dto.WorkspaceId, cancellationToken))
        {
            throw new ArgumentException($"Workspace '{dto.WorkspaceId}' does not exist.", nameof(dto.WorkspaceId));
        }

        await EnsureNoOverlapAsync(dto.WorkspaceId, dto.ChainageFrom, dto.ChainageTo, null, cancellationToken);

        var roadSection = new RoadSection
        {
            Id = Guid.NewGuid().ToString(),
            SectionName = dto.SectionName,
            WorkspaceId = dto.WorkspaceId,
            ChainageFrom = dto.ChainageFrom,
            ChainageTo = dto.ChainageTo,
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

        FieldRules.RequireText(dto.SectionName, FieldRules.MaxNameLength, "Section name is required.", nameof(dto.SectionName));
        FieldRules.RequireChainageRange(dto.ChainageFrom, dto.ChainageTo, allowEmpty: false, nameof(dto.ChainageFrom), nameof(dto.ChainageTo));

        var roadSection = await _roadSectionRepository.GetByIdAsync(id, cancellationToken);
        if (roadSection == null)
        {
            return null;
        }

        await EnsureNoOverlapAsync(roadSection.WorkspaceId, dto.ChainageFrom, dto.ChainageTo, roadSection.Id, cancellationToken);

        // Shrinking a section must not strand records outside it.
        var extent = await _pavedRoadRecordRepository.GetChainageExtentBySectionAsync(roadSection.Id, cancellationToken);
        if (extent is { } e &&
            (e.From < dto.ChainageFrom - FieldRules.ChainageTolerance || e.To > dto.ChainageTo + FieldRules.ChainageTolerance))
        {
            throw new ArgumentException(
                $"The section has records from km {e.From} to km {e.To}; the new range must include them.", nameof(dto.ChainageFrom));
        }

        roadSection.SectionName = dto.SectionName;
        roadSection.ChainageFrom = dto.ChainageFrom;
        roadSection.ChainageTo = dto.ChainageTo;
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

    private async Task EnsureNoOverlapAsync(string workspaceId, double from, double to, string? excludeId, CancellationToken cancellationToken)
    {
        // Sections of one survey must not overlap, otherwise a chainage (or GPS position) cannot be resolved to one section.
        if (await _roadSectionRepository.OverlapsAnotherSectionAsync(workspaceId, from, to, excludeId, cancellationToken))
        {
            throw new ArgumentException($"Km {from}–{to} overlaps another section of this workspace.", nameof(CreateRoadSectionDto.ChainageFrom));
        }
    }

    private static RoadSectionDto MapToDto(RoadSection roadSection)
    {
        return new RoadSectionDto
        {
            Id = roadSection.Id,
            SectionName = roadSection.SectionName,
            WorkspaceId = roadSection.WorkspaceId,
            ChainageFrom = roadSection.ChainageFrom,
            ChainageTo = roadSection.ChainageTo,
            CreatedBy = roadSection.CreatedBy,
            CreatedAt = roadSection.CreatedAt,
            UpdatedAt = roadSection.UpdatedAt
        };
    }
}
