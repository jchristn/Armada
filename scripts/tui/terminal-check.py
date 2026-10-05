#!/usr/bin/env python3
"""Scripted terminal checks for `armada tui` (TUI_APP_PLAN.md W8.4).

Runs the real TUI (Armada.Helm.dll) in a pseudo-terminal against a running throwaway Admiral, feeds the output to a
VT100 emulator (pyte), and checks what a person would see: Unicode or ASCII borders, the terminal-too-small screen,
resize handling, high contrast under NO_COLOR, key handling (go-to keys, palette, help, quit), and that the terminal
is restored on exit. With --tmux it also drives the TUI inside tmux (TERM=screen-256color) through tmux itself
(send-keys, capture-pane). Frames are written to --out for review.

Usage (normally through scripts/tui/terminal-check.sh, which starts the server and the Linux container):

  python3 scripts/tui/terminal-check.py --helm src/Armada.Helm/bin/Debug/net10.0/Armada.Helm.dll \
      --server http://127.0.0.1:39110 --token <admin api key> --out /tmp/frames [--platform macos] [--tmux]

Never touches ~/.armada: preferences, credentials (file store), and the data directory go to a temp directory.
Exit code 0 when every case passed.
"""
import argparse
import fcntl
import json
import os
import pty
import select
import shutil
import signal
import struct
import subprocess
import sys
import tempfile
import termios
import time

import pyte

ASCII_BORDERS = set("+-|")
BOX = set("\u2500\u2502\u250c\u2510\u2514\u2518\u2501\u2503\u250f\u2513\u2517\u251b")


class Tui:
    """The TUI in a pseudo-terminal, rendered through pyte."""

    def __init__(self, args, env_overrides, cols, rows, workdir):
        self.cols, self.rows = cols, rows
        self.screen = pyte.Screen(cols, rows)
        self.stream = pyte.ByteStream(self.screen)
        self.raw = bytearray()
        env = {k: v for k, v in os.environ.items() if not k.startswith("ARMADA_") and k not in ("NO_COLOR", "TERM_PROGRAM", "COLORFGBG")}
        env.update({
            "ARMADA_DATA_DIR": os.path.join(workdir, "data"),
            "ARMADA_TUI_PREFERENCES": os.path.join(workdir, "tui.json"),
            "ARMADA_TUI_CREDENTIAL_STORE": "file",
            "ARMADA_TUI_CREDENTIALS": os.path.join(workdir, "tui-credentials.json"),
            "ARMADA_TOKEN": args.token,
            "DOTNET_NOLOGO": "1",
        })
        for k, v in env_overrides.items():
            if v is None:
                env.pop(k, None)
            else:
                env[k] = v
        self.pid, self.fd = pty.fork()
        if self.pid == 0:
            os.execvpe("dotnet", ["dotnet", args.helm, "tui", "--server", args.server, "--route", "/missions"], env)
        self.set_size(cols, rows, signal_child=False)
        self.exit_code = None

    def set_size(self, cols, rows, signal_child=True):
        self.cols, self.rows = cols, rows
        fcntl.ioctl(self.fd, termios.TIOCSWINSZ, struct.pack("HHHH", rows, cols, 0, 0))
        self.screen.resize(rows, cols)
        if signal_child:
            os.kill(self.pid, signal.SIGWINCH)

    def pump(self, seconds=0.05):
        end = time.time() + seconds
        while True:
            remaining = end - time.time()
            r, _, _ = select.select([self.fd], [], [], max(0, remaining))
            if r:
                try:
                    data = os.read(self.fd, 65536)
                except OSError:
                    data = b""
                if not data:
                    return False
                self.raw.extend(data)
                self.stream.feed(data)
            if time.time() >= end:
                return True

    def text(self):
        return "\n".join(self.screen.display)

    def wait(self, predicate, timeout=20.0):
        end = time.time() + timeout
        while time.time() < end:
            self.pump(0.1)
            if predicate(self.text()):
                return True
        return predicate(self.text())

    def send(self, data):
        os.write(self.fd, data.encode("utf-8") if isinstance(data, str) else data)
        self.pump(0.15)

    def quit(self, timeout=10.0):
        self.send(b"\x11")  # Ctrl+Q
        end = time.time() + timeout
        while time.time() < end:
            self.pump(0.1)
            pid, status = os.waitpid(self.pid, os.WNOHANG)
            if pid == self.pid:
                self.exit_code = os.waitstatus_to_exitcode(status)
                break
        if self.exit_code is not None:
            for _ in range(100):
                if not self.pump(0.05):
                    break
        return self.exit_code

    def kill(self):
        if self.exit_code is None:
            try:
                os.kill(self.pid, signal.SIGKILL)
                os.waitpid(self.pid, 0)
            except (ProcessLookupError, ChildProcessError):
                pass
        try:
            os.close(self.fd)
        except OSError:
            pass

    def reverse_cells(self):
        return sum(1 for row in self.screen.buffer.values() for ch in row.values() if ch.reverse)

    def colored_cells(self):
        return sum(1 for row in self.screen.buffer.values() for ch in row.values() if ch.fg != "default" or ch.bg != "default")


