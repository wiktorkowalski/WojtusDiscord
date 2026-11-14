#!/bin/sh
# Prod database backup to Cloudflare R2 and a weekly restore drill (#332).
#
#   loop    (default) a backup every day at BACKUP_AT (HH:MM UTC); after the Sunday backup, a drill.
#   backup  one backup now, then exit:  docker compose run --rm db-backup backup
#   drill   one drill now, then exit:   docker compose run --rm db-backup drill
#
# Backup: pg_dump -Fc plus the roles (pg_dump skips them: role wojtus_query, #343) to daily/;
# the Sunday one is copied to weekly/, the one of the 1st to monthly/. R2 lifecycle rules
# expire each prefix (cloudflare-terraform wojtusdiscord-backups.tf), so nothing is deleted here.
# Drill: download the newest dump, restore it into a throwaway server inside this container,
# compare row counts with prod.
# healthchecks.io gets a success ping only after a verified upload or a passed drill, /fail otherwise.
set -eu
# Dumps hold message and DM content: owner-only files.
umask 077

# Remotes come from the environment; no config file.
export RCLONE_LOG_LEVEL=ERROR
export RCLONE_CONFIG_R2_TYPE=s3 RCLONE_CONFIG_R2_PROVIDER=Cloudflare RCLONE_CONFIG_R2_NO_CHECK_BUCKET=true
export RCLONE_CONFIG_R2_ACCESS_KEY_ID="${R2_ACCESS_KEY_ID:-}" RCLONE_CONFIG_R2_SECRET_ACCESS_KEY="${R2_SECRET_ACCESS_KEY:-}"
export RCLONE_CONFIG_R2_ENDPOINT="https://${R2_ACCOUNT_ID:-}.r2.cloudflarestorage.com"
# A local path also works here, for a test without R2.
REMOTE="${BACKUP_REMOTE:-r2:wojtusdiscord-backups}"
DUMPS=/dumps
# Restored rows may lag prod by what the bot wrote since the dump; the loop drills minutes after it.
DRILL_MIN_PERCENT="${DRILL_MIN_PERCENT:-99}"

log() { echo "db-backup: $*"; }

ping() {
    [ -n "$1" ] || { log "no ping URL set" >&2; return 0; }
    curl -fsS -m 10 --retry 5 -o /dev/null --data-raw "$2" "$1" || log "ping failed" >&2
}

# Rows per base table, "schema.table|count", sorted. count(*), never the stale n_live_tup.
count_rows() {
    psql -v ON_ERROR_STOP=1 -qAt -c "
        SELECT table_schema || '.' || table_name || '|' ||
               (xpath('/row/c/text()', query_to_xml(format('SELECT count(*) AS c FROM %I.%I', table_schema, table_name), false, true, '')))[1]::text
        FROM information_schema.tables
        WHERE table_type = 'BASE TABLE' AND table_schema NOT IN ('pg_catalog', 'information_schema')
        ORDER BY 1"
}

# Explicit returns: set -e does not apply inside a function called with if or ||.
backup() {
    name="wojtusdiscord-$(date -u +%Y-%m-%d)"
    dump="$DUMPS/$name.dump"
    roles="$DUMPS/$name.roles.sql"
    pg_dump --format=custom --file="$dump.partial" || { rm -f "$dump.partial"; return 1; }
    # The archive must be readable before it replaces anything.
    pg_restore --list "$dump.partial" >/dev/null || return 1
    mv "$dump.partial" "$dump" || return 1
    pg_dumpall --roles-only --no-role-passwords --file="$roles" || return 1
    for f in "$dump" "$roles"; do
        # copyto compares size and hash after the upload and fails on a mismatch.
        rclone copyto "$f" "$REMOTE/daily/${f##*/}" || return 1
    done
    for period in weekly monthly; do
        if { [ "$period" = weekly ] && [ "$(date -u +%u)" = 7 ]; } || { [ "$period" = monthly ] && [ "$(date -u +%d)" = 01 ]; }; then
            for f in "$dump" "$roles"; do
                rclone copyto "$REMOTE/daily/${f##*/}" "$REMOTE/$period/${f##*/}" || return 1
            done
        fi
    done
    # Two nights stay on the host for a fast restore; other files in the directory are not touched.
    ls -1t "$DUMPS"/wojtusdiscord-*.dump | tail -n +3 | while read -r old; do
        rm -f "$old" "${old%.dump}.roles.sql"
    done
    summary="$name.dump $(du -h "$dump" | cut -f1) uploaded"
    log "$summary"
}

