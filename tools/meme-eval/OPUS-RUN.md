# Opus annotation run (#371)

Annotates the meme corpus with Claude Opus through Claude Code subagents (subscription limit, no API cost).
The result goes into `meme_annotations` through the import endpoint (#369). The owner runs every prod import.

| Thing | Value |
|---|---|
| Meme channel | `344854522704822282`, guild `341531063920754700` |
| Run dir | `src/DiscordEventService/Data/meme-benchmark/opus-run/` (gitignored), below: `<run dir>` |
| State file | `<run dir>/state.json`. Do not edit it by hand. |
| Writer key | `model_id = claude-code/claude-opus-5.5`, `prompt_version = v4`, no `reasoning_effort` |
| Agent prompt | `tools/meme-eval/opus-annotation-prompt.md` |
| Scripts | `tools/meme-eval/opus_run.py`, `tools/meme-eval/validate_batch.py` |

Prod database: SELECT only. `opus_run.py export` runs one SELECT in a read-only session. Connection details are not in the repo.

## Batch statuses

`exported` → `annotated` → `validated` → `imported`. `exported` also means "an annotator works on it".
An attachment that is in a batch or in `skipped` is never exported again. This is the resume mechanism: after a stop, run the same commands again.

```
python3 tools/meme-eval/opus_run.py status --run-dir <run dir>
```

## 1. Export

```
export PGPASSWORD=<prod db password>          # or: --pg-password-file <file>
python3 tools/meme-eval/opus_run.py export --run-dir <run dir> --channel 344854522704822282 \
  --env-file .env --source prod --pg-host <prod db host> --pg-port <prod db port> --batches 5
```

- `--env-file` is the `.env` with `Discord__Token`. The script calls `attachments/refresh-urls` right before each download (a signed CDN URL lives about 24 hours). The call needs no guild membership.
- Each batch is `<run dir>/batch-NNNN/` with `manifest.json` and `images/<attachment_discord_id>.<ext>`. 40 images per batch, oldest first. The extension comes from the bytes.
- `--batches 0` only counts: indexable, in batches, skipped, pending.
- The list follows the bot: message not deleted, file name ends in jpg, jpeg, png, webp or gif. A repost is its own attachment.
- Never exported, recorded in `skipped` with the reason: GIF, over 25 MB, dead attachment, bytes that are not an image.
- A transient failure (network, refresh call) leaves the attachment pending. The exit code is 1 and the next export takes it again.
- A stopped export leaves images without a state entry. The next export picks the same attachments and keeps the files.
- A batch dir made outside this run: `opus_run.py adopt --run-dir <run dir> --batch-dir <dir> --name <name>`. The 100 eval memes are in the state file this way (`eval-batch-001`).

Keep the export ahead of the annotators: one annotator finishes a batch in about 6 minutes.

## 2. Annotate

One subagent per batch, model Opus, a few in parallel (10 worked on 2026-10-04). Wrapper prompt, with the absolute paths filled in:

```
Annotate one batch of meme images.

1. Read <repo>/tools/meme-eval/opus-annotation-prompt.md. Follow the part between
   "BEGIN AGENT PROMPT" and "END AGENT PROMPT" exactly.
2. Images: every `local_path` in <batch dir>/manifest.json, in the order of the sorted
   attachment id. The attachment id is the image file name without the extension.
3. Output file: <batch dir>/annotations.json. One file for the whole batch.
4. Validator command (the only command you run):
   python3 <repo>/tools/meme-eval/validate_batch.py <batch dir>/annotations.json --manifest <batch dir>/manifest.json --require-all
   Correct the file and run the command again until the exit code is 0.
5. Do not change any other file. Do not read the annotations of another batch.
6. Final message: output path, number of items, validator runs needed, and every image you left out with the reason.
```

- Rate limit: an annotator that stops leaves no `annotations.json` or a partial one. Delete the partial file and start a new annotator on the same batch. Nothing else needs a reset.
- An image the annotator will not describe: the validator fails with `--require-all`. Take the image out of the batch with the reason, then validate again:
  `opus_run.py skip --run-dir <run dir> --batch batch-NNNN --id <attachment id> --reason "annotator declined: <why>"`
  The command moves the item from `items` to `failed` in the manifest and records the reason in `skipped` of the state file. `--require-all` then accepts the batch without it, and no export takes the attachment again. The image file stays in the batch dir.

## 3. Validate

```
python3 tools/meme-eval/opus_run.py validate --run-dir <run dir> --batch batch-NNNN
```

Checks the file like the import does, plus the output contract: exact members, the four closed sets, no `people` on `cutout_face_or_emote`, no duplicate key, every manifest attachment present. Exit code 0 sets the status `validated`. Warnings (tag count outside 10–20, phrase count outside 3–6) do not block.

## 4. Import

The import is per item: one bad item is `rejected` with a reason, the other items are stored. The whole request fails only for: no secret (401), an indexing job is running (409, send again later), body is not a JSON array, empty, or over 500 items (400). A re-run is safe: the same metadata is `skipped`, different metadata overwrites.

### Local (dry run)

The local scratch database `meme_eval` holds only the 100 eval memes (`eval-batch-001`). An attachment of a prod batch is rejected there as "not an image in a configured meme channel".

```
cd src/DiscordEventService
MemeIndex__ChannelIds__0=344854522704822282 MemeIndex__ImportSecret=meme-eval-local ASPNETCORE_URLS="http://127.0.0.1:5099" \
  dotnet run -- "--ConnectionStrings:Postgres=Host=localhost;Port=5432;Database=meme_eval;Username=postgres;Password=$(docker exec wojtus-postgres printenv POSTGRES_PASSWORD)"

curl -sS -X POST http://127.0.0.1:5099/api/ops/meme-annotations/import \
  -H "X-Import-Secret: meme-eval-local" -H "Content-Type: application/json" \
  --data-binary @<annotations file> > <result file>
```

### Prod (the owner runs this)

Before the first import, prod needs `MemeIndex__ImportSecret`, `MemeIndex__ChannelIds__0=344854522704822282` and, recommended, `MemeIndex__MaxImagesPerRun=0`. `MemeIndex__AutomaticIndexing` stays unset: no model call starts.

```
export MEME_IMPORT_SECRET=<import secret>
curl -sS -X POST https://<prod host>/api/ops/meme-annotations/import \
  -H "X-Import-Secret: $MEME_IMPORT_SECRET" -H "Content-Type: application/json" \
  --data-binary @<run dir>/batch-NNNN/annotations.json > <run dir>/batch-NNNN/import-result.json

python3 tools/meme-eval/opus_run.py imported --run-dir <run dir> --batch batch-NNNN --result <run dir>/batch-NNNN/import-result.json
```

`imported` stores the counts and the rejects in the state file. The status becomes `imported` only when every attachment of the batch is stored. Otherwise the command lists what is missing and exits 1: correct the items, validate, import again.

Check on prod after the first batch (read-only):

```sql
SELECT model_id, prompt_version, count(*) FROM meme_annotations GROUP BY 1, 2;
SELECT status, count(*) FROM meme_index GROUP BY 1;   -- 1 = Indexed
```

## 5. Control sample

### Once, on the 100 eval memes

After the local import of `eval-batch-001`:

```
python3 tools/meme-eval/retrieval_eval.py --inputs src/DiscordEventService/Data/meme-benchmark/eval-inputs-20261002 \
  --guild 341531063920754700 --db meme_eval --out <results file>
```

The script finds every `model_id` and prints one block per writer and one for all writers together. Compare the `tok=rank` rows (the baseline after #380) of `claude-opus-5.5` and `gemini-3.8-flash`. Reference from #370: 3.8-flash@low top-1 77.9 %, MRR 0.824.

### Every ~500 memes (about 13 batches)

The retrieval eval has queries for the 100 eval memes only. For the running corpus use a judge on 20 memes:

1. Take 20 attachments from the last 500: every 25th id of the sorted list.
2. A judge subagent (Opus, a new one, no annotator context) gets each image and its annotation. It scores 1–5 for correctness against the image and lists every invented fact, every name on a cut-out face and every private person in `people`.
3. Compare the mean score with the first control sample and with the 2026-10-02 judge scores: 3.8-flash@low 4.65, Opus@low through the API 4.60.

Not built: the side-by-side variant from the ticket (3.8-flash@low output for the same 20 memes). It needs a links file for `POST /api/ops/meme-benchmark/from-file` with the slot `google/gemini-3.8-flash|effort=low` (about $0.07 for 20 images), and no script writes that file from an attachment list yet.

Known difference, measured on 2026-10-04: Opus through the subscription names no one from the face alone (`widely_recognized` in 0 of 50 memes, 11 memes with a known person described without a name). Search by a person's name depends on the second writer, 3.8-flash@low. Do not count a missing name as a quality drop.

### Stop rule

Stop the export and the annotators, and tell the owner, when one of these holds:

- The mean judge score is 0.3 or more below the first control sample.
- The judge finds a name on a cut-out face, or a private person in `people`.
- More than 10 % of the items of a batch fail the first validator run, in two batches in a row.
- An import rejects an item of a validated batch.

## 6. Record on #223

- Memes annotated and imported (`opus_run.py status`), and the skipped count per reason.
- Hours of wall clock, and the number of parallel annotators.
- Rate-limit stalls: how many, how long.
- Import rejects: count and reasons.
- Validator runs per batch, when above one.
- Control samples: mean score, findings, the decision.
- Final coverage: pending = 0 in `opus_run.py export --batches 0`, and every other attachment has a reason in `skipped`.
