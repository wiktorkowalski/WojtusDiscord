#!/usr/bin/env python3
"""Generator for the WojtusDiscord monitoring config. No dependency outside the Python stdlib.

Produces (paths relative to deploy/observability/):
  grafana/dashboards/wojtusdiscord/wojtus-overview.json   the morning page
  grafana/dashboards/wojtusdiscord/wojtus-events.json     gateway events, failures, health checks
  grafana/dashboards/wojtusdiscord/wojtus-ai.json         assistant, slash commands, meme search
  grafana/dashboards/wojtusdiscord/wojtus-runtime.json    .NET runtime, HTTP, dependencies, traces
  prometheus/wojtusdiscord-alerts.yml                     all alert rules (from alerts.py)

NOT generated: wojtus-postgres.json and wojtus-logs.json. Their JSON is the source of
truth; edit them in Grafana or by hand. Do not hand-edit a generated file: the next run
of this script overwrites it.

Run:
  python3 deploy/observability/tools/build.py       regenerate every file above
  python3 deploy/observability/tools/validate.py    panel ids, layout, units, descriptions
  python3 deploy/observability/tools/validate.py --live    also send every query to Prometheus
  deploy/observability/sync.sh --check              promtool check + rule tests + JSON
  deploy/observability/sync.sh                      copy to the homelab host and reload (the deploy)

Files:
  build.py     this file: one function per dashboard (overview, events, ai, runtime)
  lib.py       panel builders (stat, ts, bars, bargauge, state_timeline, ...), colours, the shared frame
  alerts.py    the alert rules
  validate.py  checks for the four generated dashboards

Add a panel: in the function of the dashboard, under the right d.row(...), write
  d.add(<builder>(title, targets, description, ...), width, height)
Width is in 24ths of the page; panels flow left to right and wrap. Give every panel a
description, and a line in lib.EMPTY_TEXT when "No samples in this time range" is not the
right empty text. Then run this script and validate.py.

Add an alert: see the header of alerts.py.

Counting rule (xi / xinc): the bot creates a counter series on its first increment, and
increase() never counts the sample that creates a series. Use xi(...) for every count of a
wojtus_* counter; plain rate() only for high-volume series and histogram quantiles.
"""
import os

import alerts
from lib import *  # noqa: F401,F403

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "grafana", "dashboards", "wojtusdiscord")

IV, R = "$__interval", "$__range"
PER_MIN = " / ($__interval_ms / 60000)"
ERR_RE = r"^(\[[^\]]+\] \[[^\]]+\] \[(Error|Fatal|Critical|Crit|Warn|Warning)\]|(fail|crit|warn): )"
BIRTH = (" Counts include the first sample of a new series, which increase() and rate() miss:"
         " the bot creates a counter series on its first increment.")
NOT_PROBE = 'http_route!~"/?(health|metrics)"'


def xi(metric, extra="", rng=IV, step="1m"):
    return xinc(metric, sel(J, extra), rng, step)


def hq(quantile, metric, extra="", window="$__rate_interval", by=""):
    grp = "le" + (", " + by if by else "")
    return f"histogram_quantile({quantile}, sum by ({grp})(rate({metric}_bucket{{{sel(J, extra)}}}[{window}])))"


def hqx(quantile, metric, extra="", rng=R, by="", step="1m"):
    grp = "le" + (", " + by if by else "")
    return f"histogram_quantile({quantile}, sum by ({grp})({xi(metric + '_bucket', extra, rng, step)}))"


def meanx(metric, extra="", rng=R, by=""):
    grp = f" by ({by})" if by else ""
    return (f"sum{grp}({xi(metric + '_sum', extra, rng)})"
            f" / (sum{grp}({xi(metric + '_count', extra, rng)}) > 0)")


HTTP = 'job="wojtusdiscord-http"'
EX = (" A dot is one request with a trace (an exemplar): click it to open the trace in Tempo."
      " Only a request inside a kept trace has one.")
# Event types that arrive only when the bot connects: each start replays the guild.
BOOT_TYPES = 'event_type!~"GuildCreated|GuildMembersChunked|ThreadListSynced"'
MSG_KIND_COLORS = {"text": BLUE, "attachment": ORANGE, "embed": PURPLE, "sticker": TEAL, "empty": NEUTRAL}
PHASE_COLORS = {"tokenize": PURPLE, "sql": BLUE, "map": TEAL}


def buckets(title, metric, desc, extra="", w_unit="short"):
    """One bar per histogram bucket, named by its upper bound, over the range."""
    return bargauge(title, f"sum by (le)({xi(metric + '_bucket', extra, R)})", unit=w_unit, legend="{{le}}",
                    fmt="heatmap", decimals=0, desc=desc)
EVENTS_LINK = link("Open this event type on Events & Ingest", "/d/wojtus-events/?var-event_type=${__field.labels.event_type}")
HC = "wojtus_healthcheck_"


def availability_panel():
    return state_timeline("Availability", [
        q(f"max(up{{{J}}})", "Bot process"),
        q(f"min(wojtus_gateway_connected{{{J}}}) or on () (0 * max(up{{{J}}}))", "Discord gateway"),
        q(f"max(probe_success{{{HTTP}}})", "Public URL"),
        q('max(pg_up{job="wojtus-postgres"})', "Postgres"),
        q(f"1 - max(wojtus_db_unwritable_window_pending{{{J}}})", "DB writes")],
        "Green while the part works, red while it does not: the scrape of the bot, the Discord gateway,"
        " the HTTP probe of /health through Traefik, Postgres, and writes to Postgres. A gap means no sample.")


def uptime_panel(window, step, decimals, orientation):
    days = window[:-1]
    p = stat(f"Uptime, {days} d", "", unit="percentunit", decimals=decimals, thresholds=AVAILABILITY,
             text_mode="value_and_name",
             desc=f"1 minus the time each part was seen down, as a share of the full {days} days: bot process,"
                  " Discord gateway, public URL, Postgres. Green from 99.9% (10 minutes down in a week), yellow"
                  " from 99%. A deploy restart counts as down time: about 15 s, 0.0025% of a week, so a few"
                  " deploys a day stay green and a crash loop does not. Time before a series existed counts as up.")
    p["targets"] = [q(e, name, instant=True, ref="ABCD"[i]) for i, (name, e) in enumerate(availability_exprs(window, step))]
    p["options"]["orientation"] = orientation
    if orientation == "horizontal":
        p["options"]["text"] = {"titleSize": 12, "valueSize": 18}
    return p


def health_tiles():
    return tiles("Bot health checks",
                 f'max by (check)({HC}failing{{{J}}}) or on () label_replace(vector(-1), "check", "HealthCheckJob", "", "")',
                 "{{check}}",
                 "The bot's own HealthCheckJob, every 5 minutes: failed events, ingest stall, event-type ratio,"
                 " crash loop, event silence, backfill stall, open downtime, timestamp invariant. A red tile"
                 " also fires the WojtusHealthCheckFailing alert after 10 minutes.",
                 no_value="no health-check run yet")


def memory_panel():
    return ts("Bot memory", [
        q(f"docker_container_memory_current_bytes{{{BOT}}}", "cgroup total (includes page cache)"),
        q(f"docker_container_memory_anon_bytes{{{BOT}}}", "anonymous (process memory)"),
        q(f"dotnet_process_memory_working_set_bytes{{{J}}}", "working set (.NET)"),
    ], unit="bytes", colors={"cgroup total (includes page cache)": BLUE,
                              "anonymous (process memory)": ORANGE, "working set (.NET)": PURPLE},
        desc="cgroup memory of the discord-event-service container from the host textfile collector,"
             " and the working set the runtime reports. The container has no memory limit.")


def cpu_panel():
    return ts("Process CPU", [q(f"sum(rate(process_cpu_time_seconds_total{{{J}}}[$__rate_interval]))", "CPU")],
              unit="percentunit", fixed=BLUE, legend=False, decimals=1,
              desc="CPU time of the bot process per second: 100% is one full core. The host exports no"
                   " per-container CPU series, so this is the bot's own process metric.")


def hangfire_panel():
    return ts("Hangfire jobs by state", [
        q(f'max by (state)(wojtus_hangfire_jobs{{{J},state!="succeeded"}})', "{{state}}")],
        unit="short", decimals=0, line_interp="stepAfter", soft_max=5,
        colors={"enqueued": BLUE, "scheduled": PURPLE, "processing": TEAL, "failed": CRIT},
        desc="Jobs in each Hangfire state, read from the database at most every 30 s. 'succeeded' is"
             " left out: it is a lifetime total and hides the other states.")


