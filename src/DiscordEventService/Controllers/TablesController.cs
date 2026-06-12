using System.Data;
using DiscordEventService.Data;
using DiscordEventService.Dtos;
using DiscordEventService.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DiscordEventService.Controllers;

// SECURITY: table, sort, and filter-column identifiers are validated against the
// SchemaCatalog whitelist and only the catalog's trusted literal is ever emitted into
// SQL. Filter VALUES and paging are bound as parameters. Client text is never
// interpolated as an identifier.
[ApiController]
[Route("api/tables")]
public sealed class TablesController(DiscordDbContext db, SchemaCatalog catalog) : ControllerBase
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<TableInfoDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TableInfoDto>>> GetTables(CancellationToken ct)
    {
        var counts = await GetApproxRowCountsAsync(ct);

        var tables = catalog.Tables
            .Select(t =>
            {
                var count = counts.GetValueOrDefault(t.TableName, 0);
                return new TableInfoDto(t.TableName, t.DisplayName, t.EntityName, count, count > 0);
            })
            .OrderByDescending(t => t.RowCount)
            .ThenBy(t => t.DisplayName, StringComparer.Ordinal)
            .ToList();

        return Ok(tables);
    }

    [HttpGet("{table}/columns")]
    [ProducesResponseType<IReadOnlyList<ColumnMetadataDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<IReadOnlyList<ColumnMetadataDto>> GetColumns(string table)
    {
        if (!catalog.TryGetTable(table, out var meta))
            return BadRequest(new { error = $"Unknown table '{table}'." });

        return Ok(meta.Columns.Select(ColumnMetadataDto.From).ToList());
    }

    [HttpGet("{table}")]
    [ProducesResponseType(typeof(PagedResult<Dictionary<string, object>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<Dictionary<string, object?>>>> GetRows(
        string table,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        [FromQuery] string? sort = null,
        [FromQuery] string? dir = null,
        [FromQuery] string? filterColumn = null,
        [FromQuery] string? filter = null,
        CancellationToken ct = default)
    {
        if (!catalog.TryGetTable(table, out var meta))
            return BadRequest(new { error = $"Unknown table '{table}'." });

        if (sort is not null && !meta.HasColumn(sort))
            return BadRequest(new { error = $"Unknown sort column '{sort}'." });

        if (filterColumn is not null && !meta.HasColumn(filterColumn))
            return BadRequest(new { error = $"Unknown filter column '{filterColumn}'." });

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var offset = (long)(page - 1) * pageSize;

        // Identifiers below come from the catalog (trusted model literals), never the
        // raw client string — even though they were validated to match a column above.
        var quotedTable = Quote(meta.TableName);
        var sortColumn = sort is not null ? meta.Column(sort)!.Name : meta.DefaultSortColumn;
        var direction = string.Equals(dir, "asc", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC";

        var hasFilter = !string.IsNullOrEmpty(filter) && filterColumn is not null;
        var where = string.Empty;
        if (hasFilter)
        {
            var filterCol = Quote(meta.Column(filterColumn!)!.Name);
            where = $" WHERE CAST({filterCol} AS TEXT) ILIKE @filter";
        }

        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(ct);

        var total = await CountAsync(connection, quotedTable, where, filter, ct);

        var rows = new List<Dictionary<string, object?>>(pageSize);
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText =
                $"SELECT * FROM {quotedTable}{where} " +
                $"ORDER BY {Quote(sortColumn)} {direction} LIMIT @limit OFFSET @offset";
            if (hasFilter)
                cmd.Parameters.AddWithValue("filter", $"%{filter}%");
            cmd.Parameters.AddWithValue("limit", pageSize);
            cmd.Parameters.AddWithValue("offset", offset);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var row = new Dictionary<string, object?>(reader.FieldCount, StringComparer.Ordinal);
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    var name = reader.GetName(i);
                    var value = await reader.IsDBNullAsync(i, ct) ? null : reader.GetValue(i);

                    // Snowflakes are stored as bigint; box back to ulong so the global
                    // JSON converter emits them as strings (JS number precision).
                    if (value is long l && meta.Column(name)?.Kind == ColumnKind.Snowflake)
                        value = unchecked((ulong)l);

                    row[name] = value;
                }
                rows.Add(row);
            }
        }

        return Ok(new PagedResult<Dictionary<string, object?>>(rows, total, page, pageSize));
    }

    private static async Task<long> CountAsync(
        NpgsqlConnection connection, string quotedTable, string where, string? filter, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {quotedTable}{where}";
        if (where.Length > 0)
            cmd.Parameters.AddWithValue("filter", $"%{filter}%");
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is long l ? l : Convert.ToInt64(result);
    }

    private async Task<Dictionary<string, long>> GetApproxRowCountsAsync(CancellationToken ct)
    {
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT relname, n_live_tup FROM pg_stat_user_tables";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            counts[reader.GetString(0)] = reader.GetInt64(1);
        }
        return counts;
    }

    private static string Quote(string identifier) => $"\"{identifier}\"";
}
