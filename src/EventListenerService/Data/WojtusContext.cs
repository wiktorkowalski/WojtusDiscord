using EventListenerService.Models;
using Microsoft.EntityFrameworkCore;

namespace EventListenerService.Data;

public class WojtusContext(DbContextOptions<WojtusContext> options) : DbContext(options)
{
  public DbSet<UserEmoji> UserEmojis { get; set; }

  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    modelBuilder.Entity<UserEmoji>();
  }
}
