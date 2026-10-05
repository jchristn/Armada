#!/usr/bin/env python3
"""Install verification smoke test: drive a freshly installed Admiral through the REST API.

Steps (each prints PASS or FAIL):
  1. Wait for GET /api/v1/status/health.
  2. Load the dashboard page (GET /dashboard, and --dashboard-url when given).
  3. Log in as admin@armada (POST /api/v1/authenticate) and confirm the session with GET /api/v1/whoami.
     If the server still requires a password change, change it to --password first.
  4. Register the stub inference endpoint (POST /api/v1/model-endpoints) and an ApiEndpoint captain.
  5. Create a fleet and a vessel pointing at --repo-url.
  6. Dispatch one voyage with one mission assigned to that captain.
  7. Poll the mission until it leaves Pending (and, with --expect complete, until it produces work;
     with --expect landed, until it is Complete, i.e. landed). The final status, the captain, and the
     mission log tail are printed.

--working-directory and --landing-mode set the vessel's checkout and landing mode (the onboarding check
uses a local checkout with LocalMerge so the mission lands into it). --timings prefixes every step with the
seconds elapsed since the script started.

Exit code 0 when every step passed. Standard library only (Python 3.8+), so it runs unchanged on
Linux, macOS, and Windows runners and inside minimal containers.

Example:
  smoke.py --base-url http://127.0.0.1:34010 --password 'Install-Smoke-1!' \
           --stub-url http://127.0.0.1:34050/v1 --repo-url /tmp/smoke/origin.git
"""

import argparse
import json
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid

PENDING = {"Pending"}
SUCCESS_STATES = {"WorkProduced", "PullRequestOpen", "Testing", "Review", "Complete"}
TERMINAL_STATES = SUCCESS_STATES | {"Failed", "Cancelled", "LandingFailed"}

FAILURES = []
STARTED = time.time()
TIMINGS = False


def log(message):
    print(message, flush=True)


def step(ok, label, detail=""):
    prefix = ("[%6.1fs] " % (time.time() - STARTED)) if TIMINGS else ""
    log(prefix + ("PASS  " if ok else "FAIL  ") + label + (("  (" + detail + ")") if detail else ""))
    if not ok:
        FAILURES.append(label)
    return ok


class Api(object):
    def __init__(self, base_url, timeout=30):
        self.base_url = base_url.rstrip("/")
        self.timeout = timeout
        self.token = None

    def request(self, method, path, body=None, url=None, raw=False):
        target = url if url else self.base_url + path
        data = None
        headers = {"Accept": "application/json"}
        if body is not None:
            data = json.dumps(body).encode("utf-8")
            headers["Content-Type"] = "application/json"
        if self.token:
            headers["X-Token"] = self.token
        req = urllib.request.Request(target, data=data, method=method, headers=headers)
        try:
            with urllib.request.urlopen(req, timeout=self.timeout) as resp:
                payload = resp.read()
                status = resp.status
                content_type = resp.headers.get("Content-Type", "")
        except urllib.error.HTTPError as e:
            payload = e.read()
            status = e.code
            content_type = e.headers.get("Content-Type", "") if e.headers else ""
        if raw:
            return status, payload, content_type
        text = payload.decode("utf-8", "replace") if payload else ""
        try:
            parsed = json.loads(text) if text else None
        except ValueError:
            parsed = text
        return status, parsed


def wait_healthy(api, timeout):
    deadline = time.time() + timeout
    last = ""
    while time.time() < deadline:
        try:
            status, _ = api.request("GET", "/api/v1/status/health")
            if status == 200:
                return True, ""
            last = "HTTP " + str(status)
        except Exception as e:  # connection refused while the server starts
            last = str(e)
        time.sleep(1)
    return False, last


