using EventListenerService.Models;
using Microsoft.EntityFrameworkCore;

namespace EventListenerService.Data;

public class WojtusContext : DbContext
{
  public WojtusContext(DbContextOptions<WojtusContext> options) : base(options) { }
  public WojtusContext() { }

  public DbSet<UserEmoji> UserEmojis { get; set; }
  public DbSet<MemeMessage> MemeMessages { get; set; }
  public DbSet<MemeMetadata> MemeMetadata { get; set; }

  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    modelBuilder.Entity<UserEmoji>();
    modelBuilder.Entity<MemeMessage>();
    modelBuilder.Entity<MemeMetadata>();
  }
}
