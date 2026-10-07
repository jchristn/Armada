#!/usr/bin/env python3
"""Seed a throwaway Admiral with Operations data for the mobile E2E flows (run by scripts/mobile/run-e2e.sh).

Creates, as the default admin (admin@armada / password on the default tenant): a local bare git repository with
one commit under the data directory, a fleet "E2E Fleet", a vessel "e2e-repo" on that repository, and a voyage
"Seeded voyage" (landing mode None) with two missions. There are no captains, so the missions stay Pending and
nothing runs. Standard library only.

  python3 scripts/mobile/seed-e2e.py --url http://127.0.0.1:44010 --data-dir /tmp/armada-mobile-e2e.XXXX
"""
import argparse
import json
import os
import subprocess
import sys
import urllib.request


def call(url, method, path, token=None, body=None):
    data = json.dumps(body).encode("utf-8") if body is not None else None
    req = urllib.request.Request(url + path, data=data, method=method)
    req.add_header("Content-Type", "application/json")
    if token:
        req.add_header("X-Token", token)
    with urllib.request.urlopen(req, timeout=30) as resp:
        text = resp.read().decode("utf-8")
        return json.loads(text) if text else None


def git(*args, cwd=None):
    subprocess.run(["git", *args], cwd=cwd, check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


def make_repo(data_dir):
    seed = os.path.join(data_dir, "seed")
    bare = os.path.join(seed, "e2e-repo.git")
    work = os.path.join(seed, "work")
    if os.path.isdir(bare):
        return bare
    os.makedirs(seed, exist_ok=True)
    git("init", "-q", "--bare", "-b", "main", bare)
    git("clone", "-q", bare, work)
    with open(os.path.join(work, "README.md"), "w", encoding="utf-8") as f:
        f.write("# e2e-repo\n\nSeeded for the Armada mobile E2E flows.\n")
    git("add", ".", cwd=work)
    git("-c", "user.email=e2e@armada", "-c", "user.name=Armada E2E", "commit", "-qm", "Initial commit", cwd=work)
    git("push", "-q", "origin", "HEAD:main", cwd=work)
    return bare


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--url", required=True, help="Admiral base URL")
    parser.add_argument("--data-dir", required=True, help="the throwaway Admiral's data directory")
    args = parser.parse_args()
    url = args.url.rstrip("/")

    auth = call(url, "POST", "/api/v1/authenticate", body={"Email": "admin@armada", "Password": "password", "TenantId": "default"})
    token = auth.get("Token") if auth else None
    if not token:
        print("seed: sign-in failed", file=sys.stderr)
        return 1

    repo = make_repo(args.data_dir)
    fleet = call(url, "POST", "/api/v1/fleets", token, {"Name": "E2E Fleet", "Description": "Seeded for the mobile E2E flows"})
    vessel = call(url, "POST", "/api/v1/vessels", token, {
        "Name": "e2e-repo", "FleetId": fleet["Id"], "RepoUrl": repo, "DefaultBranch": "main",
    })
    voyage = call(url, "POST", "/api/v1/voyages", token, {
        "Title": "Seeded voyage",
        "Description": "Seeded for the mobile E2E flows",
        "VesselId": vessel["Id"],
        "LandingMode": "None",
        "Missions": [
            {"Title": "Seeded mission one", "Description": "First seeded mission (no captain will run it)."},
            {"Title": "Seeded mission two", "Description": "Second seeded mission (no captain will run it)."},
        ],
    })
    print(f"seed: fleet {fleet['Id']}, vessel {vessel['Id']}, voyage {voyage['Id']}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
