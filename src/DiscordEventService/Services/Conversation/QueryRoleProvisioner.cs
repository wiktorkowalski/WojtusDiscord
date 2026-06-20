using System.Text.RegularExpressions;
using Npgsql;

namespace DiscordEventService.Services.Conversation;

// Provisions the non-superuser, NOLOGIN role that query_database drops into via SET LOCAL ROLE
// (#238 §4). The app connects as a Postgres superuser, so a read-only transaction alone does NOT
// stop privileged side effects (pg_read_file, pg_terminate_backend, …); switching to a role that
// holds only SELECT — and is NOT a superuser — is what removes them. The DDL must run as the owner
// (so GRANT/ALTER DEFAULT PRIVILEGES cover the tables/views its migrations create) and is idempotent.
// In Development the bot runs it automatically; in production it is a documented one-time manual step
// (CREATE ROLE is a privileged write against the read-only-by-convention prod DB). The role is
// NOLOGIN: it is only ever assumed via SET ROLE by the already-authenticated app login, never dialled
// into directly, so it needs no password.
internal static partial class QueryRoleProvisioner
{
    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex RoleNameRegex();

    private const string RoleTag = "ro_role";
    private const string BlockTag = "ro_provision";

    public static bool IsValidRoleName(string roleName) => RoleNameRegex().IsMatch(roleName);

    // Single source of truth for the role's grants: used by the dev auto-provisioner and the documented
    // prod step (substitute the role name). format(%I) quotes the (already validated) role identifier.
    public static string BuildProvisioningSql(string roleName)
    {
        if (!IsValidRoleName(roleName))
            throw new ArgumentException(
                $"Invalid query role name '{roleName}'. Use [A-Za-z_][A-Za-z0-9_]*.", nameof(roleName));

        return $"""
            DO ${BlockTag}$
            DECLARE
                v_role text := ${RoleTag}${roleName}${RoleTag}$;
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = v_role) THEN
                    EXECUTE format('CREATE ROLE %I NOSUPERUSER NOLOGIN NOCREATEDB NOCREATEROLE', v_role);
                END IF;

                -- Use the schema, but never create objects in it.
                EXECUTE format('GRANT USAGE ON SCHEMA public TO %I', v_role);
                EXECUTE format('REVOKE CREATE ON SCHEMA public FROM %I', v_role);

                -- SELECT on every existing relation — base tables AND the analytics views (ALL TABLES
                -- includes views) — plus everything the owner's future migrations create.
                EXECUTE format('GRANT SELECT ON ALL TABLES IN SCHEMA public TO %I', v_role);
                EXECUTE format(
                    'ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT ON TABLES TO %I', v_role);
            END
            ${BlockTag}$;
            """;
    }

    public static async Task ProvisionAsync(
        string ownerConnectionString, string roleName, CancellationToken cancellationToken)
    {
        var sql = BuildProvisioningSql(roleName);
        await using var connection = new NpgsqlConnection(ownerConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
