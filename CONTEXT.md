# Discord Event Service

A bot ingesting one Discord guild's gateway events into PostgreSQL for later analysis. Optimised for completeness and faithful timelines, not throughput.

> **Status of this glossary.** Some entries describe target state being landed in Phase 2 of #53 rather than currently-realised schema. Such entries are marked `[target — landing in §PX.Y]`. Code may not yet match; the glossary is the contract the migrations are converging on.

## Language

### Lifecycle states

**Soft-delete** `[target — landing in §P2.1 / #69]`:
The Discord-side entity was removed (channel, role, webhook, emote, sticker, message, integration, automod rule, scheduled event, stage instance, invite). Recorded with `is_deleted bool` + `deleted_at_utc timestamptz`. Row stays for history.

_Currently realised_: only `MessageEntity` has both columns. All other listed entities have `is_deleted` only; `deleted_at_utc` lands with §P2.1.

_Avoid_: archived, inactive, retired.

**Lifecycle fact**:
A row that records something that *happened* and later *ended*, where neither end of the lifecycle is a "delete". Uses domain-specific vocabulary, not the soft-delete convention.
- **Ban**: `is_active` + `BannedAtUtc` + `UnbannedAtUtc`. Unbanning is not a delete; it is the natural end of the ban's lifecycle.
- **Activity** (Discord presence "playing X"): `is_active` + `FirstSeenAtUtc` + `LastSeenAtUtc` + `EndedAtUtc`. Stopping playing is not a delete.

_Do not_ shoehorn these into `is_deleted`/`deleted_at_utc`. The shared `bool` shape is a coincidence; the semantics differ.

### References

**Snowflake** (`*_discord_id`, `ulong`): the ID Discord uses (e.g. `user_discord_id`, `channel_discord_id`). The natural key, supplied by every gateway payload, never null on event-table rows. Canonical for **event tables**.

**Internal Guid** (`*_id`): EF-managed primary/foreign key. Always populated on **core entity** rows; sometimes `NULL` on event-table rows where the user/channel/guild upsert hadn't completed yet. Canonical for **core entity tables and aggregates**.

Cross-family queries join `event.X_discord_id = core_table.discord_id`. See ADR-0002.

### Event streams

**Raw event log** (`raw_event_logs`): unprocessed JSON dump of every gateway event we receive, keyed by `received_at_utc`. Source of truth for replay.

**Structured event** (`*_events` tables — `message_events`, `member_events`, `reaction_events`, etc.): handler-parsed projection of a raw event into typed columns. Append-only.

**Core entity** (`channels`, `messages`, `users`, …): current-state snapshot maintained by upsert from events. Mutable.

**Thread** `[target — landing in §P1.9 / #109]`: a Discord thread; stored as a row in `channels` with `type ∈ {10, 11, 12}` (`NewsThread`, `PublicThread`, `PrivateThread`). `parent_discord_id` points to the parent channel the thread was spawned from. No separate `threads` table. See ADR-0003.

_Currently realised_: a single thread row already exists in `channels` (recovered by an old guild-channels backfill); `ThreadEventHandler` does not yet upsert into `channels`. §P1.9 closes that.

**Parent channel** (`channels.parent_discord_id`): for a category-child channel, the category; for a thread, the channel it was spawned in. Snowflake reference (per ADR-0002 — no Guid FK between channel rows).

**Failed event** (`failed_events`): an event whose handler threw. Dead-letter for replay. Populated by `FailedEventService`.

### Timestamps

**`received_at_utc`**: when *we* received the gateway event. Always trustworthy.

**`event_timestamp_utc`**: when the underlying Discord event *occurred*. Equals `received_at_utc` only when Discord doesn't supply a distinct timestamp (voice, presence, reaction adds). For messages, this is `e.Message.Timestamp`.

_Avoid_: "created at" / "updated at" as event-stream vocabulary — those mean entity lifecycle, not event timing.

## Flagged ambiguities

- **"Active"** is used for two unrelated concepts:
  - `bans.is_active` — ban is currently in force.
  - `activities.is_active` — Discord presence is currently playing.
  Both are lifecycle facts (see above), not soft-deletes. Resist the pull to normalise them under one column name; the underlying concepts are different.
