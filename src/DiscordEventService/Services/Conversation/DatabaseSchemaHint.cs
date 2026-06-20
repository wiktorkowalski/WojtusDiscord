using System.Text;
using DiscordEventService.Infrastructure;

namespace DiscordEventService.Services.Conversation;

// A compact, model-facing rendering of the database schema for the query_database tool description
// (#238 §4), built once at startup from the same SchemaCatalog the dashboard explorer uses (so it
// stays in lock-step with the EF model). One line per table; only the non-obvious column kinds are
// annotated (ids, timestamps, enums with their values) so the model writes correct joins/filters
// without bloating every round's tool payload. The text is stable, so it is built once and cached.
internal sealed record DatabaseSchemaHint(string Text)
{
    public static DatabaseSchemaHint Build(SchemaCatalog catalog)
    {
        var builder = new StringBuilder();
        foreach (var table in catalog.Tables.OrderBy(t => t.TableName, StringComparer.Ordinal))
        {
            builder.Append(table.TableName).Append('(');
            builder.AppendJoin(", ", table.Columns.Select(DescribeColumn));
            builder.Append(')').Append('\n');
        }

        return new DatabaseSchemaHint(builder.ToString().TrimEnd('\n'));
    }

    private static string DescribeColumn(ColumnMeta column)
    {
        var annotation = column.Kind switch
        {
            // Snowflakes/bigints come back as JSON strings; flag the ids the model joins/filters on.
            ColumnKind.Snowflake or ColumnKind.Long => ":id",
            ColumnKind.Timestamp => ":ts",
            ColumnKind.Uuid => ":uuid",
            ColumnKind.Json => ":json",
            ColumnKind.Enum => ":enum(" + DescribeEnum(column) + ")",
            _ => string.Empty,
        };
        return column.Name + annotation;
    }

    private static string DescribeEnum(ColumnMeta column) =>
        column.EnumValues is { Count: > 0 } values
            ? string.Join("|", values.Select(v => $"{v.Value}={v.Name}"))
            : string.Empty;
}
