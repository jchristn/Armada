#!/usr/bin/env python3
"""Stub OpenAI-compatible inference server for the install verification tests.

It lets an ApiEndpoint captain run a real mission without a model or an API key. The reply depends
on how many tool results the conversation already carries: none -> call write_file INSTALL_SMOKE.md;
one -> call run_process to git add and commit it; two or more -> a final message with no tool calls,
which ends the captain loop. Any POST whose path ends in /chat/completions is answered, with or without
"stream": true. GET .../models returns one model so endpoint probes succeed.

Usage: stub_inference.py [--host 127.0.0.1] [--port 34050]
Standard library only (Python 3.8+).
"""

import argparse
import json
import sys
import time
import uuid
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

MODEL = "install-smoke-stub"
FILE_NAME = "INSTALL_SMOKE.md"
FILE_CONTENT = "# Install smoke test\n\nWritten by the Armada install verification stub captain.\n"
FINAL_TEXT = "Created and committed " + FILE_NAME + ". Install smoke mission complete."
COMMIT_COMMAND = ("git add " + FILE_NAME + " && git -c user.name=\"Armada Install Verify\" "
                  "-c user.email=install-verify@armada.invalid commit -m \"Add " + FILE_NAME + "\"")


def _tool_results(messages):
    count = 0
    for message in messages or []:
        if isinstance(message, dict) and message.get("role") == "tool":
            count += 1
    return count


def _offered_tools(tools):
    names = set()
    for tool in tools or []:
        function = tool.get("function", {}) if isinstance(tool, dict) else {}
        if function.get("name"):
            names.add(function["name"])
    return names


class Handler(BaseHTTPRequestHandler):
    server_version = "ArmadaInstallStub/1.0"

    def log_message(self, fmt, *args):
        sys.stderr.write("[stub] " + (fmt % args) + "\n")
        sys.stderr.flush()

    def _send_json(self, status, payload):
        body = json.dumps(payload).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        path = self.path.split("?", 1)[0].rstrip("/")
        if path.endswith("/models") or path == "/models":
            self._send_json(200, {"object": "list", "data": [{"id": MODEL, "object": "model", "owned_by": "armada"}]})
        elif path in ("", "/health", "/v1"):
            self._send_json(200, {"status": "ok"})
        else:
            self._send_json(404, {"error": {"message": "not found: " + self.path}})

    def do_POST(self):
        length = int(self.headers.get("Content-Length") or 0)
        raw = self.rfile.read(length) if length > 0 else b"{}"
        try:
            request = json.loads(raw.decode("utf-8") or "{}")
        except ValueError:
            request = {}

        path = self.path.split("?", 1)[0].rstrip("/")
        if not path.endswith("/chat/completions"):
            self._send_json(404, {"error": {"message": "not found: " + self.path}})
            return

        messages = request.get("messages", [])
        done = _tool_results(messages)
        offered = _offered_tools(request.get("tools"))
        call = None
        if done == 0 and "write_file" in offered:
            call = ("write_file", {"file_path": FILE_NAME, "content": FILE_CONTENT})
        elif done == 1 and "run_process" in offered:
            # "&&" works in both /bin/sh and cmd.exe; the identity flags keep a bare machine from failing the commit.
            call = ("run_process", {"command": COMMIT_COMMAND, "timeout_ms": 60000})

        if call is not None:
            message = {
                "role": "assistant",
                "content": None,
                "tool_calls": [{
                    "id": "call_" + uuid.uuid4().hex[:12],
                    "type": "function",
                    "function": {"name": call[0], "arguments": json.dumps(call[1])},
                }],
            }
            finish = "tool_calls"
        else:
            message = {"role": "assistant", "content": FINAL_TEXT}
            finish = "stop"

        created = int(time.time())
        completion_id = "chatcmpl-" + uuid.uuid4().hex[:16]
        usage = {"prompt_tokens": 10, "completion_tokens": 10, "total_tokens": 20}

        if request.get("stream"):
            self.send_response(200)
            self.send_header("Content-Type", "text/event-stream")
            self.send_header("Cache-Control", "no-cache")
            self.end_headers()
            delta = {"role": "assistant"}
            if message.get("content"):
                delta["content"] = message["content"]
            if message.get("tool_calls"):
                delta["tool_calls"] = [dict(call, index=i) for i, call in enumerate(message["tool_calls"])]
            chunks = [
                {"id": completion_id, "object": "chat.completion.chunk", "created": created, "model": MODEL,
                 "choices": [{"index": 0, "delta": delta, "finish_reason": None}]},
                {"id": completion_id, "object": "chat.completion.chunk", "created": created, "model": MODEL,
                 "choices": [{"index": 0, "delta": {}, "finish_reason": finish}], "usage": usage},
            ]
            for chunk in chunks:
                self.wfile.write(("data: " + json.dumps(chunk) + "\n\n").encode("utf-8"))
            self.wfile.write(b"data: [DONE]\n\n")
            self.wfile.flush()
            return

        self._send_json(200, {
            "id": completion_id,
            "object": "chat.completion",
            "created": created,
            "model": MODEL,
            "choices": [{"index": 0, "message": message, "finish_reason": finish}],
            "usage": usage,
        })


def main():
    parser = argparse.ArgumentParser(description="Stub OpenAI-compatible inference server")
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=34050)
    args = parser.parse_args()
    httpd = ThreadingHTTPServer((args.host, args.port), Handler)
    sys.stderr.write("[stub] listening on http://%s:%d\n" % (args.host, args.port))
    sys.stderr.flush()
    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        httpd.server_close()


if __name__ == "__main__":
    main()
