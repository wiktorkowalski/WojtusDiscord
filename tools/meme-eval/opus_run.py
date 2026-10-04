#!/usr/bin/env python3
"""Export and bookkeeping for the Opus annotation run (#371). The runbook is OPUS-RUN.md.

One state file (<run dir>/state.json) holds every batch and its status:
exported -> annotated -> validated -> imported. A run that stops (rate limit, closed laptop)
continues from that file: an attachment in a batch or in "skipped" is never exported again.

    opus_run.py export   --run-dir D --channel C --env-file .env [--batches 3] [--batch-size 40] <source>
    opus_run.py adopt    --run-dir D --batch-dir B --name NAME [--status exported]
    opus_run.py mark     --run-dir D --batch NAME --status annotated
    opus_run.py skip     --run-dir D --batch NAME --id ATTACHMENT --reason "..."
    opus_run.py validate --run-dir D --batch NAME [--file annotations.json]
    opus_run.py imported --run-dir D --batch NAME --result result.json
    opus_run.py status   --run-dir D

<source> is where the attachment list comes from. The script only runs one SELECT there:
    --source prod  --pg-host H --pg-port P [--pg-db discord_event_service]   (password: env PGPASSWORD
                   or --pg-password-file; psql runs in a postgres:18 container, session read-only)
    --source local [--pg-db meme_eval] [--container wojtus-postgres]

export lists the image attachments of the channel like MemeSampleService does (message not
deleted, file name ends in an image extension), drops what is already in the state file, and
writes batches oldest first: <run dir>/batch-NNNN/images/<attachment_discord_id>.<ext> plus
manifest.json. A file is never named by position: the 2026-10-02 eval lost 30 of 40 images to a
position-based mapping. The CDN URLs are re-signed per batch, right before the download (a signed
URL lives about 24 hours). A repost is its own attachment: an import row has no content hash, so
the bot does not copy annotations to it.

An attachment that can never be annotated goes to "skipped" in the state file with the reason
(GIF, over the size cap, dead attachment, bytes are not an image). A transient failure is left
pending: the next export takes it again.
"""
import argparse
import collections
import datetime
import json
import os
import pathlib
import re
import subprocess
import sys
import time
import urllib.error
import urllib.request

import validate_batch

REFRESH_URL = "https://discord.com/api/v10/attachments/refresh-urls"
REFRESH_BATCH = 50  # AttachmentUrlRefreshService.BatchSize
USER_AGENT = "DiscordBot (https://github.com/wiktorkowalski/WojtusDiscord, 1.0)"
IMAGE_EXTENSIONS = {"jpg", "jpeg", "png", "webp", "gif"}  # ImageMagic.ImageExtensions
MAX_IMAGE_BYTES = 25 * 1024 * 1024  # MemeIndexOptions.MaxImageBytes
STATUSES = ["exported", "annotated", "validated", "imported"]
BATCH_NAME = re.compile(r"batch-(\d{4})$")
NETWORK_ERRORS = (urllib.error.URLError, TimeoutError, ConnectionError)


class Skip(Exception):
    """The attachment can never be annotated; the reason goes to the state file."""


# ---------------------------------------------------------------- state file

def load_state(run_dir):
    path = run_dir / "state.json"
    if path.exists():
        return json.loads(path.read_text())
    return {"version": 1, "batches": {}, "skipped": {}}


def save_state(run_dir, state):
    run_dir.mkdir(parents=True, exist_ok=True)
    temporary = run_dir / "state.json.tmp"
    temporary.write_text(json.dumps(state, ensure_ascii=False, indent=1))
    temporary.replace(run_dir / "state.json")  # atomic: a stopped run never leaves half a file


def get_batch(state, name):
    if name not in state["batches"]:
        sys.exit(f"no batch '{name}' in the state file; known: {', '.join(state['batches']) or 'none'}")
    return state["batches"][name]


def now():
    return datetime.datetime.now(datetime.timezone.utc).isoformat(timespec="seconds")


# ---------------------------------------------------------------- attachment list

