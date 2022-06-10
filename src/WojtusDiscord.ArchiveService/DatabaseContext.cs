using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace WojtusDiscord.ArchiveService
{
    public class DatabaseContext : DbContext
    {
        public DbSet<Activity> Activities { get; set; }

        public DatabaseContext(DbContextOptions<DatabaseContext> options) : base(options)
        {
        }
    }

    public class Activity
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Key]
        public Guid Id { get; set; }
        public ActivityType ActivityType { get; set; }
        public string? Message { get; set; }
        public string? Channel { get; set; }
        public string? User { get; set; }
        public string? Reaction { get; set; }
        
    }

    public enum ActivityType
    {
        Message,
        Reaction,
        User
    }
}
