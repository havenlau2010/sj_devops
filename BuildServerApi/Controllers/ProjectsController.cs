using BuildServer.Core;
using Microsoft.AspNetCore.Mvc;

namespace BuildServerApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProjectsController : ControllerBase
{
    private readonly BuildManager _manager;

    public ProjectsController(BuildManager manager)
    {
        _manager = manager;
    }

    [HttpGet]
    public IEnumerable<ProjectConfig> Get()
    {
        return _manager.Projects;
    }

    [HttpPost]
    public IActionResult Add(ProjectConfig project)
    {
        _manager.AddProject(project);
        return Ok(project);
    }

    [HttpPost("save")]
    public IActionResult Save()
    {
        _manager.SaveConfig();
        return Ok("Configuration saved.");
    }
}