def psql_command(args):
    if args.source == "local":
        return ["docker", "exec", "-i", args.container, "psql", "-U", "postgres", "-d", args.pg_db or "meme_eval"], None

    if not args.pg_host or not args.pg_port:
        sys.exit("--source prod needs --pg-host and --pg-port")
    env = dict(os.environ)
    if args.pg_password_file:
        env["PGPASSWORD"] = args.pg_password_file.read_text().strip()
    if not env.get("PGPASSWORD"):
        sys.exit("--source prod needs the password in env PGPASSWORD or in --pg-password-file")
    # The password and the read-only switch reach the container by name only, never on a command line.
    env["PGOPTIONS"] = "-c default_transaction_read_only=on"
    return ["docker", "run", "--rm", "-i", "-e", "PGPASSWORD", "-e", "PGOPTIONS", "postgres:18", "psql",
            "-h", args.pg_host, "-p", str(args.pg_port), "-U", args.pg_user,
            "-d", args.pg_db or "discord_event_service"], env


def load_candidates(args):
    """Every image attachment of the channel, as MemeSampleService.GetCandidatesAsync lists them."""
    sql = ("SELECT json_build_object('created_at', m.created_at_utc, 'attachments', m.attachments_json::text) "
           "FROM messages m JOIN channels c ON c.id = m.channel_id "
           f"WHERE c.discord_id = {int(args.channel)} AND m.has_attachments AND NOT m.is_deleted "
           "AND m.attachments_json IS NOT NULL ORDER BY m.discord_id;")
    command, env = psql_command(args)
    result = subprocess.run(command + ["-At", "-v", "ON_ERROR_STOP=1"], input=sql, capture_output=True, text=True, env=env)
    if result.returncode:
        sys.exit(f"the attachment query failed: {result.stderr.strip()}")

    candidates = {}
    for line in filter(None, result.stdout.split("\n")):
        row = json.loads(line)
        try:
            attachments = json.loads(row["attachments"]) or []
        except json.JSONDecodeError:
            continue  # the bot skips an unparseable attachments_json too
        for attachment in attachments:
            file_name, url = attachment.get("FileName"), attachment.get("Url")
            if file_name and url and extension_of(file_name) in IMAGE_EXTENSIONS:
                attachment_id = str(attachment["Id"])
                candidates.setdefault(attachment_id, {
                    "id": attachment_id, "file_name": file_name, "created_at": row["created_at"],
                    "url": url, "file_size": attachment.get("FileSize") or 0})
    return sorted(candidates.values(), key=lambda c: int(c["id"]))  # snowflake order = oldest first


def extension_of(file_name):
    return file_name.rsplit(".", 1)[-1].lower() if "." in file_name else ""


# ---------------------------------------------------------------- Discord

def read_token(env_file):
    for line in env_file.read_text().splitlines():
        name, _, value = line.partition("=")
        if name.strip() == "Discord__Token":
            return value.strip().strip('"').strip("'")
    sys.exit(f"Discord__Token is not in {env_file}")


def bare(url):
    return url.split("?", 1)[0]  # the signature is in the query; Discord matches on the rest


def refresh(token, bare_urls):
    """bare url -> fresh signed url, plus bare url -> error for a refresh call that failed."""
    fresh, failed = {}, {}
    for offset in range(0, len(bare_urls), REFRESH_BATCH):
        batch = bare_urls[offset:offset + REFRESH_BATCH]
        request = urllib.request.Request(
            REFRESH_URL, data=json.dumps({"attachment_urls": batch}).encode(),
            headers={"Authorization": f"Bot {token}", "Content-Type": "application/json", "User-Agent": USER_AGENT})
        try:
            with urllib.request.urlopen(request, timeout=30) as response:
                for entry in json.load(response).get("refreshed_urls") or []:
                    if entry.get("original") and entry.get("refreshed"):
                        fresh[bare(entry["original"])] = entry["refreshed"]
        except NETWORK_ERRORS as error:
            failed.update({url: f"refresh-urls call failed: {error}" for url in batch})
        time.sleep(0.3)
    return fresh, failed


