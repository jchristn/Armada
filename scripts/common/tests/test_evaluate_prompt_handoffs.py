"""Check paired evaluation controls and scoring without using a model."""
import importlib.util
import json
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

SCRIPT = Path(__file__).resolve().parents[1] / 'evaluate-prompt-handoffs.py'
SPEC = importlib.util.spec_from_file_location('evaluate_prompt_handoffs', SCRIPT)
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class EvaluationTests(unittest.TestCase):
    """Paired variants retain identical model/task settings and explicit scoring limits."""

    def test_local_runner_pins_model_and_disables_shell_and_web(self):
        commands = []
        def run(command, **kwargs):
            commands.append(command)
            final = Path(command[command.index('--output-last-message') + 1])
            final.write_text('Offline reply')
            self.assertIn('Synthetic task', kwargs['input'])
            return SimpleNamespace(returncode=0, stdout='{"type":"turn.completed","usage":{"input_tokens":10,"output_tokens":3}}', stderr='')
        with patch.object(MODULE.subprocess, 'run', side_effect=run):
            response = MODULE.local_codex_chat(None, None, 'fixed-model', 'Synthetic task')
        self.assertEqual('fixed-model', response['Model'])
        self.assertEqual(0, response['Metrics']['ToolCallCount'])
        self.assertIn('--ignore-user-config', commands[0])
        self.assertIn('read-only', commands[0])
        self.assertIn('shell_tool', commands[0])
        self.assertIn('web_search="disabled"', commands[0])
        self.assertIn('fixed-model', commands[0])

    def test_architect_scores_only_independent_mission_descriptions(self):
        case = {'kind': 'architect', 'checks': ['AC-1', 'dotnet test']}
        reply = 'AC-1 dotnet test outside plan\n```armada-plan\n{"missions":[{"description":"AC-1"}]}\n```'
        result = MODULE.score(case, reply)
        self.assertEqual(0.5, result['evidenceCoverage'])
        self.assertTrue(result['parseablePlan'])
        self.assertFalse(result['requiredEvidencePresent']['dotnet test'])

    def test_malformed_plan_cannot_score_as_complete_evidence(self):
        result = MODULE.score({'kind': 'architect', 'checks': ['AC-1']}, 'AC-1 ```armada-plan\ninvalid\n```')
        self.assertFalse(result['parseablePlan'])
        self.assertEqual(0, result['evidenceCoverage'])

    def test_live_pair_has_fixed_captain_task_and_distinct_prompt_hashes(self):
        calls = []
        case = {'id': 'worker', 'persona': 'persona.worker', 'kind': 'report',
                'baselinePrompt': 'Worker base', 'task': 'Same task', 'checks': ['blocker']}
        def chat(base, token, captain, message):
            calls.append({'captain': captain, 'message': message})
            return {'Success': True, 'Model': 'fixed-model', 'Reply': 'blocker', 'Metrics': {'ToolCallCount': 0}}
        with tempfile.TemporaryDirectory() as folder:
            output = Path(folder) / 'result.json'
            result = MODULE.evaluate([case], {'persona.worker': '\nImproved'}, 'http://test', 'secret', 'test-captain', output, chat)
            self.assertEqual(2, len(calls))
            self.assertEqual('test-captain', calls[0]['captain'])
            self.assertEqual('test-captain', calls[1]['captain'])
            self.assertIn('Same task', calls[0]['message'])
            self.assertIn('Same task', calls[1]['message'])
            runs = result['cases'][0]['runs']
            self.assertNotEqual(runs['baseline']['promptSha256'], runs['improved']['promptSha256'])
            self.assertNotIn('secret', output.read_text())
            self.assertIn('screening metric', runs['improved']['score']['automatedScoreLimit'])

    def test_model_drift_invalidates_pair(self):
        calls = []
        def chat(base, token, captain, message):
            calls.append(message)
            return {'Success': True, 'Model': str(len(calls)), 'Reply': 'ok'}
        case = {'id': 'worker', 'persona': 'persona.worker', 'kind': 'report',
                'baselinePrompt': 'Base', 'task': 'Same', 'checks': ['ok']}
        with tempfile.TemporaryDirectory() as folder:
            with self.assertRaises(RuntimeError):
                MODULE.evaluate([case], {'persona.worker': 'new'}, 'http://test', 'secret', 'captain', Path(folder) / 'out.json', chat)

    def test_downstream_receives_only_description_and_fixed_worker_prompt(self):
        calls = []
        plan = '```armada-plan\n{"missions":[{"description":"AC-1 independent instructions"}]}\n```'
        def chat(base, token, captain, message):
            calls.append(message)
            return {'Success': True, 'Model': 'fixed', 'Reply': plan if len(calls) < 3 else 'AC-1'}
        cases = [{'id': 'architect', 'persona': 'persona.architect', 'kind': 'architect',
                  'baselinePrompt': 'Architect base', 'task': 'UPSTREAM-ONLY', 'checks': ['AC-1']},
                 {'id': 'worker', 'persona': 'persona.worker', 'kind': 'report',
                  'baselinePrompt': 'Fixed worker', 'task': 'Other', 'checks': ['AC-1']}]
        with tempfile.TemporaryDirectory() as folder:
            result = MODULE.evaluate(cases, {'persona.architect': 'new architect', 'persona.worker': 'new worker'},
                                     'http://test', 'secret', 'captain', Path(folder) / 'out.json', chat)
        self.assertIn('Fixed worker', calls[2])
        self.assertNotIn('UPSTREAM-ONLY', calls[2])
        self.assertNotIn('new worker', calls[2])
        self.assertEqual(calls[2], calls[3])
        self.assertIn('downstreamWorker', result['cases'][0])


    def _blackbox_case(self):
        cases = json.loads((SCRIPT.parent / 'prompt-evaluations' / 'cases.json').read_text())
        return next(c for c in cases if c['kind'] == 'blackbox')

    def _grounded_reply(self):
        return ('## What happened\nMission msn_eval_0001 could not land: src/Sync/Runner.cs and src/Sync/RunnerTests.cs '
                'conflicted with msn_eval_0002 (commit 9b8e7d6c5a4f) in src/Armada.Core/Services/MissionLandingHandler.cs.\n'
                '## Why\nbbx-land-003 (High) and bbx-dep-001 (Medium).\n'
                '## My opinion\nIn my opinion the triage is right.\n'
                '## What could have been done differently\nYou can set dependsOnMissionId. Armada should detect overlapping scopes.\n'
                '## Next steps\n- bbx-land-003.rebase-retry\n')

    def test_blackbox_case_scores_grounding(self):
        case = self._blackbox_case()
        result = MODULE.score(case, self._grounded_reply())
        self.assertTrue(result['grounding']['grounded'], result['grounding'])
        self.assertTrue(result['grounding']['shaped'], result['grounding'])
        self.assertEqual(1.0, result['evidenceCoverage'])

    def test_blackbox_grounding_flags_a_file_not_in_the_entry(self):
        case = self._blackbox_case()
        reply = self._grounded_reply() + 'Also src/Sync/Ghost.cs from deadbeef1234 by msn_ghost_0009.\n'
        result = MODULE.score(case, reply)['grounding']
        self.assertFalse(result['grounded'])
        self.assertEqual(['src/Sync/Ghost.cs'], result['unknownFiles'])
        self.assertEqual(['deadbeef1234'], result['unknownCommits'])
        self.assertEqual(['msn_ghost_0009'], result['unknownMissions'])

    def test_blackbox_grounding_flags_an_unlabeled_opinion(self):
        reply = self._grounded_reply().replace('## My opinion\nIn my opinion', '## Thoughts\nProbably')
        result = MODULE.score(self._blackbox_case(), reply)['grounding']
        self.assertFalse(result['shaped'])
        self.assertEqual(['My opinion'], result['missingHeadings'])

    def test_blackbox_case_uses_its_own_improvement(self):
        calls = []
        case = self._blackbox_case()
        def chat(base, token, captain, message):
            calls.append(message)
            return {'Success': True, 'Model': 'fixed', 'Reply': self._grounded_reply()}
        with tempfile.TemporaryDirectory() as folder:
            MODULE.evaluate([case], {}, 'http://test', 'secret', 'captain', Path(folder) / 'out.json', chat)
        self.assertNotIn('## Black box', calls[0])
        self.assertIn('## Black box', calls[1])


if __name__ == '__main__':
    unittest.main()
