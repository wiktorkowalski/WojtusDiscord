# Phase 1 retro — what we learned

Captured 2026-05-22 after epic #53 Phase 1 + §P1.9 hotfix shipped.

## The incident that surfaced the systemic bug

**2026-05-03**: 69 messages persisted with `channel_id IS NULL` over a 5h26m window. Phase 1 had landed `§P1.4` transactions (#61) two weeks earlier, but transactions didn't help — the handler **didn't throw**, it silently wrote `channel_id = null`:

```csharp
var channel = await db.Channels.FirstOrDefaultAsync(c => c.DiscordId == e.Channel.Id);
// ...
ChannelId = channel?.Id,  // ← silently null
```

The channel in question was a thread DSharpPlus emitted `ThreadCreated` for, but `ThreadEventHandler` only wrote to `thread_events` — never upserted the thread into `channels`. So the message handler's lookup found nothing, and the FK silently went `NULL`.

The same silent-null pattern existed in ~15 other handlers. The 2026-05-03 incident only hit `messages` because threads are created on the fly; other entities (guilds, users, regular channels) are usually known at lookup time.

## Three load-bearing decisions captured as ADRs

- **ADR-0001** drop `voice_states` snapshot table. Live audit found it empty for 2.7 months despite 743 events — the upsert path had been broken since day one and nobody missed it. Current state derivable from `voice_state_events` via `DISTINCT ON`.
- **ADR-0002** snowflakes canonical in event tables, Guids canonical in core entities. Confirmed by a live probe: **100%** of event-table `Guid?` FK columns are NULL. Vestigial. Drop them in §P2.3.
- **ADR-0003** threads are channels (`type ∈ {10, 11, 12}`). Not a separate `threads` table. `ThreadEventHandler` upserts into `channels` so messages always have a valid `channel_id`.

## The "early-flush" pattern — load-bearing for any handler

The §P1.9 fix introduced `ChannelUpsertService` + `GuildUpsertService` with the canonical `ExecuteUpdate → 23505-retry-with-Add` shape (mirrors `UserService`). The catch path calls `ChangeTracker.Clear()` to drop the failed-insert entity. But if the handler had previously staged a `RawEventLog` row via `SerializeAndLogAsync` (which doesn't save, just adds to tracker), the `Clear()` discards it too — the raw event log is permanently lost.

**Rule**: in any handler that calls `SerializeAndLogAsync` and then any upsert service, add an explicit `await db.SaveChangesAsync()` **between** them. Otherwise the staged raw log is at risk on any 23505 race.

The pattern landed in MessageEventHandler, ThreadEventHandler (×3 paths), and PresenceEventHandler. Apply it to every handler in #115's audit list.

## Backfill ergonomics

- The thread-channel backfill endpoint added in §P1.9 is the operator's hand-tool. Idempotent (matches `WHERE channel_id IS NULL`). Returns counts so the operator can sanity-check. Exact path lives in `Endpoints/OpsEndpoints.cs` and is intentionally not named here — admin endpoints are presently unauthenticated, and this is a public repo.
- For the 2026-05-03 orphans the raw event JSON was stub-fallback (pre-#99 serializer bug — `{"error": "Serialization failed"...}`). Match by `raw_event_logs.channel_discord_id` (column, not JSON) within ±2s of the orphan's `first_seen_utc`. For future orphans with intact JSON the hybrid resolver added in #113 prefers a deterministic `event_json->'message'->>'id'` match.
- **Placeholder policy**: only insert a `[unknown thread …]` row if Discord API returns `NotFoundException` / `UnauthorizedException`. Let transient errors propagate so the operator can retry — don't permanently materialise a 429 as a placeholder.
- One thing we missed initially: the original §P1.9 backfill picked `db.Guilds.First()` as the placeholder's guild — fine for single-guild bot but wrong-by-construction. #113 fix derives guild per-orphan from the matched raw event's `guild_discord_id`.

## Surprises worth remembering

- The **Feb-15 private thread** that already existed in `channels` (`1472568996745449614`) got there via a **guild-channels backfill on 2026-04-26**, two months after the thread was created — not via the original ThreadCreated handler. That's why "some threads ended up in channels, most didn't" — purely incidental, depending on whether they were still visible to the listing API by the next backfill.
- Discord let us re-fetch both archived May-3 threads via `GetChannelAsync` even though they were no longer in the listing API. Recovered both with real names + parents. We allocated `placeholders=0`.
- The Feb-15 thread's `type` changed from `12` (PrivateThread) to `11` (PublicThread) after we re-fetched it in #113. Either Discord made it public sometime in the past 3 months, or our original ThreadCreated payload stored the wrong type. Either way, refreshing from live API is the right behaviour.
- Half the bot-downtime infrastructure was landed (#64, 2026-04-27) but not wired (#65, 2026-05-12) when 2026-05-03 incident happened. The "no downtime row exists in that window" was therefore *not* evidence the bot was up — just evidence the tracking wasn't recording yet. Be careful with that argument in future investigations.

## Phase 2 sequencing locked

After §P1.9 (#109) landed:

1. **§P2.1 (#69)** — add `deleted_at_utc` to 10 entities + drop `voice_states`. Unblocks §P2.5.
2. **§P2.5 (#70)** — CHECK constraint `is_deleted ↔ deleted_at_utc IS NOT NULL`.
3. **§P2.2 (#71), §P2.3 (#72), §P2.4 (#73)** — parallel-safe.
4. **§P2.6 (#74)** — NOT NULL on Message FKs. Pre-requires §P1.9 (now done) + #61.

Open follow-ups: **#112** (hybrid JSON match — done in #113), **#115** (silent-null FK audit across 10 remaining handlers; PR #114 did Presence as the first).

## Open decisions still on Epic #53

- **OD#1** historical-gap backfill — not blocking; revisit after Phase 2.
- **OD#5** `failed_events` replay strategy — affects §P4.2 only.
- **OD#6** empty-content normalization — affects §P4.5 only.

OD#2 (no dedup), OD#3 (filter widened in #63), OD#4 (drop voice_states, ADR-0001) are resolved.
