#!/usr/bin/env bash
# Push the WojtusDiscord monitoring config from this directory to the homelab host
# and reload what changed. Safe to re-run: it copies files, validates, reloads.
#
#   ./sync.sh            copy + validate + reload
#   ./sync.sh --check    validate the local files only (promtool), touch nothing
#   HOST=ubuntu ./sync.sh   ssh alias of the Docker host (default: ubuntu)
#
# What it manages (host paths under /home/ubuntu/docker):
#   grafana/dashboards/wojtusdiscord/*.json -> grafana/config/dashboards/dashboards/wojtusdiscord/
#       Grafana re-reads the folder every 30 s; no restart.
#   prometheus/wojtusdiscord-alerts.yml     -> prometheus/config/   (promtool check, then SIGHUP)
#   grafana/datasources/tempo.yml           -> grafana/config/datasources/   (needs a Grafana restart; printed, not done)
#   tempo/, postgres-exporter/              -> same-named stack dirs (compose up is printed, not done)
#
# What it does NOT manage, because the host files are shared with every other homelab
# service and are not versioned — apply these by hand once, they are mirrored here:
#   prometheus/scrape-configs.yml   block at the end of scrape_configs in prometheus.yml,
#                                   plus `- 'wojtusdiscord-alerts.yml'` under rule_files
#   blackbox/http_2xx.yml           module in blackbox.yml
#   host/prometheus-compose.patch   --web.enable-remote-write-receiver (Tempo span metrics),
#                                   --enable-feature=exemplar-storage (histogram -> trace links)
#   host/grafana-prometheus-datasource.patch   exemplarTraceIdDestinations -> Tempo
#   host/wojtusdiscord-compose.patch   pg_stat_statements preload + Telemetry__OtlpTracesEndpoint
#       After that patch: `docker compose up -d --no-deps discord-event-service` for the bot.
#       A plain `up -d` also recreates Postgres.
#   postgres-exporter/.env          symlink to ../wojtusdiscord/.env (one home for the DB password)
#   CREATE EXTENSION pg_stat_statements;   once, in the discord_event_service database
set -euo pipefail

HOST="${HOST:-ubuntu}"
REMOTE=/home/ubuntu/docker
HERE="$(cd "$(dirname "$0")" && pwd)"
PROMTOOL_IMAGE=prom/prometheus:v3.5.0

check_local() {
  docker run --rm -v "$HERE/prometheus:/c:ro" --entrypoint promtool "$PROMTOOL_IMAGE" \
    check rules /c/wojtusdiscord-alerts.yml
  if compgen -G "$HERE/prometheus/tests/*.yml" > /dev/null; then
    docker run --rm -v "$HERE/prometheus:/c:ro" -w /c/tests --entrypoint promtool "$PROMTOOL_IMAGE" \
      test rules $(cd "$HERE/prometheus/tests" && ls *.yml)
  fi
  for f in "$HERE"/grafana/dashboards/wojtusdiscord/*.json; do
    python3 -m json.tool "$f" > /dev/null || { echo "invalid JSON: $f" >&2; exit 1; }
  done
  echo "local files: OK"
}

check_local
[[ "${1:-}" == "--check" ]] && exit 0

stamp="$(date +%Y%m%d-%H%M%S)"
ssh "$HOST" "mkdir -p $REMOTE/grafana/config/dashboards/dashboards/wojtusdiscord $REMOTE/tempo/config $REMOTE/postgres-exporter \
  && cp -p $REMOTE/prometheus/config/wojtusdiscord-alerts.yml $REMOTE/prometheus/config/wojtusdiscord-alerts.yml.bak-$stamp 2>/dev/null || true"

scp -q "$HERE"/grafana/dashboards/wojtusdiscord/*.json "$HOST:$REMOTE/grafana/config/dashboards/dashboards/wojtusdiscord/"
scp -q "$HERE/prometheus/wojtusdiscord-alerts.yml" "$HOST:$REMOTE/prometheus/config/wojtusdiscord-alerts.yml"

# Files whose change needs a container restart: copy only when different, then say so.
restart_notes=()
sync_if_changed() {  # <local> <remote> <note>
  if ! ssh "$HOST" "cat '$2' 2>/dev/null" | cmp -s - "$1"; then
    ssh "$HOST" "[ -f '$2' ] && cp -p '$2' '$2.bak-$stamp' || true"
    scp -q "$1" "$HOST:$2"
    restart_notes+=("$3")
  fi
}
sync_if_changed "$HERE/grafana/datasources/tempo.yml" "$REMOTE/grafana/config/datasources/tempo.yml" "docker restart grafana"
sync_if_changed "$HERE/tempo/config/tempo.yml"        "$REMOTE/tempo/config/tempo.yml"                "cd $REMOTE/tempo && docker compose restart"
sync_if_changed "$HERE/tempo/compose.yml"             "$REMOTE/tempo/compose.yml"                     "cd $REMOTE/tempo && docker compose up -d"
sync_if_changed "$HERE/postgres-exporter/compose.yml" "$REMOTE/postgres-exporter/compose.yml"         "cd $REMOTE/postgres-exporter && docker compose up -d"

# Validate the whole Prometheus config on the host, then reload. On a failed check the
# previous rule file is put back, so a bad edit never reaches the running Prometheus.
if ! ssh "$HOST" "docker run --rm --user 1000:1000 -v $REMOTE/prometheus/config:/c:ro --entrypoint promtool $PROMTOOL_IMAGE check config /c/prometheus.yml"; then
  # No backup means a first run: remove the new file, or the next Prometheus restart would read it.
  rules=$REMOTE/prometheus/config/wojtusdiscord-alerts.yml
  ssh "$HOST" "if [ -f $rules.bak-$stamp ]; then cp -p $rules.bak-$stamp $rules; else rm -f $rules; fi" || true
  echo "promtool rejected the config on the host; previous alert file restored (or the new one removed), nothing reloaded" >&2
  exit 1
fi
ssh "$HOST" "docker exec prometheus kill -HUP 1"
sleep 20
ssh "$HOST" "docker run --rm --network monitoring curlimages/curl:latest -s http://prometheus:9090/api/v1/rules" | python3 -c '
import json, sys
groups = [g for g in json.load(sys.stdin)["data"]["groups"] if "wojtusdiscord" in g["file"]]
rules = [r for g in groups for r in g["rules"]]
bad = [(r["name"], r.get("health"), r.get("lastError", "")) for r in rules if r.get("health") not in ("ok", "unknown")]
active = [(r["name"], r["state"]) for r in rules if r.get("state") not in (None, "inactive")]
print(f"prometheus: {len(rules)} wojtusdiscord rules loaded; unhealthy: {bad or 0}; pending/firing: {active or 0}")
sys.exit(1 if bad else 0)'

if ((${#restart_notes[@]})); then
  echo "changed files that need a restart on $HOST — run these when ready:"
  printf '  %s\n' "${restart_notes[@]}"
fi
echo "done"
