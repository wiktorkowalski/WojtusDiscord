"""Panel builders for the WojtusDiscord Grafana dashboards. Used by build.py and alerts.py.

One look for all six dashboards: the colours below, thin lines with a soft gradient fill,
bars for counts, a text for every empty panel (EMPTY_TEXT), status colours only for status.
"""
import copy
import json
import re

# The frame every dashboard shares: annotations (bot start, Postgres start), the links
# to the other WojtusDiscord dashboards, tags, schema version, 6 h range, 30 s refresh.
# Dash() fills uid, title, description, variables and panels, and adds the Deploy annotation.
TEMPLATE = json.loads(r'''
{
 "annotations": {
  "list": [
   {
    "builtIn": 1,
    "datasource": {
     "type": "grafana",
     "uid": "-- Grafana --"
    },
    "enable": true,
    "hide": true,
    "iconColor": "rgba(0, 211, 255, 1)",
    "name": "Annotations & Alerts",
    "type": "dashboard"
   },
   {
    "datasource": {
     "type": "loki",
     "uid": "P8E80F9AEF21F6940"
    },
    "enable": true,
    "hide": false,
    "iconColor": "#9085e9",
    "name": "Bot start",
    "expr": "{container_name=\"discord-event-service\"} |= \"Application started. Press Ctrl+C\"",
    "instant": false,
    "titleFormat": "Bot start",
    "textFormat": "{{container_name}}",
    "tagKeys": "container_name",
    "maxLines": 50,
    "target": {
     "expr": "{container_name=\"discord-event-service\"} |= \"Application started. Press Ctrl+C\"",
     "queryType": "range",
     "refId": "Anno",
     "maxLines": 50
    }
   },
   {
    "datasource": {
     "type": "loki",
     "uid": "P8E80F9AEF21F6940"
    },
    "enable": true,
    "hide": false,
    "iconColor": "#d95926",
    "name": "Postgres start",
    "expr": "{container_name=\"wojtus-postgres\"} |= \"database system is ready to accept connections\"",
    "instant": false,
    "titleFormat": "Postgres start",
    "textFormat": "{{container_name}}",
    "tagKeys": "container_name",
    "maxLines": 50,
    "target": {
     "expr": "{container_name=\"wojtus-postgres\"} |= \"database system is ready to accept connections\"",
     "queryType": "range",
     "refId": "Anno",
     "maxLines": 50
    }
   }
  ]
 },
 "description": "",
 "editable": true,
 "fiscalYearStartMonth": 0,
 "graphTooltip": 1,
 "id": null,
 "links": [
  {
   "type": "dashboards",
   "tags": [
    "wojtusdiscord"
   ],
   "title": "WojtusDiscord",
   "icon": "external link",
   "asDropdown": false,
   "includeVars": false,
   "keepTime": true,
   "targetBlank": false,
   "tooltip": "",
   "url": ""
  }
 ],
 "preload": false,
 "refresh": "30s",
 "schemaVersion": 41,
 "tags": [
  "wojtusdiscord"
 ],
 "templating": {
  "list": []
 },
 "time": {
  "from": "now-6h",
  "to": "now"
 },
 "timepicker": {},
 "timezone": "browser",
 "title": "",
 "uid": "",
 "version": 1,
 "weekStart": ""
}
''')

PROM = {"type": "prometheus", "uid": "PBFA97CFB590B2093"}
LOKI = {"type": "loki", "uid": "P8E80F9AEF21F6940"}
TEMPO = {"type": "tempo", "uid": "tempo"}

BLUE, ORANGE, TEAL, PURPLE = "#3987e5", "#d95926", "#199e70", "#9085e9"
YELLOW, PINK, DGREEN, SALMON = "#c98500", "#d55181", "#008300", "#e66767"
GOOD, WARN, CRIT, NEUTRAL = "#0ca30c", "#fab219", "#d03b3b", "#898781"

J = 'job="wojtusdiscord"'
BOT = 'name="discord-event-service"'

# Outcome colours by series name. Later entries win, so the exact "failed" comes last.
OUTCOME_COLORS = [
    (r"/(^|[ ,])(ok|success|completed|indexed|imported|information|inserted)$/", GOOD),
    (r"/(cancelled|skipped|debug|trace)/", NEUTRAL),
    (r"/(timeout|transient|short_circuit|serialization_failed|warning|refusal|rejected|unknown_tool|check_failed|bad_argument|not_executable|conflict|updated)/", WARN),
    (r"/(^|[ ,])(failed|error|critical)$/", CRIT),
]


