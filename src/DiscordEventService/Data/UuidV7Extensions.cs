using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Data;

internal static class UuidV7Extensions
{
    public static void ConfigureUuidGeneration(this ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var idProperty = entityType.FindProperty("Id");
            if (idProperty is not null && idProperty.ClrType == typeof(Guid))
                idProperty.SetDefaultValueSql("uuidv7()");
        }
    }
}
