# Restore the prod database

Runbook for #332. Use it when the prod database on orion is broken or lost. The nightly backup and the weekly drill are in `db-backup.sh`.

Rules:
- Never delete the broken database. Rename it and keep it until the restore is verified.
- Every `docker compose run` gets `--no-deps`. Without it compose can recreate `wojtus-postgres`.
- Run all commands on orion, in `~/wojtusdiscord`.

## 1. Stop the bot

```sh
cd ~/wojtusdiscord
# The exporter holds a session too: a rename needs none open.
docker compose stop discord-event-service postgres-exporter db-backup
date -u   # note the time: the gap to backfill starts at the dump, not here
```

## 2. Get a dump

Each backup is two files: `wojtusdiscord-YYYY-MM-DD.dump` and `wojtusdiscord-YYYY-MM-DD.roles.sql`.

- **On orion** (fastest): the last two nights are in `/srv/db-dumps/wojtusdiscord/` (owner uid 999, read with `sudo`).
- **From R2**, when orion lost its disk or the local copy is bad. R2 keeps `daily/` 7 days, `weekly/` 4 weeks, `monthly/` 6 months.

```sh
mkdir -p /tmp/restore && chmod 777 /tmp/restore
docker compose run --rm --no-deps -v /tmp/restore:/restore --entrypoint sh db-backup -c '
  export RCLONE_CONFIG_R2_TYPE=s3 RCLONE_CONFIG_R2_PROVIDER=Cloudflare RCLONE_CONFIG_R2_NO_CHECK_BUCKET=true \
    RCLONE_CONFIG_R2_ACCESS_KEY_ID="$R2_ACCESS_KEY_ID" RCLONE_CONFIG_R2_SECRET_ACCESS_KEY="$R2_SECRET_ACCESS_KEY" \
    RCLONE_CONFIG_R2_ENDPOINT="https://$R2_ACCOUNT_ID.r2.cloudflarestorage.com"
  rclone lsl r2:wojtusdiscord-backups/daily/
  rclone copy r2:wojtusdiscord-backups/daily/ /restore/ --include "wojtusdiscord-YYYY-MM-DD.*"'
```

Without orion, the same files download from the Cloudflare dashboard (R2 → `wojtusdiscord-backups`). The bucket token is in 1Password.

Copy the files into the Postgres container and check that the archive is readable before you touch the database. pg_restore reads a file path, the same as the drill: stdin is not seekable.

```sh
D=wojtusdiscord-YYYY-MM-DD
docker cp /tmp/restore/$D.dump wojtus-postgres:/tmp/
docker cp /tmp/restore/$D.roles.sql wojtus-postgres:/tmp/
docker exec wojtus-postgres pg_restore --list /tmp/$D.dump | head
```

## 3a. Restore into the running Postgres (orion is fine, the data is not)

```sh
# Keep the broken copy. Rename fails while a session is open: step 1 stopped every client.
docker exec wojtus-postgres psql -U postgres -d postgres -c \
  "ALTER DATABASE discord_event_service RENAME TO discord_event_service_broken_$(date -u +%Y%m%d)"
docker exec wojtus-postgres createdb -U postgres discord_event_service
docker exec wojtus-postgres pg_restore -U postgres -d discord_event_service --exit-on-error /tmp/$D.dump
```

The roles already exist in this cluster, so `roles.sql` is not needed.

## 3b. Restore into a new cluster (new volume or new host)

1. Copy `deploy/docker-compose.yml` as `compose.yml`, plus `deploy/init/` and `deploy/db-backup/`. Restore `.env` from 1Password.
2. Start Postgres only, on an empty volume: `docker compose up -d postgres`. The init scripts create the extension and the read-only roles.
3. Copy the files in as in step 2. Apply the roles, then the dump. `pg_dump` does not carry roles (`wojtus_query`), and the restore fails without them. Errors that a role "already exists" are expected:

```sh
docker exec wojtus-postgres sh -c "grep -v '^CREATE ROLE postgres;' /tmp/$D.roles.sql | psql -U postgres -d postgres"
docker exec wojtus-postgres pg_restore -U postgres -d discord_event_service --exit-on-error /tmp/$D.dump
```

4. `--no-role-passwords` drops the passwords. Set the exporter password again (see `deploy/init/10-readonly-roles.sql`).

## 4. Verify

```sh
docker exec -i wojtus-postgres psql -U postgres -d discord_event_service -tA <<'SQL'
SELECT count(*) FROM information_schema.tables WHERE table_schema NOT IN ('pg_catalog', 'information_schema') AND table_type = 'BASE TABLE';
SELECT max(received_at_utc) FROM raw_event_logs;
SELECT count(*) FROM messages;
SQL
```

- The table count (all schemas, `hangfire` included) matches the last drill log (`docker logs wojtus-db-backup`, 74 tables on 2026-11-18).
- `max(received_at_utc)` is close to the dump time. That time is where the gap starts.

## 5. Start the bot and close the gap

```sh
docker compose up -d --no-deps discord-event-service postgres-exporter db-backup
```

- `Database__AutoMigrate` applies any migration newer than the dump.
- The boot sees the dump's last heartbeat and starts a reconnect backfill from dump time − 5 min, capped at 2 days. Look for the `Reconnect backfill enqueued … backfilling from` log line. Presence and voice events in the gap are gone for good.
- Start a backfill by hand only when that window was capped (a dump more than 2 days old) or the chain failed. While the chain runs, the endpoint answers 400 "already in progress". The bot has no published port, so call it from the `observability` network:

```sh
docker run --rm --network observability curlimages/curl:latest -s -X POST \
  -H 'Content-Type: application/json' -d '{"after": "YYYY-MM-DDTHH:MM:SSZ"}' \
  http://discord-event-service:8080/api/backfill/<guild id>
```
- Run the standard prod query set from `CLAUDE.md` and watch the logs for 15 minutes.

## 6. Clean up

When the restored database has run for a few days without problems, drop `discord_event_service_broken_*`, delete `/tmp/restore` and the copies in the container (`docker exec wojtus-postgres rm /tmp/$D.dump /tmp/$D.roles.sql`).
