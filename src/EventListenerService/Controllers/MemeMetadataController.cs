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

        // GET: api/MemeMetadata/search?query=someword
        [HttpGet("search")]
        public async Task<IActionResult> Search([FromQuery] string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return BadRequest("Query parameter is required.");

            // Use raw SQL to leverage pg_trgm similarity on keywords
            var memesMetadata = await _context.MemeMetadata
                .FromSqlRaw(@"
SELECT *
FROM meme_metadata
WHERE EXISTS (SELECT 1 FROM unnest(keywords) AS kw WHERE kw % {0})
OR EXISTS (SELECT 1 FROM unnest(objects) AS obj WHERE obj % {0})
                ", query)
                .Include(m => m.MemeMessage)
                .ToListAsync();

            return Ok(memesMetadata.Select(m => m.MemeMessage));
        }
    }
}
