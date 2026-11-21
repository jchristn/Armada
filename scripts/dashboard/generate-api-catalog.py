#!/usr/bin/env python3
"""Write the dashboard's built-in REST catalog (src/Armada.Dashboard/src/lib/apiCatalog.generated.json) from the frozen
public API surface (docs/api-surface-1.0.json): each route's method, path, summary, first tag and request type. The API
Explorer lists these at once and works without the server's /openapi.json; when that document loads, its schemas and
parameters replace the catalog. Run after scripts/<os>/generate-api-surface.sh; --check fails when the catalog is stale.
"""
import json
import os
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SURFACE = os.path.join(ROOT, "docs", "api-surface-1.0.json")
CATALOG = os.path.join(ROOT, "src", "Armada.Dashboard", "src", "lib", "apiCatalog.generated.json")


def build():
    with open(SURFACE, encoding="utf-8") as f:
        surface = json.load(f)
    routes = []
    for entry in surface["rest"]:
        routes.append({
            "method": entry["method"].lower(),
            "path": entry["route"],
            "summary": entry.get("summary") or "",
            "tag": (entry.get("tags") or ["General"])[0],
            "requestType": entry.get("requestType") or "",
            "auth": entry.get("auth") or "",
        })
    routes.sort(key=lambda r: (r["tag"], r["path"], r["method"]))
    return json.dumps({"contractVersion": surface.get("contractVersion", ""), "routes": routes}, indent=1, ensure_ascii=False) + "\n"


def main():
    text = build()
    if "--check" in sys.argv:
        current = open(CATALOG, encoding="utf-8").read() if os.path.exists(CATALOG) else ""
        if current != text:
            print("api catalog: src/Armada.Dashboard/src/lib/apiCatalog.generated.json is out of date; run python3 scripts/dashboard/generate-api-catalog.py")
            return 1
        print("api catalog is current")
        return 0
    with open(CATALOG, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)
    print("wrote " + os.path.relpath(CATALOG, ROOT))
    return 0


if __name__ == "__main__":
    sys.exit(main())
