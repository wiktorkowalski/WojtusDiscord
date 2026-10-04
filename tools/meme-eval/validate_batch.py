#!/usr/bin/env python3
"""Validates one Opus annotation batch file before the import (#371).

The file must be the body of POST /api/ops/meme-annotations/import, as opus-annotation-prompt.md
defines it. The checks repeat what the import does (MemeAnnotationImportService, MemeMetadata,
HasNullRequiredField) and add the contract of the prompt file: no extra members, the fixed
model_id, no names on a cut-out, every attachment id in the manifest.

    python3 validate_batch.py <batch.json> --manifest <batch dir>/manifest.json
                              [--model-id claude-code/claude-opus-5.5] [--require-all]

--require-all: every attachment of the manifest must have an item (one file for the whole batch).

Exit code 0 = the batch is valid. 1 = at least one error; errors are listed per item.
Warnings (a count outside the range the prompt asks for) do not change the exit code.

The closed sets are copies of prompt v4. Keep them in step with MemeImageKind, MemeLanguage
(MemeAnnotationEntity.cs), MemePersonEvidence (MemeMetadata.cs) and MemeSources.Known.
"""
import argparse
import collections
import json
import pathlib
import sys

DEFAULT_MODEL_ID = "claude-code/claude-opus-5.5"  # opus-annotation-prompt.md
PROMPT_VERSIONS = {"v4"}  # OpenRouterClient.KnownPromptVersions
MAX_BATCH = 500  # MemeAnnotationImportEndpoints.MaxBatchSize

IMAGE_KINDS = {"template_meme", "screenshot_post_or_chat", "comic", "photo_with_caption",
               "cutout_face_or_emote", "edited_photo", "video_frame", "other"}
LANGUAGES = {"pl", "en", "mixed", "none"}
EVIDENCE = {"name_visible", "widely_recognized"}
SOURCES = {"reddit", "twitter", "facebook", "instagram", "tiktok", "youtube", "discord",
           "kwejk", "jbzd", "jeja", "wykop", "demotywatory", "blasty", "memisko",
           "imgflip", "9gag", "ifunny", "other", "none"}
CUTOUT = "cutout_face_or_emote"

ITEM_REQUIRED = {"attachment_discord_id", "model_id", "prompt_version", "metadata"}
STRING_FIELDS = ("description_pl", "description_en", "ocr_text")
ARRAY_FIELDS = ("tags", "templates", "search_phrases")
METADATA_FIELDS = {*STRING_FIELDS, *ARRAY_FIELDS, "image_kind", "people", "franchise", "source", "language"}


def check_closed(errors, owner, field, allowed, where="metadata"):
    value = owner.get(field)
    if not isinstance(value, str) or value not in allowed:
        errors.append(f"{where}.{field}: {value!r} is not in the closed set")


def check_metadata(metadata, errors, warnings):
    if not isinstance(metadata, dict):
        errors.append("metadata: not an object")
        return

    for field in sorted(METADATA_FIELDS - metadata.keys()):
        errors.append(f"metadata.{field}: missing")
    for field in sorted(metadata.keys() - METADATA_FIELDS):
        errors.append(f"metadata.{field}: not in the schema")

    for field in STRING_FIELDS:
        if field in metadata and not isinstance(metadata[field], str):
            errors.append(f"metadata.{field}: must be a string")
    for field in ("description_pl", "description_en"):
        if isinstance(metadata.get(field), str) and not metadata[field].strip():
            errors.append(f"metadata.{field}: empty")

    for field in ARRAY_FIELDS:
        values = metadata.get(field)
        if field in metadata and (not isinstance(values, list) or any(not isinstance(v, str) for v in values)):
            errors.append(f"metadata.{field}: must be an array of strings")

    for field, allowed in (("image_kind", IMAGE_KINDS), ("language", LANGUAGES), ("source", SOURCES)):
        if field in metadata:
            check_closed(errors, metadata, field, allowed)

    if "franchise" in metadata and metadata["franchise"] is not None and not isinstance(metadata["franchise"], str):
        errors.append("metadata.franchise: must be a string or null")

    people = metadata.get("people")
    if "people" in metadata:
        if not isinstance(people, list):
            errors.append("metadata.people: must be an array")
        else:
            for position, person in enumerate(people):
                where = f"metadata.people[{position}]"
                if not isinstance(person, dict):
                    errors.append(f"{where}: not an object")
                    continue
                if person.keys() != {"name", "evidence"}:
                    errors.append(f"{where}: must have exactly name and evidence")
                if not isinstance(person.get("name"), str) or not person["name"].strip():
                    errors.append(f"{where}.name: must be a non-empty string")
                check_closed(errors, person, "evidence", EVIDENCE, where)
            # D3: the server would drop these names; a batch that has them broke the prompt.
            if people and metadata.get("image_kind") == CUTOUT:
                errors.append(f"metadata.people: must be empty for image_kind {CUTOUT}")

    for field, low, high in (("tags", 10, 20), ("search_phrases", 3, 6)):
        values = metadata.get(field)
        if isinstance(values, list) and not low <= len(values) <= high:
            warnings.append(f"metadata.{field}: {len(values)} entries, the prompt asks for {low}-{high}")


