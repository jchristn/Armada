#!/usr/bin/env python3
"""Generate src/Armada.VSCode/parity.json (VSCODE_EXTENSION_PLAN.md section 6).

The manifest maps every surface the VS Code extension has to reach to its VS Code implementation:

- dashboard routes (App.tsx), hub tabs, server-calling API functions (api/client.ts), WebSocket event types and
  Server-page settings fields, parsed by the TUI's parsers (scripts/tui/dashboard_surfaces.py, client_exports.py);
- the TUI's own extension entries (kind "tui", from src/Armada.Tui/parity.json);
- Harbor's tray menu commands and windows (kind "harbor", from HarborMenuCommandEnum.cs and the Window classes);
- the Helm CLI commands a VS Code user needs (kind "cli", plan section 5);
- VS Code-only features (kind "vscode", status "extension").

Each entry has a status (implemented, planned, not-applicable, extension), the VS Code surface that implements it
(`command:<id>`, `view:<id>` or `setting:<key>`, checked against package.json by the VsCode.Parity suite), the
milestone that delivers it, and notes. Existing entries keep their status, surface, milestone and notes (maintained
by hand); new surfaces are added as planned. Run after dashboard, Harbor or CLI changes:

  python3 scripts/vscode/generate-parity-manifest.py

Check mode (CI; writes nothing):

  python3 scripts/vscode/generate-parity-manifest.py --check

fails when the committed manifest differs from what the generator would write, when an entry has an unknown status,
when an implemented or extension entry names no surface, or when a not-applicable or extension entry has no notes.
Planned entries are allowed during the milestones; --forbid-planned (the release gate at milestone V8) fails on them.
"""
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "tui"))
from client_exports import ROOT, server_exports  # noqa: E402
from dashboard_surfaces import HUB_PAGES, events, read, routes, settings_fields, tabs  # noqa: E402

MANIFEST = os.path.join(ROOT, "src", "Armada.VSCode", "parity.json")
TUI_MANIFEST = os.path.join(ROOT, "src", "Armada.Tui", "parity.json")
HARBOR_MENU = os.path.join(ROOT, "src", "Armada.Core", "Hosting", "HarborMenuCommandEnum.cs")
HARBOR_DIR = os.path.join(ROOT, "src", "Armada.Harbor")
STATUSES = ["implemented", "planned", "not-applicable", "extension"]
KINDS = ["route", "tab", "api", "event", "setting", "tui", "harbor", "cli", "vscode"]

# The Helm CLI commands in scope for VS Code (plan section 5): only the entries a VS Code user needs.
CLI_COMMANDS = ["config init", "doctor", "go", "mcp install", "profile", "reset", "server", "watch"]

# Route prefix -> milestone (plan section 7). The longest matching prefix wins.
MILESTONES = {
    "/": "V1", "/inbox": "V1", "/missions": "V1", "/voyages": "V1", "/merge-queue": "V1", "/captains": "V1", "/docks": "V1",
    "/dispatch": "V1", "/evaluations": "V1", "/jobs": "V1",
    "/ask": "V2", "/backlog": "V2", "/objectives": "V2", "/planning": "V2", "/refinement": "V2",
    "/vessels": "V3", "/fleets": "V3", "/workspace": "V3",
    "/fleet-actions": "V4", "/blackbox": "V4", "/delivery": "V4", "/deployments": "V4", "/environments": "V4",
    "/releases": "V4", "/incidents": "V4", "/checks": "V4", "/runbooks": "V4",
    "/configuration": "V5", "/personas": "V5", "/pipelines": "V5", "/prompt-templates": "V5", "/playbooks": "V5",
    "/workflow-profiles": "V5", "/project-profiles": "V5", "/skills": "V5", "/model-endpoints": "V5", "/endpoints": "V5",
    "/memories": "V5", "/harbors": "V5", "/activity": "V5", "/history": "V5", "/requests": "V5", "/events": "V5",
    "/signals": "V5", "/api-explorer": "V5",
    "/server": "V6", "/storage": "V6", "/doctor": "V6", "/settings": "V6", "/admin": "V6", "/cli-permissions": "V6",
    "/setup": "V6", "/approvals": "V1", "/battery": "V1", "/dashboard": "V1", "/notifications": "V1",
}

