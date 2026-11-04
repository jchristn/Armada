#!/usr/bin/env python3
"""Deterministic Custom usage capture for the admission benchmark (Custom usage contract, schema version 1).

Usage: usage.py --account <name> --used <percent> [--used-file <path>]

--used-file, when the file exists, overrides --used with the number it contains, so a scenario can lower usage
(for example to show a window reset) without changing the capture command and therefore the account identity.

Prints one JSON object with a fixed five_hour window at the given utilization, resetting three hours from now.
The Admiral re-keys the account identity on its host; this stub never sees or prints a credential. Standard library only.
"""
import argparse
import datetime
import json
import sys


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--account", required=True)
    parser.add_argument("--used", required=True, type=float)
    parser.add_argument("--used-file")
    args = parser.parse_args()
    used = args.used
    if args.used_file:
        try:
            with open(args.used_file) as fh:
                used = float(fh.read().strip())
        except (OSError, ValueError):
            pass
    now = datetime.datetime.now(datetime.timezone.utc).replace(microsecond=0)
    reset = now + datetime.timedelta(hours=3)
    print(json.dumps({
        "schemaVersion": 1,
        "status": "Ok",
        "accountKey": args.account,
        "provider": "anthropic",
        "capturedUtc": now.isoformat().replace("+00:00", "Z"),
        "windows": [{
            "name": "five_hour", "usedPercent": used, "unit": "percent", "windowMinutes": 300,
            "resetsUtc": reset.isoformat().replace("+00:00", "Z"), "resetKind": "Fixed", "scope": "account"
        }]
    }))
    return 0


if __name__ == "__main__":
    sys.exit(main())