def sniff_extension(data):
    """ImageMagic.SniffMimeType: file names lie (FB_IMG_*.jpg that is a png), the bytes decide."""
    if data[:3] == b"\xff\xd8\xff":
        return "jpg"
    if data[:4] == b"\x89PNG":
        return "png"
    if data[:4] == b"GIF8":
        return "gif"
    if data[:4] == b"RIFF" and data[8:12] == b"WEBP":
        return "webp"
    return None


def download(url):
    request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    try:
        with urllib.request.urlopen(request, timeout=60) as response:
            data = response.read()
    except urllib.error.HTTPError as error:
        if error.code == 404:
            raise Skip("dead attachment: the CDN answered 404") from error
        raise

    if len(data) > MAX_IMAGE_BYTES:
        raise Skip(f"unsupported: image too large ({len(data)} bytes)")
    extension = sniff_extension(data)
    if extension is None:
        raise Skip("unsupported: bytes are not a recognized image format")
    if extension == "gif":
        raise Skip("unsupported: gif (by content)")
    return data, extension


# ---------------------------------------------------------------- export

def static_skip_reason(candidate):
    if extension_of(candidate["file_name"]) == "gif":
        return "unsupported: gif"
    if candidate["file_size"] > MAX_IMAGE_BYTES:
        return f"unsupported: image too large ({candidate['file_size']} bytes per metadata)"
    return None


def record_skip(state, attachment_id, file_name, reason):
    state["skipped"].setdefault(attachment_id, {"reason": reason, "file_name": file_name})


def fetch_chunk(chunk, images, token):
    """Yields (candidate, outcome, value): ("path", local path), ("skip", reason) or ("transient", error)."""
    existing = {c["id"]: next(iter(images.glob(f"{c['id']}.*")), None) for c in chunk}
    fresh, refresh_failed = refresh(token, sorted({bare(c["url"]) for c in chunk if not existing[c["id"]]}))

    for candidate in chunk:
        key = bare(candidate["url"])
        if existing[candidate["id"]]:  # a stopped export left it there
            yield candidate, "path", existing[candidate["id"]]
        elif key in refresh_failed:
            yield candidate, "transient", refresh_failed[key]
        elif key not in fresh:
            yield candidate, "skip", "dead attachment: refresh-urls declined to re-sign"
        else:
            try:
                data, extension = download(fresh[key])
            except Skip as skip:
                yield candidate, "skip", str(skip)
            except NETWORK_ERRORS as error:
                yield candidate, "transient", f"download failed: {error}"
            else:
                path = images / f"{candidate['id']}.{extension}"
                # Atomic, like save_state: a killed export leaves no truncated image for the
                # resume to adopt. The leading dot keeps the temporary file out of the <id>.* glob.
                temporary = images / f".{candidate['id']}.part"
                temporary.write_bytes(data)
                temporary.replace(path)
                yield candidate, "path", path


def export_batch(name, pending, state, args, token):
    """Takes attachments from the front of `pending` until the batch is full. Returns the transient failures."""
    batch_dir = (args.run_dir / name).resolve()
    images = batch_dir / "images"
    images.mkdir(parents=True, exist_ok=True)
    items, failed = [], []

    while len(items) < args.batch_size and pending:
        chunk = [pending.pop(0) for _ in range(min(args.batch_size - len(items), len(pending)))]
        for candidate, outcome, value in fetch_chunk(chunk, images, token):
            entry = {"attachment_discord_id": candidate["id"], "file_name": candidate["file_name"],
                     "created_at": candidate["created_at"]}
            if outcome == "path":
                items.append({**entry, "local_path": str(value)})
                continue
            if outcome == "skip":
                record_skip(state, candidate["id"], candidate["file_name"], value)
            failed.append({**entry, "reason": value, "retried_later": outcome == "transient"})

    if items:
        (batch_dir / "manifest.json").write_text(json.dumps({"items": items, "failed": failed}, ensure_ascii=False, indent=1))
        state["batches"][name] = {
            "dir": str(batch_dir), "status": "exported", "exported_at": now(),
            "annotations_file": str(batch_dir / "annotations.json"),
            "attachment_ids": [item["attachment_discord_id"] for item in items]}
    save_state(args.run_dir, state)

    print(f"{name}: {len(items)} images in {images}, {len(failed)} failed")
    for entry in failed:
        print(f"  {'pending' if entry['retried_later'] else 'SKIPPED'} {entry['attachment_discord_id']}: {entry['reason']}")
    return [entry for entry in failed if entry["retried_later"]]