def check_item(item, manifest_ids, model_id, seen, errors, warnings):
    if not isinstance(item, dict):
        errors.append("not an object")
        return None

    for field in sorted(ITEM_REQUIRED - item.keys()):
        errors.append(f"{field}: missing")
    for field in sorted(item.keys() - ITEM_REQUIRED):
        errors.append(f"{field}: not allowed by the output contract")

    attachment_id = item.get("attachment_discord_id")
    if "attachment_discord_id" in item:
        if not isinstance(attachment_id, str) or not attachment_id.isascii() or not attachment_id.isdigit():
            errors.append(f"attachment_discord_id: {attachment_id!r} must be a string of digits")
            attachment_id = None
        elif attachment_id not in manifest_ids:
            errors.append("attachment_discord_id: not in the manifest")

    if "model_id" in item and item["model_id"] != model_id:
        errors.append(f"model_id: {item['model_id']!r}, expected {model_id!r}")
    if "prompt_version" in item and item["prompt_version"] not in PROMPT_VERSIONS:
        errors.append(f"prompt_version: {item['prompt_version']!r}, known: {', '.join(sorted(PROMPT_VERSIONS))}")

    if attachment_id is not None:
        key = (attachment_id, item.get("model_id"), item.get("prompt_version"))
        if key in seen:
            errors.append("duplicate key in this batch (attachment, model_id, prompt_version)")
        seen.add(key)

    if "metadata" in item:
        check_metadata(item["metadata"], errors, warnings)
    return attachment_id


def load_manifest_ids(manifest_path):
    return {str(entry["attachment_discord_id"]) for entry in json.loads(manifest_path.read_text())["items"]}


def validate(batch_path, manifest_path, model_id=DEFAULT_MODEL_ID, require_all=False):
    """Prints the errors per item and returns how many there are. opus_run.py calls this too."""
    known_ids = load_manifest_ids(manifest_path)
    try:
        batch = json.loads(batch_path.read_text(encoding="utf-8"))
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as error:
        print(f"ERROR {batch_path}: not readable as JSON: {error}")
        return 1

    shape_error = ("the file must be a JSON array of items" if not isinstance(batch, list)
                   else "the batch is empty" if not batch
                   else f"the batch has {len(batch)} items; the import limit is {MAX_BATCH}" if len(batch) > MAX_BATCH
                   else None)
    if shape_error:
        print(f"ERROR {shape_error}")
        return 1

    seen, covered = set(), set()
    kinds = collections.Counter()
    bad_items = error_count = warning_count = 0
    for index, item in enumerate(batch):
        errors, warnings = [], []
        attachment_id = check_item(item, known_ids, model_id, seen, errors, warnings)
        covered.add(attachment_id)
        if isinstance(item, dict) and isinstance(item.get("metadata"), dict):
            kinds[str(item["metadata"].get("image_kind"))] += 1
        if errors or warnings:
            print(f"item {index} (attachment {attachment_id or '?'}):")
            for message in errors:
                print(f"  ERROR   {message}")
            for message in warnings:
                print(f"  warning {message}")
        bad_items += bool(errors)
        error_count += len(errors)
        warning_count += len(warnings)

    if require_all:
        missing = sorted(known_ids - covered)
        error_count += len(missing)
        for attachment_id in missing:
            print(f"ERROR   manifest attachment {attachment_id} has no item")

    print(f"{len(batch)} items, {bad_items} with errors ({error_count} errors), {warning_count} warnings; "
          f"image_kind: {dict(kinds.most_common())}")
    return error_count


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("batch", type=pathlib.Path)
    parser.add_argument("--manifest", required=True, type=pathlib.Path)
    parser.add_argument("--model-id", default=DEFAULT_MODEL_ID)
    parser.add_argument("--require-all", action="store_true",
                        help="error when a manifest attachment has no item (one file covers the whole manifest)")
    args = parser.parse_args()
    sys.exit(1 if validate(args.batch, args.manifest, args.model_id, args.require_all) else 0)


if __name__ == "__main__":
    main()
