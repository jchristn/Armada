#!/usr/bin/env python3
"""Map every dashboard route (src/Armada.Dashboard/src/App.tsx) to its mobile screen.

Shared by generate-parity-manifest.py (the parity manifest) and generate-route-screens.py (the Expo Router screen
files and src/navigation/dashboardRoutes.generated.ts). Each dashboard route becomes one or more mobile routes:

  - the same URL path, so a dashboard link or an armada:// deep link opens the same item, except "/" (the
    dashboard Home), which is /home on mobile because the app opens into Ask Armada;
  - an Expo Router file under src/Armada.Mobile/src/app/(app)/(<tab>)/..., where <tab> is the bottom tab whose
    stack hosts it (ask, approvals, work, more), chosen from the dashboard's own nav sections (lib/navModel.ts);
  - the workstream of MOBILE_APP_PLAN.md that implements it.
"""
import os
import re
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "tui"))
from client_exports import ROOT  # noqa: E402
from dashboard_surfaces import DASH, read, routes  # noqa: E402

MOBILE = os.path.join(ROOT, "src", "Armada.Mobile")
APP_DIR = os.path.join(MOBILE, "src", "app")
TABS_DIR = os.path.join(APP_DIR, "(app)")

# Workstream (MOBILE_APP_PLAN.md) per first path segment of a dashboard route.
WORKSTREAMS = {
    "": "W2.1", "dashboard": "W2.1",
    "ask": "W1.1", "inbox": "W1.3", "notifications": "W1.4",
    "missions": "W2.2", "voyages": "W2.3",
    "dispatch": "W2.4", "merge-queue": "W2.4", "docks": "W2.4", "signals": "W2.4", "events": "W2.4",
    "vessels": "W3.1", "workspace": "W3.1",
    "fleets": "W3.2", "captains": "W3.2", "fleet-actions": "W3.2",
    "planning": "W3.3", "backlog": "W3.3", "objectives": "W3.3",
    "delivery": "W4.1", "checks": "W4.1", "environments": "W4.1", "deployments": "W4.1", "releases": "W4.1",
    "incidents": "W4.1", "runbooks": "W4.1",
    "configuration": "W4.2", "personas": "W4.2", "pipelines": "W4.2", "prompt-templates": "W4.2",
    "playbooks": "W4.2", "workflow-profiles": "W4.2", "project-profiles": "W4.2", "skills": "W4.2",
    "activity": "W4.3", "history": "W4.3", "requests": "W4.3", "jobs": "W4.3",
    "cli-permissions": "W4.4", "api-explorer": "W4.4", "server": "W4.4", "admin": "W4.4", "doctor": "W4.4",
    "settings": "W4.4",
}

# Workstream that ships the redirects and the navigation shell.
NAV_WORKSTREAM = "W0.5"

# Nav section key (lib/navModel.ts) -> bottom tab.
SECTION_TABS = {
    "operations": "work", "delivery": "work", "fleet": "work",
    "configuration": "more", "activity": "more", "system": "more",
}

# Checked before the nav sections (Needs You lives in OPERATIONS on the dashboard, but in Approvals on mobile).
TAB_OVERRIDES = {"/ask": "ask", "/inbox": "approvals", "/notifications": "approvals", "/": "work", "/dashboard": "work"}

# Mobile-only routes (status "extension" in the manifest): pattern -> (tab, file, note).
EXTENSION_ROUTES = {
    "/approvals": ("approvals", "approvals.tsx", "Approvals tab root: the Needs You count and approvals center entry (TUI has the same route)"),
    "/more": ("more", "more.tsx", "More tab root: CONFIGURATION, ACTIVITY, SYSTEM, and app settings (phone navigation)"),
    "/preferences": ("more", "preferences.tsx", "App preferences: theme, language, push notifications for the connected server (device settings, not server settings)"),
    "/profiles": ("more", "profiles.tsx", "Server profiles: saved Admirals, one active (TUI-style connections)"),
    "/notification-center": (None, "notification-center.tsx", "In-app notification history (the dashboard's bell menu), a modal over every tab"),
    "/sign-in": (None, "sign-in.tsx", "Sign-in, biometric unlock, and unreachable-server states (the dashboard shows LoginFlow in place)"),
    "/password-change": (None, "password-change.tsx", "Default-password change with Skip (the dashboard shows PasswordChangeRequired in place)"),
}


def nav_sections():
    """[(section key, [matchers])] from lib/navModel.ts."""
    src = read(os.path.join(DASH, "lib", "navModel.ts"))
    out = []
    for m in re.finditer(r"key: '([a-z]+)',\s*label: '[^']*',\s*matchers: \[([^\]]*)\]", src):
        out.append((m.group(1), re.findall(r"'([^']+)'", m.group(2))))
    return out