# Black box calls the extension makes today, and the command or view that reaches each one.
IMPLEMENTED_API = {
    "enumerateBlackBox": "view:armadaFailures",
    "enumerateBlackBoxHotspots": "setting:armada.decorations.hotspotLimit",
    "getBlackBoxSection": "setting:armada.decorations.enabled",
    "getBlackBox": "command:armada.openBlackBox",
    "getMissionBlackBox": "command:armada.openBlackBox",
    "getVoyageBlackBox": "command:armada.openBlackBox",
    "getBlackBoxBrief": "command:armada.copyFixThis",
    "listBlackBoxNotes": "command:armada.openBlackBox",
    "addBlackBoxNote": "command:armada.addNote",
    "resolveBlackBox": "command:armada.resolveEntry",
    "runBlackBoxRecovery": "command:armada.runRecoveryPath",
    "getVesselBlackBoxDoctor": "command:armada.openBlackBox",
    "getBlackBoxTimeline": "command:armada.showBlackBoxTimeline",
    "listMissionBlackBoxEntries": "command:armada.compareBlackBoxAttempts",
    "diffBlackBoxEntries": "command:armada.compareBlackBoxAttempts",
    "enumerateBlackBoxResilienceGaps": "command:armada.showResilienceGaps",
    "previewMissionOverlap": "command:armada.previewMissionOverlap",
}

# Harbor menu commands that VS Code itself provides (editing, window management, appearance, quitting).
HARBOR_NOT_APPLICABLE = {
    "MinimizeWindow", "CloseWindow", "ZoomWindow", "BringAllToFront", "Undo", "Redo", "Cut", "Copy", "Paste", "SelectAll",
    "AppearanceSystem", "AppearanceLight", "AppearanceDark", "Quit",
}

# VS Code-only features.
VSCODE_EXTENSIONS = [
    ("connection-status-bar", "command:armada.showConnectionActions", "V0", "Status bar item with the connection state and target; opens a quick pick of connection actions"),
    ("failures-in-explorer", "setting:armada.failures.showInExplorer", "V0", "Optional copy of the Failures view in the Explorer (owner decision 4)"),
    ("fix-this-to-chat", "command:armada.sendFixThisToChat", "V0", "Hands the FIX THIS brief to the editor's chat with the brief attached"),
    ("conflict-decorations", "setting:armada.decorations.enabled", "V0", "File decorations for conflicted files and hotspot directories of the open repository's vessel"),
]


def milestone(path):
    """The milestone of the longest route prefix that matches the path ("/" only matches itself)."""
    if path == "/":
        return MILESTONES["/"]
    best_prefix, best = "", ""
    for prefix, value in MILESTONES.items():
        if prefix == "/":
            continue
        if (path == prefix or path.startswith(prefix + "/") or path.startswith(prefix + "?")) and len(prefix) > len(best_prefix):
            best_prefix, best = prefix, value
    return best


def harbor_commands():
    members = re.findall(r"^\s*([A-Z][A-Za-z]+),?\s*$", read(HARBOR_MENU), re.M)
    if not members:
        raise SystemExit("vscode parity: no members found in HarborMenuCommandEnum.cs")
    return members


def harbor_windows():
    found = set()
    for dp, dn, fn in os.walk(HARBOR_DIR):
        dn[:] = [d for d in dn if d not in ("bin", "obj")]
        for f in fn:
            if f.endswith(".cs"):
                for m in re.finditer(r"\bclass\s+(\w+)\s*:\s*Window\b", read(os.path.join(dp, f))):
                    found.add(m.group(1))
    if not found:
        raise SystemExit("vscode parity: no Harbor Window classes found")
    return sorted(found)


def tui_extensions():
    doc = json.loads(read(TUI_MANIFEST))
    return [e for e in doc.get("entries", []) if e.get("status") == "extension"]