# --------------------------------------------------------------------------- Overview
def overview():
    d = Dash("wojtus-overview", "WojtusDiscord / Overview",
             "The morning page for the WojtusDiscord bot: state, events, logs, resources, jobs, alerts"
             " and traces. Bot series come from job wojtusdiscord (the bot's /metrics).")
    d.add(text("**Is the bot alive, is it ingesting, did anything fail, what did it cost?**"
               " Row 1: the state now. Row 2: the last 24 hours. Detail is on the linked dashboards."), 24, 2)
    d.row("Status")
    ok_bad = steps((GOOD, None), (CRIT, 1))
    d.add(updown("Bot", f"up{{{J}}}", "up of the wojtusdiscord scrape job: 1 when Prometheus can scrape /metrics."), 3, 3)
    d.add(updown("Gateway", f"min(wojtus_gateway_connected{{{J}}})",
                 "1 when every gateway shard is connected to Discord.",
                 up="\u2713 ONLINE", down="\u2715 OFFLINE"), 3, 3)
    d.add(updown("Public URL", f"max(probe_success{{{HTTP}}})",
                 "Blackbox probe of https://wojtusdiscord.home.vicio.ovh/health through Traefik, every 30 s."), 3, 3)
    d.add(updown("Postgres", 'pg_up{job="wojtus-postgres"}',
                 "pg_up from postgres_exporter: 1 when the exporter can query Postgres."), 3, 3)
    d.add(stat("DB writes", f"max(wojtus_db_unwritable_window_pending{{{J}}})", unit="none",
               thresholds=ok_bad, color_mode="background", no_value="\u2715 NO DATA",
               mappings=[{"type": "value", "options": {"0": {"text": "\u2713 OK", "index": 0},
                                                        "1": {"text": "\u2715 BLOCKED", "index": 1}}}],
               desc="BLOCKED while an unwritable-database window is open, or while the window still waits"
                    " for its row: the bot cannot write events to Postgres."), 3, 3)
    d.add(stat("Firing alerts", 'count(ALERTS{service="wojtusdiscord",alertstate="firing"}) or vector(0)',
               decimals=0, thresholds=ok_bad,
               desc="Prometheus alerts with service=wojtusdiscord that fire now. The table below names them."), 3, 3)
    d.add(stat("Last event", f"time() - max(wojtus_last_event_timestamp_seconds{{{J}}} > 0)", unit="s",
               no_value="n/a", decimals=0,
               desc="Time since the last gateway event of any kind. Hours of silence are normal at night;"
                    " the bot's own event-silence check judges it."), 3, 3)
    d.add(stat("Last bot start", f"max(wojtus_process_start_time_seconds{{{J}}}) * 1000", unit="dateTimeFromNow",
               desc="Boot instant of the bot process. A recent value means a deploy or a crash."), 3, 3)
    d.add(stat("Events, 24 h", f'sum({xi("wojtus_events_total", "", "24h", "5m")})', decimals=0, no_value="0",
               thresholds=steps((CRIT, None), (GOOD, 1)),
               desc="Gateway events the bot ingested in the last 24 hours. Zero means no ingest." + BIRTH), 3, 3)
    d.add(with_links(stat("Failed, 24 h", f'sum({xi("wojtus_events_total", FAILED, "24h", "5m")})',
               decimals=0, no_value="0", thresholds=ok_bad,
               desc="Gateway events with at least one failed handler in the last 24 hours."),
               link("Open Events & Ingest", "/d/wojtus-events/")), 3, 3)
    d.add(with_links(stat("Errors, 24 h", f'sum({xi("wojtus_log_events_total", ERRORS, "24h", "5m")})',
               decimals=0, no_value="0", thresholds=ok_bad,
               desc="Error and critical log events of the last 24 hours, counted in the bot. Click the value"
                    " for the Logs dashboard."), LOGS_LINK), 3, 3)
    d.add(stat("Failed jobs", f'max(wojtus_hangfire_jobs{{{J},state="failed"}})', decimals=0,
               thresholds=ok_bad,
               desc="Jobs in the Hangfire failed state now. A failed job stays until someone deletes or requeues it."), 3, 3)
    d.add(stat("Cost, 24 h", f'sum({xi("wojtus_conversation_cost_usd_total", "", "24h", "5m")} or vector(0))'
               f' + sum({xi("wojtus_meme_vision_cost_usd_total", "", "24h", "5m")} or vector(0))',
               unit="currencyUSD", decimals=2, no_value="$0.00",
               thresholds=steps((GOOD, None), (WARN, 1), (CRIT, 2)),
               desc="Cost of conversation and meme vision model calls in the last 24 hours. The conversation"
                    " cost is what the provider billed for the call, so it holds the web search fee already."
                    " The conversation alert fires above 2 USD."), 3, 3)
    d.add(stat("Restarts, 24 h", f"sum(changes(wojtus_process_start_time_seconds{{{J}}}[24h]))", decimals=0,
               no_value="0", thresholds=steps((GOOD, None), (WARN, 4), (CRIT, 10)),
               desc="Process starts in the last 24 hours. A deploy is one restart; a few a day is normal."), 3, 3)
    d.add(stat("Health check", f"time() - max({HC}last_run_timestamp_seconds{{{J}}} > 0) or vector(-1)", unit="s",
               decimals=0, thresholds=steps((GOOD, None), (WARN, 600), (CRIT, 900)),
               mappings=[{"type": "value", "options": {"-1": {"text": "n/a", "color": NEUTRAL, "index": 0}}}],
               desc="Time since the bot's HealthCheckJob last finished. It runs every 5 minutes; above 15 minutes"
                    " the job is stuck."), 3, 3)
    d.add(stat("Build", f"wojtus_build_info{{{J}}}", unit="none", legend="{{commit}}", text_mode="name",
               desc="Commit of the deployed build."), 3, 3)
    d.add(availability_panel(), 9, 6)
    d.add(uptime_panel("7d", "1m", 2, "horizontal"), 3, 6)
    d.add(health_tiles(), 12, 6)

    d.row("Events and logs")
    d.add(bars("Events per minute by outcome",
               [q(f'sum by (outcome)({xi("wojtus_events_total")}){PER_MIN}', "{{outcome}}")],
               "Gateway events through the event pipeline. One outcome per event." + BIRTH,
               colors="outcome", decimals=1), 8, 8)
    d.add(with_links(bars("Events per minute by type",
               [q(f'sum by (event_type)({xi("wojtus_events_total")}){PER_MIN}', "{{event_type}}")],
               "Gateway events by event type, all outcomes. The legend sorts by total. Click a series for"
               " that type on Events & Ingest." + BIRTH,
               decimals=1, legend_table=True), EVENTS_LINK), 10, 8)
    d.add(bargauge("Time since last event by kind",
                   f"time() - max by (kind)(wojtus_last_event_by_kind_timestamp_seconds{{{J}}} > 0)", unit="s",
                   legend="{{kind}}", fixed=PURPLE, no_value="No event since the bot started",
                   desc="Time since the last gateway event of each kind: message, presence, voice, other."
                        " A kind with no event since the bot started has no bar."), 6, 8)
    d.add(ts("Handler duration", [q(hq(0.5, "wojtus_event_handler_duration_seconds"), "p50"),
                                  q(hq(0.95, "wojtus_event_handler_duration_seconds"), "p95")],
             unit="s", colors={"p50": BLUE, "p95": ORANGE},
             desc="Time one gateway event spends in the event pipeline, all event types."
                  " No point when no event arrives. A spike at a bot start is the guild replay"
                  " (GuildCreated). No trace exemplars: a gateway event runs outside any trace."), 8, 8)
    d.add(with_links(bars("Log events: warning and above",
               [q(f'sum by (level)({xi("wojtus_log_events_total", WARNINGS_UP)})', "{{level}}")],
               "Log events per interval that passed the level filter, counted in the bot. Click a bar for"
               " the Logs dashboard." + BIRTH,
               colors={"warning": WARN, "error": CRIT, "critical": PINK}), LOGS_LINK), 8, 8)
    d.add(ts("Log events: information and below",
             [q(f'sum by (level)(rate(wojtus_log_events_total{{{J},level!~"warning|error|critical"}}[$__rate_interval])) * 60',
                "{{level}}")],
             unit="short", decimals=0, colors={"information": BLUE, "debug": NEUTRAL, "trace": PURPLE},
             desc="Log events per minute. On its own panel: the volume is several hundred times larger."), 8, 8)

    d.row("Resources and HTTP")
    d.add(memory_panel(), 8, 8)
    d.add(cpu_panel(), 8, 8)
    d.add(bars("HTTP requests through Traefik by status class",
               [q('sum by (class)(label_replace(increase(traefik_service_requests_total{service=~"wojtusdiscord.*"}[$__interval]),'
                  ' "class", "${1}xx", "code", "(.).."))', "{{class}}")],
               "Requests Traefik sent to the bot (dashboard and API) per interval. Scrapes and health checks"
               " do not pass through Traefik.",
               colors={"2xx": GOOD, "3xx": BLUE, "4xx": WARN, "5xx": CRIT}), 8, 8)

    d.row("Jobs, connection and alerts")
    d.add(hangfire_panel(), 8, 8)
    d.add(bars("Downtime, socket closes and resumes", [
        q(f'sum({xi("wojtus_downtime_intervals_total")})', "downtime intervals"),
        q(f'sum({xi("wojtus_gateway_socket_closed_total")})', "socket closes"),
        q(f'sum({xi("wojtus_gateway_session_resumed_total")})', "session resumes")],
        "Downtime rows written, gateway socket closes and warm reconnects per interval." + BIRTH,
        colors={"downtime intervals": CRIT, "socket closes": ORANGE, "session resumes": BLUE}, stack=False), 8, 8)
    d.add(table("Firing alerts", [q('ALERTS{service="wojtusdiscord",alertstate="firing"}', instant=True, fmt="table")],
                "Prometheus alerts with service=wojtusdiscord that fire now. Empty means no alert fires.",
                transformations=[{"id": "organize", "options": {
                    "excludeByName": {"Time": True, "Value": True, "__name__": True, "alertstate": True,
                                      "service": True, "job": True, "cluster": True, "environment": True,
                                      "otel_scope_name": True, "otel_scope_version": True},
                    "renameByName": {"alertname": "Alert", "severity": "Severity", "instance": "Instance"},
                    "indexByName": {"alertname": 0, "severity": 1}}}]), 8, 8)

    d.row("Logs and traces")
    d.add(logs("Bot errors and warnings", f'{{container_name="discord-event-service"}} |~ `{ERR_RE}`',
               "Level-prefixed lines only. A 'fail:' or 'warn:' line holds only the category: its message is"
               " the next line, see the Logs dashboard."), 12, 10)
    d.add(table("Recent traces",
                [{"datasource": TEMPO, "refId": "A", "queryType": "traceql", "tableType": "traces", "limit": 20,
                  "query": '{resource.service.name="discord-event-service"}', "filters": []}],
                "Latest traces Tempo holds for the bot: HTTP requests and conversation turns."
                " Click a trace ID to open it.", ds=TEMPO), 12, 10)
    return d


