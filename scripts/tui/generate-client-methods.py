#!/usr/bin/env python3
"""Emit C# method stubs for Armada.Client from the dashboard's api/client.ts.

Usage:
  python3 scripts/tui/generate-client-methods.py --out <dir>      # write ArmadaClient.<Area>.cs files into <dir>
  python3 scripts/tui/generate-client-methods.py --missing        # list exports with no C# method yet

The generated files are a starting point: the committed partial classes under src/Armada.Client were produced by this
script and then hand-finished (irregular exports are emitted as NotImplementedException stubs marked HAND-FINISH).
Re-run with --missing after dashboard changes to see which exports still need a C# method.
"""
import os
import re
import sys

sys.path.insert(0, os.path.dirname(__file__))
from client_exports import server_exports, pascal, ROOT  # noqa: E402

AREA_FILES = {
    "Auth": "Auth", "Tenants (admin)": "Tenants", "Users (admin)": "Users", "Credentials (admin)": "Credentials",
    "Fleets": "Fleets", "Vessels": "Vessels", "Workspace": "Workspace", "Workspace terminal": "Workspace",
    "Workspace diff (review)": "Workspace", "Request History": "RequestHistory", "History": "History",
    "Objectives": "Objectives", "Captains": "Captains", "Background jobs": "Jobs", "Vessel Health": "VesselHealth",
    "Missions": "Missions", "Voyages": "Voyages", "Planning Sessions": "PlanningSessions", "Events": "Events",
    "Merge Queue": "MergeQueue", "Prompt Templates": "PromptTemplates", "Playbooks": "Playbooks",
    "Workflow Profiles": "WorkflowProfiles", "Project profiles": "ProjectProfiles", "Skills directory": "Skills",
    "Harbors (host runners)": "Harbors", "Model endpoints (embedding/inference)": "ModelEndpoints",
    "Memories": "Memories", "Ask Armada": "Ask", "Ask Armada threads (docs/ASK_ARMADA_HOME_BASE.md)": "Ask",
    "Needs-you inbox": "Inbox", "Environments": "Environments", "Deployments": "Deployments",
    "Incidents": "Incidents", "Runbooks": "Runbooks", "Releases": "Releases", "Check Runs": "CheckRuns",
    "Personas": "Personas", "Pipelines": "Pipelines", "Docks": "Docks", "Signals": "Signals",
    "Status / Health": "Status", "Settings": "Server", "Server": "Server", "Backup / Restore": "Server",
    "Generic entity lookup": "Entities", "Vessel Import": "VesselImport", "Fleet Actions": "FleetActions",
}

TYPE_MAP = {
    "StatusSnapshot": "ArmadaStatus", "FleetActionRunStatus": "FleetActionRunStatusEnum",
    "FleetActionTargetStatus": "FleetActionTargetStatusEnum", "VesselHealthCriterion": "VesselHealthCriterionEnum",
    "VesselHealthStatus": "VesselHealthStatusEnum", "ScopeEnum": "ScopeEnum", "string": "string",
    "number": "int", "boolean": "bool", "unknown": "ArmadaRawJson", "any": "ArmadaRawJson",
    "Record<string, unknown>": "ArmadaRawJson",
}

TIMEOUTS = {"PLANNING_CREATE_TIMEOUT_MS": 300000, "PLANNING_SUMMARIZE_TIMEOUT_MS": 180000}


def split_top(text, sep=","):
    parts, depth, cur, quote = [], 0, "", None
    for ch in text:
        if quote:
            cur += ch
            if ch == quote:
                quote = None
            continue
        if ch in "'\"`":
            quote = ch
            cur += ch
            continue
        if ch in "([{<":
            depth += 1
        elif ch in ")]}>":
            depth -= 1
        if ch == sep and depth == 0:
            parts.append(cur.strip())
            cur = ""
        else:
            cur += ch
    if cur.strip():
        parts.append(cur.strip())
    return parts


def cs_type(ts):
    ts = ts.strip()
    if ts in TYPE_MAP:
        return TYPE_MAP[ts]
    m = re.match(r"^Partial<(\w+)>$", ts)
    if m:
        return cs_type(m.group(1))
    m = re.match(r"^(\w+)\[\]$", ts)
    if m:
        return "List<" + cs_type(m.group(1)) + ">"
    m = re.match(r"^EnumerationResult<(\w+)>$", ts)
    if m:
        return "EnumerationResult<" + cs_type(m.group(1)) + ">"
    if re.match(r"^\w+$", ts):
        return ts
    return None


