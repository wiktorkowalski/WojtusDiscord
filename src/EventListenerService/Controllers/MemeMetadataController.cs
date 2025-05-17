using EventListenerService.Data;
using EventListenerService.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EventListenerService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MemeMetadataController : ControllerBase
    {
        private readonly WojtusContext _context;
        private readonly IMemeMetadataGenerationService _metadataService;

        public MemeMetadataController(WojtusContext context, IMemeMetadataGenerationService metadataService)
        {
            _context = context;
            _metadataService = metadataService;
        }

        // POST: api/MemeMetadata/GenerateLatest?count=10
        [HttpPost("GenerateLatest")]
        public async Task<IActionResult> GenerateLatest([FromQuery] int count = 1)
        {
            var latestMemes = await _context.MemeMessages
                .Where(m => m.ImageUrl != null)
                .Include(m => m.Metadata)
                .OrderByDescending(m => m.Timestamp)
                .Take(count)
                .Where(m => m.Metadata == null)
                .ToListAsync();

            await _metadataService.GenerateMetadataForMemesAsync(latestMemes, HttpContext.RequestAborted);
            
            return Ok(new { Message = $"Started metadata generation for {latestMemes.Count} latest memes." });
        }
    }
}
