# Handoff — Phase 2 of epic #53

> **One-shot doc.** Read it, then delete it (`rm HANDOFF.md && git add -A && git commit -m "chore: consume HANDOFF.md"`) so a future handoff isn't confused with this one. If you're not actively starting Phase 2, ignore this file and leave it be.

You're picking up after Phase 1 + the §P1.9 hotfix landed. **Don't re-read the prior conversation** — everything load-bearing was pushed into durable artifacts. Read those instead.

## Read in this order

1. **`docs/phase-1-retro.md`** — what we learned in Phase 1, why decisions were made, surprises worth remembering.
2. **Epic #53** — read the body, then the `2026-05-22 Grilling session` comment. The comment is the source of truth for current state, sequencing, open OD#s, and scope refinements.
3. **`CONTEXT.md`** + **`docs/adr/0001-drop-voice-states-snapshot.md`**, **0002**, **0003** — vocabulary + load-bearing decisions.
4. **Issue #69 (§P2.1)** — your starting point. Body already updated with refined scope (skip bans/activities, drop voice_states, trivial backfill).

## In-flight at handoff

- **PR #116** (this retro doc) — open, awaiting GH Claude review + merge.
- **#115** — open consolidated checklist for the silent-null FK audit across 10 remaining handlers (Ban, Invite, ScheduledEvent, StageInstance, Integration, AutoModRule, Channel, Role, Emoji, Sticker). One PR per handler, follow the §P1.9 / #114 pattern exactly. Not a Phase 2 blocker.

Everything else from this session is **merged**:
- #110 docs grilling, #111 §P1.9 hotfix, #113 backfill robustness, #114 PresenceEventHandler fix.

## Phase 2 execution order (locked)

1. **#69 §P2.1** — add `deleted_at_utc` to 10 entities, drop `voice_states`. Unblocks #70.
2. **#70 §P2.5** — CHECK constraint `is_deleted ↔ deleted_at_utc IS NOT NULL`.
3. **#71 §P2.2, #72 §P2.3, #73 §P2.4** — parallel-safe.
4. **#74 §P2.6** — NOT NULL on Message FKs (depends on §P1.9 which is done + #61 which is merged).

## Critical patterns to preserve

The retro doc covers these in detail; quick pointers:

- **Early-flush rule**: any handler that calls `RawEventLogService.SerializeAndLogAsync` and then any `*UpsertService` MUST `await db.SaveChangesAsync()` between them, or the staged raw event log is at risk on 23505 race. See `MessageEventHandler.cs:38`, `ThreadEventHandler.cs:31/81/128`, `PresenceEventHandler.cs:86`.
- **Reference convention**: snowflakes in event tables, Guids in core entities (ADR-0002). §P2.3 drops Guid FKs from event tables (100% NULL in prod — vestigial).
- **Soft-delete vs lifecycle-fact**: don't shoehorn `bans.is_active` or `activities.is_active` into the soft-delete convention (CONTEXT.md flagged ambiguity).
- **Threads-as-channels** (ADR-0003): no separate `threads` table. Already realised by §P1.9.

## User preferences worth remembering up-front

In `~/.claude/projects/-Users-wiktorkowalski-dev-WojtusDiscord/memory/MEMORY.md` (auto-loaded), but the load-bearing ones for Phase 2:

- **No data loss** in migrations. Before each one, state explicitly whether any meaningful data is lost (column drops, NULL backfills, etc.). Note "column loss, no info loss" when a JOIN preserves the info (relevant for §P2.3).
- **Never test against prod DB**. Local Docker postgres for `dotnet run` smoke tests. Prod is read-only via direct psql (Docker `postgres:18` container — pattern is in this session's bash history, easy to crib).
- **Bulk operations one-by-one with verification** — for issue/PR creation. Don't batch-script.
- **Pre-deploy + post-deploy + post-backfill log baselines** — capture all three with `mcp__homelab__CountErrors` + `mcp__homelab__search_logs`. Most "errors after deploy" are actually old-container shutdown noise in the 7-second window before the new container comes up; cross-check timestamps before raising alarm.

## Bot operational

- Hosted at `https://wojtusdiscord.home.vicio.ovh` (admin endpoints under `/api/ops/`).
- Prod DB at `192.168.1.12:5433` (creds in user's `.env`; container access via `docker run --rm postgres:18 psql -h 192.168.1.12 -p 5433 -U postgres -d discord_event_service`).
- Container: `discord-event-service` (logs via `mcp__homelab__GetContainerLogs`).
- Deploy: master push → GH Actions `Build and Deploy` workflow → dockge restarts the container.

## Suggested skills for Phase 2 work

- **`grill-with-docs`** if you need to challenge any assumption before writing code. The CONTEXT.md + ADRs are already populated, so the skill mostly *consults* them rather than building from scratch.
- **`code-review`** (effort `medium`) before push on each Phase 2 PR. The user's pre-commit memory mandates it.
- **`verify`** if you want to manually exercise a feature after deploy — useful for §P2.5 (verify the CHECK actually rejects bad inserts).
- **`diagnose`** only if Phase 2 surfaces another live-DB bug like 2026-05-03. Otherwise overkill.
- **Don't invoke `to-issues` for Phase 2** — the issues already exist with refined scope.

## Pause points (user cadence)

The user's `~/.claude/CLAUDE.md` defines the default cadence. The pause points relevant for Phase 2:

1. Build green + code-review passes → commit + push + PR → cadence continues automatically through GH Claude review + comment addressing.
2. **PAUSE** before `gh pr merge --squash --delete-branch`. Summarise + ask.
3. After merge → `gh run watch` the deploy → ~30s wait → pull container logs over last ~5 min → surface anomalies (not just error counts; look for unexpected patterns).
4. If a step fails (build red, review blocks, deploy fails) → STOP and surface, don't retry blindly.

## What NOT to do

- Don't fold the §P115 silent-null sweep into Phase 2 PRs. They're separate work.
- Don't re-pick the OD#4 / threads / snowflake-vs-Guid decisions — they're ADR'd.
- Don't write to `voice_states` in any Phase 2 PR — it's being dropped in §P2.1.
- Don't merge any Phase 2 PR without explicit user approval at the pause point. The user's cadence is firm on this.