def cs_param(p):
    p = p.strip()
    if p.startswith("opts?"):
        return None, None
    m = re.match(r"^params\?: \{ pageNumber\?: number; pageSize\?: number; filters\?: Record<string, string> \}$", p)
    if m:
        return "ArmadaPageQuery? query = null", "query"
    m = re.match(r"^(\w+) = (\d+)$", p)
    if m:
        return f"int {m.group(1)} = {m.group(2)}", m.group(1)
    m = re.match(r"^(\w+) = (true|false)$", p)
    if m:
        return f"bool {m.group(1)} = {m.group(2)}", m.group(1)
    m = re.match(r"^(\w+)(\?)?: (.+?)( = \{\})?$", p)
    if not m:
        return "/*UNPARSED " + p + "*/", None
    name, opt, ts, empty = m.group(1), m.group(2), m.group(3), m.group(4)
    if name == "params":
        name = "query"
    nullable = bool(opt) or bool(empty) or ts.endswith("| null")
    ts = ts.replace(" | null", "").strip()
    t = cs_type(ts)
    if t is None:
        return "/*UNPARSED " + p + "*/", None
    if nullable:
        if t == "bool":
            return f"bool {name} = false", name
        return f"{t}? {name} = null", name
    return f"{t} {name}", name


def cs_path(path):
    path = path.strip()
    if path.startswith("'"):
        return '"' + path.strip("'") + '"'
    if not path.startswith("`"):
        return None
    inner = path[1:-1]
    inner = inner.replace("${ASK}", "/api/v1/ask")
    inner = re.sub(r"\$\{askThreadPath\((\w+)\)\}", r"/api/v1/ask/threads/{E(\1)}", inner)
    inner = re.sub(r"\$\{encodeURIComponent\((\w+)\)\}", r"{E(\1)}", inner)
    inner = re.sub(r"\$\{buildQuery\(params\)\}", r"{ArmadaQueryString.FromPage(query)}", inner)
    inner = re.sub(r"\$\{build\w+Query\(params\)\}", r"{ArmadaQueryString.FromObject(query)}", inner)
    inner = re.sub(r"\$\{encodeWorkspaceQueryPath\((\w+)\)\}", r"{ArmadaQueryString.EscapePath(\1)}", inner)
    inner = re.sub(r"\$\{(lines|maxResults)\}", r"{\1}", inner)
    inner = re.sub(r"\$\{(\w+)\}", r"{E(\1)}", inner)
    if "${" in inner or "?" in inner and ":" in inner and "'" in inner:
        return None
    return '$"' + inner + '"'


def cs_body(arg, name):
    arg = arg.strip()
    if re.match(r"^\w+$", arg):
        return arg
    m = re.match(r"^(\w+) \|\| \{\}$", arg)
    if m:
        return m.group(1)
    if arg == "{}":
        return "null"
    m = re.match(r"^\{ (.+) \}$", arg)
    if m and "..." not in arg and "?" not in arg:
        fields = []
        for kv in split_top(m.group(1)):
            mm = re.match(r"^(\w+)(: (\w+))?$", kv.strip())
            if not mm:
                return None
            key = mm.group(1)
            val = mm.group(3) or mm.group(1)
            fields.append(f"{key[0].upper() + key[1:]} = {val}")
        return "new { " + ", ".join(fields) + " }"
    return None


def cs_opts(arg):
    m = re.search(r"timeout: ([\w\s\*]+)", arg)
    if not m:
        return "null"
    expr = m.group(1).strip()
    if expr in TIMEOUTS:
        return f"ArmadaRequestOptions.WithTimeout({TIMEOUTS[expr]})"
    try:
        return f"ArmadaRequestOptions.WithTimeout({int(eval(expr))})"
    except Exception:
        return None


