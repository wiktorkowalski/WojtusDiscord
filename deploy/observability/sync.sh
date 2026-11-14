#!/usr/bin/env bash
# Push the WojtusDiscord dashboards and alert rules from this directory to orion and reload.
# Safe to re-run: it copies files, validates, reloads.
#
#   ./sync.sh            copy + validate + reload
#   ./sync.sh --check    validate the local files only (promtool), touch nothing
#   HOST=orion ./sync.sh   ssh alias of the host (default: orion)
#
# What it manages on orion (the shared stack in ~/monitoring reads these directories):
#   grafana/dashboards/wojtusdiscord/*.json -> /srv/observability/dashboards/wojtusdiscord/
#       Grafana re-reads the folder; no restart.
#   prometheus/wojtusdiscord-alerts.yml     -> /srv/observability/rules/   (promtool check, then SIGHUP)
#
# Not managed here: the stack itself (Prometheus, Loki, Tempo, Grafana, Alloy, blackbox, scrape jobs,
# datasources) lives by hand in ~/monitoring on orion, and the bot compose is deploy/docker-compose.yml.
set -euo pipefail

HOST="${HOST:-orion}"
RULES=/srv/observability/rules
DASH=/srv/observability/dashboards/wojtusdiscord
HERE="$(cd "$(dirname "$0")" && pwd)"
PROMTOOL_IMAGE=prom/prometheus:v3.15.1

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
# The backup lives outside the rules directory: Prometheus loads every *.yml there.
ssh "$HOST" "mkdir -p $DASH ~/monitoring/backups && cp -p $RULES/wojtusdiscord-alerts.yml ~/monitoring/backups/wojtusdiscord-alerts.yml.bak-$stamp 2>/dev/null || true"

scp -q "$HERE"/grafana/dashboards/wojtusdiscord/*.json "$HOST:$DASH/"
scp -q "$HERE/prometheus/wojtusdiscord-alerts.yml" "$HOST:$RULES/wojtusdiscord-alerts.yml"
ssh "$HOST" "chmod 644 $DASH/*.json $RULES/wojtusdiscord-alerts.yml"

# Validate the whole Prometheus config with the rules on the host, then reload. On a failed check the
# previous rule file is put back, so a bad edit never reaches the running Prometheus.
if ! ssh "$HOST" "docker run --rm -v ~/monitoring/prometheus:/etc/prometheus:ro -v $RULES:/etc/prometheus/rules:ro --entrypoint promtool $PROMTOOL_IMAGE check config /etc/prometheus/prometheus.yml"; then
  ssh "$HOST" "if [ -f ~/monitoring/backups/wojtusdiscord-alerts.yml.bak-$stamp ]; then cp -p ~/monitoring/backups/wojtusdiscord-alerts.yml.bak-$stamp $RULES/wojtusdiscord-alerts.yml; else rm -f $RULES/wojtusdiscord-alerts.yml; fi" || true
  echo "promtool rejected the config on orion; previous alert file restored (or the new one removed), nothing reloaded" >&2
  exit 1
fi
ssh "$HOST" "docker kill -s HUP monitoring-prometheus-1 > /dev/null"
sleep 20
ssh "$HOST" "docker run --rm --network observability curlimages/curl:latest -s http://prometheus:9090/api/v1/rules" | python3 -c '
import json, sys
groups = [g for g in json.load(sys.stdin)["data"]["groups"] if "wojtusdiscord" in g["file"]]
rules = [r for g in groups for r in g["rules"]]
bad = [(r["name"], r.get("health"), r.get("lastError", "")) for r in rules if r.get("health") not in ("ok", "unknown")]
active = [(r["name"], r["state"]) for r in rules if r.get("state") not in (None, "inactive", "unknown")]
print(f"prometheus: {len(rules)} wojtusdiscord rules loaded; unhealthy: {bad or 0}; pending/firing: {active or 0}")
sys.exit(1 if bad else 0)'
echo "done"