def sel(*parts):
    return ",".join(p for p in parts if p)


STEP_SECONDS = {"1m": 60, "5m": 300}
START = 'wojtus_process_start_time_seconds{job="wojtusdiscord"}'
UPTIME = 'wojtus_process_uptime_seconds{job="wojtusdiscord"}'


def xinc(metric, selector, rng, step="1m"):
    """Increase of a bot counter that also counts the first sample of a new series.

    increase() and rate() never count the sample that creates a series, and the bot
    creates a counter series on its first increment. With a deploy a few times a day
    and events as rare as one a day, most increments create a series.

    Per step: the value minus the earlier value of the same process life; the full
    value when there is no such earlier value (new series, or the process restarted
    since the last step). The earlier value is the last sample of the 10 minutes
    before, so one failed scrape counts nothing twice. In the first minutes of a
    process life that window can still hold a sample of the old process, so there
    the earlier value is the plain instant value, which ends at the restart.
    """
    m = f"{metric}{{{selector}}}"
    on = "on (job, instance)"
    young = 600 + STEP_SECONDS[step]
    prev = (f"((last_over_time({m}[10m] offset {step}) and {on} ({UPTIME} > {young}))"
            f" or ({m} offset {step} and {on} ({UPTIME} <= {young})))"
            f" and {on} ({START} == last_over_time({START}[10m] offset {step}))")
    return f"sum_over_time((({m} - ({prev}) >= 0) or ({m} + 0))[{rng}:{step}])"


def q(expr, legend="", instant=False, fmt=None, ref=None):
    t = {"datasource": PROM, "expr": expr, "refId": ref or "A", "legendFormat": legend,
         "editorMode": "code", "instant": instant, "range": not instant}
    if fmt:
        t["format"] = fmt
    return t


def lq(expr, legend="", max_lines=None):
    t = {"datasource": LOKI, "expr": expr, "refId": "A", "legendFormat": legend,
         "editorMode": "code", "queryType": "range"}
    if max_lines:
        t["maxLines"] = max_lines
    return t


def _refs(targets):
    for i, t in enumerate(targets):
        t["refId"] = "ABCDEFGHIJKLMNOP"[i]
    return targets


def steps(*pairs):
    return {"mode": "absolute", "steps": [{"color": c, "value": v} for c, v in pairs]}


NEUTRAL_STEPS = steps((NEUTRAL, None))
STAT_OPTS = {"reduceOptions": {"calcs": ["lastNotNull"], "fields": "", "values": False},
             "colorMode": "none", "graphMode": "none", "justifyMode": "auto", "orientation": "auto",
             "textMode": "auto", "wideLayout": True, "showPercentChange": False,
             "percentChangeColorMode": "standard"}


def stat(title, expr, unit="short", desc=None, thresholds=None, color_mode=None, graph="none",
         mappings=None, decimals=None, no_value=None, legend="", text_mode="auto", instant=True,
         ds=PROM, calc="lastNotNull"):
    if graph != "none":
        instant = False
    target = q(expr, legend, instant=instant) if ds is PROM else lq(expr, legend)
    defaults = {"unit": unit, "color": {"mode": "thresholds"},
                "thresholds": thresholds or NEUTRAL_STEPS, "mappings": mappings or []}
    if decimals is not None:
        defaults["decimals"] = decimals
    if no_value is not None:
        defaults["noValue"] = no_value
    opts = copy.deepcopy(STAT_OPTS)
    opts["colorMode"] = color_mode or ("value" if thresholds else "none")
    opts["graphMode"] = graph
    opts["textMode"] = text_mode
    opts["reduceOptions"]["calcs"] = [calc]
    p = {"type": "stat", "title": title, "datasource": ds, "targets": [target],
         "fieldConfig": {"defaults": defaults, "overrides": []}, "options": opts}
    if desc:
        p["description"] = desc
    return p


def updown(title, expr, desc, up="✓ UP", down="✕ DOWN"):
    return stat(title, expr, unit="none", desc=desc, thresholds=steps((CRIT, None), (GOOD, 1)),
                color_mode="background", no_value="✕ NO DATA",
                mappings=[{"type": "value", "options": {"0": {"text": down, "index": 0},
                                                         "1": {"text": up, "index": 1}}}])


