---
name: meme-catchup
description: Meme catch-up — annotate the meme images that are not in the search index yet with Opus subagents and import them to prod. Use when the owner asks to index new memes, to catch up the meme index, or for "indeksowanie memów".
---

# Meme catch-up

Live indexing is off by the owner's choice (#223): a new meme reaches `/meme` only through this run. The commands, the batch statuses and the import rules are in `tools/meme-eval/OPUS-RUN.md` — read it first; this file holds only the order and the rules the runbook does not.

The owner's request is the go for the whole run, the prod import included. Target: prod (`MEME_IMPORT_URL`, home network only).

## Inputs

- Run dir: `src/DiscordEventService/Data/meme-benchmark/opus-run/` (gitignored; `state.json` remembers every batch, so a stopped run resumes with the same commands).
- Prod DB host, port and password: agent memory `reference_prod_db.md`. Pass the password as `PGPASSWORD` in the environment of the one command that needs it.
- `MEME_IMPORT_SECRET`: from the environment. Unset → ask the owner once and use his answer for this run only.

## Steps

1. **Count.** `opus_run.py export … --batches 0`. `0 pending` → report "nothing waits" and stop. Otherwise run the first SELECT of runbook section 4 and note the prod row count of `claude-code/claude-opus-5.5`: step 6 compares against it. Done when you have the `pending` number and that row count.
2. **Export.** `--batches N` with N = pending / 40, rounded up. Done when `opus_run.py status` lists every new batch as `exported` and a second `--batches 0` prints `0 pending`. A transient failure (exit 1) → run the same export again.
3. **Annotate.** One Opus subagent per batch, with the spawn line of runbook section 2 and the absolute `REPO` and `BATCH_DIR`. At most 4 annotators at a time: start the next one when one reports, and shut down the one that reported. Done when every new batch has a one-line report with `validator exit 0`, or names the items it left out.
   - An annotator that stops without a file → start the batch again.
   - An item left out on purpose → `opus_run.py skip` with the annotator's reason, then tell the owner which Discord message it is. Skipped items are never queued again.
4. **Validate.** `opus_run.py validate` for each new batch, after the last export has ended. Done when `status` shows every new batch as `validated`.
5. **Import.** Check that no deploy runs (`gh run list --limit 3`), then `import_run.sh <run dir>`. Done when it exits 0 and `status` shows 0 exported, 0 annotated, 0 validated. A 409 → an indexing job runs: wait and run it again. A rejected item → stop and report it with its reason.
6. **Verify on prod, read-only.** The two SELECTs of runbook section 4. Done when the `claude-code/claude-opus-5.5` row count equals the count of step 1 plus the `imported` numbers that `import_run.sh` printed in step 5, and a final `--batches 0` prints `0 pending`.

## Report

One block for the owner: memes imported, batches, items skipped with reasons, rejects, the new prod row count, and anything that did not verify.
