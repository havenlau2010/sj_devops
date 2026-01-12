using BuildServer.Core;
using Microsoft.AspNetCore.Mvc;

namespace BuildServerApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BuildsController : ControllerBase
{
    private readonly BuildManager _manager;

    public BuildsController(BuildManager manager)
    {
        _manager = manager;
    }

    [HttpPost("deploy")]
    public async Task<IActionResult> Deploy()
    {
        var result = await _manager.RunWorkflowAsync();
        return Ok(result);
    }

    [HttpGet]
    public IActionResult GetList([FromQuery] int limit = 50)
    {
        if (_manager.Database == null) return StatusCode(503, "Database not initialized");
        var builds = _manager.Database.GetRecentBuilds(limit);
        return Ok(builds);
    }

    [HttpGet("{id}")]
    public IActionResult GetDetails(long id)
    {
        if (_manager.Database == null) return StatusCode(503, "Database not initialized");
        var details = _manager.Database.GetProjectBuildRecords(id);
        return Ok(details);
    }

    [HttpGet("{id}/log")]
    public IActionResult GetBuildLog(long id)
    {
        if (_manager.Database == null) return StatusCode(503, "Database not initialized");
        var log = _manager.Database.GetBuildLogContent(id);
        if (log == null) return NotFound("Log not found");
        return Content(log, "text/plain");
    }

    [HttpGet("projects/{id}/log")]
    public IActionResult GetProjectLog(long id)
    {
        if (_manager.Database == null) return StatusCode(503, "Database not initialized");
        var log = _manager.Database.GetProjectBuildLogContent(id);
        if (log == null) return NotFound("Log not found");
        return Content(log, "text/plain");
    }
}
