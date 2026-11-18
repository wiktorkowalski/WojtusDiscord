# Restore the prod database

Runbook for #332. Use it when the prod database on orion is broken or lost. The nightly backup and the weekly drill are in `db-backup.sh`.

Rules:
- Never delete the broken database. Rename it and keep it until the restore is verified.
- Every `docker compose run` gets `--no-deps`. Without it compose can recreate `wojtus-postgres`.
- Run all commands on orion, in `~/wojtusdiscord`.

## 1. Stop the bot

```sh
cd ~/wojtusdiscord
docker compose stop discord-event-service
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

Check that the archive is readable before you touch the database:

```sh
docker exec -i wojtus-postgres pg_restore --list < /tmp/restore/wojtusdiscord-YYYY-MM-DD.dump | head
```

## 3a. Restore into the running Postgres (orion is fine, the data is not)

```sh
D=/tmp/restore/wojtusdiscord-YYYY-MM-DD
# Keep the broken copy. Rename fails while a session is open: the bot must be stopped (step 1).
docker exec wojtus-postgres psql -U postgres -d postgres -c \
  "ALTER DATABASE discord_event_service RENAME TO discord_event_service_broken_$(date -u +%Y%m%d)"
docker exec wojtus-postgres createdb -U postgres discord_event_service
docker exec -i wojtus-postgres pg_restore -U postgres -d discord_event_service --exit-on-error < $D.dump
```

The roles already exist in this cluster, so `roles.sql` is not needed.

## 3b. Restore into a new cluster (new volume or new host)

1. Copy `deploy/docker-compose.yml` as `compose.yml`, plus `deploy/init/` and `deploy/db-backup/`. Restore `.env` from 1Password.
2. Start Postgres only, on an empty volume: `docker compose up -d postgres`. The init scripts create the extension and the read-only roles.
3. Apply the roles, then the dump. `pg_dump` does not carry roles (`wojtus_query`), and the restore fails without them. Errors that a role "already exists" are expected:

```sh
D=/tmp/restore/wojtusdiscord-YYYY-MM-DD
grep -v '^CREATE ROLE postgres;' $D.roles.sql | docker exec -i wojtus-postgres psql -U postgres -d postgres
docker exec -i wojtus-postgres pg_restore -U postgres -d discord_event_service --exit-on-error < $D.dump
```

4. `--no-role-passwords` drops the passwords. Set the exporter password again (see `deploy/init/10-readonly-roles.sql`).

## 4. Verify

```sh
docker exec -i wojtus-postgres psql -U postgres -d discord_event_service -tA <<'SQL'
SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE';
SELECT max(received_at_utc) FROM raw_event_logs;
SELECT count(*) FROM messages;
SQL
```

- The table count matches the last drill log (`docker logs wojtus-db-backup`, 74 tables on 2026-11-18).
- `max(received_at_utc)` is close to the dump time. That time is where the gap starts.

## 5. Start the bot and close the gap

```sh
docker compose up -d --no-deps discord-event-service
```

- `Database__AutoMigrate` applies any migration newer than the dump.
- The boot closes the downtime row, and reconnect-backfill fetches messages missed while the bot was down.
- For the gap between the dump and the stop, start a backfill from the dump time. The bot has no published port, so call it from the `observability` network. Presence and voice events in the gap are gone for good.

```sh
docker run --rm --network observability curlimages/curl:latest -s -X POST \
  -H 'Content-Type: application/json' -d '{"after": "YYYY-MM-DDTHH:MM:SSZ"}' \
  http://discord-event-service:8080/api/backfill/<guild id>
```
- Run the standard prod query set from `CLAUDE.md` and watch the logs for 15 minutes.

## 6. Clean up

When the restored database has run for a few days without problems, drop `discord_event_service_broken_*` and delete `/tmp/restore`.
