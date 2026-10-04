#!/usr/bin/env python3
"""Downloads the images of one annotation batch (#371), named by attachment id.

Reads the attachments from the local database (meme_index + messages), refreshes the signed CDN
URLs through Discord (attachments/refresh-urls, 50 per call, needs no guild membership) and
writes each image as <attachment_discord_id>.<ext>. A file is never named by position: the
2026-10-02 eval lost 30 of 40 images to a position-based mapping.

    python3 download_batch.py --guild <id> --out <batch dir> --env-file <.env with Discord__Token>
                              [--ids-file memes.json] [--db meme_eval]

--ids-file limits the batch to the ids in a JSON array of objects with an "id" member.
Output: <batch dir>/images/<id>.<ext> and <batch dir>/manifest.json:
  {"items": [{attachment_discord_id, file_name, created_at, local_path}], "failed": [{..., reason}]}
A re-run keeps the files that are already there.
"""
import argparse
import json
import pathlib
import subprocess
import sys
import time
import urllib.error
import urllib.request

REFRESH_URL = "https://discord.com/api/v10/attachments/refresh-urls"
REFRESH_BATCH = 50  # AttachmentUrlRefreshService.BatchSize
USER_AGENT = "DiscordBot (https://github.com/wiktorkowalski/WojtusDiscord, 1.0)"
IMAGE_EXTENSIONS = {"jpg", "jpeg", "png", "webp", "gif"}


def read_token(env_file):
    for line in env_file.read_text().splitlines():
        name, _, value = line.partition("=")
        if name.strip() == "Discord__Token":
            return value.strip().strip('"').strip("'")
    sys.exit(f"Discord__Token is not in {env_file}")


def load_attachments(container, database, guild):
    sql = ("SELECT json_build_object('id', m.attachment_discord_id::text, 'file_name', m.file_name, "
           "'created_at', msg.created_at_utc, 'attachments', msg.attachments_json) "
           "FROM meme_index m JOIN messages msg ON msg.id = m.message_id "
           f"WHERE m.guild_discord_id = {guild} ORDER BY m.attachment_discord_id;")
    result = subprocess.run(
        ["docker", "exec", "-i", container, "psql", "-U", "postgres", "-d", database, "-At", "-v", "ON_ERROR_STOP=1"],
        input=sql, capture_output=True, text=True)
    if result.returncode:
        sys.exit(result.stderr)

    rows = []
    for line in result.stdout.split("\n"):
        if not line:
            continue
        row = json.loads(line)
        stored = next((a["Url"] for a in row.pop("attachments") or [] if str(a.get("Id")) == row["id"]), None)
        rows.append({**row, "url": stored})
    return rows


def bare(url):
    return url.split("?", 1)[0]


def refresh(token, bare_urls):
    """bare url -> fresh signed url, plus the bare urls whose refresh call failed."""
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
        except (urllib.error.URLError, TimeoutError) as error:
            failed.update({url: f"refresh batch failed: {error}" for url in batch})
        time.sleep(0.3)
    return fresh, failed


def download(url, path):
    request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    with urllib.request.urlopen(request, timeout=60) as response:
        data = response.read()
    if not data:
        raise ValueError("empty body")
    path.write_bytes(data)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--guild", required=True, type=int)
    parser.add_argument("--out", required=True, type=pathlib.Path)
    parser.add_argument("--env-file", required=True, type=pathlib.Path)
    parser.add_argument("--ids-file", type=pathlib.Path)
    parser.add_argument("--db", default="meme_eval")
    parser.add_argument("--container", default="wojtus-postgres")
    args = parser.parse_args()

    rows = load_attachments(args.container, args.db, args.guild)
    failed = []
    if args.ids_file:
        wanted = {str(item["id"]) for item in json.loads(args.ids_file.read_text())}
        known = {row["id"] for row in rows}
        failed += [{"attachment_discord_id": i, "reason": "not in the database"} for i in sorted(wanted - known)]
        rows = [row for row in rows if row["id"] in wanted]

    images = args.out / "images"
    images.mkdir(parents=True, exist_ok=True)

    def fail(row, reason):
        failed.append({"attachment_discord_id": row["id"], "file_name": row["file_name"],
                       "created_at": row["created_at"], "reason": reason})

    todo = []
    items = []
    for row in rows:
        extension = row["file_name"].rsplit(".", 1)[-1].lower() if "." in row["file_name"] else ""
        if extension not in IMAGE_EXTENSIONS:
            fail(row, f"not an image extension: '{extension}'")
            continue
        if not row["url"]:
            fail(row, "no stored url for this attachment id in attachments_json")
            continue
        row["path"] = images / f"{row['id']}.{extension}"
        todo.append(row)

    missing = [row for row in todo if not row["path"].exists()]
    fresh, refresh_failed = refresh(read_token(args.env_file), sorted({bare(row["url"]) for row in missing}))

    for row in todo:
        if not row["path"].exists():
            key = bare(row["url"])
            if key in refresh_failed:
                fail(row, refresh_failed[key])
                continue
            if key not in fresh:
                fail(row, "dead attachment: refresh-urls declined to re-sign")
                continue
            try:
                download(fresh[key], row["path"])
            except (urllib.error.URLError, TimeoutError, ValueError) as error:
                fail(row, f"download failed: {error}")
                continue
        items.append({"attachment_discord_id": row["id"], "file_name": row["file_name"],
                      "created_at": row["created_at"], "local_path": str(row["path"].resolve())})

    manifest = args.out / "manifest.json"
    manifest.write_text(json.dumps({"items": items, "failed": failed}, ensure_ascii=False, indent=1))
    print(f"{len(items)} images in {images}, {len(failed)} failed, manifest {manifest}")
    for entry in failed:
        print(f"  FAILED {entry['attachment_discord_id']}: {entry['reason']}")
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
