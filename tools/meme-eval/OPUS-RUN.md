# Opus annotation run (#371)

Annotates the meme corpus with Claude Opus through Claude Code subagents (subscription limit, no API cost).
The result goes into `meme_annotations` through the import endpoint (#369). A prod import needs the owner's go and the import secret.

| Thing | Value |
|---|---|
| Meme channel | `344854522704822282`, guild `341531063920754700` |
| Run dir | `src/DiscordEventService/Data/meme-benchmark/opus-run/` (gitignored), below: `<run dir>` |
| State file | `<run dir>/state.json`. Do not edit it by hand. |
| Writer key | `model_id = claude-code/claude-opus-5.5`, `prompt_version = v4`, no `reasoning_effort` |
| Agent prompt | `tools/meme-eval/opus-annotation-prompt.md` |
| Annotator task | `tools/meme-eval/ANNOTATOR-TASK.md` |
| Scripts | `tools/meme-eval/opus_run.py`, `tools/meme-eval/validate_batch.py`, `tools/meme-eval/import_run.sh`, `tools/meme-eval/corpus_copy.sh` |

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
- State file and a running export: the export reads the state again before it writes each batch, so a `validate` or `skip` during the downloads stays. The file has no lock. Two commands that write in the same second can still lose one change: run `status` after a parallel `validate` and repeat it when the status did not change.

Keep the export ahead of the annotators: one annotator finishes a batch of 40 in about 7 minutes.

## 2. Annotate

One subagent per batch, model Opus. The task is in `tools/meme-eval/ANNOTATOR-TASK.md`; the spawn message is one line with the absolute paths filled in:

```
Read `<repo>/tools/meme-eval/ANNOTATOR-TASK.md` and do that task for REPO = `<repo>` and BATCH_DIR = `<run dir>/batch-NNNN`. Report in ONE line as the file says.
```

- Parallelism: each annotator process starts its own MCP servers, so every start is a CPU spike on the machine. The owner sets the number (4 at the end of the 2026-10-04 run). Start the next annotator only when one reports, and close the one that reported.
- The annotator validates its own file but does not write the state file. After its report run `opus_run.py validate` (section 3).
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

### Prod

A prod import is a write to the production database. It needs the owner's go for that run, and the secret comes from the owner: in the shell as `MEME_IMPORT_SECRET`, never in a file. Do not start an import while a deploy runs: a merge restarts the bot.

Before the first import, prod needs `MemeIndex__ImportSecret`, `MemeIndex__ChannelIds__0=344854522704822282` and, recommended, `MemeIndex__MaxImagesPerRun=0`. `MemeIndex__AutomaticIndexing` stays unset: no model call starts.

```
export MEME_IMPORT_SECRET=<import secret>
export MEME_IMPORT_URL=https://<prod host>
tools/meme-eval/import_run.sh <run dir> batch-0001      # the first batch alone, then the check below
tools/meme-eval/import_run.sh <run dir>                 # every other 'validated' batch
```

`import_run.sh` sends each batch with status `validated` to the endpoint at `MEME_IMPORT_URL` (required; no default, so a forgotten variable never sends to prod), stores the response as `import-result.json` next to the annotations file and calls `opus_run.py imported` with it. A batch with status `imported` is not sent again. The script stops at the first failure: a response that is not 2xx, or an attachment of the batch that is not stored.

`imported` stores the counts and the rejects in the state file. The status becomes `imported` only when every attachment of the batch is stored. Otherwise the command lists what is missing and exits 1: correct the items, validate, import again.

The 100 eval memes: `eval-batch-001` has four annotation files (`opus-batch-001/out/batch-00N.json`). Join them into one JSON array, then register the file: `opus_run.py validate --run-dir <run dir> --batch eval-batch-001 --file <joined file>`. After that `import_run.sh` sends it like every other batch.

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

The script finds every `model_id` and prints one block per writer and one for all writers together. Compare the `tok=prod` rows marked `(baseline)`: production search since #380 (stop list in the rank query only).

Measured on 2026-10-05, 249 queries, top-1 / recall@5 / MRR:

| Writer | top-1 | recall@5 | MRR |
|---|---|---|---|
| `claude-code/claude-opus-5.5` | 88.0 % | 92.8 % | 0.904 |
| `google/gemini-3.8-flash` @low | 88.8 % | 94.4 % | 0.916 |
| `google/gemini-3-flash-preview` | 87.1 % | 95.2 % | 0.910 |
| all three writers together | 92.8 % | 98.0 % | 0.951 |

Numbers from this query set dated before 2026-10-05 are about 7 points lower and wrong: `eval-inputs-20261002/blindq0.json` had the ids of three memes out of step with `qimg0.json`, so 18 queries pointed at the wrong meme. The file in the run data is corrected (`blindq0.json.orig` is the old one). Before a new measurement, check that `blindq*.json` and `qimg*.json` list the same ids in the same order. Paired comparisons from the old runs hold: the 18 queries missed in every variant.

### On the real corpus

The 100-meme corpus is too easy: the target competes with 99 memes. For a ranking question, measure against the whole corpus in a local copy. Do not run the eval on prod: it creates a temporary view and sends several thousand ranking queries.

```
export PGHOST=<prod db host> PGPORT=<prod db port> PGPASSWORD=<prod db password>
tools/meme-eval/corpus_copy.sh                      # the owner runs this; makes the local database meme_corpus
python3 tools/meme-eval/retrieval_eval.py --inputs <eval inputs> --guild 341531063920754700 --db meme_corpus \
  --trigram-weights 0.5,1.0 --glued-probes --out <results file>
```

- `corpus_copy.sh` stops when the local database exists already. Drop it first (`DROP DATABASE meme_corpus;` in the local container) or pass another name.
- `corpus_copy.sh` reads prod in read-only sessions: the schema, `meme_annotations`, `meme_index`, and from `messages` only `id`, `created_at_utc`, `is_deleted` of the rows that hold a meme. No message content leaves prod.
- One variant (249 queries, one tokenizer, one weight) takes about 2 minutes on the local database. The script runs four tokenizers per weight. Run the weights as parallel processes.
- The script prints gained / lost against production search, with no p-value. Use a two-sided sign test on the two counts.

Measured on 2026-10-05, 5,029 memes, Opus as the only writer, production search, 249 queries: top-1 63.9 %, recall@5 77.5 %, MRR 0.709. 21 targets are not in the first 100 results. On the 100-meme corpus the same writer has 88.0 / 92.8 / 0.904. A query passes a median of 344 memes through the filter. Target first by query type: quote 49 of 49, vague 27 of 40, topic 24 of 41, scene 23 of 40, template 20 of 35, who 13 of 35, owner-written 3 of 9.

The results for `TrigramWeight` and the hyphen question are on #380 (closed with no change). That sweep ran before the label correction, so its tables leave the 18 queries out (n = 231).

### Every ~500 memes (about 13 batches)

The retrieval eval has queries for the 100 eval memes only. For the running corpus use a judge on 20 memes:

1. Take 20 attachments from the last 500: every 25th id of the sorted list.
2. A judge subagent (Opus, a new one, no annotator context) gets each image and its annotation. It scores 1–5 for correctness against the image and lists every invented fact, every name on a cut-out face and every private person in `people`.
3. Compare the mean score with the first control sample and with the 2026-10-02 judge scores: 3.8-flash@low 4.65, Opus@low through the API 4.60.

Not built: the side-by-side variant from the ticket (3.8-flash@low output for the same 20 memes). It needs a links file for `POST /api/ops/meme-benchmark/from-file` with the slot `google/gemini-3.8-flash|effort=low` (about $0.07 for 20 images), and no script writes that file from an attachment list yet.

Judge result of 2026-10-04: 22 memes, two from every fifth batch of 0003–0053. Mean 4.86 (19 × 5, 3 × 4), one invented tag, no name on a cut-out face, no private person in `people`. Judge result of 2026-10-05: 28 memes, two from every fifth batch of 0058–0123. Mean 4.79 (23 × 5, 4 × 4, 1 × 3), one invented fact, no name on a cut-out face, no private person in `people`, no annotation that belongs to another image. Weakest field: `templates` holds a scene description in place of a template name in 4 of the 28.

The judge is Opus too: read both results as "no quality problem", not as an exact score.

Known difference, measured on 2026-10-04: Opus through the subscription names no one from the face alone (`widely_recognized` in 0 of 50 memes, 11 memes with a known person described without a name). Search by a person's name depends on the second writer, 3.8-flash@low. Do not count a missing name as a quality drop.

### Stop rule

The owner's rule since 2026-10-04: run to the end. Stop the export and the annotators, and tell the owner, only for a real error or unusable output. Examples:

- A command or an annotator fails in the same way again after one retry.
- An annotation file that the validator does not accept after the annotator's corrections.
- An import rejects an item of a validated batch.
- The judge finds a name on a cut-out face, or a private person in `people`.

A single annotator that stops (rate limit, closed laptop) is not a stop: start the batch again.

## 6. Record on #223

- Memes annotated and imported (`opus_run.py status`), and the skipped count per reason.
- Hours of wall clock, and the number of parallel annotators.
- Rate-limit stalls: how many, how long.
- Import rejects: count and reasons.
- Validator runs per batch, when above one.
- Control samples: mean score, findings, the decision.
- Final coverage: pending = 0 in `opus_run.py export --batches 0`, and every other attachment has a reason in `skipped`.

## 7. Actuals of the 2026-10-04 run

| | Count |
|---|---|
| Indexable attachments on prod | 5,040 |
| Annotated, validated and imported | 5,029: 124 batches (4,929) and the 100 eval memes |
| Skipped | 11: 10 GIFs, 1 declined by the annotator |
| Accepted by the validator | 125 of 125 batches |
| Import | 5,029 imported, 0 overwritten, 0 rejected |
| Prod after the import | 5,029 rows `claude-code/claude-opus-5.5` / `v4`, 5,029 `meme_index` rows with status 1 |

- Wall clock: export 10:09–13:33 UTC with 0 download failures; last batch validated 19:24 UTC, with pauses by the owner; the import of 125 requests took about 1 minute.
- Speed: about 7 minutes per batch of 40 per annotator. 10 annotators in parallel at the start, 4 at the end.
- Two repost watermarks without platform UI (imgflip and jbzd): the tie-break entered the prompt at about batch 0056. Batches 0001–0055 have `jbzd` or `imgflip` in `source` for such an image.