def _custom(style, stack, fill=None, line_interp="linear", points="never", threshold_line=False):
    bars = style == "bars"
    line = style == "line"
    return {"drawStyle": style, "lineWidth": 0 if bars else 1,
            "fillOpacity": fill if fill is not None else (85 if bars else 14 if line else 0),
            "showPoints": points, "pointSize": 6 if points == "always" else 5,
            "lineInterpolation": line_interp, "spanNulls": False,
            "gradientMode": "opacity" if line else "none",
            "barAlignment": 0, "barWidthFactor": 0.8, "axisBorderShow": False, "axisPlacement": "auto",
            "axisLabel": "", "axisColorMode": "text", "axisCenteredZero": False,
            "scaleDistribution": {"type": "linear"},
            "stacking": {"mode": "normal" if stack else "none", "group": "A"},
            "thresholdsStyle": {"mode": "line" if threshold_line else "off"},
            "hideFrom": {"legend": False, "tooltip": False, "viz": False}}


def color_overrides(colors):
    """colors: dict name->colour, or 'outcome' for the outcome regex set."""
    if colors == "outcome":
        return [{"matcher": {"id": "byRegexp", "options": rx},
                 "properties": [{"id": "color", "value": {"mode": "fixed", "fixedColor": c}}]}
                for rx, c in OUTCOME_COLORS]
    return [{"matcher": {"id": "byName", "options": n},
             "properties": [{"id": "color", "value": {"mode": "fixed", "fixedColor": c}}]}
            for n, c in (colors or {}).items()]


def ts(title, targets, unit="short", desc=None, style="line", stack=False, colors=None, vmin=0,
       vmax=None, decimals=None, legend=True, legend_table=False, threshold=None, fixed=None,
       min_interval=None, points="never", fill=None, line_interp="linear", ds=PROM, no_value=None,
       soft_max=None):
    defaults = {"unit": unit,
                "custom": _custom(style, stack, fill, line_interp, points, threshold is not None),
                "color": {"mode": "fixed", "fixedColor": fixed} if fixed else {"mode": "palette-classic"},
                "thresholds": threshold or NEUTRAL_STEPS, "mappings": []}
    if vmin is not None:
        defaults["min"] = vmin
    if vmax is not None:
        defaults["max"] = vmax
    if decimals is not None:
        defaults["decimals"] = decimals
    if no_value is not None:
        defaults["noValue"] = no_value
    # All-zero data gets a 0-100 axis from Grafana. A soft maximum of 1 keeps a quiet count
    # panel readable and does not limit larger values.
    if soft_max is None and vmax is None and unit in ("short", "none", "ops"):
        soft_max = 1
    if soft_max is not None:
        defaults["custom"]["axisSoftMin"] = 0
        defaults["custom"]["axisSoftMax"] = soft_max
    lg = {"showLegend": legend, "displayMode": "table" if legend_table else "list",
          "placement": "right" if legend_table else "bottom",
          "calcs": ["sum"] if legend_table else []}
    if legend_table:
        lg["sortBy"] = "Total"
        lg["sortDesc"] = True
    p = {"type": "timeseries", "title": title, "datasource": ds, "targets": _refs(targets),
         "fieldConfig": {"defaults": defaults, "overrides": color_overrides(colors)},
         "options": {"legend": lg, "tooltip": {"mode": "multi", "sort": "desc", "hideZeros": False}}}
    if desc:
        p["description"] = desc
    if min_interval:
        p["interval"] = min_interval
    return p


def link(title, url):
    """A data link on every series of a panel. ${__url_time_range} keeps the time range."""
    return {"title": title, "url": url + ("&" if "?" in url else "?") + "${__url_time_range}", "targetBlank": False}


LOGS_LINK = link("Open the Logs dashboard", "/d/wojtus-logs/")


def with_links(panel, *links):
    panel["fieldConfig"]["defaults"]["links"] = list(links)
    return panel


