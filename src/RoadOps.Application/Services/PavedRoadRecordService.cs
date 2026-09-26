using RoadOps.Application.Common;
using RoadOps.Application.DTOs;
using RoadOps.Application.Repositories;
using RoadOps.Application.Rules;
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

        FieldRules.RequireText(dto.CreatedBy, FieldRules.MaxNameLength, "Created by is required.", nameof(dto.CreatedBy));
        var fields = ValidateFields(dto);

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

        EnsureInsideSection(dto, section);

        var now = DateTimeOffset.UtcNow;
        var record = new PavedRoadRecord
        {
            Id = Guid.NewGuid().ToString(),
            WorkspaceId = dto.WorkspaceId,
            SectionId = dto.SectionId,
            CreatedBy = dto.CreatedBy,
            CreatedAt = now,
        };
        Apply(record, dto, fields, now);

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
        FieldRules.RequireChainageRange(chainageFrom, chainageTo, allowEmpty: true, nameof(chainageFrom), nameof(chainageTo));

        var records = await _pavedRoadRecordRepository.FindByChainageRangeAsync(chainageFrom, chainageTo, workspaceId, page.Validate(), cancellationToken);
        return records.Map(MapToDto);
    }

    public async Task<PavedRoadRecordDto?> UpdateAsync(string id, UpdatePavedRoadRecordDto dto, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Paved road record ID is required.", nameof(id));
        }

        var fields = ValidateFields(dto);

        var record = await _pavedRoadRecordRepository.GetByIdAsync(id, cancellationToken);
        if (record == null)
        {
            return null;
        }

        var section = await _roadSectionRepository.GetByIdAsync(record.SectionId, cancellationToken);
        if (section != null)
        {
            EnsureInsideSection(dto, section);
        }

        Apply(record, dto, fields, DateTimeOffset.UtcNow);

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

    private sealed record ValidatedFields(string DistressType, string? Notes);

    /// <summary>Validates the fields shared by create and update. Throws <see cref="ArgumentException"/> naming the offending field.</summary>
    private static ValidatedFields ValidateFields(PavedRoadRecordFieldsDto dto)
    {
        FieldRules.RequireChainageRange(dto.ChainageFrom, dto.ChainageTo, allowEmpty: true, nameof(dto.ChainageFrom), nameof(dto.ChainageTo));

        if (!Enum.IsDefined(dto.SurfaceType))
        {
            throw new ArgumentException("Surface type is not a known surface type.", nameof(dto.SurfaceType));
        }

        FieldRules.RequireText(dto.DistressType, FieldRules.MaxNameLength, "Distress type is required.", nameof(dto.DistressType));
        var distress = DistressCatalog.Canonicalise(dto.DistressType) ?? dto.DistressType.Trim();

        // "None" means the segment was inspected and is sound: degree and extent are 0. Any distress is rated 1-5.
        var (minRating, maxRating) = distress == DistressCatalog.None ? (0, 0) : (1, FieldRules.MaxDegree);
        FieldRules.RequireRange(dto.Degree, minRating, maxRating, nameof(dto.Degree));
        FieldRules.RequireRange(dto.Extent, minRating, maxRating, nameof(dto.Extent));

        FieldRules.RequireRange(dto.RutDepthMm, 0, FieldRules.MaxRutDepthMm, nameof(dto.RutDepthMm));
        FieldRules.RequireRange(dto.Latitude, -90, 90, nameof(dto.Latitude));
        FieldRules.RequireRange(dto.Longitude, -180, 180, nameof(dto.Longitude));
        FieldRules.RequireRange(dto.LengthM, 0, FieldRules.MaxLengthM, nameof(dto.LengthM));
        FieldRules.RequireRange(dto.WidthM, 0, FieldRules.MaxWidthM, nameof(dto.WidthM));
        FieldRules.RequireRange(dto.DepthMm, 0, FieldRules.MaxDepthMm, nameof(dto.DepthMm));

        FieldRules.RequireMaxLength(dto.RidingQuality, FieldRules.MaxNameLength, nameof(dto.RidingQuality));
        FieldRules.RequireMaxLength(dto.SkidResistance, FieldRules.MaxNameLength, nameof(dto.SkidResistance));
        FieldRules.RequireMaxLength(dto.StdRef, FieldRules.MaxNameLength, nameof(dto.StdRef));
        FieldRules.RequireMaxLength(dto.RecommendedAction, FieldRules.MaxRecommendedActionLength, nameof(dto.RecommendedAction));

        var imagePaths = dto.ImagePaths ?? [];
        if (imagePaths.Length > FieldRules.MaxImagePaths || imagePaths.Any(p => string.IsNullOrWhiteSpace(p) || p.Length > FieldRules.MaxImagePathLength))
        {
            throw new ArgumentException(
                $"At most {FieldRules.MaxImagePaths} image paths of up to {FieldRules.MaxImagePathLength} characters are allowed.", nameof(dto.ImagePaths));
        }

        return new ValidatedFields(distress, FieldRules.CleanNotes(dto.Notes, nameof(dto.Notes)));
    }

    private static void EnsureInsideSection(PavedRoadRecordFieldsDto dto, RoadSection section)
    {
        if (dto.ChainageFrom < section.ChainageFrom - FieldRules.ChainageTolerance ||
            dto.ChainageTo > section.ChainageTo + FieldRules.ChainageTolerance)
        {
            throw new ArgumentException(
                $"Km {dto.ChainageFrom}–{dto.ChainageTo} is outside section '{section.SectionName}' (km {section.ChainageFrom}–{section.ChainageTo}).",
                nameof(dto.ChainageFrom));
        }
    }

    private static void Apply(PavedRoadRecord record, PavedRoadRecordFieldsDto dto, ValidatedFields fields, DateTimeOffset now)
    {
        record.ChainageFrom = dto.ChainageFrom;
        record.ChainageTo = dto.ChainageTo;
        record.SurfaceType = dto.SurfaceType;
        record.DistressType = fields.DistressType;
        record.Degree = dto.Degree;
        record.Extent = dto.Extent;
        record.RutDepthMm = dto.RutDepthMm;
        record.RidingQuality = dto.RidingQuality ?? string.Empty;
        record.SkidResistance = dto.SkidResistance ?? string.Empty;
        record.StdRef = dto.StdRef ?? string.Empty;
        // An explicit action (e.g. an engineer's override) is kept; otherwise the shared rules decide.
        record.RecommendedAction = string.IsNullOrWhiteSpace(dto.RecommendedAction)
            ? RecommendedActionRules.Recommend(fields.DistressType, dto.Degree, dto.Extent)
            : dto.RecommendedAction.Trim();
        record.Latitude = dto.Latitude;
        record.Longitude = dto.Longitude;
        record.ImagePaths = dto.ImagePaths ?? [];
        record.Notes = fields.Notes;
        record.LengthM = dto.LengthM;
        record.WidthM = dto.WidthM;
        record.DepthMm = dto.DepthMm;
        record.UpdatedAt = now;
    }

    internal static PavedRoadRecordDto MapToDto(PavedRoadRecord record)
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
            Notes = record.Notes,
            LengthM = record.LengthM,
            WidthM = record.WidthM,
            DepthMm = record.DepthMm,
            CreatedBy = record.CreatedBy,
            CreatedAt = record.CreatedAt,
            UpdatedAt = record.UpdatedAt
        };
    }
}
