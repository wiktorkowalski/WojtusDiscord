namespace DiscordEventService.Infrastructure;

// Logical column type from EF metadata — drives both raw-SQL value coercion and frontend cell rendering.
public enum ColumnKind
{
    String,
    Int,
    Long,
    Snowflake,
    Bool,
    Timestamp,
    Uuid,
    Json,
    Number,
    Enum,
    Other,
}
