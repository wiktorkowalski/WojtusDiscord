# WojtusDiscord

## Stack

- .NET (C#), use `dotnet build/run/format/restore`
- PostgreSQL via `docker-compose.yml` (local dev DB on port 5432)
- Dev bot account + separate Discord server configured in `.env`
- Discord MCP (`SaseQ/discord-mcp` via Docker) available globally — **drive live tests autonomously with it**: send/edit messages, add reactions, read channels, join voice, manage roles, etc. without prompting the user. Only ask the user to act when the MCP is unavailable or the action genuinely can't be done via MCP
- Production DB: READ ONLY (SELECT-only by convention), never run migrations or writes against prod. Connect via `docker run --rm postgres:18 psql ...` (no local psql installed). Connection details are in the agent's private local memory; DB is reachable only on the home network

## Development workflow

1. **Code changes** — work locally, check `dotnet build` for warnings and errors
2. **Test migrations** — run against local Postgres: `docker compose up -d postgres` (container `wojtus-postgres`, host port 5432, db `postgres`). EF migrations auto-apply on bot boot. NEVER point the app at prod
3. **Test locally** — boot the dev bot from `src/DiscordEventService` (background, log to a file), let it connect + cold-sync, then **drive the live test yourself via the Discord MCP** (send/edit/react/voice in the dev server) to exercise the changed paths. Verify in the bot log and the local DB. Only fall back to asking the user to act if the MCP is unavailable. (Concrete boot/stop recipe + dev guild id live in agent memory)
4. **Test aggressively** — verify every change end-to-end whenever possible. Build green alone is not enough signal. For changes with a crisp behavioral contract, add a Testcontainers integration test (no DB mocking) **and** still live-verify on the dev bot
5. **Create PR** — push branch, `gh pr create`, wait for GitHub Claude review
6. **Review loop** — after every review round, read ALL comments (top-level + inline via `gh api repos/<o>/<r>/pulls/<N>/comments`). Apply fixes, push, repeat until approved and no outstanding comments
7. **Wait for user approval** — do NOT merge without explicit user approval, even if review is clean and checks pass
8. **Post-merge deployment** — after merge, watch deploy via `gh run watch`, wait for container restart, then `mcp__homelab__get_container_status` (expect healthy) + pull logs (`mcp__homelab__GetContainerLogs` for `discord-event-service`) + targeted `mcp__homelab__QueryLoki` covering ~5–15 min around deploy. Surface anomalies (new patterns, unexpected callsites), not just error counts
9. **Post-deploy verification** — **standard step, do it autonomously**: run read-only SELECT checks against the prod DB to confirm the change's data looks sane (recent rows, no duplicates/corruption from the change, relevant invariants hold). Connection details + a reusable verification query set are in agent memory. Drive any needed Discord actions via the MCP. Only prompt the user for the prod DB password (and only when not already in memory)