# --------------------------------------------------------------------------- Events & Ingest
def events():
    var = {"type": "query", "name": "event_type", "label": "Event type", "datasource": PROM,
           "definition": f"label_values(wojtus_events_total{{{J}}}, event_type)",
           "query": {"qryType": 1, "query": f"label_values(wojtus_events_total{{{J}}}, event_type)",
                     "refId": "PrometheusVariableQueryEditor-VariableQuery"},
           "refresh": 2, "sort": 1, "regex": "", "multi": True, "includeAll": True, "allValue": ".*",
           "current": {"selected": True, "text": ["All"], "value": ["$__all"]}, "options": [],
           "hide": 0, "skipUrlSync": False}
    d = Dash("wojtus-events", "WojtusDiscord / Events & Ingest",
             "Gateway event ingest of the WojtusDiscord bot: volume and handler time per event type, failures,"
             " database write gaps, gateway connection, backfill and health checks. Job wojtusdiscord.",
             variables=[var])
    E = 'event_type=~"$event_type"'
    EV, HD, RS = "wojtus_events_total", "wojtus_event_handler_duration_seconds", "wojtus_event_raw_size_bytes"

    d.add(text("**What does the bot ingest, how fast, and what fails?** Pick event types at the top left; totals cover the time range."), 24, 2)
    d.row("Volume")
    d.add(stat("Events", f"sum({xi(EV, E, R)})", decimals=0, no_value="0",
               desc="Gateway events in the range, selected event types." + BIRTH), 4, 4)
    d.add(stat("Failed events", f'sum({xi(EV, sel(E, FAILED), R)})', decimals=0, no_value="0",
               thresholds=steps((GOOD, None), (CRIT, 1)),
               desc="Events with at least one failed handler in the range."), 4, 4)
    d.add(stat("Serialization failures", f'sum({xi(EV, sel(E, SER_FAILED), R)})',
               decimals=0, no_value="0", thresholds=steps((GOOD, None), (WARN, 1)),
               desc="Events stored with a fallback payload because the serializer failed. The handlers still ran."), 4, 4)
    d.add(stat("Dead letters", f'sum({xi("wojtus_event_dead_letters_total", E, R)})', decimals=0, no_value="0",
               thresholds=steps((GOOD, None), (CRIT, 1)),
               desc="Failures the database refused, written to the JSONL fallback file in the range."), 4, 4)
    d.add(stat("Handler p95", hq(0.95, HD, sel(E, BOOT_TYPES), R), unit="s", no_value="n/a",
               thresholds=steps((GOOD, None), (WARN, 0.5), (CRIT, 2)),
               desc="95th percentile of the time an event spends in the pipeline, over the range. Without"
                    " the event types that arrive only when the bot connects (GuildCreated,"
                    " GuildMembersChunked, ThreadListSynced): one guild replay of 250 to 500 ms at a start"
                    " was the whole p95 of a quiet night. 'Handler p95 by type' below shows them."), 4, 4)
    d.add(stat("Failed share", f'sum({xi(EV, sel(E, FAILED), R)}) / sum({xi(EV, E, R)})',
               unit="percentunit", decimals=2, no_value="0%",
               thresholds=steps((GOOD, None), (WARN, 0.01), (CRIT, 0.05)),
               desc="Failed events as a share of all events in the range."), 4, 4)
    d.add(bars("Events per minute by type", [q(f"sum by (event_type)({xi(EV, E)}){PER_MIN}", "{{event_type}}")],
               "Gateway events by event type, all outcomes. The legend sorts by total." + BIRTH,
               decimals=1, legend_table=True), 14, 9)
    d.add(bars("Events per minute by outcome", [q(f"sum by (outcome)({xi(EV, E)}){PER_MIN}", "{{outcome}}")],
               "One outcome per event: ok, failed (a handler threw) or serialization_failed.",
               colors="outcome", decimals=1), 10, 9)
    d.add(bargauge("Events by type", f"sort_desc(sum by (event_type)({xi(EV, E, R)}))", legend="{{event_type}}",
                   decimals=0, desc="Events per event type in the range."), 8, 10)
    d.add(bargauge("Share of events by type",
                   f"sort_desc(sum by (event_type)({xi(EV, E, R)}) / scalar(sum({xi(EV, E, R)})))",
                   unit="percentunit", legend="{{event_type}}", decimals=1, vmax=1,
                   desc="Each selected event type as a share of the selected events in the range."), 8, 10)
    d.add(bargauge("Raw payload size p95 by type",
                   f"sort_desc({hq(0.95, RS, E, R, by='event_type')} > 0)", unit="bytes", legend="{{event_type}}",
                   decimals=0, desc="95th percentile of the JSON size stored in raw_event_logs, over the range."
                                    " Buckets end at 1 MiB."), 8, 10)

    d.row("Messages")
    MS, ML, MA = "wojtus_messages_total", "wojtus_message_length", "wojtus_message_attachments"
    msgs = f"sum({xi(MS, '', R)})"
    d.add(stat("Messages", msgs, decimals=0, no_value="0",
               desc="Guild messages created in the range, counted from the event itself: a message whose"
                    " database write fails still counts. Not filtered by the event type picker." + BIRTH), 4, 4)
    d.add(stat("From humans", f"sum({xi(MS, AUTHOR_HUMAN, R)}) / ({msgs} > 0)", unit="percentunit", decimals=0,
               no_value="n/a", desc="Share of the messages in the range that a person wrote. The rest are bots,"
                                    " this bot included."), 4, 4)
    d.add(stat("Replies", f"sum({xi(MS, IS_REPLY, R)}) / ({msgs} > 0)", unit="percentunit", decimals=0,
               no_value="n/a", desc="Share of the messages in the range that reply to another message."), 4, 4)
    d.add(stat("With an attachment",
               f"1 - sum({xi(MA + '_bucket', LE_ZERO, R)}) / (sum({xi(MA + '_count', '', R)}) > 0)",
               unit="percentunit", decimals=0, no_value="n/a",
               desc="Share of the messages in the range with at least one attachment."), 4, 4)
    d.add(stat("Mean length", meanx(ML), unit="none", decimals=0, no_value="n/a",
               desc="Mean length of a message in characters, over the range. A message with only an"
                    " attachment has length 0."), 4, 4)
    d.add(stat("Median length", hqx(0.5, ML), unit="none", decimals=0, no_value="n/a",
               desc="Median length of a message in characters, over the range: an estimate from the"
                    " buckets 0, 1, 10, 25, 50, 100, 200, 500, 1000, 2000, 4000."), 4, 4)
    d.add(bars("Messages per minute by kind", [q(f"sum by (kind)({xi(MS)}){PER_MIN}", "{{kind}}")],
               "Guild messages by content kind. The kind is the first that holds of: attachment, sticker,"
               " embed, text, empty." + BIRTH, colors=MSG_KIND_COLORS, decimals=1), 9, 9)
    d.add(bars("Messages by author", [q(f"sum by (author)({xi(MS)})", "{{author}}")],
               "Messages per interval by author type: human or bot.",
               colors={"human": BLUE, "bot": NEUTRAL}), 5, 9)
    d.add(buckets("Message length", ML,
                  "Messages in the range by length in characters. Each bar is a bucket, named by its upper"
                  " bound: 0 (no text), 1, 10, 25, 50, 100, 200, 500, 1000, 2000, 4000, +Inf."), 5, 9)
    d.add(buckets("Attachments per message", MA,
                  "Messages in the range by attachment count. Each bar is a bucket, named by its upper"
                  " bound: 0, 1, 2, 3, 5, 10, +Inf."), 5, 9)

    d.row("Handler duration")
    d.add(ts("Handler duration percentiles", [q(hq(0.5, HD, E), "p50"), q(hq(0.95, HD, E), "p95"), q(hq(0.99, HD, E), "p99")],
             unit="s", colors={"p50": BLUE, "p95": ORANGE, "p99": PINK},
             desc="Time one gateway event spends in the event pipeline, selected event types. No trace"
                  " exemplars here: a gateway event runs outside any trace, so there is no trace to open."), 8, 9)
    d.add(heatmap("Handler duration heatmap", f"sum by (le)(increase({HD}_bucket{{{sel(J, E)}}}[$__interval]))",
                  "Events per duration bucket and interval. Bucket bounds: 5 ms to 30 s."), 8, 9)
    d.add(bargauge("Handler p95 by type", f"sort_desc({hq(0.95, HD, E, R, by='event_type')} > 0)", unit="s",
                   legend="{{event_type}}", thresholds=steps((GOOD, None), (WARN, 0.5), (CRIT, 2)),
                   desc="95th percentile handler time per event type over the range."), 8, 9)
    d.add(ts("Handler p95 by type over time", [q(hq(0.95, HD, E, by="event_type"), "{{event_type}}")], unit="s",
             legend_table=False, desc="95th percentile handler time per event type. No point when no event arrives."), 12, 8)
    d.add(ts("Raw payload bytes per second", [q(f"sum by (event_type)(rate({RS}_sum{{{sel(J, E)}}}[$__rate_interval]))", "{{event_type}}")],
             unit="Bps", stack=True, fill=30,
             desc="Bytes of raw event JSON written to raw_event_logs, by event type."), 12, 8)

    d.row("Failures and database write gaps")
    d.add(bars("Handler failures by event type",
               [q(f'sum by (event_type)({xi("wojtus_event_failures_total", E)})', "{{event_type}}")],
               "Failures handed to FailedEventService per interval, hard and soft. One event can record"
               " more than one failure." + BIRTH), 8, 8)
    d.add(bars("Handler failures by handler",
               [q(f'sum by (handler)({xi("wojtus_event_failures_total", E)})', "{{handler}}")],
               "The same failures by handler name."), 8, 8)
    d.add(bars("Dead letters", [q(f'sum by (event_type, handler)({xi("wojtus_event_dead_letters_total", E)})',
                                  "{{event_type}} / {{handler}}")],
               "Failures the database refused, sent to the JSONL fallback. Each one needs a manual replay.",
               colors=None), 8, 8)
    d.add(bars("Serialization failures by type",
               [q(f'sum by (event_type)({xi(EV, sel(E, SER_FAILED))})', "{{event_type}}")],
               "Events whose payload the serializer could not write. Read the 2026-05 blackout notes before"
               " a change to RawEventLogService."), 8, 8)
    d.add(ts("Unwritable window pending", [q(f"max(wojtus_db_unwritable_window_pending{{{J}}})", "pending")],
             unit="none", decimals=0, vmax=1, fixed=CRIT, legend=False, line_interp="stepAfter", fill=30,
             desc="1 while an unwritable-database window is open or waits for its row."), 8, 8)
    d.add(bars("Heartbeat writes by outcome", [
        q(f'sum by (outcome)({xi("wojtus_heartbeat_writes_total")}){PER_MIN}', "{{outcome}}"),
        q(f'sum({xi("wojtus_db_unwritable_failed_writes_total")}){PER_MIN}', "counted toward an unwritable window")],
        "Heartbeat rows per minute; the bot writes one every 5 s, so ok is 12 per minute. A failed write"
        " that counts toward an unwritable window is a separate series.",
        colors={"ok": GOOD, "failed": CRIT, "counted toward an unwritable window": PURPLE}, decimals=1, stack=False), 8, 8)

    d.row("Gateway")
    d.add(ts("Gateway latency", [q(f"max(wojtus_gateway_latency_seconds{{{J}}})", "latency")], unit="s",
             fixed=BLUE, legend=False, desc="Gateway heartbeat latency as DSharpPlus reports it."), 6, 8)
    d.add(ts("Gateway connected", [q(f"min(wojtus_gateway_connected{{{J}}})", "connected")], unit="none",
             decimals=0, vmax=1, fixed=GOOD, legend=False, line_interp="stepAfter", fill=30,
             desc="1 when every gateway shard is connected. A gap is a bot restart."), 6, 8)
    d.add(bars("Socket closes by close code",
               [q(f'sum by (close_code)({xi("wojtus_gateway_socket_closed_total")})', "{{close_code}}")],
               "Gateway socket closes per interval. 1000 and 1001 are normal closes; 4xxx codes are"
               " Discord errors." + BIRTH), 6, 8)
    d.add(bars("Session resumes and guild downloads", [
        q(f'sum({xi("wojtus_gateway_session_resumed_total")})', "session resumed (warm)"),
        q(f'sum({xi("wojtus_gateway_guild_download_completed_total")})', "guild download (cold)")],
        "Warm reconnects and cold connects per interval. A cold connect follows every bot start.",
        colors={"session resumed (warm)": BLUE, "guild download (cold)": ORANGE}, stack=False), 6, 8)
    d.add(ts("Time since last event by kind",
             [q(f"time() - max by (kind)(wojtus_last_event_by_kind_timestamp_seconds{{{J}}} > 0)", "{{kind}}")],
             unit="s", colors={"message": BLUE, "presence": PURPLE, "voice": TEAL, "other": NEUTRAL},
             desc="Time since the last gateway event of each kind. A sawtooth: it grows until the next event."
                  " Long teeth at night are normal."), 10, 8)
    d.add(ts("Members in voice channels", [q(f"max(wojtus_voice_members{{{J}}})", "members")], unit="short",
             decimals=0, fixed=TEAL, legend=False, line_interp="stepAfter",
             desc="Members connected to a voice channel now, as the bot's cache sees them."), 6, 8)
    d.add(bars("Voice state changes",
               [q(f'sum by (change)({xi("wojtus_voice_state_changes_total")})', "{{change}}")],
               "Voice state changes per interval: join, leave, move, mute, unmute, deafen, undeafen,"
               " stream_start, stream_stop, video_start, video_stop, and 'other' (a change of none of"
               " these, such as a suppress flag). One event can count more than one change. Mute and"
               " deafen do not tell the member's own switch from the server's." + BIRTH,
               legend_table=True), 8, 8)

    d.row("Health checks and downtime")
    d.add(state_timeline("Health checks", [q(f"max by (check)({HC}failing{{{J}}})", "{{check}}")],
                         "One band per check of the bot's HealthCheckJob: green while it passes, red while it fails.",
                         good="ok", bad="failing", invert=True), 12, 8)
    d.add(ts("Health-check ages", [
        q(f'max by (check)({HC}value{{{J},check=~"ingest_stall|open_downtime"}})', "{{check}}"),
        q(f'max by (event_type)({HC}value{{{J},check="event_silence"}})', "silence: {{event_type}}")],
        unit="s", desc="The checks that measure a time: age of the newest raw event (ingest_stall), age of an open"
                       " downtime row (open_downtime) and the silence per watched event type (event_silence)."), 12, 8)
    d.add(ts("Health-check counts", [
        q(f'max by (check)({HC}value{{{J},check!~"ingest_stall|open_downtime|event_silence"}})', "{{check}}")],
        unit="short", decimals=0, line_interp="stepAfter",
        desc="The checks that count: failed events, restarts in 30 minutes (crash_loop), event types below their"
             " baseline (event_ratio), stalled backfills, rows that break the timestamp invariant."), 8, 8)
    d.add(bars("Health-check alerts by check",
               [q(f'sum by (check)({xi("wojtus_healthcheck_alerts_total")})', "{{check}}")],
               "Alerts the bot's HealthCheckJob sent to Discord, per interval." + BIRTH), 8, 8)
    d.add(bars("Webhook failures",
               [q(f'sum({xi("wojtus_healthcheck_webhook_failures_total")})', "webhook failures")],
               "Health-check alerts the webhook did not deliver. Such an alert reached nobody.",
               colors={"webhook failures": CRIT}), 4, 8)
    d.add(bars("Downtime by type",
               [q(f'sum by (type)({xi("wojtus_downtime_intervals_total")})', "{{type}}")],
               "Downtime rows the bot wrote, per interval, by type."), 4, 8)

    d.row("Database writes")
    d.add(bars("Upserts per minute by entity",
               [q(f'sum by (entity)({xi("wojtus_upserts_total")}){PER_MIN}', "{{entity}}")],
               "Entity upserts and insert-or-get writes (guild, channel, user, member, message, ...) per"
               " minute." + BIRTH, decimals=1, legend_table=True), 9, 8)
    d.add(bars("Upserts per minute by result",
               [q(f'sum by (result)({xi("wojtus_upserts_total")}){PER_MIN}', "{{result}}")],
               "The same writes by result. 'conflict' means another writer won the race and the row was then"
               " updated. 'existing' is an insert-or-get that found the row and left it as it was"
               " (a message that arrives twice).",
               colors={"inserted": GOOD, "updated": BLUE, "existing": NEUTRAL, "conflict": WARN}, decimals=1), 5, 8)
    d.add(bars("Foreign keys not resolved",
               [q(f'sum by (entity)({xi("wojtus_fk_unresolved_total")})', "{{entity}}")],
               "Required foreign keys FkResolver could not resolve, by entity. Each one is an event whose row"
               " was not written."), 5, 8)
    d.add(bars("Typing events throttled",
               [q(f'sum({xi("wojtus_typing_throttled_total")})', "throttled")],
               "TypingStarted events the 10 s throttle dropped before the pipeline.", colors={"throttled": NEUTRAL}), 5, 8)

    d.row("Boot")
    d.add(bargauge("Boot phases, last start", f"max by (phase)(wojtus_boot_phase_duration_seconds{{{J}}})", unit="s",
                   legend="{{phase}}", fixed=PURPLE, no_value="No boot phase recorded",
                   desc="Duration of each phase of the last start: migrate, backfill_sweep, guild_download, quick_sync."), 8, 7)
    d.add(ts("Boot phases across restarts", [q(f"max by (phase)(wojtus_boot_phase_duration_seconds{{{J}}})", "{{phase}}")],
             unit="s", line_interp="stepAfter",
             desc="The same durations over time. Each step is one bot start; the value holds until the next."), 16, 7)

    d.row("Backfill", collapsed=True)
    d.add(bars("Backfill runs by type and outcome",
               [q(f'sum by (type, outcome)({xi("wojtus_backfill_runs_total")})', "{{type}} {{outcome}}")],
               "Backfill job runs per interval. 'cancelled' is normal on a deploy." + BIRTH, colors="outcome"), 6, 8)
    d.add(ts("Backfill run duration", [q(meanx("wojtus_backfill_run_duration_seconds", rng=IV, by="type"), "{{type}}")],
             unit="s", points="always", min_interval="1m",
             desc="Mean duration of the backfill runs that ended in each interval, by type."), 6, 8)
    d.add(ts("Backfill items per second", [q(f"sum by (type)(rate(wojtus_backfill_items_total{{{J}}}[$__rate_interval]))", "{{type}}")],
             unit="ops", desc="Items (channels) a cursor backfill finished per second."), 6, 8)
    d.add(bars("Backfill item errors",
               [q(f'sum by (type)({xi("wojtus_backfill_item_errors_total")})', "{{type}}")],
               "Items (channels) a cursor backfill failed on and skipped, per interval."), 6, 8)
    d.add(bars("Orphan replay rows by result",
               [q(f'sum by (result)({xi("wojtus_orphan_replay_rows_total")})', "{{result}}")],
               "Raw events an orphan replay run looked at, per interval: scanned, inserted or skipped.",
               colors={"scanned": NEUTRAL, "inserted": GOOD, "skipped": BLUE}, stack=False), 24, 7)
    return d


