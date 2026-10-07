#!/usr/bin/env python3
"""Self-test of the mobile parity gate: a missing or extra entry fails --check, planned entries pass until
--forbid-planned, and hand-maintained fields survive regeneration. Run: python3 scripts/mobile/test_parity_manifest.py
"""
import importlib.util
import json
import os
import shutil
import sys
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("mobile_parity", os.path.join(HERE, "generate-parity-manifest.py"))
parity = importlib.util.module_from_spec(spec)
sys.argv = [sys.argv[0]]
spec.loader.exec_module(parity)


class ParityGateTest(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.mkdtemp()
        self.real = parity.MANIFEST
        parity.MANIFEST = os.path.join(self.tmp, "parity.json")
        shutil.copy(self.real, parity.MANIFEST)

    def tearDown(self):
        parity.MANIFEST = self.real
        shutil.rmtree(self.tmp)

    def run_check(self, forbid_planned=False):
        old = {(e["kind"], e["key"]): e for e in json.load(open(parity.MANIFEST))["entries"]}
        entries = parity.generate(old)
        return parity.check(entries, parity.render(entries), forbid_planned)

    def rewrite(self, mutate):
        doc = json.load(open(parity.MANIFEST))
        mutate(doc["entries"])
        with open(parity.MANIFEST, "w", encoding="utf-8", newline="\n") as f:
            f.write(json.dumps(doc, indent=2, ensure_ascii=True) + "\n")

    def test_committed_manifest_passes(self):
        self.assertEqual(self.run_check(), 0)

    def test_missing_entry_fails(self):
        self.rewrite(lambda entries: entries.pop(0))
        self.assertEqual(self.run_check(), 1)

    def test_extra_entry_fails(self):
        self.rewrite(lambda entries: entries.append({"kind": "route", "key": "/gone", "status": "implemented", "mobile": "", "workstream": "W0.5", "notes": ""}))
        self.assertEqual(self.run_check(), 1)

    def test_planned_fails_only_with_forbid_planned(self):
        self.assertEqual(self.run_check(forbid_planned=False), 0)
        self.assertEqual(self.run_check(forbid_planned=True), 1)

    def test_extension_without_notes_fails(self):
        def strip(entries):
            for e in entries:
                if e["status"] == "extension":
                    e["notes"] = ""
                    return
        self.rewrite(strip)
        self.assertEqual(self.run_check(), 1)

    def test_hand_edits_survive_regeneration(self):
        def mark(entries):
            for e in entries:
                if e["kind"] == "api" and e["key"] == "getMission":
                    e["status"] = "implemented"
                    e["mobile"] = "MissionsScreen"
        self.rewrite(mark)
        old = {(e["kind"], e["key"]): e for e in json.load(open(parity.MANIFEST))["entries"]}
        regenerated = {(e["kind"], e["key"]): e for e in parity.generate(old)}
        self.assertEqual(regenerated[("api", "getMission")]["mobile"], "MissionsScreen")


if __name__ == "__main__":
    unittest.main()
