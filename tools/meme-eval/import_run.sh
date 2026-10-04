#!/usr/bin/env bash
# Sends every 'validated' batch of an Opus run to the import endpoint and records the result.
# The owner runs this. A re-run is safe: an 'imported' batch is not sent again.
#
#   export MEME_IMPORT_SECRET=<import secret>
#   export MEME_IMPORT_URL=https://<prod host>
#   tools/meme-eval/import_run.sh <run dir> [batch name ...]
#
# No batch name = every batch with status 'validated'. Stops at the first failure
# (401 wrong secret, 409 an indexing job is running, a rejected or missing item).
set -euo pipefail

run_dir=${1:?usage: import_run.sh <run dir> [batch name ...]}
shift
: "${MEME_IMPORT_SECRET:?set MEME_IMPORT_SECRET in the shell}"
base_url=${MEME_IMPORT_URL:?set MEME_IMPORT_URL (the prod host, or http://127.0.0.1:5099 locally)}
tools_dir=$(cd "$(dirname "$0")" && pwd)

# One line per batch to send: <name><TAB><annotations file>
python3 - "$run_dir" "$@" <<'PY' |
import json, pathlib, sys
run_dir, wanted = pathlib.Path(sys.argv[1]), sys.argv[2:]
batches = json.loads((run_dir / "state.json").read_text())["batches"]
unknown = [name for name in wanted if name not in batches]
if unknown:  # before the first line goes out: the loop below sends what it reads
    sys.exit(f"no batch in the state file: {', '.join(unknown)}")
for name in wanted or sorted(batches):
    batch = batches[name]
    if batch["status"] == "validated":
        print(f"{name}\t{batch['annotations_file']}")
    elif wanted:
        print(f"{name}: status '{batch['status']}', not sent", file=sys.stderr)
PY
while IFS=$'\t' read -r name annotations; do
    result="$(dirname "$annotations")/import-result.json"
    http_code=$(curl -sS -o "$result" -w '%{http_code}' -X POST "$base_url/api/ops/meme-annotations/import" \
        -H "X-Import-Secret: $MEME_IMPORT_SECRET" -H "Content-Type: application/json" \
        --data-binary "@$annotations")
    if [[ $http_code != 2* ]]; then
        echo "$name: HTTP $http_code: $(head -c 300 "$result")" >&2
        exit 1
    fi
    python3 "$tools_dir/opus_run.py" imported --run-dir "$run_dir" --batch "$name" --result "$result"
done