drill() {
    work=$(mktemp -d)
    trap 'pg_ctl -D "$work/data" -m immediate stop >/dev/null 2>&1 || true; rm -rf "$work"' EXIT
    latest=$(rclone lsf "$REMOTE/daily/" --include '*.dump' | sort | tail -n 1)
    [ -n "$latest" ] || { log "no dump in $REMOTE/daily/" >&2; return 1; }
    rclone copyto "$REMOTE/daily/$latest" "$work/$latest" || return 1
    rclone copyto "$REMOTE/daily/${latest%.dump}.roles.sql" "$work/roles.sql" || return 1
    count_rows > "$work/live.txt" || return 1
    # The subshell gets its own PG* settings: nothing in it can reach prod.
    # && chains: set -e is off on the left of ||.
    (
        export PGHOST="$work" PGPORT=5499 PGUSER=postgres PGDATABASE=drill
        unset PGPASSWORD
        initdb --pgdata="$work/data" --username=postgres --auth=trust >/dev/null &&
        pg_ctl -D "$work/data" -o "-p 5499 -k $work -c listen_addresses=" -w -l "$work/server.log" start >/dev/null &&
        # The bootstrap role exists already.
        grep -v '^CREATE ROLE postgres;' "$work/roles.sql" | psql -v ON_ERROR_STOP=1 -q -d postgres >/dev/null &&
        createdb drill &&
        pg_restore --exit-on-error --dbname=drill "$work/$latest" &&
        count_rows > "$work/restored.txt"
    ) || { tail -n 20 "$work/server.log" >&2 2>/dev/null; return 1; }
    cut -d'|' -f1 "$work/live.txt" > "$work/live.tables"
    cut -d'|' -f1 "$work/restored.txt" > "$work/restored.tables"
    if ! diff "$work/live.tables" "$work/restored.tables" >&2; then
        log "drill: table lists differ (< prod, > restored)" >&2
        return 1
    fi
    tables=$(wc -l < "$work/live.tables" | tr -d ' ')
    live=$(awk -F'|' '{ s += $2 } END { print s + 0 }' "$work/live.txt")
    restored=$(awk -F'|' '{ s += $2 } END { print s + 0 }' "$work/restored.txt")
    summary="drill $latest: $tables tables, $restored of $live prod rows restored"
    if [ "$tables" -eq 0 ] || [ $((restored * 100)) -lt $((live * DRILL_MIN_PERCENT)) ]; then
        log "$summary, below ${DRILL_MIN_PERCENT}%" >&2
        return 1
    fi
    log "$summary"
}

run() {  # run backup|drill: the ping URL depends on the job
    if [ "$1" = backup ]; then url="${HC_BACKUP_URL:-}"; else url="${HC_RESTORE_URL:-}"; fi
    # A subshell: the drill's EXIT trap cleans up when the job ends, not when the loop does.
    if out=$( "$1" 2>&1 ); then
        echo "$out"
        ping "$url" "$(echo "$out" | tail -n 1)"
    else
        echo "$out" >&2
        log "$1 failed" >&2
        ping "${url:+$url/fail}" "$(echo "$out" | tail -n 20)"
        return 1
    fi
}

case "${1:-loop}" in
    backup|drill) run "$1" ;;
    loop)
        : "${BACKUP_AT:?BACKUP_AT is HH:MM UTC}"
        while true; do
            now=$(date -u +%s)
            next=$(date -u -d "$(date -u +%Y-%m-%d) $BACKUP_AT:00" +%s)
            [ "$next" -le "$now" ] && next=$((next + 86400))
            sleep $((next - now))
            # A failure is logged and pinged; the loop keeps running.
            run backup || true
            [ "$(date -u +%u)" = 7 ] && { run drill || true; }
        done ;;
    *) echo "usage: db-backup [loop|backup|drill]" >&2; exit 2 ;;
esac
