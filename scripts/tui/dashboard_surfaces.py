#!/usr/bin/env python3
"""Shared parser for the dashboard surfaces the parity manifests track (src/Armada.Dashboard/src).

Routes (App.tsx), hub tabs (pages with Tabs), WebSocket event types, and Server-page settings fields. Used by
scripts/tui/generate-parity-manifest.py and scripts/mobile/generate-parity-manifest.py; the server-calling API
functions come from client_exports.py. Test.Shared/Infrastructure/DashboardSource.cs mirrors these rules in C#.
"""
import os
import re

from client_exports import ROOT

DASH = os.path.join(ROOT, "src", "Armada.Dashboard", "src")

HUB_PAGES = {
    "CaptainsHub": ("/captains", "tab"), "DeliveryHub": ("/delivery", "tab"), "DispatchHub": ("/dispatch", "tab"),
    "MissionsHub": ("/missions", "tab"), "ServerHub": ("/server", "tab"), "VesselsHub": ("/vessels", "tab"),
    "Configuration": ("/configuration", "tab"), "Activity": ("/activity", "source"), "FleetActions": ("/fleet-actions", "tab"),
    "CliPermissions": ("/cli-permissions", "tab"),
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
    # The field tables (REPOSITORY_HEALTH_FIELDS, ..._THRESHOLD_FIELDS) live in lib/settingsRanges.ts, shared with the mobile app.
    rh_fields = rh + read(os.path.join(DASH, "lib", "settingsRanges.ts"))
    rh_keys = re.findall(r"\{ key: '([a-zA-Z]+)', label: msg\(", rh_fields)
    if not rh_keys:
        raise SystemExit("dashboard_surfaces: repository health field tables not found; update settings_fields()")
    for key in rh_keys:
        prefix = "settings.repositoryHealth.thresholds." if key.endswith("Warn") or key.endswith("Fail") else "settings.repositoryHealth."
        fields.add(prefix + key)
    perm = read(os.path.join(DASH, "components", "settings", "CliPermissionSettings.tsx"))
    m = re.search(r"interface Draft \{([^}]*)\}", perm)
    if not m:
        raise SystemExit("dashboard_surfaces: CliPermissionSettings Draft interface not found; update settings_fields()")
    if m:
        for f in re.findall(r"^\s*([a-zA-Z]+):", m.group(1), re.M):
            fields.add("settings.permissions." + f)
    # RETENTION_FIELDS lives in lib/settingsRanges.ts, shared with the mobile app.
    ret = read(os.path.join(DASH, "lib", "settingsRanges.ts"))
    m = re.search(r"const RETENTION_FIELDS: RetentionField\[\] = \[([^\]]*)\]", ret)
    if not m:
        raise SystemExit("dashboard_surfaces: RETENTION_FIELDS not found; update settings_fields()")
    if m:
        for f in re.findall(r"'([a-zA-Z]+)'", m.group(1)):
            fields.add("settings.retention." + f)
    # ASK_FIELDS (the edited Ask Armada settings) lives in lib/settingsRanges.ts too.
    m = re.search(r"const ASK_FIELDS: AskSettingsField\[\] = \[([^\]]*)\]", ret)
    if not m:
        raise SystemExit("dashboard_surfaces: ASK_FIELDS not found; update settings_fields()")
    for f in re.findall(r"'([a-zA-Z]+)'", m.group(1)):
        fields.add("settings.ask." + f)
    return sorted(fields)
