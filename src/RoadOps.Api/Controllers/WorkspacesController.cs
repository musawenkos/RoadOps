using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RoadOps.Application.Common;
using RoadOps.Application.DTOs;
using RoadOps.Application.Services;
using RoadOps.Auth;

namespace RoadOps.Api.Controllers;

// Reading needs any key; create and update need editor, delete (a hard, cascading delete) needs admin.
[ApiController]
[Authorize(Roles = ApiKeyRoles.Reader)]
[Route("api/workspaces")]
public class WorkspacesController : ControllerBase
{
    private readonly WorkspaceService _workspaceService;

    public WorkspacesController(WorkspaceService workspaceService)
    {
        _workspaceService = workspaceService;
    }

    [HttpPost]
    [Authorize(Roles = ApiKeyRoles.Editor)]
    public async Task<ActionResult<WorkspaceDto>> Create([FromBody] CreateWorkspaceDto dto, CancellationToken cancellationToken)
    {
        try
        {
            // Who created it comes from the API key, never from the request body.
            dto.CreatedBy = User.Identity!.Name!;
            var result = await _workspaceService.CreateAsync(dto, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.UserMessage());
        }
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<WorkspaceDto>> GetById(string id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _workspaceService.GetByIdAsync(id, cancellationToken);
            if (result == null)
            {
                return NotFound();
            }
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.UserMessage());
        }
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<WorkspaceDto>>> GetAll([FromQuery] int page = PageRequest.DefaultPage, [FromQuery] int pageSize = PageRequest.DefaultPageSize, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _workspaceService.GetPageAsync(new PageRequest(page, pageSize), cancellationToken);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.UserMessage());
        }
    }

    [HttpPut("{id}")]
    [Authorize(Roles = ApiKeyRoles.Editor)]
    public async Task<ActionResult<WorkspaceDto>> Update(string id, [FromBody] UpdateWorkspaceDto dto, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _workspaceService.UpdateAsync(id, dto, cancellationToken);
            if (result == null)
            {
                return NotFound();
            }
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.UserMessage());
        }
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = ApiKeyRoles.Admin)]
    public async Task<ActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        try
        {
            await _workspaceService.DeleteAsync(id, cancellationToken);
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.UserMessage());
        }
    }
}
