using ActivityArchiveService.Database;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ActivityArchiveService.Controllers;

[ApiController]
[Route("[controller]")]
public class PingController : ControllerBase
{
    private readonly ActivityArchiveContext _context;

    public PingController(ActivityArchiveContext context)
    {
        _context = context;
        _context.Database.IsNpgsql();
    }
    
    [HttpGet("ping", Name = "Ping")]
    public IActionResult Ping()
    {
        return Ok(new { result = "ActivityArchiveService is running" });
    }
}