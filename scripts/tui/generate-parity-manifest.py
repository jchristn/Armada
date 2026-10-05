#!/usr/bin/env python3
"""Generate src/Armada.Tui/parity.json from the dashboard source.

The manifest maps every dashboard surface to its TUI implementation: routes (App.tsx), hub tabs (pages with Tabs),
server-calling API functions (api/client.ts), WebSocket event types, and Server-page settings fields. Existing entries
keep their status and notes; new surfaces are added as "planned". Run after dashboard changes:

  python3 scripts/tui/generate-parity-manifest.py

Release gate (CI runs it on every push; it writes nothing):

  python3 scripts/tui/generate-parity-manifest.py --check

fails when the committed manifest differs from what the generator would write (a dashboard surface was added or
removed without regenerating), when any entry is still "planned", or when a "not-applicable" or "extension" entry
has no notes saying why. The Tui.Parity test suite parses the same sources and enforces the same rules.
"""
import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(__file__))
from client_exports import server_exports, pascal, ROOT  # noqa: E402

DASH = os.path.join(ROOT, "src", "Armada.Dashboard", "src")
MANIFEST = os.path.join(ROOT, "src", "Armada.Tui", "parity.json")

HUB_PAGES = {
    "CaptainsHub": ("/captains", "tab"), "DeliveryHub": ("/delivery", "tab"), "DispatchHub": ("/dispatch", "tab"),
    "MissionsHub": ("/missions", "tab"), "ServerHub": ("/server", "tab"), "VesselsHub": ("/vessels", "tab"),
    "Configuration": ("/configuration", "tab"), "Activity": ("/activity", "source"), "FleetActions": ("/fleet-actions", "tab"),
}

EVENT_RE = re.compile(r"'((?:mission|voyage|captain|deployment|objective|incident|planning-session|objective-refinement-session|ask)\.[a-z][a-z.\-]*)'")


def read(path):
    with open(path, encoding="utf-8") as f:
        return f.read()


def routes():
    src = read(os.path.join(DASH, "App.tsx"))
    out = []
    for m in re.finditer(r'<Route\s+(index\s+)?(?:path="([^"]+)")?', src):
        if m.group(1):
            out.append("/")
        elif m.group(2):
            out.append("/" + m.group(2))
    redirects = {}
    for m in re.finditer(r'<Route path="([^"]+)" element=\{<Navigate to="([^"]+)"', src):
        redirects["/" + m.group(1)] = m.group(2)
    return out, redirects


def tabs():
    out = []
    pages = os.path.join(DASH, "pages")
    for fname in sorted(os.listdir(pages)):
        if not fname.endswith(".tsx") or fname.endswith(".test.tsx"):
            continue
        page = fname[:-4]
        if page not in HUB_PAGES:
            continue
        for m in re.finditer(r"\{ key: '([^']+)', label: '([^']+)'", read(os.path.join(pages, fname))):
            out.append((page, m.group(1), m.group(2)))
    return out


def events():
    found = set()
    for dp, dn, fn in os.walk(DASH):
        for f in fn:
            if not (f.endswith(".ts") or f.endswith(".tsx")) or ".test." in f:
                continue
            for m in EVENT_RE.finditer(read(os.path.join(dp, f))):
                value = m.group(1)
                if value.endswith(".") or value.startswith("mission.rules"):
                    continue
                found.add(value)
    return sorted(found)


def settings_fields():
    fields = set()
    server = read(os.path.join(DASH, "pages", "Server.tsx"))
    for m in re.finditer(r"settings\??\.((?:remoteControl\??\.)?[a-zA-Z]+)", server):
        name = m.group(1).replace("?.", ".")
        if name in ("json", "import", "fleetActions", "remoteControl"):
            continue
        fields.add("settings." + name)
    imp = read(os.path.join(DASH, "components", "settings", "ImportFleetActionSettings.tsx"))
    for m in re.finditer(r"importDraft\??\.([a-zA-Z]+)", imp):
        fields.add("settings.import." + m.group(1))
    for m in re.finditer(r"fleetDraft\??\.([a-zA-Z]+)", imp):
        fields.add("settings.fleetActions." + m.group(1))
    rh = read(os.path.join(DASH, "components", "vessels", "health", "RepositoryHealthSettingsSection.tsx"))
    for m in re.finditer(r"settings\??\.([a-zA-Z]+)", rh):
        fields.add("settings.repositoryHealth." + m.group(1))
    for m in re.finditer(r"\{ key: '([a-zA-Z]+)', label: msg\(", rh):
        key = m.group(1)
        prefix = "settings.repositoryHealth.thresholds." if key.endswith("Warn") or key.endswith("Fail") else "settings.repositoryHealth."
        fields.add(prefix + key)
    ret = read(os.path.join(DASH, "components", "settings", "RetentionSettings.tsx"))
    m = re.search(r"const FIELDS: RetentionField\[\] = \[([^\]]*)\]", ret)
    if m:
        for f in re.findall(r"'([a-zA-Z]+)'", m.group(1)):
            fields.add("settings.retention." + f)
    return sorted(fields)


