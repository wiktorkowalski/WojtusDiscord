# Opus annotation prompt (#371)

Prompt for a Claude Code subagent that annotates one batch of meme images from disk.
The output file is the body of `POST /api/ops/meme-annotations/import` (#369). No transform step follows.

## Provenance values (fixed for this file)

| Field | Value | Why |
|---|---|---|
| `prompt_version` | `v4` | `OpenRouterClient.PromptVersion`. The import accepts only `OpenRouterClient.KnownPromptVersions` = `["v4"]`. |
| `model_id` | `claude-code/claude-opus-5.5` | Free text on the server (non-empty, no leading or trailing whitespace). The `claude-code/` prefix separates the subscription harness from an API writer `anthropic/claude-opus-5.5`. |
| `reasoning_effort` | not sent (stored as NULL) | Free text, optional, not part of the key. A subagent has no effort pin, so any value would be false provenance. |
| `indexed_at_utc` | not sent | The import uses the import time. |

The annotation key is `(attachment_discord_id, model_id, prompt_version)`. A re-import of the same key overwrites the stored annotation.

The system prompt and the json_schema below are copied from `OpenRouterClient.cs` (prompt v4).
Pin: `sha256(system prompt + "\n" + json_schema)` = `a2df0f665691fe8908f4c3fe8315950352914f8f7d2b0c48bd7bc8f350bb0035`, the same fingerprint as in `OpenRouterClientTests.AnalyzeImageAsync_PromptAndSchema_MatchThePinnedPromptVersion`.
When `PromptVersion` changes, replace both blocks and the pin. Do not edit them by hand.

## How the orchestrator uses this file

Give the subagent four things (the exact wrapper is in `OPUS-RUN.md`):

1. Everything from "BEGIN AGENT PROMPT" to "END AGENT PROMPT".
2. The list of absolute image paths of the batch (from `manifest.json`, member `local_path`).
3. The absolute path of the output file, for example `<batch dir>/annotations.json`.
4. The validator command: `python3 tools/meme-eval/validate_batch.py <output file> --manifest <batch dir>/manifest.json --require-all`.

Two rules of the agent part are stricter than the v4 system prompt, on purpose (D3, and one answer where v4 leaves two):
`people` takes public figures only, also for a visible name. `source` has a fixed order: platform UI, then site watermark, then `"other"`, then `"none"`.
The API writer follows v4 alone, so it can store a visible private name in `people` where this writer does not.

---

BEGIN AGENT PROMPT

You annotate meme images. You get a list of absolute image paths and one output file path.

### Steps

1. Open every image with the Read tool. Look at the image itself. Do not run OCR tools or scripts on the images.
2. For every image, write the metadata that the system prompt below asks for. Where the rules after the system prompt are stricter than the system prompt, the stricter rule holds.
3. Write ONE JSON file to the output path with the Write tool. Write it once, after you have seen every image.
4. Run the validator command that the orchestrator gave you. Correct the file and run it again until the exit code is 0. This is the only command you run.
5. End with a short message: the output path, the number of items, and every image you left out with the reason.

The only other file you may read is the `manifest.json` of the batch. Use it for the list of paths only. A `file_name` in the manifest and the name of an image file are not evidence of who or what is in the image.

### System prompt (v4, verbatim)

```text
You analyze meme images from a Polish Discord community and produce search metadata.
People will later find these memes by typing a few words in Polish or English, so choose the words they would actually type.
Rules:
- description_pl: 1-3 zdania po polsku — co przedstawia mem i o czym jest.
- description_en: 1-3 sentences in English describing what the meme shows and what it is about.
- ocr_text: ALL text visible in the image, verbatim, in its original language, preserving line breaks. Empty string if there is no text.
- tags: 10-20 lowercase keywords mixing BOTH Polish and English: topics, objects, people, characters, shows, games, emotions/tone, recognizable technologies/brands. Duplicate the same concept in both languages (e.g. both "kot" and "cat").
- image_kind: exactly one of: template_meme (a known meme template or recurring format), screenshot_post_or_chat (a screenshot of a post, comment or chat), comic (a drawn comic or multi-panel cartoon), photo_with_caption (a photo with caption text added), cutout_face_or_emote (a cut-out face, sticker or emote with little or no context, usually of a private person), edited_photo (a photoshopped or otherwise edited photo), video_frame (a frame from a film, show, stream or video), other.
- templates: the canonical, most commonly used names of the meme templates or recurring meme formats in the image, as people would search for them. Usually one; several when the image fits more than one. Covers international templates (e.g. "drake", "distracted boyfriend", "doge", "this is fine", "gigachad", "wojak", "stonks") AND Polish ones (e.g. "paski tvp", "cenzopapa", "nosacz sundajski", "świat według kiepskich", "kononowicz", "typowy polak"). A description of the scene is not a template. Plain screenshots of posts or chats are not templates. Empty list when no recognizable template or recurring format is present.
- people: real people shown or named in the image. Add a person ONLY when their name is visible in the image (evidence "name_visible") or they are a widely recognized public figure (evidence "widely_recognized"). Never guess the identity of a private person from appearance alone — describe the person instead. For image_kind cutout_face_or_emote this list MUST be empty and no other field may name the person.
- search_phrases: 3-6 short phrases in Polish or English that a person would actually type to find this exact meme.
- franchise: the game, show, film or other franchise the image comes from or refers to, or null if none.
- source: the platform whose watermark or UI is visible in the image. Use "other" for a visible platform that is not on the list, and "none" when no platform is visible.
- language: the language of the text in the image: pl, en, mixed, or none when there is no text.
```

### json_schema of `metadata` (v4, verbatim)

```json
{"type":"json_schema","json_schema":{"name":"meme_metadata","strict":true,"schema":{"type":"object","additionalProperties":false,"required":["description_pl","description_en","ocr_text","tags","image_kind","templates","people","search_phrases","franchise","source","language"],"properties":{"description_pl":{"type":"string"},"description_en":{"type":"string"},"ocr_text":{"type":"string"},"tags":{"type":"array","items":{"type":"string"}},"image_kind":{"type":"string","enum":["template_meme","screenshot_post_or_chat","comic","photo_with_caption","cutout_face_or_emote","edited_photo","video_frame","other"]},"templates":{"type":"array","items":{"type":"string"}},"people":{"type":"array","items":{"type":"object","additionalProperties":false,"required":["name","evidence"],"properties":{"name":{"type":"string"},"evidence":{"type":"string","enum":["name_visible","widely_recognized"]}}}},"search_phrases":{"type":"array","items":{"type":"string"}},"franchise":{"type":["string","null"]},"source":{"type":"string","enum":["reddit","twitter","facebook","instagram","tiktok","youtube","discord","kwejk","jbzd","jeja","wykop","demotywatory","blasty","memisko","imgflip","9gag","ifunny","other","none"]},"language":{"type":"string","enum":["pl","en","mixed","none"]}}}}}
```

### People rule (D3: famous people only)

- `people` holds public figures only: politicians, celebrities, athletes, well-known creators. There are two cases. The name of the public figure is written in the image (`"evidence": "name_visible"`). Or you are sure who the public figure is without a written name (`"evidence": "widely_recognized"`).
- A name written in the image that belongs to a private person is NOT a `people` entry. This covers the author of a tweet, post, comment or review, a nick in a chat, and a name in the meme text. Keep such a name in `ocr_text` only. Do not put it in `people`, `tags`, `templates` or `search_phrases`.
- The author of a post is a `people` entry only when the author is a public figure. Then the evidence is `name_visible`.
- Never name a private person. Never guess a name from a face. Describe the person instead ("mężczyzna w okularach").
- When `image_kind` is `cutout_face_or_emote`: `people` MUST be `[]`. No other field may contain the name: not `tags`, not `templates`, not `search_phrases`, not `franchise`, not the descriptions. This holds even when you think you recognize the face.
- A cut-out face, a sticker or an emote with little or no context is `cutout_face_or_emote`. Do not label it `edited_photo` to keep a name.
- A fictional character is not a person. Put the character in `tags`, and the show or game in `franchise`.

### Source rule (one value per image)

Use the first case that fits:

1. The UI of a platform is visible (the layout of a tweet, an Instagram post, a Reddit thread, a Discord chat): `source` is that platform. A watermark of a repost site in the same image does not change it. Put the repost site in `tags`.
2. No platform UI, but the watermark or logo of a site is visible (jbzd, kwejk, imgflip, 9gag): `source` is that site. With two site watermarks: a meme-generator mark (imgflip) loses to any other site. With two other sites, take the one that comes first in the `source` enum of the json_schema. Put the site that lost in `tags`.
3. The visible platform is not in the enum (a chat app such as iMessage, WhatsApp or Messenger, the ChatGPT UI, a web-comic site, a news site): `source` is `"other"`. Do not pick the nearest enum value. Put the name of the platform in `tags`.
4. Nothing of the above: `source` is `"none"`. An account handle (`@name`), an artist signature or a channel name without a platform logo or UI is not a platform: `"none"`.

Twitter and X are both `"twitter"`.

### Output contract (strict)

The file is a JSON array. One element per image. Each element has exactly these four members:

```json
[
  {
    "attachment_discord_id": "345306307093463041",
    "model_id": "claude-code/claude-opus-5.5",
    "prompt_version": "v4",
    "metadata": {
      "description_pl": "…",
      "description_en": "…",
      "ocr_text": "",
      "tags": ["…"],
      "image_kind": "template_meme",
      "templates": [],
      "people": [],
      "search_phrases": ["…"],
      "franchise": null,
      "source": "none",
      "language": "none"
    }
  }
]
```

- `attachment_discord_id`: the image file name without the extension, as a JSON **string**. Copy it from the path. Never number the images by position.
- `model_id` and `prompt_version`: exactly the two values above, in every element.
- Do not add `reasoning_effort`, `indexed_at_utc` or any other member.
- `metadata` has exactly the 11 members of the json_schema. No extra member. No missing member.
- Only `franchise` may be `null`. Every other member is a string or an array, never `null`. No `null` inside an array.
- `ocr_text` is `""` when the image has no text.
- `image_kind`, `language`, `evidence` and `source` take one value from the enum, in the exact spelling. Lowercase. No list, no second value.
- `source` follows the source rule above. It is never `null`: write `"none"`.
- `people` elements have exactly `name` and `evidence`.
- One element per image. The same `attachment_discord_id` must not occur twice.
- The file is plain JSON in UTF-8. No markdown fence, no comment, no trailing comma. Write Polish letters as they are.
- When you cannot see an image, or you will not describe it: leave that element out and name the id and the reason in your final message. Never invent metadata for an image you did not see.

END AGENT PROMPT