def command_export(args):
    state = load_state(args.run_dir)
    state.setdefault("channel_discord_id", str(args.channel))
    candidates = load_candidates(args)

    for candidate in candidates:
        reason = static_skip_reason(candidate)
        if reason:
            record_skip(state, candidate["id"], candidate["file_name"], reason)
    save_state(args.run_dir, state)

    in_batches = {i for batch in state["batches"].values() for i in batch["attachment_ids"]}
    pending = [c for c in candidates if c["id"] not in in_batches and c["id"] not in state["skipped"]]
    print(f"{len(candidates)} indexable attachments in channel {args.channel}: {len(in_batches)} in batches, "
          f"{len(state['skipped'])} skipped, {len(pending)} pending")

    token = read_token(args.env_file) if args.batches else None
    numbers = [int(m.group(1)) for m in map(BATCH_NAME.match, state["batches"]) if m]
    number = max(numbers, default=0)
    transient = []
    for _ in range(args.batches):
        if not pending:
            print("nothing is pending")
            break
        number += 1
        transient += export_batch(f"batch-{number:04d}", pending, state, args, token)

    if transient:
        sys.exit(f"{len(transient)} attachments failed for now; the next export takes them again")


# ---------------------------------------------------------------- bookkeeping

def command_adopt(args):
    """Registers a batch dir made outside this run, so its attachments are not exported again."""
    state = load_state(args.run_dir)
    if args.name in state["batches"]:
        sys.exit(f"batch '{args.name}' is already in the state file")
    batch_dir = args.batch_dir.resolve()
    attachment_ids = sorted(validate_batch.load_manifest_ids(batch_dir / "manifest.json"), key=int)
    state["batches"][args.name] = {
        "dir": str(batch_dir), "status": args.status, "exported_at": now(), "annotations_file": None,
        "attachment_ids": attachment_ids}
    save_state(args.run_dir, state)
    print(f"{args.name}: {len(attachment_ids)} attachments adopted as '{args.status}'")


def command_mark(args):
    state = load_state(args.run_dir)
    get_batch(state, args.batch)["status"] = args.status
    save_state(args.run_dir, state)
    print(f"{args.batch}: {args.status}")


def command_skip(args):
    """Takes one attachment out of its batch for good, with the reason (for example: the annotator refused it)."""
    state = load_state(args.run_dir)
    batch = get_batch(state, args.batch)
    if args.id not in batch["attachment_ids"]:
        sys.exit(f"{args.batch} has no attachment {args.id}")

    manifest_path = pathlib.Path(batch["dir"]) / "manifest.json"
    manifest = json.loads(manifest_path.read_text())
    item = next(i for i in manifest["items"] if str(i["attachment_discord_id"]) == args.id)
    manifest["items"].remove(item)
    manifest.setdefault("failed", []).append({**item, "reason": args.reason, "retried_later": False})
    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=1))

    batch["attachment_ids"].remove(args.id)
    record_skip(state, args.id, item["file_name"], args.reason)
    save_state(args.run_dir, state)
    print(f"{args.batch}: {args.id} skipped ({args.reason})")


def command_validate(args):
    state = load_state(args.run_dir)
    batch = get_batch(state, args.batch)
    annotations = args.file.resolve() if args.file else pathlib.Path(batch["annotations_file"] or "")
    if not annotations.is_file():
        sys.exit(f"{args.batch}: no annotations file at '{annotations}' (pass --file)")

    manifest = pathlib.Path(batch["dir"]) / "manifest.json"
    if validate_batch.validate(annotations, manifest, require_all=True):
        sys.exit(f"{args.batch}: NOT valid; the status stays '{batch['status']}'")

    batch.update(status="validated", annotations_file=str(annotations), validated_at=now())
    save_state(args.run_dir, state)
    print(f"{args.batch}: validated")


