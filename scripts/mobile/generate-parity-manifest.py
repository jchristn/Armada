#!/usr/bin/env python3
"""Generate src/Armada.Mobile/parity.json from the dashboard source (MOBILE_APP_PLAN.md, "Parity enforcement").

The manifest maps every dashboard surface to its mobile equivalent: routes (App.tsx), hub tabs, server-calling API
functions (api/client.ts), WebSocket event types, and Server-page settings fields, parsed by the same code as the
TUI manifest (scripts/tui/dashboard_surfaces.py and client_exports.py). Each entry has a status (implemented,
planned, not-applicable, extension), the mobile screen or service, and the workstream that delivers it. Existing
entries keep their status, screen, workstream, and notes (maintained by hand); new surfaces are added as planned
with the workstream from the plan. Run after dashboard changes:

  python3 scripts/mobile/generate-parity-manifest.py

Check mode (CI on every push; writes nothing):

  python3 scripts/mobile/generate-parity-manifest.py --check

fails when the committed manifest differs from what the generator would write (a dashboard surface was added or
removed without regenerating), when an entry has an unknown status or no workstream, when a "not-applicable" or
"extension" entry has no notes, or when an "implemented" route has no screen file. Planned entries are allowed
until the release gate: from W6 on, CI adds --forbid-planned, which also fails on any entry still "planned".
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(__file__))
sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "tui"))
from client_exports import server_exports  # noqa: E402
from dashboard_surfaces import HUB_PAGES, events, read, settings_fields, tabs  # noqa: E402
from mobile_routes import EXTENSION_ROUTES, MOBILE, WORKSTREAMS, build, extension_file, first_segment  # noqa: E402

MANIFEST = os.path.join(MOBILE, "parity.json")
STATUSES = ["implemented", "planned", "not-applicable", "extension"]

# client.ts area (section header) -> workstream that builds the screens calling it.
AREA_WORKSTREAMS = {
    "Ask Armada threads (docs/ASK_ARMADA_HOME_BASE.md)": "W1.1", "Ask Armada": "W1.1",
    "CLI tool permissions": "W1.2", "Needs-you inbox": "W1.3",
    "Status / Health": "W2.1", "Missions": "W2.2", "Generic entity lookup": "W2.2", "Voyages": "W2.3",
    "Merge Queue": "W2.4", "Docks": "W2.4", "Signals": "W2.4", "Events": "W2.4",
    "Vessels": "W3.1", "Vessel Health": "W3.1", "Vessel Import": "W3.1", "Vessel History": "W3.1", "Workspace": "W3.1",
    "Workspace terminal": "W3.1", "Workspace diff (review)": "W3.1",
    "Fleets": "W3.2", "Captains": "W3.2", "Fleet Actions": "W3.2",
    "Objectives": "W3.3", "Planning Sessions": "W3.3",
    "Deployments": "W4.1", "Environments": "W4.1", "Releases": "W4.1", "Incidents": "W4.1", "Runbooks": "W4.1",
    "Check Runs": "W4.1",
    "Personas": "W4.2", "Pipelines": "W4.2", "Prompt Templates": "W4.2", "Playbooks": "W4.2", "Skills directory": "W4.2",
    "Memories": "W4.2", "Workflow Profiles": "W4.2", "Project profiles": "W4.2", "Harbors (host runners)": "W4.2",
    "Model endpoints (embedding/inference)": "W4.2",
    "Request History": "W4.3", "History": "W4.3", "Background jobs": "W4.3",
    "Server": "W4.4", "Settings": "W4.4", "Users (admin)": "W4.4", "Tenants (admin)": "W4.4", "Credentials (admin)": "W4.4",
    "Backup / Restore": "W4.4",
    "Auth": "W0.4",
}

# Client calls the foundation (W0) uses for real: name -> (mobile service, notes).
IMPLEMENTED_API = {
    "authenticate": ("SignInScreen", "email + tenant + password sign-in"),
    "lookupTenants": ("SignInScreen", "tenant lookup by email"),
    "whoami": ("AuthContext", "session validation, admin flags, password-change state"),
    "changePassword": ("PasswordChangeScreen", "default password change"),
    "getInbox": ("ApprovalsContext", "Approvals badge count (W0.6); the approvals center itself is W1.3"),
}

# Proxy session calls belong to proxy profiles (W5.4).
PROXY_API = {"getProxySessionContext", "clearProxySessionInstance", "logoutProxy"}

ENTITY_EVENTS = {"mission.changed", "voyage.changed", "captain.changed", "deployment.changed", "objective.changed", "incident.changed"}

TAB_ORDER = ["route", "tab", "api", "event", "setting"]


def event_workstream(ev):
    if ev.startswith("ask."):
        return "W1.1"
    if ev.startswith("planning-session") or ev.startswith("objective-refinement-session"):
        return "W3.3"
    return "W1.4"


def generate(old):
    entries = []

    def add(kind, key, status, mobile, workstream, notes=""):
        prev = old.get((kind, key))
        if prev:
            entries.append(prev)
            return
        entries.append({"kind": kind, "key": key, "status": status, "mobile": mobile, "workstream": workstream, "notes": notes})

    seen_dashboard = set()
    for r in build():
        if r["dashboard"] in seen_dashboard:
            continue
        seen_dashboard.add(r["dashboard"])
        screen = "src/app/" + r["file"]
        if r["redirect"]:
            add("route", r["dashboard"], "implemented", screen, r["workstream"], "redirect to " + r["redirect"])
        else:
            note = "mobile path " + r["pattern"] if r["pattern"] != r["dashboard"] else ""
            if r["dashboard"].endswith("?"):
                note = "two mobile routes: %s and %s" % (r["dashboard"][: r["dashboard"].rindex("/")], r["dashboard"][:-1])
            add("route", r["dashboard"], "planned", screen, r["workstream"], note)

    for page, key, label in tabs():
        route, param = HUB_PAGES[page]
        add("tab", page + ":" + key, "planned", route + "?" + param + "=" + key, WORKSTREAMS[first_segment(route)], label)

    for area, name, kind, text in server_exports():
        if name in IMPLEMENTED_API:
            service, note = IMPLEMENTED_API[name]
            add("api", name, "implemented", service, "W0.4" if area == "Auth" else "W0.6", note)
        elif name in PROXY_API:
            add("api", name, "planned", "ProxyProfile", "W5.4", area)
        else:
            if area not in AREA_WORKSTREAMS:
                raise SystemExit("mobile parity: no workstream for client.ts area %r (%s); add it to AREA_WORKSTREAMS" % (area, name))
            add("api", name, "planned", "", AREA_WORKSTREAMS[area], area)

    for ev in events():
        if ev in ENTITY_EVENTS:
            add("event", ev, "implemented", "NotificationContext", "W0.6", "notification center and toast (shared lib/notificationEvents)")
        else:
            add("event", ev, "planned", "", event_workstream(ev), "")

    for field in settings_fields():
        add("setting", field, "planned", "ServerSettingsScreen", "W4.4", "")

    for pattern, (tab, name, note) in EXTENSION_ROUTES.items():
        add("route", pattern, "extension", "src/app/" + extension_file(pattern), "W0.4" if pattern in ("/sign-in", "/password-change", "/profiles") else "W0.5", note)
    add("api", "mobile:fetchCatalog", "extension", "LocaleContext", "W0.3",
        "server i18n catalog (/dashboard/i18n/armada.json) fetched after sign-in; the dashboard loads it from its own origin")
    add("api", "mobile:probeServer", "extension", "ProfileForm", "W0.4",
        "unauthenticated health probe for a server profile's Test connection button")
    add("api", "mobile:pushDevices", "extension", "src/push/pushApi.ts (PushContext, Preferences)", "W5.3",
        "this device's push registration: POST/PUT/DELETE /api/v1/push/devices and POST .../test (the dashboard has no push UI)")
    add("api", "mobile:proxyNativeSession", "extension", "src/proxy/proxyApi.ts (AuthContext, ProxySignIn)", "W5.4",
        "Armada.Proxy sign-in for native clients: challenge + proof login, instances, and instance selection with a bearer proxy session (the dashboard uses the portal's cookie)")

    entries.sort(key=lambda e: (TAB_ORDER.index(e["kind"]), e["key"]))
    return entries


def render(entries):
    doc = {
        "description": "Dashboard-to-mobile parity manifest (MOBILE_APP_PLAN.md, Parity enforcement). Generated by scripts/mobile/generate-parity-manifest.py; statuses, screens, workstreams, and notes are maintained by hand once an entry exists.",
        "statuses": STATUSES,
        "entries": entries,
    }
    return json.dumps(doc, indent=2, ensure_ascii=True) + "\n"


def check(entries, rendered, forbid_planned):
    problems = []
    current = read(MANIFEST) if os.path.exists(MANIFEST) else ""
    if current != rendered:
        problems.append("src/Armada.Mobile/parity.json is out of date with the dashboard source; run python3 scripts/mobile/generate-parity-manifest.py and commit it")
    for e in entries:
        label = "%s %s" % (e["kind"], e["key"])
        if e["status"] not in STATUSES:
            problems.append("unknown status %r: %s" % (e["status"], label))
        if not e.get("workstream", "").strip():
            problems.append("no workstream: " + label)
        if e["status"] in ("not-applicable", "extension") and not e.get("notes", "").strip():
            problems.append("%s entry without notes: %s" % (e["status"], label))
        if e["kind"] == "route" and e["status"] in ("implemented", "extension"):
            screen = e.get("mobile", "")
            if not screen.startswith("src/app/") or not os.path.exists(os.path.join(MOBILE, screen)):
                problems.append("%s route without a screen file (%s): %s" % (e["status"], screen or "none", label))
        if forbid_planned and e["status"] == "planned":
            problems.append("planned (not implemented): " + label)
    for p in problems:
        print("mobile parity: " + p, file=sys.stderr)
    counts = {}
    for e in entries:
        counts[e["status"]] = counts.get(e["status"], 0) + 1
    summary = ", ".join("%d %s" % (counts[s], s) for s in STATUSES if s in counts)
    if problems:
        print("mobile parity check failed (%d problem(s)); %s" % (len(problems), summary), file=sys.stderr)
        return 1
    print("mobile parity check passed: %d entries (%s)%s" % (len(entries), summary, "; none planned" if forbid_planned else ""))
    return 0


def main():
    args = sys.argv[1:]
    old = {}
    if os.path.exists(MANIFEST):
        for e in json.loads(read(MANIFEST)).get("entries", []):
            old[(e["kind"], e["key"])] = e
    entries = generate(old)
    rendered = render(entries)
    if "--check" in args:
        return check(entries, rendered, "--forbid-planned" in args)
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