def tab_for(pattern, sections):
    for prefix, tab in TAB_OVERRIDES.items():
        if pattern == prefix or (prefix != "/" and pattern.startswith(prefix + "/")):
            return tab
    for key, matchers in sections:
        for prefix in matchers:
            if pattern == prefix or pattern.startswith(prefix + "/"):
                return SECTION_TABS[key]
    raise SystemExit("mobile_routes: no tab for dashboard route %s; add it to a nav section or TAB_OVERRIDES" % pattern)


def first_segment(pattern):
    return pattern.strip("/").split("/")[0]


def workstream_for(pattern):
    seg = first_segment(pattern)
    if seg not in WORKSTREAMS:
        raise SystemExit("mobile_routes: no workstream for dashboard route %s; add its first segment to WORKSTREAMS" % pattern)
    return WORKSTREAMS[seg]


def app_patterns(pattern):
    """Mobile URL patterns for one dashboard route (an optional segment becomes two routes)."""
    if pattern == "/":
        return ["/home"]
    if pattern.endswith("?"):
        base = pattern[: pattern.rindex("/")]
        return [base, pattern[:-1]]
    return [pattern]


TITLE_OVERRIDES = {
    "/ask/:threadId": "Ask Armada", "/api-explorer/:operationId": "API Explorer", "/planning/:id": "Planning Session",
    "/voyages/create": "Create Voyage", "/releases/new": "New Release", "/prompt-templates/create": "New Prompt Template",
    "/vessels/import": "Import Vessels", "/vessels/health": "Vessel Health", "/vessels/:id/onboarding": "Vessel Onboarding",
    "/vessels/:id/history": "View History", "/merge-queue/:id": "Merge Queue Entry", "/fleet-actions/runs/:id": "Fleet Action Run",
    "/workspace/:vesselId": "Workspace", "/workspace/:vesselId/:panel": "Workspace", "/checks/:id": "Check Run",
    "/doctor": "Diagnostics", "/backlog/:id": "Backlog Item",
}


def nav_labels():
    """{route: label} for every nav item in lib/navModel.ts (Dashboard, Ask Armada, section items)."""
    src = read(os.path.join(DASH, "lib", "navModel.ts"))
    return {m.group(1): m.group(2) for m in re.finditer(r"to: '([^']+)',\s*label: '([^']+)'", src)}


def title_for(pattern, labels=None):
    """English screen title (translated at runtime through the shared catalog)."""
    if pattern in ("/", "/home"):
        return "Dashboard"
    if pattern in TITLE_OVERRIDES:
        return TITLE_OVERRIDES[pattern]
    if labels and pattern in labels:
        return labels[pattern]
    parts = [p for p in pattern.strip("/").split("/") if p]
    static = [p for p in parts if not p.startswith(":")]
    words = " ".join(w.capitalize() for w in static[-1].replace("-", " ").split())
    if parts[-1].startswith(":") and len(static) == len(parts) - 1:
        if words.endswith("ies"):
            return words[:-3] + "y"
        if words.endswith("s"):
            return words[:-1]
        return words + " Item"
    return words


def build():
    """Every mobile route derived from the dashboard: dicts with dashboard, pattern, tab, file, workstream, redirect."""
    route_list, redirects = routes()
    sections = nav_sections()
    labels = nav_labels()
    out = []
    seen = set()
    for dashboard in route_list:
        for pattern in app_patterns(dashboard):
            if pattern in seen:
                continue
            seen.add(pattern)
            redirect = redirects.get(dashboard)
            out.append({
                "dashboard": dashboard,
                "pattern": pattern,
                "tab": tab_for(dashboard, sections),
                "workstream": NAV_WORKSTREAM if redirect else workstream_for(dashboard),
                "redirect": redirect,
                "title": title_for(pattern, labels),
            })
    patterns = [r["pattern"] for r in out]
    for r in out:
        r["file"] = file_for(r["pattern"], r["tab"], patterns)
    return out


def file_for(pattern, tab, all_patterns):
    """Expo Router file (relative to src/app) for a mobile pattern."""
    parts = ["[%s]" % p[1:] if p.startswith(":") else p for p in pattern.strip("/").split("/")]
    has_children = any(other != pattern and other.startswith(pattern + "/") for other in all_patterns)
    rel = "/".join(parts) + ("/index.tsx" if has_children else ".tsx")
    return "(app)/(%s)/%s" % (tab, rel)


def extension_file(pattern):
    tab, name, _ = EXTENSION_ROUTES[pattern]
    return name if tab is None else "(app)/(%s)/%s" % (tab, name)