def command_imported(args):
    """Records the response of the import endpoint. A batch with rejected or missing items stays 'validated'."""
    state = load_state(args.run_dir)
    batch = get_batch(state, args.batch)
    result = json.loads(args.result.read_text())
    if "items" not in result:
        sys.exit(f"{args.result} is not an import response: {json.dumps(result)[:300]}")

    stored = {item["attachmentDiscordId"] for item in result["items"] if item["outcome"] in ("imported", "skipped")}
    rejected = [{"attachment_discord_id": item.get("attachmentDiscordId"), "reason": item.get("reason")}
                for item in result["items"] if item["outcome"] == "rejected"]
    missing = sorted(set(batch["attachment_ids"]) - stored)

    batch["import"] = {key: result[key] for key in ("imported", "overwritten", "skipped", "rejected")}
    batch["rejected"], batch["not_stored"] = rejected, missing
    if not missing:
        batch.update(status="imported", imported_at=now())
    save_state(args.run_dir, state)

    print(f"{args.batch}: {batch['import']}, status '{batch['status']}'")
    for entry in rejected:
        print(f"  REJECTED {entry['attachment_discord_id']}: {entry['reason']}")
    if missing:
        sys.exit(f"{args.batch}: {len(missing)} attachments of the batch are not stored: {', '.join(missing)}")


def command_status(args):
    state = load_state(args.run_dir)
    totals = dict.fromkeys(STATUSES, 0)
    for name, batch in sorted(state["batches"].items()):
        totals[batch["status"]] += len(batch["attachment_ids"])
        problems = len(batch.get("rejected", [])) + len(batch.get("not_stored", []))
        print(f"{name:16} {batch['status']:10} {len(batch['attachment_ids']):4}"
              + (f"  rejected or not stored: {problems}" if problems else ""))
    print("attachments: " + ", ".join(f"{count} {status}" for status, count in totals.items())
          + f", {len(state['skipped'])} skipped")
    reasons = collections.Counter(entry["reason"].split(" (")[0] for entry in state["skipped"].values())
    for reason, count in sorted(reasons.items()):
        print(f"  skipped {count:4}  {reason}")


def main():
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    commands = parser.add_subparsers(dest="command", required=True)

    def command(name, handler):
        sub = commands.add_parser(name)
        sub.add_argument("--run-dir", required=True, type=pathlib.Path)
        sub.set_defaults(handler=handler)
        return sub

    export = command("export", command_export)
    export.add_argument("--channel", required=True, type=int)
    export.add_argument("--env-file", required=True, type=pathlib.Path, help=".env with Discord__Token")
    export.add_argument("--batches", type=int, default=1, help="0 = only count what is pending")
    export.add_argument("--batch-size", type=int, default=40)
    export.add_argument("--source", choices=["prod", "local"], required=True)
    export.add_argument("--pg-host")
    export.add_argument("--pg-port")
    export.add_argument("--pg-user", default="postgres")
    export.add_argument("--pg-db")
    export.add_argument("--pg-password-file", type=pathlib.Path)
    export.add_argument("--container", default="wojtus-postgres")

    adopt = command("adopt", command_adopt)
    adopt.add_argument("--batch-dir", required=True, type=pathlib.Path)
    adopt.add_argument("--name", required=True)
    adopt.add_argument("--status", choices=STATUSES, default="exported")

    mark = command("mark", command_mark)
    mark.add_argument("--batch", required=True)
    mark.add_argument("--status", choices=STATUSES, required=True)

    skip = command("skip", command_skip)
    skip.add_argument("--batch", required=True)
    skip.add_argument("--id", required=True)
    skip.add_argument("--reason", required=True)

    validate = command("validate", command_validate)
    validate.add_argument("--batch", required=True)
    validate.add_argument("--file", type=pathlib.Path)

    imported = command("imported", command_imported)
    imported.add_argument("--batch", required=True)
    imported.add_argument("--result", required=True, type=pathlib.Path)

    command("status", command_status)

    args = parser.parse_args()
    args.handler(args)


if __name__ == "__main__":
    main()
