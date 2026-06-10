# Meme index stores metadata only, no image archive

**Status**: Accepted (2026-06-09).

The meme-search feature persists vision-model metadata (descriptions, OCR text, tags, source, template) about image attachments in meme channels, but never archives the image bytes. Image bytes are downloaded transiently during indexing — hashed for dedupe, sent to the vision model, then discarded. Search results are delivered as Discord jump links (`discord.com/channels/{guild}/{channel}/{message}`); displaying an image inline means re-fetching the message via the bot at query time for a fresh signed CDN URL, since stored attachment URLs carry `ex=`/`hm=` signatures and expire within ~24h.

Considered and rejected: archiving originals (+thumbnails) to a homelab volume or MinIO (~2.4 GB for the 2017–2026 corpus). Rejected because the chosen UX — slash command answering with jump links into the channel — needs no served images, and Discord itself remains the image store. Accepted consequence, deliberately: **if a message is deleted (or Discord drops the file), its image is unrecoverable** — the index row survives but is excluded from search results, and a later decision to archive can only cover images that still exist. Re-running the corpus through a better model costs a full re-download (~63 history-pagination API calls + ~2 GB transfer), which is cheap at this scale.
