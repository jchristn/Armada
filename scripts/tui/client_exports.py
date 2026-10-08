#!/usr/bin/env python3
"""Shared parser for src/Armada.Dashboard/src/api/client.ts.

Lists every exported server-calling function with its parameter list, verb, path template, generic type, and the
section ("area") header it lives under. Used by generate-client-methods.py and generate-parity-manifest.py.
"""
import re
import os

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
CLIENT_TS = os.path.join(ROOT, "src", "Armada.Dashboard", "src", "api", "client.ts")

# Exports of client.ts that do not call the server.
NON_SERVER_EXPORTS = {
    "ApiError", "apiErrorCode", "apiErrorMessage", "isApiStatus", "setAuthToken", "setOnUnauthorized", "camelizeKeys", "encodeBrowsePath",
    "configureClient", "getClientBaseUrl",
}


def read_source():
    with open(CLIENT_TS, "r", encoding="utf-8") as f:
        return f.read()


def split_statements(src):
    """Yield (area, name, kind, text) for each export const/function, text being the full statement."""
    area = "General"
    i = 0
    lines = src.split("\n")
    n = len(lines)
    out = []
    while i < n:
        line = lines[i]
        m_area = re.match(r"^// =+ (.+?) =+\s*$", line)
        if m_area:
            area = m_area.group(1).strip()
        m_area2 = re.match(r"^// (Background jobs|Project profiles|Skills directory|Harbors.*|Model endpoints.*|Ask Armada|Needs-you inbox|Workspace terminal|Workspace diff.*)$", line)
        if m_area2:
            area = m_area2.group(1).strip()
        if line.startswith("// Vessel Import"):
            area = "Vessel Import"
        if line.startswith("// Fleet Actions"):
            area = "Fleet Actions"
        m = re.match(r"^export (const|async function|function|class|interface) (\w+)", line)
        if m:
            kind, name = m.group(1), m.group(2)
            # collect until statement end: for const, until a line ending with ';' at depth 0; for functions, until
            # the closing brace at column 0.
            buf = [line]
            j = i
            if kind in ("async function", "function", "class", "interface"):
                if not line.rstrip().endswith("}"):
                    j = i + 1
                    while j < n and not lines[j].startswith("}"):
                        buf.append(lines[j])
                        j += 1
                    if j < n:
                        buf.append(lines[j])
            else:
                depth = 0
                k = i
                while k < n:
                    l = lines[k]
                    depth += l.count("(") + l.count("{") + l.count("[") - l.count(")") - l.count("}") - l.count("]")
                    if k > i:
                        buf.append(l)
                    if depth <= 0 and l.rstrip().endswith(";"):
                        break
                    k += 1
                j = k
            out.append((area, name, kind, "\n".join(buf)))
            i = j + 1
            continue
        i += 1
    return out


def server_exports():
    """Every exported function that calls the server, in source order."""
    result = []
    for area, name, kind, text in split_statements(read_source()):
        if kind in ("class", "interface"):
            continue
        if name in NON_SERVER_EXPORTS:
            continue
        result.append((area, name, kind, text))
    return result


def pascal(name):
    return name[0].upper() + name[1:]


if __name__ == "__main__":
    exports = server_exports()
    for area, name, kind, text in exports:
        print(f"{area}\t{name}")
    print(len(exports), "server-calling exports")
