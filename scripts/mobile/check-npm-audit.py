#!/usr/bin/env python3
"""npm audit for src/Armada.Mobile with a reviewed allowlist (security review F-55).

The dashboard job runs `npm audit --audit-level=high` and must be clean. The mobile app cannot be: Expo and React
Native pull build-time tooling (the Expo CLI's code signing, Metro and Jest file watching) whose current releases
still depend on packages with open high advisories and no fixed version. Those advisories never reach the app
bundle. This check runs `npm audit --json` and fails on any advisory of the given level or higher (default high)
that is not in src/Armada.Mobile/audit-allowlist.json, where every entry names the advisory, the package, and why it
does not affect the shipped app. Allowlist entries that npm no longer reports are printed so they can be removed.

  python3 scripts/mobile/check-npm-audit.py                 # from anywhere
  python3 scripts/mobile/check-npm-audit.py --level moderate
"""
import argparse
import json
import os
import subprocess
import sys

LEVELS = ["info", "low", "moderate", "high", "critical"]
MOBILE_DIR = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "src", "Armada.Mobile"))


def advisory_id(url):
    return url.rstrip("/").rsplit("/", 1)[-1] if url else ""


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--level", default="high", choices=LEVELS)
    parser.add_argument("--allowlist", default=os.path.join(MOBILE_DIR, "audit-allowlist.json"))
    args = parser.parse_args()

    with open(args.allowlist, encoding="utf-8") as handle:
        allow = {entry["id"]: entry for entry in json.load(handle)["advisories"]}
    for entry in allow.values():
        if not entry.get("package") or not entry.get("reason"):
            print("allowlist entry " + entry["id"] + " needs a package and a reason", file=sys.stderr)
            return 2

    npm = "npm.cmd" if os.name == "nt" else "npm"
    result = subprocess.run([npm, "audit", "--json"], cwd=MOBILE_DIR, capture_output=True, text=True)
    try:
        report = json.loads(result.stdout)
    except ValueError:
        print("npm audit did not return JSON:\n" + result.stdout + result.stderr, file=sys.stderr)
        return 2
    if "vulnerabilities" not in report:
        print("npm audit failed: " + json.dumps(report.get("error", report))[:2000], file=sys.stderr)
        return 2

    threshold = LEVELS.index(args.level)
    seen = {}
    for name, vuln in report["vulnerabilities"].items():
        for via in vuln.get("via", []):
            if isinstance(via, dict):
                seen[advisory_id(via.get("url"))] = (via.get("name", name), via.get("severity", "info"), via.get("title", ""))

    failures = []
    for ident, (package, severity, title) in sorted(seen.items()):
        if LEVELS.index(severity) < threshold:
            continue
        status = "allowed" if ident in allow else "NOT ALLOWED"
        print("%-12s %-9s %-24s %s %s" % (status, severity, package, ident, title))
        if ident not in allow:
            failures.append(ident)

    for ident in sorted(set(allow) - set(seen)):
        print("stale allowlist entry (no longer reported, remove it): " + ident)

    if failures:
        print("\n%d advisory(ies) at or above %s are not in %s." % (len(failures), args.level, os.path.relpath(args.allowlist)), file=sys.stderr)
        return 1
    print("npm audit (mobile): no advisories at or above %s outside the allowlist." % args.level)
    return 0


if __name__ == "__main__":
    sys.exit(main())
