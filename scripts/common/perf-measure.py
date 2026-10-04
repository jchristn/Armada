#!/usr/bin/env python3
"""Measure Armada REST endpoint latency for the performance baseline.

Usage: perf-measure.py <base-url> <bearer-token> <runs> <warmup> <output.tsv>

Each endpoint gets <warmup> unmeasured requests, then <runs> measured requests over one persistent HTTP connection
(the dashboard's browser also reuses connections). Prints a Markdown table and writes name, method, path, p50, p95,
max (ms), response bytes, and HTTP status as TSV. Exits non-zero if any endpoint returned a non-2xx status.
"""
import http.client
import json
import statistics
import sys
import time
from urllib.parse import urlparse

ENDPOINTS = [
    # name, method, path, body (dict or None)
    ("status", "GET", "/api/v1/status", None),
    ("missions first page", "GET", "/api/v1/missions?pageSize=25", None),
    ("missions deep page (350)", "GET", "/api/v1/missions?pageSize=25&pageNumber=350", None),
    ("missions filtered (status)", "GET", "/api/v1/missions?pageSize=25&status=WorkProduced", None),
    ("mission summaries (dashboard)", "GET", "/api/v1/missions/summaries?pageSize=10", None),
    ("voyages first page", "GET", "/api/v1/voyages?pageSize=25", None),
    ("voyages deep page (35)", "GET", "/api/v1/voyages?pageSize=25&pageNumber=35", None),
    ("vessels first page", "GET", "/api/v1/vessels?pageSize=25", None),
    ("vessels deep page (18)", "GET", "/api/v1/vessels?pageSize=25&pageNumber=18", None),
    ("vessels all (dashboard, 9999)", "GET", "/api/v1/vessels?pageSize=9999", None),
    ("captains all (dashboard, 9999)", "GET", "/api/v1/captains?pageSize=9999", None),
    ("vessel health first page", "POST", "/api/v1/vessel-health/enumerate", {"PageNumber": 1, "PageSize": 25}),
    ("vessel health deep page (18)", "POST", "/api/v1/vessel-health/enumerate", {"PageNumber": 18, "PageSize": 25}),
    ("vessel health summary", "GET", "/api/v1/vessel-health/summary", None),
    ("jobs (unpaged list)", "GET", "/api/v1/jobs", None),
    ("jobs active (header poll)", "GET", "/api/v1/jobs?status=Queued,Running&pageSize=50", None),
    ("ask threads first page", "POST", "/api/v1/ask/threads/enumerate", {"PageNumber": 1, "PageSize": 25}),
    ("fleet action runs count", "POST", "/api/v1/fleet-action-runs/enumerate", {"PageNumber": 1, "PageSize": 1, "Status": "Running"}),
]


def percentile(values, pct):
    ordered = sorted(values)
    if not ordered:
        return 0.0
    k = (len(ordered) - 1) * pct / 100.0
    lo = int(k)
    hi = min(lo + 1, len(ordered) - 1)
    return ordered[lo] + (ordered[hi] - ordered[lo]) * (k - lo)


def main():
    if len(sys.argv) != 6:
        print(__doc__, file=sys.stderr)
        return 2
    base, token, runs, warmup, output = sys.argv[1], sys.argv[2], int(sys.argv[3]), int(sys.argv[4]), sys.argv[5]
    url = urlparse(base)
    conn = http.client.HTTPConnection(url.hostname, url.port, timeout=120)
    headers = {"Authorization": "Bearer " + token, "Content-Type": "application/json", "Connection": "keep-alive"}
    rows = []
    failed = False
    for name, method, path, body in ENDPOINTS:
        payload = json.dumps(body) if body is not None else None
        timings = []
        size = 0
        status = 0
        for i in range(warmup + runs):
            start = time.perf_counter()
            conn.request(method, path, body=payload, headers=headers)
            resp = conn.getresponse()
            data = resp.read()
            elapsed = (time.perf_counter() - start) * 1000.0
            status = resp.status
            size = len(data)
            if i >= warmup:
                timings.append(elapsed)
        if status < 200 or status >= 300:
            failed = True
        rows.append((name, method, path, percentile(timings, 50), percentile(timings, 95), max(timings), size, status))

    with open(output, "w") as fh:
        fh.write("name\tmethod\tpath\tp50_ms\tp95_ms\tmax_ms\tbytes\tstatus\n")
        for r in rows:
            fh.write("%s\t%s\t%s\t%.1f\t%.1f\t%.1f\t%d\t%d\n" % r)

    print("| Endpoint | Request | p50 (ms) | p95 (ms) | max (ms) | Response bytes | HTTP |")
    print("|---|---|---:|---:|---:|---:|---:|")
    for r in rows:
        print("| %s | `%s %s` | %.1f | %.1f | %.1f | %d | %d |" % r)
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