def main():
    parser = argparse.ArgumentParser(description="Armada install verification smoke test")
    parser.add_argument("--base-url", required=True, help="Admiral REST base URL, e.g. http://127.0.0.1:34010")
    parser.add_argument("--password", required=True, help="admin@armada password (ARMADA_INITIAL_ADMIN_PASSWORD)")
    parser.add_argument("--stub-url", required=True, help="Stub inference base URL as seen from the Admiral, e.g. http://127.0.0.1:34050/v1")
    parser.add_argument("--repo-url", required=True, help="Git repository URL or path as seen from the Admiral")
    parser.add_argument("--default-branch", default="main")
    parser.add_argument("--dashboard-url", action="append", default=[], help="Extra URL that must serve the React dashboard (repeatable)")
    parser.add_argument("--admiral-dashboard", choices=["react", "any"], default="react",
                        help="react: the Admiral's /dashboard must serve the React build; any: the legacy pages also pass")
    parser.add_argument("--api-via", default=None, help="Extra base URL (for example the nginx dashboard) whose /api/v1/whoami must accept the session")
    parser.add_argument("--email", default="admin@armada")
    parser.add_argument("--tenant", default="default")
    parser.add_argument("--health-timeout", type=int, default=180)
    parser.add_argument("--mission-timeout", type=int, default=240)
    parser.add_argument("--expect", choices=["nonpending", "complete", "landed"], default="complete",
                        help="nonpending: any state after Pending passes; complete: the mission must produce work; "
                             "landed: the mission must reach Complete (landed)")
    parser.add_argument("--working-directory", default=None, help="Vessel WorkingDirectory (the user's checkout)")
    parser.add_argument("--landing-mode", default=None, choices=["LocalMerge", "PullRequest", "MergeQueue", "None"],
                        help="Vessel LandingMode")
    parser.add_argument("--timings", action="store_true", help="Prefix each step with the elapsed seconds")
    args = parser.parse_args()

    global TIMINGS
    TIMINGS = args.timings

    api = Api(args.base_url)
    tag = uuid.uuid4().hex[:8]

    ok, detail = wait_healthy(api, args.health_timeout)
    if not step(ok, "Admiral healthy at " + api.base_url, detail):
        return 1

    # Dashboard pages are public HTML; the login happens in the browser against the same API checked below.
    # The React dashboard is recognized by its /dashboard/assets/ module script, which must load too; the
    # legacy embedded pages (served when no React build ships with the server) do not count as React.
    targets = [(api.base_url + "/dashboard", args.admiral_dashboard == "react")]
    targets += [(u, True) for u in args.dashboard_url]
    for url, require_react in targets:
        status, payload, content_type = api.request("GET", "", url=url, raw=True)
        text = payload.decode("utf-8", "replace")
        is_html = "<html" in text[:4096].lower() or "text/html" in content_type
        match = re.search(r'src="(/dashboard/assets/[^"]+\.js)"', text)
        kind = "react" if match else "legacy"
        detail = "HTTP " + str(status) + ", " + str(len(payload)) + " bytes, " + kind
        ok = status == 200 and is_html and (kind == "react" or not require_react)
        step(ok, "Dashboard page " + url, detail)
        if match:
            parts = urllib.parse.urlsplit(url)
            asset_url = parts.scheme + "://" + parts.netloc + match.group(1)
            status, asset, _ = api.request("GET", "", url=asset_url, raw=True)
            step(status == 200 and len(asset) > 1000, "Dashboard script " + asset_url, "HTTP " + str(status) + ", " + str(len(asset)) + " bytes")

    status, auth = api.request("POST", "/api/v1/authenticate",
                               {"TenantId": args.tenant, "Email": args.email, "Password": args.password})
    token = auth.get("Token") if isinstance(auth, dict) else None
    if not step(status == 200 and bool(token), "Log in as " + args.email, "HTTP " + str(status)):
        log("      response: " + json.dumps(auth)[:400])
        return 1
    api.token = token

    if isinstance(auth, dict) and auth.get("PasswordChangeRequired"):
        new_password = args.password + "-changed"
        status, body = api.request("PUT", "/api/v1/account/password",
                                   {"CurrentPassword": args.password, "NewPassword": new_password})
        step(status == 200, "Forced password change", "HTTP " + str(status))

    status, whoami = api.request("GET", "/api/v1/whoami")
    user = (whoami or {}).get("User") or {} if isinstance(whoami, dict) else {}
    step(status == 200 and user.get("Email") == args.email, "Session accepted by /api/v1/whoami", "HTTP " + str(status))
    if isinstance(whoami, dict) and whoami.get("PasswordChangeRequired"):
        step(False, "No password change pending after login")

    if args.api_via:
        status, via = api.request("GET", "", url=args.api_via.rstrip("/") + "/api/v1/whoami")
        step(status == 200, "Session accepted through " + args.api_via, "HTTP " + str(status))

    status, endpoint = api.request("POST", "/api/v1/model-endpoints", {
        "Name": "install-smoke-stub-" + tag,
        "Kind": "Inference",
        "Provider": "OpenAI",
        "BaseUrl": args.stub_url,
        "Model": "install-smoke-stub",
        "ApiKey": "install-smoke-no-key",
        "TimeoutMs": 30000,
    })
    endpoint_id = endpoint.get("Id") if isinstance(endpoint, dict) else None
    if not step(status in (200, 201) and bool(endpoint_id), "Register stub inference endpoint", "HTTP " + str(status)):
        log("      response: " + json.dumps(endpoint)[:400])
        return 1

    status, captain = api.request("POST", "/api/v1/captains", {
        "Name": "install-smoke-" + tag,
        "Runtime": "ApiEndpoint",
        "ModelEndpointId": endpoint_id,
        "Model": "install-smoke-stub",
    })
    captain_id = captain.get("Id") if isinstance(captain, dict) else None
    if not step(status in (200, 201) and bool(captain_id), "Create ApiEndpoint captain", "HTTP " + str(status)):
        log("      response: " + json.dumps(captain)[:400])
        return 1

    status, fleet = api.request("POST", "/api/v1/fleets", {"Name": "install-smoke-" + tag})
    fleet_id = fleet.get("Id") if isinstance(fleet, dict) else None
    if not step(status in (200, 201) and bool(fleet_id), "Create fleet", "HTTP " + str(status)):
        log("      response: " + json.dumps(fleet)[:400])
        return 1

    vessel_body = {
        "Name": "install-smoke-" + tag,
        "FleetId": fleet_id,
        "RepoUrl": args.repo_url,
        "DefaultBranch": args.default_branch,
    }
    if args.working_directory:
        vessel_body["WorkingDirectory"] = args.working_directory
    if args.landing_mode:
        vessel_body["LandingMode"] = args.landing_mode
    status, vessel = api.request("POST", "/api/v1/vessels", vessel_body)
    vessel_id = vessel.get("Id") if isinstance(vessel, dict) else None
    if not step(status in (200, 201) and bool(vessel_id), "Create vessel from " + args.repo_url, "HTTP " + str(status)):
        log("      response: " + json.dumps(vessel)[:400])
        return 1

    status, voyage = api.request("POST", "/api/v1/voyages", {
        "Title": "Install smoke " + tag,
        "Description": "Install verification: one mission driven by the stub inference endpoint.",
        "VesselId": vessel_id,
        "Missions": [{
            "Title": "Write INSTALL_SMOKE.md",
            "Description": "Create a file named INSTALL_SMOKE.md at the repository root with a one-line note.",
            "RequestedCaptainId": captain_id,
        }],
    })
    voyage_id = voyage.get("Id") if isinstance(voyage, dict) else None
    if not step(status in (200, 201) and bool(voyage_id), "Dispatch voyage with one mission", "HTTP " + str(status)):
        log("      response: " + json.dumps(voyage)[:600])
        return 1

    mission = None
    seen = []
    deadline = time.time() + args.mission_timeout
    while time.time() < deadline:
        status, page = api.request("GET", "/api/v1/missions?voyageId=" + voyage_id + "&pageSize=50")
        objects = page.get("Objects", []) if isinstance(page, dict) else (page if isinstance(page, list) else [])
        candidates = [m for m in objects if isinstance(m, dict) and m.get("VoyageId") == voyage_id]
        if candidates:
            mission = candidates[0]
            state = mission.get("Status")
            if not seen or seen[-1] != state:
                seen.append(state)
                log("      mission " + str(mission.get("Id")) + " -> " + str(state))
            if args.expect == "nonpending" and state not in PENDING:
                break
            if state in TERMINAL_STATES:
                break
        time.sleep(2)

    final_state = mission.get("Status") if mission else None
    path = " -> ".join(str(s) for s in seen) if seen else "no mission found"
    if args.expect == "nonpending":
        step(final_state is not None and final_state not in PENDING, "Mission left Pending", path)
    else:
        step(final_state is not None and final_state not in PENDING, "Mission left Pending", path)
        step(final_state in SUCCESS_STATES, "Mission produced work", path)
        if args.expect == "landed":
            step(final_state == "Complete", "Mission landed (Complete)", path)

    if mission:
        step(mission.get("CaptainId") == captain_id or final_state in PENDING,
             "Mission ran on the stub captain", "captain " + str(mission.get("CaptainId")))
        if final_state in SUCCESS_STATES:
            # The diff is captured as the mission finishes; give the capture a few seconds.
            diff_text = ""
            for _ in range(15):
                status, diff = api.request("GET", "/api/v1/missions/" + mission["Id"] + "/diff")
                diff_text = json.dumps(diff) if not isinstance(diff, str) else diff
                if status == 200:
                    break
                time.sleep(2)
            step(status == 200 and "INSTALL_SMOKE.md" in diff_text, "Mission diff contains INSTALL_SMOKE.md", "HTTP " + str(status))
        status, mission_log = api.request("GET", "/api/v1/missions/" + mission["Id"] + "/log?lines=40")
        if status == 200:
            if isinstance(mission_log, dict) and isinstance(mission_log.get("Log"), str):
                text = mission_log["Log"]
            else:
                text = mission_log if isinstance(mission_log, str) else json.dumps(mission_log)
            log("      mission log (tail):")
            lines = [l for l in text.splitlines() if l.strip() and not l.startswith("[ARMADA:TOOLEVENT]")]
            for line in lines[-8:]:
                log("        " + line[:200])
        if final_state not in SUCCESS_STATES and mission.get("FailureReason"):
            log("      failure reason: " + str(mission.get("FailureReason"))[:400])

    log("")
    if FAILURES:
        log("RESULT: FAIL (" + str(len(FAILURES)) + " step(s) failed: " + "; ".join(FAILURES) + ")")
        return 1
    log("RESULT: PASS (mission final state " + str(final_state) + ")")
    return 0


if __name__ == "__main__":
    sys.exit(main())