def non_ascii(text):
    return sorted({c for c in text if ord(c) > 0x7E})


def signed_in(text):
    return "? Help" in text and ("Missions" in text or "Armada" in text) and "API Key Login" not in text


class Results:
    def __init__(self, out, platform):
        self.out, self.platform, self.rows = out, platform, []

    def frame(self, name, text):
        with open(os.path.join(self.out, "%s-%s.txt" % (self.platform, name)), "w", encoding="utf-8") as f:
            f.write(text + "\n")

    def check(self, case, what, ok, detail=""):
        self.rows.append({"platform": self.platform, "case": case, "check": what, "ok": bool(ok), "detail": detail})
        print("%s  %-24s %-48s %s" % ("PASS" if ok else "FAIL", case, what, detail if not ok else ""))
        return ok


def run_pty_case(args, res, name, env, cols=120, rows=40, expect_ascii=None, steps=None):
    work = tempfile.mkdtemp(prefix="armada-tui-term-")
    tui = Tui(args, env, cols, rows, work)
    try:
        ok = tui.wait(signed_in, 40)
        res.frame(name, tui.text())
        if not res.check(name, "renders signed in at %dx%d" % (cols, rows), ok, tui.text()[-400:]):
            return
        text = tui.text()
        if expect_ascii is True:
            res.check(name, "ASCII only (fallback)", not non_ascii(text), "non-ASCII: %r" % non_ascii(text)[:20])
            res.check(name, "ASCII borders", "+--" in text or "+-" in text)
        elif expect_ascii is False:
            res.check(name, "Unicode box drawing", any(c in BOX for c in text), "no box-drawing characters")
        for step in steps or []:
            step(tui, res, name)
        code = tui.quit()
        res.check(name, "Ctrl+Q exits with code 0", code == 0, "exit %r" % code)
        tail = bytes(tui.raw[-4000:])
        res.check(name, "terminal restored (cursor shown)", b"\x1b[?25h" in tail, "no show-cursor sequence at exit")
        if b"\x1b[?1049h" in tui.raw:
            res.check(name, "alternate screen left", b"\x1b[?1049l" in tail, "no leave-alternate-screen sequence at exit")
    finally:
        tui.kill()
        shutil.rmtree(work, ignore_errors=True)


def step_keys(tui, res, name):
    tui.send("gv")
    ok = tui.wait(lambda t: "[Vessels]" in t or "Vessels  Onboard" in t or "Import repositories" in t, 10)
    res.frame(name + "-vessels", tui.text())
    res.check(name, "g v opens Vessels", ok)
    tui.send(b"\x0b")  # Ctrl+K
    ok = tui.wait(lambda t: "Command palette" in t, 5)
    res.frame(name + "-palette", tui.text())
    res.check(name, "Ctrl+K opens the palette", ok)
    tui.send("jobs")
    ok = tui.wait(lambda t: "Jobs" in t, 5)
    tui.send(b"\r")
    ok = tui.wait(lambda t: "Command palette" not in t and "Jobs" in t, 5)
    res.check(name, "palette runs a destination (Jobs)", ok)
    tui.send("?")
    ok = tui.wait(lambda t: "Keyboard shortcuts" in t or "Ctrl+K" in t and "palette" in t.lower(), 5)
    res.frame(name + "-help", tui.text())
    res.check(name, "? opens help", ok)
    tui.send(b"\x1b")
    tui.pump(0.3)
    ok = tui.wait(lambda t: "Keyboard shortcuts" not in t, 5)
    res.check(name, "Esc closes help", ok)


def step_resize(tui, res, name):
    tui.set_size(80, 24)
    ok = tui.wait(lambda t: "? Help" in t and len(t.split("\n")) == 24, 10)
    res.frame(name + "-80x24", tui.text())
    res.check(name, "resize to 80x24 re-renders", ok)
    res.check(name, "80x24 hides the sidebar", "OPERATIONS" not in tui.text())
    tui.set_size(70, 20)
    ok = tui.wait(lambda t: "Terminal too small" in t, 10)
    res.frame(name + "-70x20", tui.text())
    res.check(name, "70x20 shows terminal too small", ok)
    tui.set_size(120, 40)
    ok = tui.wait(lambda t: "? Help" in t and "OPERATIONS" in t, 10)
    res.check(name, "back to 120x40 restores the layout", ok)


def step_high_contrast(tui, res, name):
    res.check(name, "NO_COLOR: high contrast (ASCII borders)", "+--" in tui.text(), "borders are not ASCII")
    res.check(name, "NO_COLOR: selection in reverse video", tui.reverse_cells() > 0, "no reverse-video cells")


