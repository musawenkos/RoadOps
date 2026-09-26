using Microsoft.AspNetCore.Mvc;
using RoadOps.Application.Common;
using RoadOps.Application.DTOs;
using RoadOps.Application.Services;

namespace RoadOps.Api.Controllers;

[ApiController]
[Route("api/paved-road-records")]
public class PavedRoadRecordsController : ControllerBase
{
    private readonly PavedRoadRecordService _pavedRoadRecordService;

    public PavedRoadRecordsController(PavedRoadRecordService pavedRoadRecordService)
    {
        _pavedRoadRecordService = pavedRoadRecordService;
    }

    [HttpPost]
    public async Task<ActionResult<PavedRoadRecordDto>> Create([FromBody] CreatePavedRoadRecordDto dto, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _pavedRoadRecordService.CreateAsync(dto, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<PavedRoadRecordDto>> GetById(string id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _pavedRoadRecordService.GetByIdAsync(id, cancellationToken);
            if (result == null)
            {
                return NotFound();
            }
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<PavedRoadRecordDto>>> GetAll([FromQuery] int page = PageRequest.DefaultPage, [FromQuery] int pageSize = PageRequest.DefaultPageSize, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _pavedRoadRecordService.GetPageAsync(new PageRequest(page, pageSize), cancellationToken);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("workspace/{workspaceId}")]
    public async Task<ActionResult<PagedResult<PavedRoadRecordDto>>> GetByWorkspaceId(string workspaceId, [FromQuery] int page = PageRequest.DefaultPage, [FromQuery] int pageSize = PageRequest.DefaultPageSize, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _pavedRoadRecordService.GetByWorkspaceIdAsync(workspaceId, new PageRequest(page, pageSize), cancellationToken);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("section/{sectionId}")]
    public async Task<ActionResult<PagedResult<PavedRoadRecordDto>>> GetBySectionId(string sectionId, [FromQuery] int page = PageRequest.DefaultPage, [FromQuery] int pageSize = PageRequest.DefaultPageSize, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _pavedRoadRecordService.GetBySectionIdAsync(sectionId, new PageRequest(page, pageSize), cancellationToken);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("chainage")]
    public async Task<ActionResult<PagedResult<PavedRoadRecordDto>>> FindByChainageRange(
        [FromQuery] double chainageFrom,
        [FromQuery] double chainageTo,
        [FromQuery] string? workspaceId,
        [FromQuery] int page = PageRequest.DefaultPage, [FromQuery] int pageSize = PageRequest.DefaultPageSize, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _pavedRoadRecordService.FindByChainageRangeAsync(chainageFrom, chainageTo, workspaceId, new PageRequest(page, pageSize), cancellationToken);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<PavedRoadRecordDto>> Update(string id, [FromBody] UpdatePavedRoadRecordDto dto, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _pavedRoadRecordService.UpdateAsync(id, dto, cancellationToken);
            if (result == null)
            {
                return NotFound();
            }
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        try
        {
            await _pavedRoadRecordService.DeleteAsync(id, cancellationToken);
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
