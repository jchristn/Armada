#!/usr/bin/env python3
"""Stub Claude Code CLI for the mobile Ask follow flow (src/Armada.Mobile/e2e/ask-follow).

scripts/mobile/run-e2e.sh --ask-follow puts it first on the throwaway Admiral's PATH as "claude", so a ClaudeCode
captain answers Ask messages without a model. It reads the prompt on stdin (as Claude Code does in --print mode),
ignores its arguments, and writes stream-json the way `claude --print --output-format stream-json
--include-partial-messages` does: when the user's message contains "stream", the reply streamed a few words at a time
(text_delta events), then the assistant message and the result; otherwise only the assistant message and the result,
so the whole reply reaches the app at once (as an API-endpoint captain's reply does). The reply is long enough to
overflow a phone screen, names the user's last message, and ends with "End of reply to <message>." so a flow can wait
for the end of a specific reply. `claude --version` prints a version.

Environment: ARMADA_STUB_CLAUDE_DELAY_MS, the pause between streamed pieces (default 150), and
ARMADA_STUB_CLAUDE_THINK_MS, the pause before the reply starts (default 1500; a real captain never answers within the
round trip of the request that started its turn, and the app is not built for one that does).
Standard library only (Python 3.8+).
"""

import json
import os
import sys
import time
import uuid

FILLER = (
    "The fleet is healthy and every vessel reports a clean working tree. "
    "No missions are waiting for a captain, and the merge queue is empty. "
    "Recent deployments finished without errors, and the last health check passed on every repository."
)


def emit(event):
    sys.stdout.write(json.dumps(event) + "\n")
    sys.stdout.flush()


def last_user_text(prompt):
    # The Admiral sends the conversation as speaker-labelled lines ("User: ..."); the flows send one short line.
    for line in reversed(prompt.splitlines()):
        line = line.strip()
        if line.startswith("User:"):
            return line[len("User:"):].strip() or "your message"
    lines = [line.strip() for line in prompt.splitlines() if line.strip()]
    return lines[-1] if lines else "your message"


def reply_for(user_text):
    paragraphs = ["Reply to: " + user_text + "."]
    for index in range(4):
        paragraphs.append("Paragraph " + str(index + 1) + ". " + FILLER)
    paragraphs.append("End of reply to " + user_text + ".")
    return "\n\n".join(paragraphs)


def pieces(text, words_per_piece=3):
    words = text.split(" ")
    for start in range(0, len(words), words_per_piece):
        piece = " ".join(words[start:start + words_per_piece])
        yield piece if start + words_per_piece >= len(words) else piece + " "


def main():
    if "--version" in sys.argv[1:]:
        print("2.0.0 (Claude Code)")
        return 0
    delay = max(0, int(os.environ.get("ARMADA_STUB_CLAUDE_DELAY_MS", "150"))) / 1000.0
    think = max(0, int(os.environ.get("ARMADA_STUB_CLAUDE_THINK_MS", "1500"))) / 1000.0
    prompt = sys.stdin.read()
    user_text = last_user_text(prompt)
    text = reply_for(user_text)
    streamed = "stream" in user_text.lower()
    session = str(uuid.uuid4())
    started = time.time()
    emit({"type": "system", "subtype": "init", "session_id": session, "model": "stub", "tools": []})
    time.sleep(think)
    if streamed:
        emit({"type": "stream_event", "session_id": session, "event": {"type": "message_start", "message": {"role": "assistant"}}})
        emit({"type": "stream_event", "session_id": session,
              "event": {"type": "content_block_start", "index": 0, "content_block": {"type": "text", "text": ""}}})
        for piece in pieces(text):
            emit({"type": "stream_event", "session_id": session,
                  "event": {"type": "content_block_delta", "index": 0, "delta": {"type": "text_delta", "text": piece}}})
            time.sleep(delay)
        emit({"type": "stream_event", "session_id": session, "event": {"type": "content_block_stop", "index": 0}})
    emit({"type": "assistant", "session_id": session,
          "message": {"role": "assistant", "content": [{"type": "text", "text": text}]}})
    elapsed = int((time.time() - started) * 1000)
    emit({"type": "result", "subtype": "success", "is_error": False, "session_id": session, "result": text,
          "duration_ms": elapsed, "num_turns": 1, "total_cost_usd": 0,
          "usage": {"input_tokens": 10, "output_tokens": 10}})
    return 0


if __name__ == "__main__":
    sys.exit(main())