def check(entries, rendered):
    """Release gate: the committed manifest matches the generator output, nothing is planned, exceptions say why."""
    problems = []
    current = read(MANIFEST) if os.path.exists(MANIFEST) else ""
    if current != rendered:
        problems.append("src/Armada.Tui/parity.json is out of date with the dashboard source; run python3 scripts/tui/generate-parity-manifest.py and commit it")
    for e in entries:
        if e["status"] == "planned":
            problems.append("planned (not implemented): %s %s" % (e["kind"], e["key"]))
        elif e["status"] in ("not-applicable", "extension") and not e.get("notes", "").strip():
            problems.append("%s entry without notes: %s %s" % (e["status"], e["kind"], e["key"]))
    for p in problems:
        print("parity: " + p, file=sys.stderr)
    if problems:
        print("parity check failed (%d problem(s))" % len(problems), file=sys.stderr)
        return 1
    print("parity check passed: %d entries, none planned" % len(entries))
    return 0


def main():
    check_only = "--check" in sys.argv[1:]
    old = {}
    if os.path.exists(MANIFEST):
        for e in json.loads(read(MANIFEST)).get("entries", []):
            old[(e["kind"], e["key"])] = e
    entries = []

    def add(kind, key, status, tui, workstream="", notes=""):
        prev = old.get((kind, key))
        if prev:
            entries.append(prev)
            return
        entries.append({"kind": kind, "key": key, "status": status, "tui": tui, "workstream": workstream, "notes": notes})

    route_list, redirects = routes()
    for r in route_list:
        if r in redirects:
            add("route", r, "implemented", "Router", "W1.12", "redirect to " + redirects[r])
        else:
            add("route", r, "planned", "", "", "")
    for page, key, label in tabs():
        route, param = HUB_PAGES[page]
        add("tab", page + ":" + key, "planned", route + "?" + param + "=" + key, "", label)
    for area, name, kind, text in server_exports():
        add("api", name, "implemented", "ArmadaClient." + pascal(name) + "Async", "W0.2", area)
    entity = {"mission.changed", "voyage.changed", "captain.changed", "deployment.changed", "objective.changed", "incident.changed"}
    for ev in events():
        if ev in entity:
            add("event", ev, "implemented", "NotificationService", "W1.10", "toast and notification history")
        elif ev.startswith("ask."):
            add("event", ev, "planned", "AskScreen", "W2.8", "")
        elif ev.startswith("planning-session"):
            add("event", ev, "planned", "PlanningScreen", "W3.4", "")
        else:
            add("event", ev, "planned", "BacklogItemScreen", "W3.6", "")
    for field in settings_fields():
        add("setting", field, "planned", "ServerSettingsScreen", "W7.7", "")
    for route in ["/approvals", "/setup"]:
        add("route", route, "extension", "", "", "TUI-only route")
    for name in ["GetI18nCatalogAsync", "GetOpenApiDocumentAsync", "SendRawAsync"]:
        add("api", "tui:" + name, "extension", "ArmadaClient." + name, "W0.2", "TUI-only client call")

    entries.sort(key=lambda e: (["route", "tab", "api", "event", "setting"].index(e["kind"]), e["key"]))
    doc = {
        "description": "Dashboard-to-TUI parity manifest (TUI_APP_PLAN.md, Parity enforcement). Generated by scripts/tui/generate-parity-manifest.py; statuses are maintained by hand.",
        "statuses": ["implemented", "planned", "not-applicable", "extension"],
        "entries": entries,
    }
    rendered = json.dumps(doc, indent=2, ensure_ascii=True) + "\n"
    if check_only:
        return check(entries, rendered)
    with open(MANIFEST, "w", encoding="utf-8", newline="\n") as f:
        f.write(rendered)
    counts = {}
    for e in entries:
        counts.setdefault(e["kind"], {}).setdefault(e["status"], 0)
        counts[e["kind"]][e["status"]] += 1
    print(json.dumps(counts))
    return 0


if __name__ == "__main__":
    sys.exit(main())
