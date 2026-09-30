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
[Route("api/road-sections")]
public class RoadSectionsController : ControllerBase
{
    private readonly RoadSectionService _roadSectionService;

    public RoadSectionsController(RoadSectionService roadSectionService)
    {
        _roadSectionService = roadSectionService;
    }

    [HttpPost]
    [Authorize(Roles = ApiKeyRoles.Editor)]
    public async Task<ActionResult<RoadSectionDto>> Create([FromBody] CreateRoadSectionDto dto, CancellationToken cancellationToken)
    {
        try
        {
            // Who created it comes from the API key, never from the request body.
            dto.CreatedBy = User.Identity!.Name!;
            var result = await _roadSectionService.CreateAsync(dto, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.UserMessage());
        }
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<RoadSectionDto>> GetById(string id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _roadSectionService.GetByIdAsync(id, cancellationToken);
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
    public async Task<ActionResult<PagedResult<RoadSectionDto>>> GetAll([FromQuery] int page = PageRequest.DefaultPage, [FromQuery] int pageSize = PageRequest.DefaultPageSize, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _roadSectionService.GetPageAsync(new PageRequest(page, pageSize), cancellationToken);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.UserMessage());
        }
    }

    [HttpGet("workspace/{workspaceId}")]
    public async Task<ActionResult<PagedResult<RoadSectionDto>>> GetByWorkspaceId(string workspaceId, [FromQuery] int page = PageRequest.DefaultPage, [FromQuery] int pageSize = PageRequest.DefaultPageSize, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _roadSectionService.GetByWorkspaceIdAsync(workspaceId, new PageRequest(page, pageSize), cancellationToken);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.UserMessage());
        }
    }

    [HttpPut("{id}")]
    [Authorize(Roles = ApiKeyRoles.Editor)]
    public async Task<ActionResult<RoadSectionDto>> Update(string id, [FromBody] UpdateRoadSectionDto dto, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _roadSectionService.UpdateAsync(id, dto, cancellationToken);
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
            await _roadSectionService.DeleteAsync(id, cancellationToken);
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.UserMessage());
        }
    }
}