def state_timeline(title, targets, desc, good="up", bad="down", invert=False, row_height=0.8):
    """One coloured band per series: green while the value is the good one, red while not."""
    g, b = (0, 1) if invert else (1, 0)
    return {"type": "state-timeline", "title": title, "datasource": PROM, "targets": _refs(targets),
            "description": desc,
            "fieldConfig": {"defaults": {
                "unit": "none", "color": {"mode": "thresholds"},
                "custom": {"fillOpacity": 85, "lineWidth": 0, "insertNulls": False, "spanNulls": False,
                           "hideFrom": {"legend": False, "tooltip": False, "viz": False}},
                "thresholds": steps((GOOD, None), (CRIT, 1)) if invert else steps((CRIT, None), (GOOD, 1)),
                "mappings": [{"type": "value", "options": {str(g): {"text": good, "index": 0},
                                                           str(b): {"text": bad, "index": 1},
                                                           "-1": {"text": "no sample", "color": NEUTRAL, "index": 2}}}]},
                "overrides": []},
            "options": {"mergeValues": True, "showValue": "never", "alignValue": "left", "rowHeight": row_height,
                        "legend": {"showLegend": False, "displayMode": "list", "placement": "bottom"},
                        "tooltip": {"mode": "single", "sort": "none", "hideZeros": False}}}


def text(markdown):
    """The line under the title bar: what the page answers."""
    return {"type": "text", "title": "", "datasource": {"type": "grafana", "uid": "-- Dashboard --"},
            "transparent": True, "options": {"mode": "markdown", "content": markdown,
                                             "code": {"language": "plaintext", "showLineNumbers": False, "showMiniMap": False}}}


def tiles(title, expr, legend, desc, good="\u2713 ok", bad="\u2715 FAILING", no_value="no data yet"):
    """One state tile per series of a 0/1 gauge where 1 is bad."""
    p = stat(title, expr, unit="none", desc=desc, thresholds=steps((GOOD, None), (CRIT, 1)),
             color_mode="background", legend=legend, text_mode="value_and_name", no_value=no_value,
             mappings=[{"type": "value", "options": {
                 "0": {"text": good, "index": 0}, "1": {"text": bad, "index": 1},
                 "-1": {"text": "no run yet", "color": NEUTRAL, "index": 2}}}])
    p["options"]["justifyMode"] = "center"
    p["options"]["wideLayout"] = False
    return p


def bars(title, targets, desc, unit="short", colors=None, decimals=0, legend_table=False, stack=True):
    """Counts per interval. The 1 m minimum interval keeps the xinc subquery step valid."""
    return ts(title, targets, unit=unit, desc=desc, style="bars", stack=stack, colors=colors,
              decimals=decimals, min_interval="1m", legend_table=legend_table, soft_max=1)


def bargauge(title, expr, unit="short", legend="", desc=None, thresholds=None, fixed=BLUE,
             decimals=None, vmax=None, fmt=None, no_value=None):
    defaults = {"unit": unit, "min": 0, "mappings": [],
                "color": {"mode": "thresholds"} if thresholds else {"mode": "fixed", "fixedColor": fixed},
                "thresholds": thresholds or NEUTRAL_STEPS}
    if vmax is not None:
        defaults["max"] = vmax
    if decimals is not None:
        defaults["decimals"] = decimals
    if no_value is not None:
        defaults["noValue"] = no_value
    # A bar gauge with one series drops the series name: name each bar by its label.
    if legend.startswith("{{") and legend.endswith("}}") and legend.count("{{") == 1:
        defaults["displayName"] = "${__field.labels." + legend[2:-2] + "}"
    p = {"type": "bargauge", "title": title, "datasource": PROM,
         "targets": [q(expr, legend, instant=True, fmt=fmt)],
         "fieldConfig": {"defaults": defaults, "overrides": []},
         "options": {"displayMode": "basic", "orientation": "horizontal", "valueMode": "text",
                     "namePlacement": "left", "showUnfilled": True, "sizing": "manual",
                     "minVizHeight": 16, "minVizWidth": 8, "maxVizHeight": 28,
                     "legend": {"showLegend": False, "displayMode": "list", "placement": "bottom", "calcs": []},
                     "reduceOptions": {"calcs": ["lastNotNull"], "fields": "", "values": False}}}
    if desc:
        p["description"] = desc
    return p


