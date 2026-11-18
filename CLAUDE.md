# WojtusDiscord

## Stack

- .NET (C#), use `dotnet build/run/format/restore`
- PostgreSQL via `docker-compose.yml` (local dev DB on port 5432)
- Dev bot account + separate Discord server configured in `.env`
- Discord MCP (`SaseQ/discord-mcp` via Docker) available globally — **drive live tests autonomously with it**: send/edit messages, add reactions, read channels, join voice, manage roles, etc. without prompting the user. Only ask the user to act when the MCP is unavailable or the action genuinely can't be done via MCP
- Prod runs on **orion** (Hetzner dedicated, `ssh orion`), compose in `~/wojtusdiscord` from `deploy/docker-compose.yml`. The homelab instance is retired: never touch `ssh ubuntu` for WojtusDiscord.
- Monitoring: Prometheus + Loki + Tempo + Grafana on orion (`~/monitoring`, set up by hand, not in this repo). Dashboards and alert rules live in `deploy/observability/`; `./sync.sh` there validates and pushes them to orion. Dashboards and rules are generated: edit `tools/build.py` / `tools/alerts.py`, not the output. Bot metrics come from `Infrastructure/BotMetrics.cs` (`/metrics`); a new metric needs a panel and, if it can go wrong, a rule
- Production DB: READ ONLY, never run migrations or writes against prod. Read as role `wojtus_agent` (`pg_read_all_data`, read-only transactions, `deploy/init/10-readonly-roles.sql`). Connect over `ssh orion` with the SQL on stdin, so no shell parses it twice: `ssh orion docker exec -i wojtus-postgres psql -U wojtus_agent -d discord_event_service <<'SQL'` … `SQL` (the DB has no published port)

## Development workflow

1. **Code changes** — work locally, check `dotnet build` for warnings and errors
2. **Test migrations** — run against local Postgres: `docker compose up -d postgres` (container `wojtus-postgres`, host port 5432, db `postgres`). EF migrations auto-apply on bot boot. NEVER point the app at prod
3. **Test locally** — boot the dev bot from `src/DiscordEventService` (background, log to a file), let it connect + cold-sync, then exercise the changed paths via the Discord MCP (send/edit/react/voice in the dev server). Verify in the bot log and the local DB. (Concrete boot/stop recipe + dev guild id live in agent memory)
4. **Test aggressively** — verify every change end-to-end whenever possible. Build green alone is not enough signal. For changes with a crisp behavioral contract, add a Testcontainers integration test (no DB mocking) **and** still live-verify on the dev bot
5. **Create PR** — push branch, `gh pr create`, wait for GitHub Claude review
6. **Review loop** — after every review round, read ALL comments (top-level + inline via `gh api repos/<o>/<r>/pulls/<N>/comments`). Apply fixes, push, repeat until approved and no outstanding comments
7. **Merge** — review clean and checks green → `gh pr merge <N> --squash --delete-branch`, no user approval needed
8. **Post-merge deployment** — after merge, watch deploy via `gh run watch`, wait for container restart, then over `ssh orion`: `docker ps` (expect `discord-event-service` healthy) + `docker logs --since <deploy time> discord-event-service` + targeted Loki queries (`ssh orion 'docker run --rm --network observability curlimages/curl:latest -s -G http://loki:3100/loki/api/v1/query_range --data-urlencode "query=..."'`, the whole remote command quoted once) covering ~5–15 min around deploy. Surface anomalies (new patterns, unexpected callsites), not just error counts
9. **Post-deploy verification** — **standard step, do it autonomously**: run read-only SELECT checks against the prod DB to confirm the change's data looks sane (recent rows, no duplicates/corruption from the change, relevant invariants hold). A reusable verification query set is in agent memory


**Docs-only changes skip the pipeline (#352).** `build.yml` and `ci.yml` carry `paths-ignore: ['**.md', 'docs/**', 'deploy/observability/**']`, so **a merge touching only Markdown neither builds the image nor deploys**. The same holds for a merge that touches only `deploy/observability/**` (config of the monitoring stack, no part of the image). A docs-only *PR* still builds — see the second bullet. Two consequences:

- Steps 8 and 9 do not apply — there is no deploy to watch. `:latest` stays on the previous commit, so `/health` reports a `GIT_SHA` **behind** master HEAD. That is correct, not a failed deploy.
- The filter on `build.yml` is on `push` only, never `pull_request`: `build-deploy / Build` is a required check produced by a job inside the reusable workflow, and a workflow skipped by path filtering leaves its contexts pending forever. A docs-only PR therefore still runs Build, and only the `ci.yml` checks are skipped.

**Skipping a deploy the filters cannot catch.** GitHub honours `[skip ci]` (also `[ci skip]`, `[no ci]`, `[skip actions]`, `[actions skip]`) in the head commit message — no workflow support needed. Use it for a change that alters no part of the image, typically a `.github/workflows/*`-only edit.

- Pass it explicitly: `gh pr merge <N> --squash --delete-branch --subject "<title> [skip ci]"`. This is the only form that holds. The repo is on `squash_merge_commit_title: COMMIT_OR_PR_TITLE`, which takes the **commit** title for a single-commit PR and the PR title only from two commits up — so a marker in the PR title alone is silently dropped on the usual one-commit PR, and prod restarts anyway.
- **Never put it in a commit on the branch.** A run skipped by commit message is never created, so its checks stay **pending** — exactly like path filtering. `build-deploy / Build` would never report and the PR could not be merged at all.