def run_tmux_case(args, res):
    name = "tmux-screen-256color"
    if shutil.which("tmux") is None:
        res.check(name, "tmux available", False, "tmux not installed")
        return
    work = tempfile.mkdtemp(prefix="armada-tui-tmux-")
    sock = os.path.join(work, "tmux.sock")
    env = " ".join([
        "ARMADA_DATA_DIR=%s/data" % work, "ARMADA_TUI_PREFERENCES=%s/tui.json" % work, "ARMADA_TUI_CREDENTIAL_STORE=file",
        "ARMADA_TUI_CREDENTIALS=%s/cred.json" % work, "ARMADA_TOKEN=%s" % args.token, "LANG=C.UTF-8",
    ])
    cmd = "env %s dotnet %s tui --server %s --route /missions; echo EXITED:$?; sleep 30" % (env, args.helm, args.server)

    def tmux(*a):
        return subprocess.run(["tmux", "-S", sock, "-f", "/dev/null"] + list(a), capture_output=True, text=True)

    def capture():
        return tmux("capture-pane", "-p", "-t", "armada").stdout

    def wait(pred, timeout=20):
        end = time.time() + timeout
        while time.time() < end:
            if pred(capture()):
                return True
            time.sleep(0.2)
        return pred(capture())

    try:
        tmux("new-session", "-d", "-s", "armada", "-x", "120", "-y", "40", "-e", "TERM=screen-256color", cmd)
        tmux("set-option", "-g", "default-terminal", "screen-256color")
        ok = wait(signed_in, 40)
        res.frame(name, capture())
        if not res.check(name, "renders signed in inside tmux", ok, capture()[-400:]):
            return
        text = capture()
        res.check(name, "Unicode box drawing in tmux", any(c in BOX for c in text))
        tmux("send-keys", "-t", "armada", "g", "v")
        res.check(name, "g v opens Vessels", wait(lambda t: "Import repositories" in t or "[Vessels]" in t, 10))
        tmux("send-keys", "-t", "armada", "C-k")
        res.check(name, "Ctrl+K opens the palette", wait(lambda t: "Command palette" in t, 5))
        tmux("send-keys", "-t", "armada", "Escape")
        res.check(name, "Esc closes the palette", wait(lambda t: "Command palette" not in t, 5))
        tmux("send-keys", "-t", "armada", "M-Left")
        res.check(name, "Alt+Left goes back (Missions)", wait(lambda t: "[Missions]" in t, 5))
        tmux("resize-window", "-t", "armada", "-x", "80", "-y", "24")
        ok = wait(lambda t: "? Help" in t and "OPERATIONS" not in t, 10)
        res.frame(name + "-80x24", capture())
        res.check(name, "tmux resize to 80x24 re-renders", ok)
        tmux("send-keys", "-t", "armada", "C-q")
        res.check(name, "Ctrl+Q exits with code 0", wait(lambda t: "EXITED:0" in t, 10), capture()[-200:])
    finally:
        tmux("kill-server")
        shutil.rmtree(work, ignore_errors=True)


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--helm", required=True)
    p.add_argument("--server", required=True)
    p.add_argument("--token", required=True)
    p.add_argument("--out", required=True)
    p.add_argument("--platform", default=sys.platform)
    p.add_argument("--tmux", action="store_true")
    args = p.parse_args()
    os.makedirs(args.out, exist_ok=True)
    res = Results(args.out, args.platform)

    utf8 = {"TERM": "xterm-256color", "LANG": "en_US.UTF-8", "LC_ALL": None, "LC_CTYPE": None}
    run_pty_case(args, res, "xterm-256color-utf8", utf8, expect_ascii=False, steps=[step_keys, step_resize])
    run_pty_case(args, res, "xterm-lang-c", {"TERM": "xterm", "LANG": "C", "LC_ALL": None, "LC_CTYPE": None}, expect_ascii=True, steps=[step_keys])
    run_pty_case(args, res, "term-dumb", {"TERM": "dumb", "LANG": "en_US.UTF-8", "LC_ALL": None, "LC_CTYPE": None}, expect_ascii=True)
    run_pty_case(args, res, "no-color", dict(utf8, NO_COLOR="1"), steps=[step_high_contrast])
    run_pty_case(args, res, "ssh-like-no-locale", {"TERM": "xterm-256color", "LANG": None, "LC_ALL": None, "LC_CTYPE": None,
                                                   "SSH_CONNECTION": "10.0.0.2 50000 10.0.0.1 22", "SSH_TTY": "/dev/pts/9"}, expect_ascii=False, steps=[step_keys])
    run_pty_case(args, res, "80x24", utf8, cols=80, rows=24, expect_ascii=False)
    if args.platform.startswith("linux"):
        run_pty_case(args, res, "linux-console-utf8", {"TERM": "linux", "LANG": "C.UTF-8", "LC_ALL": None, "LC_CTYPE": None}, expect_ascii=False, steps=[step_keys])
        run_pty_case(args, res, "linux-console-posix", {"TERM": "linux", "LANG": "POSIX", "LC_ALL": None, "LC_CTYPE": None}, expect_ascii=True)
    if args.tmux:
        run_tmux_case(args, res)

    with open(os.path.join(args.out, "%s-results.json" % args.platform), "w", encoding="utf-8") as f:
        json.dump(res.rows, f, indent=2)
    failed = [r for r in res.rows if not r["ok"]]
    print("%s: %d checks, %d failed" % (args.platform, len(res.rows), len(failed)))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