def heatmap(title, expr, desc, unit="s"):
    return {"type": "heatmap", "title": title, "datasource": PROM, "description": desc,
            "interval": "1m",
            "targets": [q(expr, "{{le}}", fmt="heatmap")],
            "fieldConfig": {"defaults": {"unit": unit, "custom": {
                "hideFrom": {"legend": False, "tooltip": False, "viz": False},
                "scaleDistribution": {"type": "linear"}}}, "overrides": []},
            "options": {"calculate": False, "cellGap": 1, "cellValues": {"unit": "short", "decimals": 0},
                        "color": {"mode": "scheme", "scheme": "Blues", "fill": BLUE, "exponent": 0.5,
                                  "reverse": False, "scale": "exponential", "steps": 64},
                        "exemplars": {"color": ORANGE}, "filterValues": {"le": 1e-9},
                        "legend": {"show": True}, "rowsFrame": {"layout": "auto"},
                        "showValue": "never",
                        "tooltip": {"mode": "single", "showColorScale": False, "yHistogram": False},
                        "yAxis": {"axisPlacement": "left", "reverse": False, "unit": unit}}}


def logs(title, expr, desc, max_lines=200, no_value="No matching log line in this time range"):
    return {"type": "logs", "title": title, "datasource": LOKI, "targets": [lq(expr, max_lines=max_lines)],
            "description": desc, "fieldConfig": {"defaults": {"noValue": no_value}, "overrides": []},
            "options": {"showTime": True, "showLabels": False, "showCommonLabels": False,
                        "wrapLogMessage": True, "prettifyLogMessage": False, "enableLogDetails": True,
                        "enableInfiniteScrolling": False, "dedupStrategy": "none", "sortOrder": "Descending"}}


def table(title, targets, desc, ds=PROM, transformations=None, overrides=None, unit="none"):
    p = {"type": "table", "title": title, "datasource": ds, "targets": targets, "description": desc,
         "fieldConfig": {"defaults": {"unit": unit,
                                      "custom": {"align": "auto", "cellOptions": {"type": "auto"},
                                                 "inspect": False, "filterable": False},
                                      "color": {"mode": "thresholds"}, "thresholds": NEUTRAL_STEPS,
                                      "mappings": []},
                         "overrides": overrides or []},
         "options": {"showHeader": True, "cellHeight": "sm",
                     "footer": {"show": False, "reducer": ["sum"], "countRows": False, "fields": ""}}}
    if transformations:
        p["transformations"] = transformations
    return p


# Text a panel shows when its query returns no series. First match on the title wins.
EMPTY_TEXT = [
    (r"Firing alerts", "No alerts firing"),
    (r"rejected", "No dashboard search rejected"),
    (r"Recent traces", "No traces in this time range"),
    (r"[Dd]ead letters", "No dead letters"),
    (r"Serialization failures", "No serialization failures"),
    (r"Handler failures", "No handler failures recorded"),
    (r"warning and above", "No warnings or errors logged"),
    (r"Downtime, socket closes", "No downtime, socket close or resume recorded"),
    (r"Socket closes", "No socket closes recorded"),
    (r"Downtime (intervals|by type)", "No downtime recorded"),
    (r"Session resumes", "No reconnect recorded"),
    (r"[Ww]ebhook failures", "No webhook failures"),
    (r"Health-check alerts", "No health-check alerts sent"),
    (r"Backfill item errors", "No backfill item errors"),
    (r"Orphan replay", "No orphan replay run in this time range"),
    (r"Backfill", "No backfill run in this time range"),
    (r"Usage alerts", "No cost cap crossed"),
    (r"Tool", "No tool calls yet"),
    (r"Turn|Rounds|Round latency|Tokens by model|Cost by model", "No conversation turns yet"),
    (r"[Ss]earch|Hits per", "No meme searches yet"),
    (r"Vision|Index outcomes", "No meme indexing in this time range"),
    (r"Import items", "No meme import in this time range"),
    (r"Upserts", "No upserts recorded yet"),
    (r"Foreign keys", "No unresolved foreign key"),
    (r"Typing", "No typing event throttled"),
    (r"Boot phases", "No boot phase recorded yet"),
    (r"Health-check (ages|counts)|Health checks", "No health-check run recorded yet"),
    (r"Time since last event", "No event since the bot started"),
    (r"Members in voice", "No voice sample yet"),
    (r"Command", "No slash command yet"),
    (r"Trace export", "No trace export failure"),
    (r"Probe|Availability", "No sample in this time range"),
    (r"Discord REST|OpenRouter|Other hosts", "No request to this host in the time range"),
    (r"Outbound errors", "No outbound request failed"),
    (r"error status", "No span with an error status"),
    (r"Traefik", "No requests through Traefik"),
    (r"Requests per minute by route|Request duration|Request p95|Responses per minute", "No inbound requests besides probes"),
    (r"Span|Service graph", "No spans in this time range"),
    (r"Connection open time", "No new connection opened"),
    (r"Exceptions", "No exceptions thrown"),
]


