using System.Data;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using DiscordEventService.Configuration;
using DiscordEventService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace DiscordEventService.Services.Conversation;

// Backs the query_database tool (#238 §4): runs ONE model-written read-only SELECT and returns the
// rows as JSON. It reuses EF's connection, but the app login is a Postgres SUPERUSER — so a read-only
// transaction alone is NOT enough (a superuser can still call pg_read_file, pg_terminate_backend, …,
// none of which are data writes). The guard therefore, before any model SQL runs, makes the txn read
// only AND switches to a non-superuser SELECT-only role (SET LOCAL ROLE) that lacks those privileges;
// the role + txn reset on the always-RollbackAsync. Writes are rejected (25006), privileged/file
// functions are rejected (permission denied), runaway queries are bounded by a client CommandTimeout
// under a server statement_timeout, and rows + per-value lengths are capped. bigint columns (Discord
// snowflakes) are stringified for precision. The result is untrusted DATA, never instructions.
internal sealed partial class DatabaseQueryService(
    DiscordDbContext db,
    IOptions<ConversationOptions> options,
    ILogger<DatabaseQueryService> logger)
{
    // The role from config is embedded into SET LOCAL ROLE as a quoted identifier, so validate its shape
    // (it should be a plain identifier; the AddConversationQueryRole migration provisions the default).
    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex QueryRoleNameRegex();

    // Cap on a single field's serialized length, so a SELECT repeat('x', 1e9) can't OOM us — the row
    // cap bounds row COUNT, this bounds row WIDTH.
    private const int MaxFieldLength = 2000;
    private const string TruncationMarker = "…[truncated]";

    // Fail fast instead of blocking if the model's SQL contends on a lock (e.g. an advisory lock or a
    // row lock another transaction holds) — an analytics read should never wait on a writer.
    private const string LockTimeout = "3s";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // The model reads the JSON; relaxed escaping keeps Polish text legible and the payload small.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        // float8/float4 can be NaN/±Infinity; default Strict handling would throw mid-serialize, and that
        // throw escapes the DB-exception catch filters — emit them as named literals instead.
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public async Task<string> ExecuteAsync(string? sql, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!QueryRoleNameRegex().IsMatch(settings.QueryRoleName))
            return "Database querying is misconfigured (invalid query role) and is unavailable.";

        var trimmed = sql?.Trim() ?? string.Empty;
        if (ValidateSingleSelect(trimmed) is { } guardError)
            return guardError;

        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        try
        {
            if (openedHere)
                await connection.OpenAsync(cancellationToken);

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            // First thing in the txn, before any model SQL: read-only AND drop superuser by switching to
            // the SELECT-only role; bound the server-side runtime. SET LOCAL scopes all of it to the txn,
            // so RollbackAsync restores EF's connection to its read-write superuser self. (The role name
            // is config, validated above, so embedding it as a quoted identifier is injection-safe.)
            var guardSql =
                $"SET TRANSACTION READ ONLY; SET LOCAL ROLE \"{settings.QueryRoleName}\"; "
                + $"SET LOCAL statement_timeout = '{settings.QueryServerTimeoutSeconds}s'; "
                + $"SET LOCAL lock_timeout = '{LockTimeout}'";
            await using (var guard = new NpgsqlCommand(guardSql, connection, transaction))
                await guard.ExecuteNonQueryAsync(cancellationToken);

            string json;
            await using (var command = new NpgsqlCommand(trimmed, connection, transaction)
            {
                CommandTimeout = settings.QueryTimeoutSeconds,
            })
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                json = await ReadRowsAsJsonAsync(reader, settings.QueryRowLimit, cancellationToken);
            }

            // The reader is closed; roll back — nothing to commit, and this resets the role + txn flags.
            await transaction.RollbackAsync(cancellationToken);
            return json;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The whole turn was cancelled (shutdown / turn timeout) — let it unwind.
            throw;
        }
        catch (PostgresException ex)
        {
            // A write rejected by the read-only txn (25006), a privileged function rejected for the
            // non-superuser role (42501), a syntax error, a server statement_timeout (57014) — hand the
            // DB's own message back so the model can self-correct.
            logger.LogInformation("query_database rejected: {SqlState} {Message}", ex.SqlState, ex.MessageText);
            return $"SQL error [{ex.SqlState}]: {ex.MessageText}";
        }
        catch (Exception ex) when (ex is NpgsqlException or TimeoutException or OperationCanceledException)
        {
            // Client-side CommandTimeout and connection faults land here.
            logger.LogWarning(ex, "query_database failed");
            return "The query was stopped (it ran too long or the connection failed). Narrow it and retry.";
        }
        finally
        {
            // Leave EF's connection exactly as we found it.
            if (openedHere && connection.State == ConnectionState.Open)
                await connection.CloseAsync();
        }
    }

    // Defence-in-depth, NOT the security boundary (the read-only txn + non-superuser role are). Keeps the
    // tool's contract a single SELECT so behaviour is predictable and stacked statements never reach the wire.
    private static string? ValidateSingleSelect(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return "Provide a SQL query: a single read-only SELECT (or WITH … SELECT) statement.";

        // One statement only: drop a single trailing ';', then reject any that remain. (A ';' inside a
        // string literal would be over-rejected — acceptable for an analytics tool; the model rephrases.)
        var normalized = sql.TrimEnd().TrimEnd(';').TrimEnd();
        if (normalized.Contains(';'))
            return "Only a single statement is allowed — send one SELECT without ';' separators.";

        if (!StartsWithKeyword(normalized, "SELECT") && !StartsWithKeyword(normalized, "WITH"))
            return "Only read-only queries are allowed: start with SELECT (or a WITH … SELECT CTE).";

        return null;
    }

    private static bool StartsWithKeyword(string sql, string keyword) =>
        sql.Length >= keyword.Length
        && sql.AsSpan(0, keyword.Length).Equals(keyword, StringComparison.OrdinalIgnoreCase)
        && (sql.Length == keyword.Length || !char.IsLetterOrDigit(sql[keyword.Length]));

    private static async Task<string> ReadRowsAsJsonAsync(
        NpgsqlDataReader reader, int rowLimit, CancellationToken cancellationToken)
    {
        var fieldCount = reader.FieldCount;
        var names = new string[fieldCount];
        for (var i = 0; i < fieldCount; i++)
            names[i] = reader.GetName(i);

        List<Dictionary<string, object?>> rows = [];
        var truncated = false;
        while (await reader.ReadAsync(cancellationToken))
        {
            // Read N rows; the (N+1)th successful read proves more exist, so flag and stop.
            if (rows.Count == rowLimit)
            {
                truncated = true;
                break;
            }

            var row = new Dictionary<string, object?>(fieldCount, StringComparer.Ordinal);
            for (var i = 0; i < fieldCount; i++)
                row[names[i]] = ConvertValue(reader.IsDBNull(i) ? null : reader.GetValue(i));
            rows.Add(row);
        }

        return JsonSerializer.Serialize(new { row_count = rows.Count, truncated, rows }, JsonOptions);
    }

    // Returns only JSON-safe shapes (null / bool / number / string / string[]) so STJ can never throw on
    // an exotic Npgsql type (inet -> IPAddress, ranges, …). bigint snowflakes -> strings (precision);
    // timestamps -> ISO-8601; strings/byte-hex are length-capped; anything unrecognised is stringified.
    private static object? ConvertValue(object? value) => value switch
    {
        null or DBNull => null,
        bool b => b,
        long l => l.ToString(CultureInfo.InvariantCulture),
        ulong u => u.ToString(CultureInfo.InvariantCulture), // defensive: this DB maps snowflakes to bigint, not uint8
        decimal d => d.ToString(CultureInfo.InvariantCulture),
        int or short or byte or sbyte or uint or ushort or double or float => value, // STJ-safe JSON numbers
        string s => Cap(s),
        DateTime dt => dt.ToString("o", CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.ToString("o", CultureInfo.InvariantCulture),
        Guid g => g.ToString(),
        long[] longs => longs.Select(v => v.ToString(CultureInfo.InvariantCulture)).ToArray(),
        string[] strings => strings.Select(Cap).ToArray(),
        byte[] bytes => Cap(Convert.ToHexString(bytes)),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => Cap(value.ToString() ?? string.Empty),
    };

    private static string Cap(string value) =>
        value.Length <= MaxFieldLength ? value : value[..MaxFieldLength] + TruncationMarker;
}
