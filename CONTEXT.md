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

_DB-enforced_ (§B1 / #199): `is_active = false ⇔ end-timestamp IS NOT NULL` is a CHECK constraint on each — `ck_bans_lifecycle` (`unbanned_at_utc`), `ck_activities_lifecycle` (`ended_at_utc`) — mirroring the soft-delete constraint convention but on the domain end columns (`LifecycleFactConstraint`).

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

**`event_timestamp_utc`**: when the underlying Discord event *occurred*. Equals `received_at_utc` only when Discord doesn't supply a distinct timestamp (voice, presence, reaction adds). For messages, this is `e.Message.Timestamp`; for audit-log entries, the entry snowflake's `CreationTimestamp` (§B3); for invite creation, `invite.CreatedAt` (§B3).

_Avoid_: "created at" / "updated at" as event-stream vocabulary — those mean entity lifecycle, not event timing.

### Meme indexing

**Meme (stat)**:
The loose community metric: any message with an attachment or embed, in any channel. Powers the Memes metric, MemeLords, and per-person MemeCount.
_Avoid_: using bare "meme" for the searchable entity below.

**Indexed meme**:
One image attachment from a meme channel that has vision-model-generated metadata and is findable via meme search. The unit is the attachment, not the message — a message with three images yields three indexed memes.
_Avoid_: meme (collides with the stat), media message.

**Meme channel**:
A channel whose image attachments are in scope for meme indexing. A configurable set, seeded with #memes; not derived from channel name.

### Conversational assistant

The bot's interactive side: members talk to the bot, it answers questions about the guild from ingested data, and (for admins) performs Discord actions. A distinct bounded concern from ingestion — it *reads* the event/entity stores and the live Discord API; it does not produce the event stream.

**Conversation**:
A thread- or DM-scoped exchange between a member and the bot, keyed by the channel/thread Snowflake (a thread spawned from an @mention, or a DM channel). Its Turns are recorded append-only.
_Avoid_: "session", "chat history" as the entity name.

**Turn**:
One full exchange in a Conversation: a member's message through the agentic loop's rounds to the final Assistant reply. What the loop's round cap, timeout, and tracing span are scoped to.
_Avoid_: "turn" for a single recorded entry — that is a Conversation message.

**Conversation message**:
One recorded entry in a Conversation's append-only history — a member message, an assistant reply (possibly bundling its tool calls), or a tool result. Several Conversation messages make up one Turn. Not a Structured event (gateway projection) and not a Discord message (see Flagged ambiguities).

**Assistant reply**:
The bot's natural-language output Turn (as opposed to an assistant tool-call Turn) — the text a member reads.

**Round**:
One model invocation inside a Conversation turn's agentic loop. A round either requests tools (loop continues) or yields a final Assistant reply. Each round is surfaced visibly — interim message → tool calls → next round — rather than collapsed into one reply.

**Read tool**:
A tool answering a question with no side effects — from **ingested** data (a curated query such as meme search, or the guarded read-only `query_database`) or from **live** Discord state (a lookup where only the live answer is authoritative and the ingested copy is derived or laggy, e.g. who is in voice right now). The SQL path runs inside a read-only transaction that drops to the non-superuser, SELECT-only `wojtus_query` role (via `SET LOCAL ROLE`) before any model SQL, never as the superuser ingestion login; the role is provisioned by the `AddConversationQueryRole` migration (see ADR-0006).
_Avoid_: treating "read" as implying "from the database" — the defining property is no side effects, not the data source.

**Action tool**:
A tool performing a Discord write (post, react, role, pin, delete, moderation). Admin-only; irreversible ones require explicit button confirmation.
_Avoid_: collapsing Read tools and Action tools into one undifferentiated "tool" — their authorization and reversibility differ.

## Flagged ambiguities

- **"Message" (three senses)**:
  - A Discord gateway message ingested into `messages` / `message_events` — the canonical event-stream meaning.
  - A **Conversation message** (the LLM-chat sense — one entry in a Conversation's history).
  - The bot's own posted Discord message, which is itself re-ingested as the first sense.
  Reserve bare "message" for the ingested Discord entity; say **Conversation message** for conversation entries.
- **"Context"**: the retrieved Conversation history window (what the bot replays to the model) vs the model's token context window vs the per-event ingestion `EventContext`. Disambiguate per use.
- **"Active"** is used for two unrelated concepts:
  - `bans.is_active` — ban is currently in force.
  - `activities.is_active` — Discord presence is currently playing.
  Both are lifecycle facts (see above), not soft-deletes. Resist the pull to normalise them under one column name; the underlying concepts are different.
