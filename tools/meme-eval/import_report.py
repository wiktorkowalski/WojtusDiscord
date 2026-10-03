#!/usr/bin/env python3
"""Load a meme benchmark report into meme_annotations through the import endpoint (#369).

Each benchmark slot becomes one model_id, so the retrieval eval can score every writer alone
and together. The import applies the cut-out rule, so the stored rows are what prod would hold.

    python3 import_report.py <benchmark-*.json> [--base http://127.0.0.1:5099] [--secret ...]

Prints, per slot: cells in the report, imported / skipped / rejected, and how many raw cells
break the "cut-out face => no people" rule (the count the indexer logs as "Cut-out rule applied").
"""
import argparse
import json
import urllib.request

PROMPT_VERSION = "v4"
CUTOUT_KIND = "cutout_face_or_emote"
MAX_BATCH_SIZE = 500  # MemeAnnotationImportEndpoints.MaxBatchSize


def post(base, secret, batch):
    request = urllib.request.Request(
        f"{base}/api/ops/meme-annotations/import",
        data=json.dumps(batch).encode(),
        headers={"Content-Type": "application/json", "X-Import-Secret": secret},
        method="POST",
    )
    with urllib.request.urlopen(request, timeout=300) as response:
        return json.load(response)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("report")
    parser.add_argument("--base", default="http://127.0.0.1:5099")
    parser.add_argument("--secret", default="meme-eval-local")
    parser.add_argument("--dry-run", action="store_true", help="count only, send nothing")
    args = parser.parse_args()

    # Python ints are exact, so the snowflakes survive the round trip (jq 1.6 would round them).
    with open(args.report) as file:
        report = json.load(file)

    # The annotation key has no effort: two slots of one model would overwrite each other.
    model_ids = [slot.partition("|")[0] for slot in report["Slots"]]
    if len(set(model_ids)) != len(model_ids):
        raise SystemExit(f"two slots share a model id, import them from separate reports: {report['Slots']}")

    for slot in report["Slots"]:
        # A slot is "model|effort=low"; the model id alone is the annotation key.
        model_id, _, option = slot.partition("|")
        effort = option.removeprefix("effort=") or None

        batch, failed, cutouts, cutout_violations = [], 0, 0, 0
        for item in report["Items"]:
            cell = next((c for c in item["Cells"] if c["Slot"] == slot), None)
            metadata = cell and cell["Result"].get("Metadata")
            if not metadata:
                failed += 1  # skipped image or failed cell: this writer cannot find the meme
                continue
            if metadata.get("image_kind") == CUTOUT_KIND:
                cutouts += 1
                cutout_violations += bool(metadata.get("people"))
            batch.append({
                "attachment_discord_id": str(item["Sample"]["AttachmentDiscordId"]),
                "model_id": model_id,
                "prompt_version": PROMPT_VERSION,
                "reasoning_effort": effort,
                "metadata": metadata,
            })

        line = f"{slot}: {len(batch)} cells, {failed} without metadata, " \
               f"{cutouts} cut-outs, {cutout_violations} with people (rule violations)"
        if args.dry_run:
            print(line)
            continue

        results = [post(args.base, args.secret, batch[start:start + MAX_BATCH_SIZE])
                   for start in range(0, len(batch), MAX_BATCH_SIZE)]
        totals = {key: sum(r[key] for r in results) for key in ("imported", "skipped", "rejected")}
        print(f"{line}; imported {totals['imported']}, skipped {totals['skipped']}, rejected {totals['rejected']}")
        for rejected in (i for r in results for i in r["items"] if i["outcome"] == "rejected"):
            print(f"  rejected {rejected['attachmentDiscordId']}: {rejected['reason']}")


if __name__ == "__main__":
    main()
