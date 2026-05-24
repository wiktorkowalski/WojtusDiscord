# WojtusDiscord

## Stack

- .NET (C#), use `dotnet build/run/format/restore`
- PostgreSQL via `docker-compose.yml` (local dev DB on port 5432)
- Dev bot account + separate Discord server configured in `.env`
- Production DB: READ ONLY, never run migrations or writes against prod. Prompt user for credentials and address before connecting. Use `docker run --rm -it postgres:18 psql` to query (no local psql installed)

## Development workflow

1. **Code changes** — work locally, check `dotnet build` for warnings and errors
2. **Test migrations** — run against local Postgres (`docker compose up -d postgres`)
3. **Test locally** — run dev bot with `dotnet run` against local DB and dev Discord server. Prompt user to perform actions in Discord when needed (send messages, react, join voice, etc.) and verify behavior in logs and database
4. **Test aggressively** — verify every change end-to-end whenever possible. Build green alone is not enough signal
5. **Create PR** — push branch, `gh pr create`, wait for GitHub Claude review
6. **Review loop** — after every review round, read ALL comments (top-level + inline via `gh api repos/<o>/<r>/pulls/<N>/comments`). Apply fixes, push, repeat until approved and no outstanding comments
7. **Wait for user approval** — do NOT merge without explicit user approval, even if review is clean and checks pass
8. **Post-merge deployment** — after merge, watch deploy via `gh run watch`, then pull container logs (`mcp__homelab__GetContainerLogs` for `discord-event-service`) covering ~5 min before and after deploy. Surface anomalies
9. **Post-deploy verification** — if needed, ask user to trigger actions in Discord, then check logs and prod database to confirm changes work in production