# --------------------------------------------------------------------------- Conversation & Memes
def ai():
    d = Dash("wojtus-ai", "WojtusDiscord / Conversation & Memes",
             "The LLM assistant and the meme search of the WojtusDiscord bot. Volume is low (about 30"
             " conversation turns a month), so panels show counts per interval and totals, not rates."
             " Job wojtusdiscord.", time_from="now-7d")
    C = "wojtus_conversation_"
    cost_t = steps((GOOD, None), (WARN, 1), (CRIT, 2))

    d.add(text("**What do the assistant and the meme search do, how long do they take, what do they cost?** Volume is low: widen the time range for history."), 24, 2)
    d.row("Conversation")
    fee = (" The cost is what the provider billed for the call: it holds the web search fee already,"
           " so the fee is not added a second time.")
    d.add(stat("Turns", f"sum({xi(C + 'turns_total', '', R)})", decimals=0, no_value="0",
               desc="Conversation turns in the range." + BIRTH), 3, 4)
    d.add(stat("Failed turns", f"sum({xi(C + 'turns_total', NOT_OK, R)})", decimals=0,
               no_value="0", thresholds=steps((GOOD, None), (CRIT, 1)),
               desc="Turns in the range that ended in a timeout or an error."), 3, 4)
    d.add(stat("Cost, 24 h", f"sum({xi(C + 'cost_usd_total', '', '24h', '5m')})", unit="currencyUSD", decimals=2,
               no_value="$0.00", thresholds=cost_t,
               desc="Cost of conversation model calls, last 24 hours." + fee), 3, 4)
    d.add(stat("Cost, 7 d", f"sum({xi(C + 'cost_usd_total', '', '7d', '5m')})", unit="currencyUSD", decimals=2,
               no_value="$0.00", desc="Cost of conversation model calls, last 7 days." + fee), 3, 4)
    d.add(stat("Cost, 30 d", f"sum({xi(C + 'cost_usd_total', '', '30d', '5m')})", unit="currencyUSD", decimals=2,
               no_value="$0.00", desc="Cost of conversation model calls, last 30 days." + fee), 3, 4)
    d.add(stat("Web fee, 30 d", f"sum({xi(C + 'web_search_cost_usd_total', '', '30d', '5m')})",
               unit="currencyUSD", decimals=2, no_value="$0.00",
               desc="The part of 'Cost, 30 d' that is the web search fee: the cost of a call less the cost of"
                    " the model itself. Zero when the provider did not report both costs."), 3, 4)
    d.add(stat("Web search", f"sum({xi(C + 'web_search_requests_total', '', R)})", decimals=0, no_value="0",
               desc="Web searches the provider ran for conversation model calls in the range."), 3, 4)
    d.add(stat("Retries", f"sum({xi(C + 'retries_total', '', R)})", decimals=0, no_value="0",
               thresholds=steps((GOOD, None), (WARN, 1)),
               desc="Model calls in the range that were a second or later attempt of a round."), 3, 4)

    d.add(bars("Turns by outcome", [q(f"sum by (outcome)({xi(C + 'turns_total')})", "{{outcome}}")],
               "Conversation turns per interval." + BIRTH, colors="outcome"), 8, 8)
    d.add(ts("Turn duration", [q(meanx(C + "turn_duration_seconds", rng=IV), "mean"),
                               q(hqx(0.95, C + "turn_duration_seconds", rng=IV), "p95", exemplar=True)],
             unit="s", points="always", min_interval="1m", colors={"mean": BLUE, "p95": ORANGE},
             desc="Time from the start of a turn to its last message, for the turns that ended in each"
                  " interval. One point per interval with a turn." + EX), 8, 8)
    d.add(bars("Cost by model", [q(f"sum by (model)({xi(C + 'cost_usd_total')})", "{{model}}")],
               "Cost of conversation model calls per interval, by model.", unit="currencyUSD", decimals=3), 8, 8)
    d.add(bars("Tokens by model and direction",
               [q(f"sum by (model, direction)({xi(C + 'tokens_total')})", "{{model}} {{direction}}")],
               "Tokens billed by conversation model calls per interval. Input is the prompt, output is"
               " the completion."), 8, 8)
    d.add(bars("Rounds and retries", [
        q(f"sum by (model, outcome)({xi(C + 'rounds_total')})", "{{model}} {{outcome}}"),
        q(f"sum by (model)({xi(C + 'retries_total')})", "{{model}} retry")],
        "Model calls of the conversation loop per interval, one per usage ledger row. A retry is a"
        " second or later attempt of a round and is also a round.", colors="outcome", stack=False), 8, 8)
    d.add(ts("Round latency by model", [q(meanx(C + "round_duration_seconds", rng=IV, by="model"), "{{model}} mean"),
                                        q(hqx(0.95, C + "round_duration_seconds", rng=IV, by="model"), "{{model}} p95",
                                          exemplar=True)],
             unit="s", points="always", min_interval="1m",
             desc="Latency of one conversation model call, for the calls in each interval." + EX), 8, 8)
    d.add(ts("First token latency by model",
             [q(meanx(C + "first_token_seconds", rng=IV, by="model"), "{{model}} mean"),
              q(hqx(0.95, C + "first_token_seconds", rng=IV, by="model"), "{{model}} p95", exemplar=True)],
             unit="s", points="always", min_interval="1m",
             desc="Time from the start of a model call to its first visible text: what the person waits"
                  " before the answer starts. Reasoning and tool-call chunks come before it. A call that"
                  " ends with tool calls only records nothing." + EX), 8, 8)
    d.add(ts("Context size by model",
             [q(meanx(C + "context_messages", rng=IV, by="model"), "{{model}} mean"),
              q(hqx(0.95, C + "context_messages", rng=IV, by="model"), "{{model}} p95")],
             unit="none", decimals=0, points="always", min_interval="1m",
             desc="Messages sent to the model in one call: system prompt, memory window and tool results."
                  " The p95 is an estimate from the buckets 0, 1, 2, 5, 10, 25, 50, 100: above 100 it"
                  " reads 100. A larger context costs more input tokens."), 8, 8)
    d.add(bars("Web searches", [
        q(f"sum by (model)({xi(C + 'web_search_requests_total')})", "{{model}} searches")],
        "Web searches the provider ran for conversation model calls, per interval and model. The fee is on the"
         " 'Web search fee' panel: a count and a cost do not share one axis."), 4, 8)
    d.add(bars("Web search fee", [q(f"sum by (model)({xi(C + 'web_search_cost_usd_total')})", "{{model}}")],
               "Web search fee per interval and model: the cost of a call less the cost of the model itself. It is a"
               " part of 'Cost by model', not an addition to it.", unit="currencyUSD", decimals=3), 4, 8)
    d.add(bargauge("Tool calls by tool", f"sort_desc(sum by (tool)({xi(C + 'tool_calls_total', '', R)}))",
                   legend="{{tool}}", decimals=0, desc="Conversation tool calls in the range, by tool."), 5, 8)
    d.add(bars("Tool calls by outcome", [q(f"sum by (outcome)({xi(C + 'tool_calls_total')})", "{{outcome}}")],
               "Tool calls per interval. 'error' means the tool threw and the model got the error text;"
               " 'unknown_tool' means the model invented a tool name.", colors="outcome"), 5, 8)
    d.add(bargauge("Tool p95 by tool",
                   f"sort_desc({hqx(0.95, C + 'tool_duration_seconds', '', R, by='tool')} > 0)", unit="s",
                   legend="{{tool}}", desc="95th percentile of the time one tool call takes, over the range."), 4, 8)
    d.add(ts("Tool duration p95 over time",
             [q(hqx(0.95, C + "tool_duration_seconds", rng=IV, by="tool"), "{{tool}}", exemplar=True)],
             unit="s", points="always", min_interval="1m",
             desc="95th percentile of the time one tool call takes, for the calls in each interval, by tool." + EX), 6, 8)
    d.add(bars("Usage alerts by cap", [q(f"sum by (cap)({xi(C + 'usage_alerts_total')})", "{{cap}}")],
               "Cost-cap alerts the bot fired, per interval."), 4, 8)

    d.add(logs("Conversation turns (log)", '{container_name="discord-event-service"} |= "Conversation turn "',
               "One line per conversation turn: outcome, invoker, rounds, tools, cost, duration, trace id,"
               " question and answer. Search the trace id in Tempo to open the trace.", max_lines=100), 24, 8)

    d.row("Slash commands")
    CD = "wojtus_command_"
    d.add(bars("Command executions by command",
               [q(f"sum by (command)({xi(CD + 'executions_total')})", "{{command}}")],
               "Slash command executions per interval." + BIRTH), 8, 8)
    d.add(bars("Command executions by outcome",
               [q(f"sum by (outcome)({xi(CD + 'executions_total')})", "{{outcome}}")],
               "ok, failed, check_failed, bad_argument, not_executable or cancelled. 'failed' is an exception"
               " that left the command, or an error the command caught and answered itself (/meme does"
               " that when its search fails).", colors="outcome"), 6, 8)
    d.add(bargauge("Command p95",
                   f"sort_desc({hqx(0.95, CD + 'duration_seconds', '', R, by='command')} > 0)", unit="s",
                   legend="{{command}}", thresholds=steps((GOOD, None), (WARN, 1.5), (CRIT, 3)),
                   desc="95th percentile from the creation of the interaction to the end of the command, over the"
                        " range. Discord drops an interaction with no answer after 3 s."), 5, 8)
    d.add(bars("Searches rejected (429)",
               [q(f"sum by (reason)({xi('wojtus_meme_dashboard_rejections_total')})", "{{reason}}")],
               "Dashboard meme searches answered 429: 'concurrency' (too many at one time) or 'rate' (the budget"
               " of the minute).", colors={"concurrency": ORANGE, "rate": WARN}), 5, 8)

    M = "wojtus_meme_"
    SD = M + "search_duration_seconds"
    SP = M + "search_phase_duration_seconds"
    # Measured on prod 2026-10-06: 1.5 to 1.9 s per search (mean 1.58 s). Buckets near it end at
    # 1 s and 2.5 s, so the p95 of normal searches reads up to 2.5 s. Yellow from 2.5 s, red from
    # 3 s: the WojtusMemeSearchSlow alert (mean above 3 s) and the time Discord gives an interaction.
    lat_t = steps((GOOD, None), (WARN, 2.5), (CRIT, 3))
    d.row("Meme search")
    d.add(stat("Searches", f"sum({xi(M + 'searches_total', '', R)})", decimals=0, no_value="0",
               desc="Meme searches in the range, every caller." + BIRTH), 6, 4)
    d.add(stat("Mean search latency", meanx(SD), unit="s", thresholds=lat_t, no_value="n/a",
               desc="Mean time of one meme search over the range. This value is exact; the percentiles"
                    " are estimates from buckets. Measured on prod: 1.5 to 1.9 s (issue #401 expected"
                    " 0.8 s). Yellow from 2.5 s, red from 3 s (the alert)."), 6, 4)
    d.add(stat("Search latency p95", hqx(0.95, SD), unit="s", thresholds=lat_t, no_value="n/a",
               desc="95th percentile over the range, an estimate from buckets that end at 1 s and 2.5 s:"
                    " searches of 1.5 to 1.9 s read as up to 2.4 s. Above 2.5 s means more than 5% of the"
                    " searches took longer than 2.5 s."), 6, 4)
    d.add(stat("Searches with no hit",
               f"sum({xi(M + 'search_results_bucket', LE_ZERO, R)}) / sum({xi(M + 'search_results_count', '', R)})",
               unit="percentunit", decimals=0, no_value="n/a",
               desc="Share of search pages that returned no meme, over the range."), 6, 4)
    d.add(ts("Meme search latency", [q(hqx(0.5, SD, rng=IV), "p50"), q(hqx(0.95, SD, rng=IV), "p95", exemplar=True),
                                     q(hqx(0.99, SD, rng=IV), "p99"), q(meanx(SD, rng=IV), "mean")],
             unit="s", points="always", min_interval="1m", threshold=lat_t,
             colors={"p50": BLUE, "p95": ORANGE, "p99": PINK, "mean": PURPLE},
             desc="Time one meme search takes (tokenize and SQL; the map phase runs after it), for the"
                  " searches in each interval. The lines mark 2.5 s and 3 s (the alert on the mean)."
                  " The mean is exact; the percentiles are estimates from buckets that end at 1 s and"
                  " 2.5 s. One point per interval with a search." + EX), 8, 10)
    d.add(bars("Mean search time by phase", [q(meanx(SP, rng=IV, by="phase"), "{{phase}}")],
               "Where the time of a search goes: mean time of each phase per interval, stacked. A bar is one"
               " interval wide: zoom in, or read the two bar gauges below for the whole range."
               " tokenize = split the query in memory, sql = the Postgres query, map = build the hits in"
               " memory. tokenize plus sql is the search latency on the left; if sql is the tall part,"
               " the time is in Postgres (issue #401).", unit="s", decimals=None, colors=PHASE_COLORS), 8, 10)
    d.add(ts("Search phase p95", [q(hqx(0.95, SP, rng=IV, by="phase"), "{{phase}}", exemplar=True)],
             unit="s", points="always", min_interval="1m", colors=PHASE_COLORS, log_y=True,
             desc="95th percentile of each phase, for the searches in each interval, on a log axis: the"
                  " phases differ by a factor of 1000 and more. An estimate from buckets: 0.1 ms to 10 s,"
                  " near the SQL time they end at 1 s and 2.5 s." + EX), 8, 10)
    d.add(bargauge("Phase share of search time",
                   f"sum by (phase)({xi(SP + '_sum', '', R)}) / scalar(sum({xi(SP + '_sum', '', R)}))",
                   unit="percentunit", legend="{{phase}}", decimals=1, vmax=1,
                   desc="Each phase as a share of the total phase time of the searches in the range."), 6, 8)
    d.add(bargauge("Search phase mean", meanx(SP, by="phase"), unit="s", legend="{{phase}}",
                   desc="Mean time of each phase over the range. Exact: sum divided by count."), 6, 8)
    d.add(bargauge("Hits per search page",
                   f"sum by (le)({xi(M + 'search_results_bucket', '', R)})", legend="{{le}}", fmt="heatmap",
                   decimals=0, desc="Search pages in the range by hit count. Each bar is a bucket, named by"
                                    " its upper bound: 0, 1, 2, 5, 10, 25, 50, 100, +Inf."), 6, 8)
    d.add(bars("Dashboard requests unavailable (503)",
               [q(f"sum by (endpoint, reason)({xi(M + 'dashboard_unavailable_total')})", "{{endpoint}} {{reason}}")],
               "Meme dashboard requests answered 503, per interval. endpoint: index, search_usage or"
               " thumbnail. reason: busy (the answer is being computed for another request), and for a"
               " thumbnail wait_timeout, queue_full, budget (the caps), refresh_timeout, refresh_failed"
               " (Discord gave no fresh URL) or recent_failure (a failure of the last minutes, answered"
               " from memory). The browser retries a 503." + BIRTH), 6, 8)
    d.add(bars("Searches by caller", [q(f"sum by (caller)({xi(M + 'searches_total')})", "{{caller}}")],
               "Meme searches per interval, by the kind of caller. 'Tester' is the dashboard."), 12, 8)
    d.add(ts("Mean search latency by caller", [q(meanx(SD, rng=IV, by="caller"), "{{caller}}")], unit="s",
             points="always", min_interval="1m", threshold=lat_t,
             desc="Mean time of one meme search per interval, by caller."), 12, 8)

    # Collapsed: AutomaticIndexing is off in prod, so the row is empty outside a catch-up or an import.
    d.row("Meme indexing (empty outside a catch-up or an import)", collapsed=True)
    d.add(bars("Index outcomes", [q(f"sum by (outcome)({xi(M + 'index_outcomes_total')})", "{{outcome}}")],
               "Meme indexing outcomes per attachment, per interval. AutomaticIndexing is off in prod, so"
               " this stays empty outside a catch-up.", colors="outcome"), 8, 8)
    d.add(bars("Vision calls by model and outcome",
               [q(f"sum by (model, outcome)({xi(M + 'vision_calls_total')})", "{{model}} {{outcome}}")],
               "Vision model calls for meme analysis per interval.", colors="outcome"), 8, 8)
    d.add(ts("Vision call latency", [q(meanx(M + "vision_duration_seconds", rng=IV, by="model"), "{{model}} mean"),
                                     q(hqx(0.95, M + "vision_duration_seconds", rng=IV, by="model"), "{{model}} p95")],
             unit="s", points="always", min_interval="1m",
             desc="Latency of one vision model call, for the calls in each interval."), 8, 8)
    d.add(bars("Vision tokens by model and direction",
               [q(f"sum by (model, direction)({xi(M + 'vision_tokens_total')})", "{{model}} {{direction}}")],
               "Tokens billed by vision model calls per interval."), 8, 8)
    d.add(bars("Vision cost by model", [q(f"sum by (model)({xi(M + 'vision_cost_usd_total')})", "{{model}}")],
               "Cost of vision model calls per interval.", unit="currencyUSD", decimals=3), 8, 8)
    d.add(bars("Import items by outcome", [q(f"sum by (outcome)({xi(M + 'import_items_total')})", "{{outcome}}")],
               "Meme annotation import items per interval: imported, skipped or rejected.", colors="outcome"), 8, 8)
    return d


