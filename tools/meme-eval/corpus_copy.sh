#!/usr/bin/env bash
# Copies the meme corpus from prod into a local scratch database for retrieval_eval.py.
# The owner runs this: it reads prod. Every prod session is read-only.
# No message content and no author leaves prod: from `messages` only id, created_at_utc and
# is_deleted of the rows that hold a meme. That is all the search query reads from the table.
#
#   export PGHOST=<prod db host> PGPORT=<prod db port> PGPASSWORD=<prod db password>
#   tools/meme-eval/corpus_copy.sh [local database, default meme_corpus]
#
# CORPUS_DUMP_DIR=<dir> keeps the three dump files there. A dir that already holds them is
# loaded as it is, with no prod read.
set -euo pipefail

database=${1:-meme_corpus}
container=${CORPUS_CONTAINER:-wojtus-postgres}
dump_dir=${CORPUS_DUMP_DIR:-$(mktemp -d)}
[[ -n ${CORPUS_DUMP_DIR:-} ]] || trap 'rm -rf "$dump_dir"' EXIT
mkdir -p "$dump_dir"

prod() {
    docker run --rm -e PGHOST -e PGPORT -e PGPASSWORD -e PGOPTIONS='-c default_transaction_read_only=on' \
        postgres:18 "$@" -U "${PGUSER:-postgres}" -d "${PGDATABASE:-discord_event_service}"
}

local_psql() {
    docker exec -i "$container" psql -U postgres -q -v ON_ERROR_STOP=1 "$@"
}

if [[ ! -s $dump_dir/schema.sql || ! -s $dump_dir/data.sql || ! -s $dump_dir/messages_min.csv ]]; then
    : "${PGHOST:?set PGHOST, PGPORT and PGPASSWORD in the shell}" "${PGPORT:?}" "${PGPASSWORD:?}"
    prod pg_dump --schema-only --no-owner --no-privileges > "$dump_dir/schema.sql"
    prod pg_dump --data-only --disable-triggers -t public.meme_annotations -t public.meme_index > "$dump_dir/data.sql"
    prod psql -v ON_ERROR_STOP=1 -c "COPY (SELECT id, created_at_utc, is_deleted FROM messages
        WHERE id IN (SELECT message_id FROM meme_index)) TO STDOUT CSV" > "$dump_dir/messages_min.csv"
fi

local_psql -d postgres -c "CREATE DATABASE $database;"
local_psql -d "$database" < "$dump_dir/schema.sql"
# The full table stays on prod. CASCADE drops the foreign keys that point at it.
local_psql -d "$database" -c "DROP TABLE public.messages CASCADE;
    CREATE TABLE public.messages (id uuid PRIMARY KEY, created_at_utc timestamptz NOT NULL, is_deleted boolean NOT NULL);"
local_psql -d "$database" < "$dump_dir/data.sql"
local_psql -d "$database" -c "\copy public.messages FROM STDIN CSV" < "$dump_dir/messages_min.csv"

local_psql -d "$database" -At -c "
    SELECT 'annotations: ' || count(*) FROM meme_annotations;
    SELECT 'indexed memes: ' || count(*) FROM meme_index WHERE status = 1;
    SELECT 'memes without a message row: ' || count(*) FROM meme_index m
        LEFT JOIN messages msg ON msg.id = m.message_id WHERE msg.id IS NULL;"
