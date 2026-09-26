using RoadOps.Application.Common;
using RoadOps.Application.DTOs;
using RoadOps.Application.Repositories;
using RoadOps.Domain.Entities;

namespace RoadOps.Application.Services;

public class PavedRoadRecordService
{
    private readonly IPavedRoadRecordRepository _pavedRoadRecordRepository;
    private readonly IWorkspaceRepository _workspaceRepository;
    private readonly IRoadSectionRepository _roadSectionRepository;

    public PavedRoadRecordService(
        IPavedRoadRecordRepository pavedRoadRecordRepository,
        IWorkspaceRepository workspaceRepository,
        IRoadSectionRepository roadSectionRepository)
    {
        _pavedRoadRecordRepository = pavedRoadRecordRepository;
        _workspaceRepository = workspaceRepository;
        _roadSectionRepository = roadSectionRepository;
    }

    public async Task<PavedRoadRecordDto> CreateAsync(CreatePavedRoadRecordDto dto, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dto.WorkspaceId))
        {
            throw new ArgumentException("Workspace ID is required.", nameof(dto.WorkspaceId));
        }

        if (string.IsNullOrWhiteSpace(dto.SectionId))
        {
            throw new ArgumentException("Section ID is required.", nameof(dto.SectionId));
        }

        if (string.IsNullOrWhiteSpace(dto.CreatedBy))
        {
            throw new ArgumentException("Created by is required.", nameof(dto.CreatedBy));
        }

        if (dto.ChainageFrom < 0)
        {
            throw new ArgumentException("Chainage from must be non-negative.", nameof(dto.ChainageFrom));
        }

        if (dto.ChainageTo < 0)
        {
            throw new ArgumentException("Chainage to must be non-negative.", nameof(dto.ChainageTo));
        }

        if (dto.ChainageFrom > dto.ChainageTo)
        {
            throw new ArgumentException("Chainage from must be less than or equal to chainage to.", nameof(dto.ChainageFrom));
        }

        if (!await _workspaceRepository.ExistsAsync(dto.WorkspaceId, cancellationToken))
        {
            throw new ArgumentException($"Workspace '{dto.WorkspaceId}' does not exist.", nameof(dto.WorkspaceId));
        }

        var section = await _roadSectionRepository.GetByIdAsync(dto.SectionId, cancellationToken);
        if (section == null)
        {
            throw new ArgumentException($"Road section '{dto.SectionId}' does not exist.", nameof(dto.SectionId));
        }

        if (section.WorkspaceId != dto.WorkspaceId)
        {
            throw new ArgumentException($"Road section '{dto.SectionId}' does not belong to workspace '{dto.WorkspaceId}'.", nameof(dto.SectionId));
        }

        var record = new PavedRoadRecord
        {
            Id = Guid.NewGuid().ToString(),
            WorkspaceId = dto.WorkspaceId,
            SectionId = dto.SectionId,
            ChainageFrom = dto.ChainageFrom,
            ChainageTo = dto.ChainageTo,
            SurfaceType = dto.SurfaceType,
            DistressType = dto.DistressType,
            Degree = dto.Degree,
            Extent = dto.Extent,
            RutDepthMm = dto.RutDepthMm,
            RidingQuality = dto.RidingQuality,
            SkidResistance = dto.SkidResistance,
            StdRef = dto.StdRef,
            RecommendedAction = dto.RecommendedAction,
            Latitude = dto.Latitude,
            Longitude = dto.Longitude,
            ImagePaths = dto.ImagePaths,
            CreatedBy = dto.CreatedBy,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await _pavedRoadRecordRepository.AddAsync(record, cancellationToken);

        return MapToDto(record);
    }

    public async Task<PavedRoadRecordDto?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Paved road record ID is required.", nameof(id));
        }

        var record = await _pavedRoadRecordRepository.GetByIdAsync(id, cancellationToken);
        return record != null ? MapToDto(record) : null;
    }

    public async Task<PagedResult<PavedRoadRecordDto>> GetPageAsync(PageRequest page, CancellationToken cancellationToken = default)
    {
        var records = await _pavedRoadRecordRepository.GetPageAsync(page.Validate(), cancellationToken);
        return records.Map(MapToDto);
    }

    public async Task<PagedResult<PavedRoadRecordDto>> GetByWorkspaceIdAsync(string workspaceId, PageRequest page, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            throw new ArgumentException("Workspace ID is required.", nameof(workspaceId));
        }

        var records = await _pavedRoadRecordRepository.GetByWorkspaceIdAsync(workspaceId, page.Validate(), cancellationToken);
        return records.Map(MapToDto);
    }

    public async Task<PagedResult<PavedRoadRecordDto>> GetBySectionIdAsync(string sectionId, PageRequest page, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sectionId))
        {
            throw new ArgumentException("Section ID is required.", nameof(sectionId));
        }

        var records = await _pavedRoadRecordRepository.GetBySectionIdAsync(sectionId, page.Validate(), cancellationToken);
        return records.Map(MapToDto);
    }

    public async Task<PagedResult<PavedRoadRecordDto>> FindByChainageRangeAsync(double chainageFrom, double chainageTo, string? workspaceId, PageRequest page, CancellationToken cancellationToken = default)
    {
        if (chainageFrom < 0)
        {
            throw new ArgumentException("Chainage from must be non-negative.", nameof(chainageFrom));
        }

        if (chainageTo < 0)
        {
            throw new ArgumentException("Chainage to must be non-negative.", nameof(chainageTo));
        }

        if (chainageFrom > chainageTo)
        {
            throw new ArgumentException("Chainage from must be less than or equal to chainage to.", nameof(chainageFrom));
        }

        var records = await _pavedRoadRecordRepository.FindByChainageRangeAsync(chainageFrom, chainageTo, workspaceId, page.Validate(), cancellationToken);
        return records.Map(MapToDto);
    }

    public async Task<PavedRoadRecordDto?> UpdateAsync(string id, UpdatePavedRoadRecordDto dto, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Paved road record ID is required.", nameof(id));
        }

        if (dto.ChainageFrom < 0)
        {
            throw new ArgumentException("Chainage from must be non-negative.", nameof(dto.ChainageFrom));
        }

        if (dto.ChainageTo < 0)
        {
            throw new ArgumentException("Chainage to must be non-negative.", nameof(dto.ChainageTo));
        }

        if (dto.ChainageFrom > dto.ChainageTo)
        {
            throw new ArgumentException("Chainage from must be less than or equal to chainage to.", nameof(dto.ChainageFrom));
        }

        var record = await _pavedRoadRecordRepository.GetByIdAsync(id, cancellationToken);
        if (record == null)
        {
            return null;
        }

        record.ChainageFrom = dto.ChainageFrom;
        record.ChainageTo = dto.ChainageTo;
        record.SurfaceType = dto.SurfaceType;
        record.DistressType = dto.DistressType;
        record.Degree = dto.Degree;
        record.Extent = dto.Extent;
        record.RutDepthMm = dto.RutDepthMm;
        record.RidingQuality = dto.RidingQuality;
        record.SkidResistance = dto.SkidResistance;
        record.StdRef = dto.StdRef;
        record.RecommendedAction = dto.RecommendedAction;
        record.Latitude = dto.Latitude;
        record.Longitude = dto.Longitude;
        record.ImagePaths = dto.ImagePaths;
        record.UpdatedAt = DateTimeOffset.UtcNow;

        await _pavedRoadRecordRepository.UpdateAsync(record, cancellationToken);

        return MapToDto(record);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Paved road record ID is required.", nameof(id));
        }

        await _pavedRoadRecordRepository.DeleteAsync(id, cancellationToken);
        return true;
    }

    private static PavedRoadRecordDto MapToDto(PavedRoadRecord record)
    {
        return new PavedRoadRecordDto
        {
            Id = record.Id,
            WorkspaceId = record.WorkspaceId,
            SectionId = record.SectionId,
            ChainageFrom = record.ChainageFrom,
            ChainageTo = record.ChainageTo,
            SurfaceType = record.SurfaceType,
            DistressType = record.DistressType,
            Degree = record.Degree,
            Extent = record.Extent,
            RutDepthMm = record.RutDepthMm,
            RidingQuality = record.RidingQuality,
            SkidResistance = record.SkidResistance,
            StdRef = record.StdRef,
            RecommendedAction = record.RecommendedAction,
            Latitude = record.Latitude,
            Longitude = record.Longitude,
            ImagePaths = record.ImagePaths,
            CreatedBy = record.CreatedBy,
            CreatedAt = record.CreatedAt,
            UpdatedAt = record.UpdatedAt
        };
    }
}