# --------------------------------------------------------------------------- Runtime & HTTP
def runtime():
    d = Dash("wojtus-runtime", "WojtusDiscord / Runtime & HTTP",
             "The .NET runtime, the process, HTTP in and out, the Npgsql client and Hangfire of the"
             " WojtusDiscord bot. Job wojtusdiscord; container memory from the host.")
    HS, HC = "http_server_request_duration_seconds", "http_client_request_duration_seconds"

    d.add(text("**Is the process healthy and is HTTP fast?** .NET runtime, HTTP in and out, the public URL probe, the Npgsql pool, Hangfire, traces."), 24, 2)
    d.row("Availability")
    d.add(uptime_panel("7d", "1m", 3, "vertical"), 12, 4)
    d.add(uptime_panel("30d", "5m", 3, "vertical"), 12, 4)
    d.add(availability_panel(), 24, 6)

    d.row("Process")
    d.add(memory_panel(), 12, 8)
    d.add(ts("Process CPU by state", [q(f"sum by (cpu_mode)(rate(process_cpu_time_seconds_total{{{J}}}[$__rate_interval]))", "{{cpu_mode}}")],
             unit="percentunit", decimals=1, stack=True, fill=30, colors={"user": BLUE, "system": ORANGE},
             desc="CPU time of the bot process per second, user and system: 100% is one full core."), 12, 8)

    d.row(".NET runtime")
    d.add(ts("GC heap size by generation",
             [q(f"sum by (gc_heap_generation)(dotnet_gc_last_collection_heap_size_bytes{{{J}}})", "{{gc_heap_generation}}")],
             unit="bytes", stack=True, fill=30,
             desc="Managed heap size after the last collection, by generation (gen0, gen1, gen2, loh, poh)."), 8, 8)
    d.add(ts("GC collections per minute",
             [q(f"sum by (gc_heap_generation)(rate(dotnet_gc_collections_total{{{J}}}[$__rate_interval])) * 60", "{{gc_heap_generation}}")],
             unit="short", decimals=1, desc="Garbage collections per minute by generation."), 8, 8)
    d.add(ts("GC pause time", [q(f"sum(rate(dotnet_gc_pause_time_seconds_total{{{J}}}[$__rate_interval]))", "pause")],
             unit="percentunit", decimals=3, fixed=ORANGE, legend=False,
             desc="Share of wall time the runtime spends paused for garbage collection."), 8, 8)
    d.add(ts("Allocation rate", [q(f"sum(rate(dotnet_gc_heap_total_allocated_bytes_total{{{J}}}[$__rate_interval]))", "allocated")],
             unit="Bps", fixed=BLUE, legend=False, desc="Bytes allocated on the managed heap per second."), 8, 8)
    d.add(ts("Thread pool", [q(f"max(dotnet_thread_pool_thread_count_total{{{J}}})", "threads"),
                             q(f"max(dotnet_thread_pool_queue_length_total{{{J}}})", "queued work items")],
             unit="short", decimals=0, colors={"threads": BLUE, "queued work items": ORANGE},
             desc="Thread pool threads and work items that wait for a thread. A queue that stays above zero"
                  " means thread pool starvation."), 8, 8)
    d.add(ts("Exceptions per minute by type",
             [q(f"sum by (error_type)(rate(dotnet_exceptions_total{{{J}}}[$__rate_interval])) * 60", "{{error_type}}")],
             unit="short", decimals=1, style="bars", stack=True,
             desc="Exceptions thrown in managed code, caught ones included, by exception type."), 8, 8)
    d.add(ts("Lock contentions per minute",
             [q(f"sum(rate(dotnet_monitor_lock_contentions_total{{{J}}}[$__rate_interval])) * 60", "contentions")],
             unit="short", decimals=1, fixed=BLUE, legend=False,
             desc="Times a thread had to wait for a monitor lock."), 8, 8)
    d.add(ts("Thread pool work items per second",
             [q(f"sum(rate(dotnet_thread_pool_work_item_count_total{{{J}}}[$__rate_interval]))", "work items")],
             unit="ops", fixed=BLUE, legend=False, desc="Work items the thread pool completed per second."), 8, 8)
    d.add(ts("JIT and assemblies", [q(f"sum(rate(dotnet_jit_compiled_methods_total{{{J}}}[$__rate_interval])) * 60", "methods compiled per minute"),
                                    q(f"max(dotnet_assembly_count{{{J}}})", "assemblies loaded")],
             unit="short", decimals=0, colors={"methods compiled per minute": BLUE, "assemblies loaded": PURPLE},
             desc="JIT activity and loaded assemblies. Both settle a few minutes after a start."), 8, 8)

    d.row("ASP.NET Core (inbound HTTP)")
    probe_note = " Scrapes of /metrics and health checks of /health are left out."
    HSN = HS + "_count"
    d.add(bars("Requests per minute by route",
               [q(f"sum by (http_request_method, http_route)({xi(HSN, NOT_PROBE)}){PER_MIN}",
                  "{{http_request_method}} {{http_route}}")],
               "Requests the bot served, by route template. An empty route is a request no endpoint"
               " matched (static file or 404)." + probe_note + BIRTH, decimals=1, legend_table=True), 12, 9)
    d.add(ts("Request duration percentiles", [q(hqx(0.5, HS, NOT_PROBE, rng=IV), "p50"),
                                              q(hqx(0.95, HS, NOT_PROBE, rng=IV), "p95", exemplar=True),
                                              q(hqx(0.99, HS, NOT_PROBE, rng=IV), "p99")],
             unit="s", colors={"p50": BLUE, "p95": ORANGE, "p99": PINK}, points="always", min_interval="1m",
             desc="Duration of inbound requests, all routes, for the requests in each interval. One point"
                  " per interval with a request." + probe_note + EX), 12, 9)
    d.add(bargauge("Request p95 by route", f"sort_desc({hqx(0.95, HS, NOT_PROBE, R, by='http_route')} > 0)", unit="s",
                   legend="{{http_route}}", thresholds=steps((GOOD, None), (WARN, 2.5), (CRIT, 5)),
                   desc="95th percentile request duration per route over the range. Yellow from 2.5 s, red"
                        " from 5 s: the meme search route takes 1.5 to 1.9 s (see Conversation & Memes), and"
                        " its p95 reads up to 2.5 s because the buckets end at 1 s and 2.5 s." + probe_note), 8, 9)
    d.add(bars("Responses per minute by status code",
               [q(f"sum by (http_response_status_code)({xi(HSN, NOT_PROBE)}){PER_MIN}", "{{http_response_status_code}}")],
               "Responses by HTTP status code. 503 and 429 from the meme dashboard are caps the bot set"
               " itself." + probe_note, decimals=1,
               colors={"200": GOOD, "204": GOOD, "302": BLUE, "304": BLUE, "400": ORANGE, "404": ORANGE,
                       "429": WARN, "500": CRIT, "503": WARN}), 8, 9)
    d.add(ts("Active requests and connections", [q(f"sum(http_server_active_requests{{{J}}})", "active requests"),
                                                 q(f"sum(kestrel_active_connections{{{J}}})", "Kestrel connections")],
             unit="short", decimals=0, colors={"active requests": BLUE, "Kestrel connections": PURPLE},
             desc="Requests in flight and open Kestrel connections. The scrape itself counts as one request."), 8, 9)

    d.row("Public URL probe")
    d.add(ts("Probe duration by phase",
             [q(f"max by (phase)(probe_http_duration_seconds{{{HTTP}}})", "{{phase}}")],
             unit="s", stack=True, fill=40,
             colors={"resolve": PURPLE, "connect": BLUE, "tls": TEAL, "processing": ORANGE, "transfer": YELLOW},
             desc="Blackbox probe of /health through Traefik every 30 s, split into DNS resolve, TCP connect, TLS,"
                  " server processing and transfer. 'processing' is the time the bot takes."), 14, 8)
    d.add(state_timeline("Probe result", [q(f"max(probe_success{{{HTTP}}})", "Public URL")],
                         "Green while the probe gets HTTP 200 from /health.", row_height=0.6), 6, 8)
    d.add(stat("HTTP status", f"max(probe_http_status_code{{{HTTP}}})", unit="none", decimals=0,
               thresholds=steps((CRIT, None), (GOOD, 200), (CRIT, 300)), no_value="no answer", value_size=48,
               desc="Status code of the last probe. 0 means no HTTP answer."), 4, 8)

    d.row("Outbound dependencies (HttpClient)")
    HCC = HC + "_count"
    DISCORD, OPENROUTER = 'server_address="discord.com"', 'server_address=~".*openrouter.*"'
    OTHER = 'server_address!~"discord.com|.*openrouter.*"'
    bad_t = steps((GOOD, None), (WARN, 1))
    d.add(stat("Discord requests", f"sum({xi(HCC, DISCORD, R)})", decimals=0, no_value="0",
               desc="Requests to the Discord REST API (discord.com) in the range: boot sync, backfill, replies." + BIRTH), 4, 4)
    d.add(stat("Discord non-2xx", f'sum({xi(HCC, sel(DISCORD, NON_2XX), R)})', decimals=0,
               no_value="0", thresholds=bad_t,
               desc="Discord REST answers outside 2xx in the range, 429 included, and requests with no answer."), 4, 4)
    d.add(stat("Discord rate limits", f'sum({xi(HCC, sel(DISCORD, HTTP_429), R)})', decimals=0,
               no_value="0", thresholds=bad_t,
               desc="HTTP 429 from Discord in the range. DSharpPlus waits and retries; many of them mean a backfill"
                    " runs too fast."), 4, 4)
    d.add(stat("OpenRouter requests", f"sum({xi(HCC, OPENROUTER, R)})", decimals=0, no_value="0",
               desc="Requests to OpenRouter in the range: conversation model calls and meme vision calls."), 4, 4)
    d.add(stat("OpenRouter non-2xx", f'sum({xi(HCC, sel(OPENROUTER, NON_2XX), R)})',
               decimals=0, no_value="0", thresholds=bad_t,
               desc="OpenRouter answers outside 2xx in the range, and requests with no answer."), 4, 4)
    d.add(stat("Other hosts, failed", f'sum({xi(HCC, sel(OTHER, HAS_ERROR), R)})', decimals=0, no_value="0",
               thresholds=bad_t, desc="Failed requests to every other host (Tempo, Langfuse, webhooks) in the range."), 4, 4)
    d.add(bars("Discord REST requests by status",
               [q(f"sum by (http_response_status_code)({xi(HCC, DISCORD)}){PER_MIN}", "{{http_response_status_code}}")],
               "Requests per minute to discord.com by HTTP status." + BIRTH, decimals=1,
               colors={"200": GOOD, "201": GOOD, "204": GOOD, "429": WARN, "400": ORANGE, "403": ORANGE, "404": ORANGE,
                       "500": CRIT, "502": CRIT, "503": CRIT}), 8, 8)
    d.add(ts("Discord REST latency", [q(hq(0.5, HC, DISCORD), "p50"), q(hq(0.95, HC, DISCORD), "p95", exemplar=True)],
             unit="s", colors={"p50": BLUE, "p95": ORANGE}, points="auto",
             desc="Duration of requests to discord.com. No point when no request is sent." + EX), 8, 8)
    d.add(bars("OpenRouter requests by status",
               [q(f"sum by (http_response_status_code)({xi(HCC, OPENROUTER)}){PER_MIN}", "{{http_response_status_code}}")],
               "Requests per minute to OpenRouter by HTTP status.", decimals=1,
               colors={"200": GOOD, "429": WARN, "500": CRIT, "502": CRIT, "503": CRIT}), 8, 8)
    d.add(ts("OpenRouter latency", [q(hqx(0.5, HC, OPENROUTER, rng=IV), "p50"),
                                    q(hqx(0.95, HC, OPENROUTER, rng=IV), "p95", exemplar=True)],
             unit="s", colors={"p50": BLUE, "p95": ORANGE}, points="always", min_interval="1m",
             desc="Duration of requests to OpenRouter, for the requests in each interval. A model call runs for"
                  " seconds to minutes." + EX), 8, 8)
    d.add(bars("Other hosts, requests by host",
               [q(f"sum by (server_address)({xi(HCC, OTHER)}){PER_MIN}", "{{server_address}}")],
               "Requests per minute to every other host: the Discord gateway upgrade, Tempo, Langfuse, webhooks.",
               decimals=1), 8, 8)
    d.add(bars("Outbound errors",
               [q(f'sum by (server_address, http_response_status_code, error_type)({xi(HCC, HAS_ERROR)})',
                  "{{server_address}} {{http_response_status_code}} {{error_type}}")],
               "Outbound requests that failed, per interval: a 4xx or 5xx status, a timeout or a connection error."), 8, 8)

    d.row("Npgsql client")
    d.add(ts("Pool connections by state",
             [q(f"sum by (db_client_connection_state)(db_client_connection_count{{{J}}})", "{{db_client_connection_state}}"),
              ],
             unit="short", decimals=0, colors={"idle": BLUE, "used": ORANGE}, stack=True, fill=30,
             desc="Connections of the Npgsql pool, idle and in use, stacked. The pool maximum is 100."
                  " 'used' near the maximum means a connection leak or a stuck job."), 6, 8)
    d.add(ts("Commands executing", [q(f"sum(db_client_operation_npgsql_executing{{{J}}})", "executing")],
             unit="short", decimals=0, fixed=ORANGE, legend=False,
             desc="Database commands in flight at the scrape instant. Npgsql exports no count of requests"
                  " that wait for a connection."), 6, 8)
    d.add(ts("Command duration",
             [q(hq(0.5, "db_client_operation_duration_seconds"), "p50"),
              q(hq(0.95, "db_client_operation_duration_seconds"), "p95", exemplar=True),
              q(f"sum(rate(db_client_operation_duration_seconds_sum{{{J}}}[$__rate_interval]))"
                f" / sum(rate(db_client_operation_duration_seconds_count{{{J}}}[$__rate_interval]))", "mean")],
             unit="s", colors={"p50": BLUE, "p95": ORANGE, "mean": PURPLE},
             desc="Duration of database commands as the driver measures it, network time included." + EX), 6, 8)
    d.add(ts("Bytes read and written",
             [q(f"sum(rate(db_client_operation_npgsql_bytes_read_bytes_total{{{J}}}[$__rate_interval]))", "read"),
              q(f"sum(rate(db_client_operation_npgsql_bytes_written_bytes_total{{{J}}}[$__rate_interval]))", "written")],
             unit="Bps", colors={"read": BLUE, "written": ORANGE},
             desc="Bytes the driver read from and wrote to Postgres per second."), 6, 8)
    d.add(ts("Commands per second", [q(f"sum(rate(db_client_operation_duration_seconds_count{{{J}}}[$__rate_interval]))", "commands")],
             unit="ops", fixed=BLUE, legend=False, desc="Database commands the bot completed per second."), 12, 7)
    d.add(ts("Connection open time",
             [q(f"sum(rate(db_client_connection_npgsql_create_time_seconds_sum{{{J}}}[$__rate_interval]))"
                f" / sum(rate(db_client_connection_npgsql_create_time_seconds_count{{{J}}}[$__rate_interval]))", "mean")],
             unit="s", fixed=BLUE, legend=False, points="always",
             desc="Mean time to open a new physical connection. No point when the pool opens none."), 12, 7)

    d.row("Hangfire")
    d.add(hangfire_panel(), 10, 8)
    d.add(ts("Succeeded jobs per hour", [q(f'max(delta(wojtus_hangfire_jobs{{{J},state="succeeded"}}[1h]))', "succeeded")],
             unit="short", decimals=0, fixed=GOOD, legend=False,
             desc="Jobs that succeeded in the last hour. The succeeded state is a lifetime total in the"
                  " Hangfire tables, so a bot restart does not reset it."), 6, 8)
    d.add(stat("Hangfire servers", f"max(max_over_time(wojtus_hangfire_servers{{{J}}}[2m]))", decimals=0,
               thresholds=steps((CRIT, None), (GOOD, 1)), value_size=48,
               desc="Hangfire servers alive, the highest value of the last 2 minutes: the first read after a"
                    " bot start is 0. The bot runs one."), 4, 8)
    d.add(stat("Recurring jobs", f"max(wojtus_hangfire_recurring_jobs{{{J}}})", decimals=0, value_size=48,
               desc="Hangfire recurring jobs registered."), 4, 8)

    d.row("Traces: Tempo span metrics", collapsed=True)
    opt = (" From Tempo's metrics generator (source=tempo). Tempo holds only traces with a request or a"
           " conversation turn as root.")
    SM = 'service="discord-event-service"'
    d.add(bars("Spans by name",
               [q(f"sum by (span_name)(increase(traces_spanmetrics_calls_total{{{SM}}}[$__interval]))", "{{span_name}}")],
               "Spans per interval by span name: inbound requests, database commands and outbound calls"
               " inside a kept trace." + opt, decimals=1), 8, 8)
    d.add(ts("Span duration p95 by name",
             [q(f"histogram_quantile(0.95, sum by (le, span_name)(rate(traces_spanmetrics_latency_bucket{{{SM}}}[$__rate_interval])))", "{{span_name}}")],
             unit="s", points="always", desc="95th percentile span duration by span name." + opt), 8, 8)
    d.add(bars("Spans with an error status",
               [q(f'sum by (span_name)(increase(traces_spanmetrics_calls_total{{{SM},status_code="STATUS_CODE_ERROR"}}[$__interval]))', "{{span_name}}")],
               "Spans per interval that ended with an error status. Empty means no span failed." + opt, decimals=1), 8, 8)
    d.add(bars("Service graph requests",
               [q('sum by (client, server)(increase(traces_service_graph_request_total{source="tempo"}[$__interval]))', "{{client}} to {{server}}")],
               "Calls between services that Tempo derives from the spans: user to bot, bot to Postgres.", decimals=1), 8, 8)
    d.add(bars("Trace export failures",
               [q(f'sum({xi("wojtus_trace_export_failures_total")})', "export failures")],
               "Errors the bot's OTLP exporter reported: Tempo not reachable or the export failed. Spans of"
               " that time are lost.", colors={"export failures": CRIT}), 8, 8)
    d.add(ts("Spans Tempo received per second",
             [q('sum(rate(tempo_distributor_spans_received_total{job="tempo"}[$__rate_interval]))', "received"),
              q('sum(rate(tempo_receiver_refused_spans{job="tempo"}[$__rate_interval]))', "refused"),
              q('sum(rate(tempo_discarded_spans_total{job="tempo"}[$__rate_interval]))', "discarded")],
             unit="ops", colors={"received": BLUE, "refused": CRIT, "discarded": WARN},
             desc="Spans the Tempo distributor took in, spans its receiver refused and spans Tempo"
                  " discarded. Refused or discarded means traces of the bot are lost."), 8, 8)
    d.add(ts("Tempo push duration p95",
             [q('histogram_quantile(0.95, sum by (le)(rate(tempo_distributor_push_duration_seconds_bucket{job="tempo"}[$__rate_interval])))', "p95")],
             unit="s", fixed=BLUE, legend=False, desc="95th percentile of the time Tempo takes to accept one push."), 8, 8)
    return d


if __name__ == "__main__":
    for build, name in ((overview, "wojtus-overview"), (events, "wojtus-events"), (ai, "wojtus-ai"), (runtime, "wojtus-runtime")):
        build().write(os.path.join(OUT, name + ".json"))
        print("wrote", name + ".json")
    alerts.write()
    print("wrote wojtusdiscord-alerts.yml")