def build(old):
    entries = []

    def add(kind, key, status, surface="", stone="", notes=""):
        prev = old.get((kind, key))
        if prev:
            entries.append(prev)
            return
        entries.append({"kind": kind, "key": key, "status": status, "surface": surface, "milestone": stone, "notes": notes})

    route_list, redirects = routes()
    for r in route_list:
        notes = "redirect to " + redirects[r] if r in redirects else ""
        if r == "/*":
            add("route", r, "not-applicable", "", "", "Browser catch-all that renders the dashboard's NotFound page; VS Code has no URL bar to mistype")
            continue
        if r == "/blackbox/:id":
            notes = "Partial today: the entry webview (armada.openBlackBox) has FIX THIS, recovery, resolve, notes, Ask, timeline and attempt diff, but not pin, download or Copy JSON"
        add("route", r, "planned", "", milestone(r), notes)
    for page, key, label in tabs():
        route, param = HUB_PAGES[page]
        notes = label
        if page == "MissionsHub" and key == "blackbox":
            notes = label + "; partial today: the Failures view lists open entries, without the fingerprint, hotspot, retention and storage sub-tabs"
        add("tab", page + ":" + key, "planned", "", milestone("/" + key) or milestone(route), notes)
    for area, name, kind, text in server_exports():
        if name in IMPLEMENTED_API:
            add("api", name, "implemented", IMPLEMENTED_API[name], "V0", area)
        else:
            add("api", name, "planned", "", "", area)
    for ev in events():
        add("event", ev, "planned", "", "V2" if ev.startswith(("ask.", "planning-session", "objective")) else "V1", "")
    for field in settings_fields():
        add("setting", field, "planned", "", "V6", "")
    for e in tui_extensions():
        key = e["kind"] + ":" + e["key"]
        add("tui", key, "planned", "", milestone(e["key"]) if e["kind"] == "route" else "", "TUI-only " + e["kind"] + ": " + e.get("notes", ""))
    for member in harbor_commands():
        if member in HARBOR_NOT_APPLICABLE:
            add("harbor", "menu:" + member, "not-applicable", "", "", "Editing, window, appearance and quit commands come from VS Code itself")
        else:
            add("harbor", "menu:" + member, "planned", "", "V7", "")
    for window in harbor_windows():
        if window == "HarborDialog":
            add("harbor", "window:" + window, "not-applicable", "", "", "Generic message dialog; VS Code notifications and modal dialogs replace it")
        else:
            add("harbor", "window:" + window, "planned", "", "V7", "")
    for command in CLI_COMMANDS:
        add("cli", command, "planned", "", "V1" if command in ("go", "watch") else "V6", "")
    for key, surface, stone, notes in VSCODE_EXTENSIONS:
        add("vscode", key, "extension", surface, stone, notes)

    entries.sort(key=lambda e: (KINDS.index(e["kind"]), e["key"]))
    return entries


def problems_of(entries, rendered, forbid_planned):
    problems = []
    current = read(MANIFEST) if os.path.exists(MANIFEST) else ""
    if current != rendered:
        problems.append("src/Armada.VSCode/parity.json is out of date; run python3 scripts/vscode/generate-parity-manifest.py and commit it")
    for e in entries:
        if e["status"] not in STATUSES:
            problems.append("unknown status %s: %s %s" % (e["status"], e["kind"], e["key"]))
        if e["status"] in ("implemented", "extension") and not re.match(r"^(command|view|setting):\S+$", e.get("surface", "")):
            problems.append("%s entry without a command:, view: or setting: surface: %s %s" % (e["status"], e["kind"], e["key"]))
        if e["status"] in ("not-applicable", "extension") and not e.get("notes", "").strip():
            problems.append("%s entry without notes: %s %s" % (e["status"], e["kind"], e["key"]))
        if forbid_planned and e["status"] == "planned":
            problems.append("planned (not implemented): %s %s" % (e["kind"], e["key"]))
    return problems


def main():
    args = sys.argv[1:]
    check_only = "--check" in args
    forbid_planned = "--forbid-planned" in args
    old = {}
    if os.path.exists(MANIFEST):
        for e in json.loads(read(MANIFEST)).get("entries", []):
            old[(e["kind"], e["key"])] = e
    entries = build(old)
    doc = {
        "description": "Parity manifest of the VS Code extension (VSCODE_EXTENSION_PLAN.md section 6). Generated by scripts/vscode/generate-parity-manifest.py; statuses, surfaces, milestones and notes are maintained by hand.",
        "statuses": STATUSES,
        "entries": entries,
    }
    rendered = json.dumps(doc, indent=2, ensure_ascii=True) + "\n"
    counts = {}
    for e in entries:
        counts.setdefault(e["kind"], {}).setdefault(e["status"], 0)
        counts[e["kind"]][e["status"]] += 1
    if check_only:
        problems = problems_of(entries, rendered, forbid_planned)
        for p in problems:
            print("parity: " + p, file=sys.stderr)
        if problems:
            print("vscode parity check failed (%d problem(s))" % len(problems), file=sys.stderr)
            return 1
        planned = sum(1 for e in entries if e["status"] == "planned")
        print("vscode parity check passed: %d entries, %d planned" % (len(entries), planned))
        return 0
    with open(MANIFEST, "w", encoding="utf-8", newline="\n") as f:
        f.write(rendered)
    print(json.dumps(counts))
    return 0


if __name__ == "__main__":
    sys.exit(main())
