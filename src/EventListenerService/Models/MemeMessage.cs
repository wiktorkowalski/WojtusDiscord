using System;

namespace EventListenerService.Models
{
    public class MemeMessage
    {
        [System.ComponentModel.DataAnnotations.Key]
        public ulong MessageId { get; set; }
        public ulong ChannelId { get; set; }
        public ulong AuthorId { get; set; }
        public string AuthorUsername { get; set; }
        public string Content { get; set; }
        public DateTime Timestamp { get; set; }
        public string? ImageUrl { get; set; }
        public string? ImageUrlProxy { get; set; }

        public MemeMetadata? Metadata { get; set; }
    }
}
