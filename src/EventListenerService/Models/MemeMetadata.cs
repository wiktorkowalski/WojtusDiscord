using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EventListenerService.Models
{
    public class MemeMetadata
    {
        [Key]
        public required Guid Id { get; set; }

        [ForeignKey(nameof(MemeMessage))]
        public required ulong MessageId { get; set; }

        public required MemeMessage MemeMessage { get; set; }

        public string? Description { get; set; }
        public string? TextContent { get; set; }
        public string[]? Keywords { get; set; }
        public string[]? Objects { get; set; }
        public string? Tone { get; set; }
    }
}