def generate(area, name, kind, text):
    method = pascal(name) + "Async"
    hand = f"        // HAND-FINISH: {name}\n        public Task {method}(CancellationToken token = default)\n        {{\n            throw new NotImplementedException(\"{name}\");\n        }}\n"
    if kind != "const":
        return hand
    m = re.match(r"^export const \w+ = \((.*?)\) =>\s*(.*);\s*$", text, re.S)
    if not m:
        return hand
    params_text, body = m.group(1), " ".join(m.group(2).split())
    mm = re.match(r"^(get|post|put|del)<(.+?)>\((.*)\)$", body)
    if not mm:
        return hand
    verb, ret_ts, args_text = mm.group(1), mm.group(2), mm.group(3)
    args = split_top(args_text)
    cs_params, names = [], []
    for p in split_top(params_text):
        sig, pname = cs_param(p)
        if sig is None:
            continue
        if "UNPARSED" in sig:
            return hand
        cs_params.append(sig)
        names.append(pname)
    path = cs_path(args[0])
    if path is None:
        return hand
    body_arg, opts_arg = None, "null"
    if verb in ("post", "put"):
        if len(args) > 1:
            body_arg = cs_body(args[1], name)
            if body_arg is None:
                return hand
        else:
            body_arg = "null"
        if len(args) > 2:
            opts_arg = cs_opts(args[2])
    else:
        if len(args) > 1:
            opts_arg = cs_opts(args[1])
    if opts_arg is None:
        return hand
    ret = None if ret_ts.strip() == "void" else cs_type(ret_ts)
    if ret_ts.strip() != "void" and ret is None:
        return hand
    sig = ", ".join(cs_params + ["CancellationToken token = default"])
    http = {"get": "Get", "post": "Post", "put": "Put", "del": "Delete"}[verb]
    doc_path = args[0].strip().replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")
    lines = [f"        /// <summary>", f"        /// Dashboard <c>{name}</c>: {verb.upper()} {doc_path}.", f"        /// </summary>"]
    for pname in names:
        lines.append(f"        /// <param name=\"{pname}\">{pname}.</param>")
    lines.append("        /// <param name=\"token\">Cancellation token.</param>")
    if ret:
        lines.append(f"        /// <returns>The response.</returns>")
    lines.append("        /// <exception cref=\"ArmadaApiException\">Thrown for a non-success response, timeout, or transport failure.</exception>")
    if ret is None:
        call_method = {"get": "HttpMethod.Get", "post": "HttpMethod.Post", "put": "HttpMethod.Put", "del": "HttpMethod.Delete"}[verb]
        b = body_arg if body_arg is not None else "null"
        lines.append(f"        public Task {method}({sig})")
        lines.append("        {")
        lines.append(f"            return SendNoResultAsync({call_method}, {path}, {b}, {opts_arg}, token);")
        lines.append("        }")
    else:
        lines.append(f"        public Task<{ret}?> {method}({sig})")
        lines.append("        {")
        if verb in ("post", "put"):
            lines.append(f"            return {http}Async<{ret}>({path}, {body_arg}, {opts_arg}, token);")
        else:
            lines.append(f"            return {http}Async<{ret}>({path}, {opts_arg}, token);")
        lines.append("        }")
    return "\n".join(lines) + "\n"


HEADER = """namespace Armada.Client
{{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client.Http;
    using Armada.Client.Models;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// {area} API calls (dashboard <c>api/client.ts</c> parity).
    /// </summary>
    public partial class ArmadaClient
    {{
        #region Public-Methods

"""
FOOTER = """        #endregion
    }
}
"""


def main():
    args = sys.argv[1:]
    exports = server_exports()
    if "--missing" in args:
        client_dir = os.path.join(ROOT, "src", "Armada.Client")
        source = ""
        for fname in os.listdir(client_dir):
            if fname.startswith("ArmadaClient") and fname.endswith(".cs"):
                with open(os.path.join(client_dir, fname), encoding="utf-8") as f:
                    source += f.read()
        missing = [n for (_, n, _, _) in exports if (" " + pascal(n) + "Async(") not in source]
        for n in missing:
            print(n)
        print(f"{len(missing)} of {len(exports)} exports have no C# method")
        return 1 if missing else 0
    out = args[args.index("--out") + 1] if "--out" in args else "."
    files = {}
    for area, name, kind, text in exports:
        files.setdefault(AREA_FILES.get(area, "Misc"), []).append(generate(area, name, kind, text))
    os.makedirs(out, exist_ok=True)
    hand = 0
    for fname, methods in files.items():
        body = "\n".join(methods)
        hand += body.count("HAND-FINISH")
        with open(os.path.join(out, f"ArmadaClient.{fname}.cs"), "w", encoding="utf-8") as f:
            f.write(HEADER.format(area=fname) + body + "\n" + FOOTER)
    print(f"wrote {len(files)} files, {len(exports)} methods, {hand} need hand-finishing")
    return 0


if __name__ == "__main__":
    sys.exit(main())
