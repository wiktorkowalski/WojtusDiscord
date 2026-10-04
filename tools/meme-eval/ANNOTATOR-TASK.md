# Annotator task (one batch)

You annotate meme images for a search index. You look at each image yourself with the Read tool. No repo changes, no git, no network. Do not call an advisor and do not spawn agents.

Your spawn message gives you two absolute paths: `REPO` (the repository root) and `BATCH_DIR` (for example `<run dir>/batch-0056`).

1. Read `REPO/tools/meme-eval/opus-annotation-prompt.md`. The part between "BEGIN AGENT PROMPT" and "END AGENT PROMPT" is your instruction set and output contract. Follow it exactly: schema, closed sets, the `source` and `people` rules, `model_id` = `claude-code/claude-opus-5.5`, `prompt_version` = `v4`, no `reasoning_effort`.
2. Read `BATCH_DIR/manifest.json`. Annotate ALL entries of `items`. For each one Read the image at its `local_path`. The key is the attachment id from the manifest entry, which equals the file name. Describe only what you see; do not invent text, people or templates. The manifest `file_name` is not evidence of who is in the image. If an image cannot be read, or you decline to describe it, leave it out.
3. Write ONE file: `BATCH_DIR/annotations.json` (the import body: a JSON array).
4. Validate: `python3 REPO/tools/meme-eval/validate_batch.py BATCH_DIR/annotations.json --manifest BATCH_DIR/manifest.json --require-all`. No other scripts. Fix every error and re-run until exit code 0. If the only failure is an item you left out on purpose, stop there.

Final report, ONE line only: `<batch name>: <n>/<total> items, validator exit <code>, left out: <ids with reason, or none>`. Do not list ambiguities.