def empty_text(title):
    for rx, text in EMPTY_TEXT:
        if re.search(rx, title):
            return text
    return "No samples in this time range"


DEPLOY_ANNOTATION = {
    "datasource": PROM, "enable": True, "hide": False, "iconColor": TEAL, "name": "Deploy",
    # A commit label that was not there two minutes ago: a new build went live.
    "expr": 'wojtus_build_info{job="wojtusdiscord"} unless (wojtus_build_info{job="wojtusdiscord"} offset 2m)',
    "step": "60s", "titleFormat": "Deploy", "textFormat": "{{commit}}", "tagKeys": "commit",
    "useValueForTime": False}


def add_deploy_annotation(dashboard):
    lst = dashboard["annotations"]["list"]
    lst[:] = [a for a in lst if a.get("name") != "Deploy"]
    lst.append(copy.deepcopy(DEPLOY_ANNOTATION))


AVAILABILITY = steps((CRIT, None), (WARN, 0.99), (GOOD, 0.999))


def availability_exprs(window, step):
    """Share of the window each part was up. A part with no sample while the bot target exists counts as down."""
    up = 'max(up{job="wojtusdiscord"})'
    return [
        ("Bot process", f'avg_over_time(up{{job="wojtusdiscord"}}[{window}])'),
        ("Discord gateway", f'avg_over_time((min(wojtus_gateway_connected{{job="wojtusdiscord"}}) or on () (0 * {up}))[{window}:{step}])'),
        ("Public URL", f'avg_over_time(probe_success{{job="wojtusdiscord-http"}}[{window}])'),
        ("Postgres", f'avg_over_time(pg_up{{job="wojtus-postgres"}}[{window}])'),
    ]


class Dash:
    """A dashboard under construction. add() places panels left to right and wraps at 24 columns."""

    def __init__(self, uid, title, desc, variables=None, time_from="now-6h"):
        self.d = copy.deepcopy(TEMPLATE)
        add_deploy_annotation(self.d)
        self.d.update(uid=uid, title=title, description=desc)
        self.d["templating"] = {"list": variables or []}
        self.d["time"] = {"from": time_from, "to": "now"}
        self.panels = []
        self.next_id = 1
        self.x = 0
        self.y = 0
        self.line_h = 0
        self.into = None  # collapsed row that takes the next panels

    def _id(self):
        self.next_id += 1
        return self.next_id - 1

    def _newline(self):
        self.y += self.line_h
        self.x = 0
        self.line_h = 0

    def row(self, title, collapsed=False):
        self._newline()
        r = {"type": "row", "id": self._id(), "title": title, "collapsed": collapsed,
             "gridPos": {"h": 1, "w": 24, "x": 0, "y": self.y}, "panels": []}
        self.panels.append(r)
        self.y += 1
        self.into = r if collapsed else None

    def add(self, panel, w, h):
        if self.x + w > 24:
            self._newline()
        panel = dict(panel)
        ordered = {"type": panel.pop("type"), "id": self._id(), "title": panel.pop("title"),
                   "datasource": panel.pop("datasource"),
                   "gridPos": {"x": self.x, "y": self.y, "w": w, "h": h}}
        ordered.update(panel)
        if ordered["type"] in ("timeseries", "bargauge", "table", "heatmap", "state-timeline"):
            ordered["fieldConfig"]["defaults"].setdefault("noValue", empty_text(ordered["title"]))
        (self.into["panels"] if self.into is not None else self.panels).append(ordered)
        self.x += w
        self.line_h = max(self.line_h, h)

    def write(self, path):
        self.d["panels"] = self.panels
        with open(path, "w") as f:
            json.dump(self.d, f, indent=2, ensure_ascii=False)
            f.write("\n")
        return self.d
