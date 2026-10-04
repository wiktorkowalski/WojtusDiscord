# Meme search is Postgres hybrid FTS + trigram, embeddings deferred

**Status**: Accepted (2026-06-09).

Meme search queries match in Postgres using two GIN indexes on `meme_index`: a `tsvector` built with the `simple` config + `unaccent` over descriptions, OCR text and tags (multi-word matching and ranking via `websearch_to_tsquery`/`ts_rank`), blended with `pg_trgm` similarity (typo tolerance and Polish inflection — "postgresie" shares trigrams with "postgres"). Both are contrib extensions available in the stock `postgres:18` image. The corpus is Polish/English mixed and Postgres has no built-in Polish stemmer, which is why the `simple` config plus trigram fuzziness was chosen over a language-config FTS; metadata is generated bilingually (PL+EN descriptions and tags) to compensate on the recall side.

Considered and rejected for v1: **pgvector embeddings** — semantic search would be strictly better for "I remember the vibe, not the words" queries and would make bilingual duplication unnecessary, but OpenRouter (the project's single LLM provider) serves zero embedding models (verified 2026-06-09), so it would force a second API provider plus swapping prod's stock Postgres image for a pgvector build. **External engines** (Meilisearch/Elasticsearch) — an extra infra service for a ~5k-row corpus. Embeddings remain the intended v2: the schema is additive (one vector column + index), and at this scale even brute-force cosine without an index would suffice. The May-2025 POC (`image-contents-search-poc` branch) already used the `pg_trgm` `%` operator on keyword arrays, which this decision formalises.

## Addendum 2026-10-03 (#367): N annotations per indexed meme

The vision metadata, both generated columns and both GIN indexes moved from `meme_index` to `meme_annotations`: one row per (attachment, model, prompt version). `meme_index` keeps the lifecycle of the attachment only. The owner's decision behind it: keep several results per meme and search across all of them, so the corpus backfill stops being a one-way door on the model choice.

- **Search** scores every annotation and keeps the best one per attachment, then applies the same order and limit as before. A weak or wrong annotation can add a hit. It can never push a correct one down.
- **The query** is a hand-built `to_tsquery` with OR-joined tokens, not `websearch_to_tsquery` as written above. The `simple` config keeps stopwords, so AND semantics would empty natural-language queries.
- **Status `Indexed`** means: the attachment has at least one annotation and is findable. No CHECK can span two tables, so a writer adds the annotation and flips the status in one `SaveChanges`.
- **Who annotates what.** The manual backfill visits every attachment without a status row, every Pending or Failed one, and also every Indexed one that has no annotation from the configured model and prompt version. That is how a second model reaches the corpus, and it happens only on a human trigger. Skipped rows are never revisited, with one exception since #373 (addendum below): a model refusal. The weekly sweep and the live hook look at the status only. A model or prompt change therefore never makes them pay for the whole corpus again.
- **An Indexed row is never downgraded.** When an extra annotation fails or is refused, the row stays Indexed and findable.
- **Reposts** (same content hash) copy every annotation of the oldest indexed original that they lack, each time the indexer processes them. The sweep and the live hook process a repost once, so later annotations of the original do not follow. The manual backfill revisits a repost that lacks the configured model and prompt version, and then copies again.
- **Prompt version** is a constant next to the prompt (`OpenRouterClient.PromptVersion`), bumped by hand with any prompt or schema change. Reasoning effort is stored on the annotation as provenance and is not part of the key.

## Addendum 2026-10-03 (#368): metadata schema v2

The model contract and `meme_annotations` gained fields that match how people search: `templates[]` (replaces the single `template`), `people[]` with the evidence for each name, `search_phrases[]`, `franchise`, `image_kind`, `language`, and `source` as a closed set. The reason is the 2026-10-02 evaluation on #223: `template` held scene descriptions, one meme often fits two templates, and models guessed identities for cut-out faces.

- **Weights.** A = `templates`, `search_phrases`, people names, `franchise`, `tags`, `source`. B = `ocr_text`. C = both descriptions. `search_text` (the trigram side) holds the same fields without weights.
- **`source`** is one value of a fixed list, or null when no platform is visible. A check constraint holds the list. The value `other` means "a platform that is not on the list": it is stored, and it is left out of both search columns, because it is a bucket and not a word someone searches for.
- **People** are stored as jsonb (`name` + `evidence`: `name_visible` or `widely_recognized`). The writer also fills `people_names text[]`, which the generated columns read through the existing `f_text_array_join`. No new SQL function.
- **Rows written before schema v2** keep null in `image_kind` and `language` (unknown, not `other` / `none`), an empty `people` and `search_phrases`, and `templates` made from the old `template`. A legacy `source` was matched to the list without case; `x` became `twitter`; `none`, `null` and a blank became null; any other text became `other` and moved to `tags`.
- **The cut-out rule.** For `image_kind = cutout_face_or_emote` the writer stores no people. It also removes every tag, template and search phrase that names one of them, and clears a `franchise` that does. An entry names a person when it holds the full name, one word of the name that has 3 or more characters, or such a word of 5 or more characters with an ending of up to 4 letters (the Polish inflected forms: „Gonciarzem” for „Gonciarz”). The comparison ignores case and accents. The rule is code in the one place that writes an annotation, so the model path, the repost copy and the import (#369) all pass through it. The prompt asks for the same, and the benchmark showed that a prompt alone does not hold.
- **`raw_response_json` for a cut-out** holds the metadata after the rule, not the model's verbatim output, when the rule removed something. The assistant's SQL tool reads every table, so a guessed name must not stay in the raw column.
- **Known limits.** The rule does not rewrite `description_pl`, `description_en` and `ocr_text`. A name that a model writes there for a cut-out stays, at weight C or B, and the trigram side of search can reach it. An inflected form that changes the stem stays too („Pawła” for „Paweł”). The rule has nothing to match when a cut-out comes with an empty people list. Each time the rule drops something, the indexer logs the counts, never the names.
- **Prompt version** is `v4`: prompt v2 plus the new fields. v3's stricter template wording scored lower and is not used.

## Addendum 2026-10-03 (#369): the import and the automatic-indexing switch

`POST /api/ops/meme-annotations/import` writes annotations produced outside the bot (the owner's plan: Claude Opus through the Claude Code subscription, #371). The bot never calls a model for an imported annotation.

- **Same checks, same writer.** The item metadata is deserialized as `MemeMetadata` (closed sets), checked for nulls, and written through `MemeAttachmentIndexer`. The cut-out rule therefore holds for the import too. `prompt_version` must be in `OpenRouterClient.KnownPromptVersions`.
- **The key is the identity.** A re-import of a key with the same metadata and reasoning effort leaves the annotation as it is ("skipped"), `indexed_at_utc` included. Anything else replaces the stored annotation in place, also one that the API path wrote under that key: `first_seen_utc` stays, and `indexed_at_utc` becomes the value from the item.
- **Status.** The import creates the `meme_index` row when the attachment has none, and marks it Indexed in the same save as the annotation. A Pending, Failed or Skipped row becomes Indexed too, also for a "skipped" item: it has an annotation, and search needs the status. `attempt_count` is not changed.
- **Not during an indexing run.** The endpoint answers 409 while a backfill or sweep checkpoint is active. A running job holds its rows in memory, and a model failure after the import would write Failed over the Indexed status. The live hook has no checkpoint, so this guard does not see it; a re-import repairs such a row.
- **What the sweep does afterwards.** The sweep and the live hook look at the status only, so they never pay for an imported attachment. The manual backfill still visits it when it has no annotation from the configured model and prompt version. That is the one way the API model reaches an imported meme, and a human starts it.
- **No content hash.** The import downloads nothing, so a row that it creates has `content_hash` NULL and takes no part in repost dedupe: a repost of it does not get a copy, and it does not get copies from an original. The hash is filled when the manual backfill later processes the row. An export for #371 has to list reposts as their own attachments.
- **`raw_response_json`** holds the item's `metadata` serialized again from the parsed contract, after the cut-out rule. A member outside the contract is dropped: an external writer has no strict schema, and such a member could carry a name past the rule.
- **Access.** A shared secret in the `X-Import-Secret` header (`MemeIndex:ImportSecret`), until #339. With no secret configured the endpoint refuses every request.
- **`MemeIndex:AutomaticIndexing`, default off.** The import needs `MemeIndex:ChannelIds` to know which attachments are memes. Before this change `ChannelIds` plus an OpenRouter key (the model has a default) was enough to start the live hook (a model call for every new meme, not capped by `MaxImagesPerRun`) and the weekly sweep. Now both do nothing until `AutomaticIndexing` is true. The manual backfill endpoint does not read the switch: it is a human trigger already.

## Addendum 2026-10-04 (#373): a refusal belongs to the model

A model refusal is the outcome of one model and one prompt version. Before this change it was stored as `Skipped` on the attachment, so no other model ever saw the image. The owner chose the small fix: no attempts table.

- **The marker.** `meme_index.refused_by_model_id` and `refused_by_prompt_version` name the last writer that refused the image. Both are null on a row that no model refused. The status rules do not change: a refusal still makes a row `Skipped`, and an Indexed row stays Indexed and only gets the marker.
- **The manual backfill** revisits a Skipped row that has a marker, unless the marker names the configured model and prompt version. It also leaves an Indexed row alone when the marker names the configured writer. So a refusal costs one model call per writer, not one per run.
- **Attachment-level skips** (dead attachment, file too large, not an image) have no marker and stay terminal for every writer. Such a skip on a Skipped refusal row removes the marker. An Indexed row keeps its marker.
- **The sweep and the live hook** still look at the status only. A refusal row is terminal for them, whichever model refused.
- **The import** makes a refusal row Indexed, as it does for every Skipped row. The marker stays, so the manual backfill does not offer the image to the refusing model again.
- **Known limits.** The marker holds one writer. When two models both refuse an image, each change of the configured model asks once more. `attempt_count` and the sweep cap of 3 are still per attachment, not per model. A deterministic failure of an extra annotation on an Indexed row (schema violation, a rejected save, a dead attachment) still leaves no trace and is retried on each manual run. The same holds for a save that Postgres rejects while another model revisits a Skipped refusal row: the recovery path leaves a Skipped row untouched. A refusal row from before this change has no marker and stays terminal.

## Addendum 2026-10-04 (#380): function words stay out of the rank query

Search builds two OR-joined `to_tsquery` values from the same tokens.

- **The filter** (`@@`) keeps every token. A function word is often what lets an inflected query through: "steamie" is not "steam" in the `simple` config, and the trigram score then ranks the row.
- **The rank** (`ts_rank`) drops a short Polish and English stop list (`MemeSearchService.RankStopWords`). `search_phrases` are natural language at weight A, so "w" and "na" scored like content words. A query of function words only keeps them in the rank query.
- The raw query for `word_similarity` is unchanged. The list is compared with the tokens before `unaccent`.
- **Measured** on #370 (249 queries, 100 memes): top-1 77.9 → 81.5 % for one writer, 80.3 → 83.1 % for both. The same list in the filter loses 6–9 rows, so it is not there.
