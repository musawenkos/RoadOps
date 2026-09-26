using RoadOps.Application.Common;
using RoadOps.Application.DTOs;
using RoadOps.Application.Repositories;
using RoadOps.Domain.Entities;

namespace RoadOps.Application.Services;

public class WorkspaceService
{
    private readonly IWorkspaceRepository _workspaceRepository;

    public WorkspaceService(IWorkspaceRepository workspaceRepository)
    {
        _workspaceRepository = workspaceRepository;
    }

    public async Task<WorkspaceDto> CreateAsync(CreateWorkspaceDto dto, CancellationToken cancellationToken = default)
    {
        FieldRules.RequireText(dto.Name, FieldRules.MaxNameLength, "Workspace name is required.", nameof(dto.Name));
        FieldRules.RequireText(dto.AssessmentType, FieldRules.MaxNameLength, "Assessment type is required.", nameof(dto.AssessmentType));
        var corridor = FieldRules.NormaliseCorridor(dto.Corridor, nameof(dto.Corridor));
        FieldRules.RequireSurveyYear(dto.SurveyYear, nameof(dto.SurveyYear));
        FieldRules.RequireText(dto.CreatedBy, FieldRules.MaxNameLength, "Created by is required.", nameof(dto.CreatedBy));

        var workspace = new Workspace
        {
            Id = Guid.NewGuid().ToString(),
            Name = dto.Name,
            AssessmentType = dto.AssessmentType,
            Corridor = corridor,
            SurveyYear = dto.SurveyYear,
            Status = Domain.Enum.WorkspaceStatus.Active,
            CreatedBy = dto.CreatedBy,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await _workspaceRepository.AddAsync(workspace, cancellationToken);

        return MapToDto(workspace);
    }

    public async Task<WorkspaceDto?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Workspace ID is required.", nameof(id));
        }

        var workspace = await _workspaceRepository.GetByIdAsync(id, cancellationToken);
        return workspace != null ? MapToDto(workspace) : null;
    }

    public async Task<PagedResult<WorkspaceDto>> GetPageAsync(PageRequest page, CancellationToken cancellationToken = default)
    {
        var workspaces = await _workspaceRepository.GetPageAsync(page.Validate(), cancellationToken);
        return workspaces.Map(MapToDto);
    }

    public async Task<WorkspaceDto?> UpdateAsync(string id, UpdateWorkspaceDto dto, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Workspace ID is required.", nameof(id));
        }

        FieldRules.RequireText(dto.Name, FieldRules.MaxNameLength, "Workspace name is required.", nameof(dto.Name));
        FieldRules.RequireText(dto.AssessmentType, FieldRules.MaxNameLength, "Assessment type is required.", nameof(dto.AssessmentType));
        var corridor = FieldRules.NormaliseCorridor(dto.Corridor, nameof(dto.Corridor));
        FieldRules.RequireSurveyYear(dto.SurveyYear, nameof(dto.SurveyYear));

        if (!Enum.IsDefined(dto.Status))
        {
            throw new ArgumentException("Status is not a known workspace status.", nameof(dto.Status));
        }

        var workspace = await _workspaceRepository.GetByIdAsync(id, cancellationToken);
        if (workspace == null)
        {
            return null;
        }

        workspace.Name = dto.Name;
        workspace.AssessmentType = dto.AssessmentType;
        workspace.Corridor = corridor;
        workspace.SurveyYear = dto.SurveyYear;
        workspace.Status = dto.Status;
        workspace.UpdatedAt = DateTimeOffset.UtcNow;

        await _workspaceRepository.UpdateAsync(workspace, cancellationToken);

        return MapToDto(workspace);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Workspace ID is required.", nameof(id));
        }

        await _workspaceRepository.DeleteAsync(id, cancellationToken);
        return true;
    }

    private static WorkspaceDto MapToDto(Workspace workspace)
    {
        return new WorkspaceDto
        {
            Id = workspace.Id,
            Name = workspace.Name,
            AssessmentType = workspace.AssessmentType,
            Corridor = workspace.Corridor,
            SurveyYear = workspace.SurveyYear,
            Status = workspace.Status,
            CreatedBy = workspace.CreatedBy,
            CreatedAt = workspace.CreatedAt,
            UpdatedAt = workspace.UpdatedAt
        };
    }
}
