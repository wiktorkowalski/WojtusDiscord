# Threads are channels

**Status**: Accepted. Handler & schema realisation lands in §P1.9 (#109).

A Discord thread is modelled as a row in `channels` with `ChannelType` ∈ `{NewsThread, PublicThread, PrivateThread}` — not as a separate `threads` table. Once §P1.9 lands, `ThreadEventHandler` will upsert the thread into `channels` on `ThreadCreated`, update it on `ThreadUpdated`, and on `ThreadDeleted` flip `is_deleted=true`. (The `deleted_at_utc` column lands separately in §P2.1 / #69; `ThreadDeleted` populates it from §P2.1 onward.) `thread_events` continues to record the event stream against the same channel row; messages in a thread point their `channel_id` to that thread row.

Decided after the 2026-05-03 incident: 67 message rows had `channel_id IS NULL` because their parent threads only existed in `thread_events`, never in `channels`. Prior behaviour relied on the periodic guild-channels backfill incidentally upserting threads it could still see in the listing API — threads that had been archived or deleted before the next backfill were lost forever. Treating threads as channels makes the upsert happen at `ThreadCreated` time, before any backfill, and matches the implicit precedent of the single thread row already in `channels`.
