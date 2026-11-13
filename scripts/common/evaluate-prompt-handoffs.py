#!/usr/bin/env python3
"""Run bounded paired prompt evaluations against one configured captain; never dispatch missions."""
import argparse
import datetime
import importlib.util
import json
from pathlib import Path
import re
import subprocess
import tempfile
import time

SCRIPT_DIR = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location('prompt_deployment', SCRIPT_DIR / 'apply-prompt-improvements.py')
DEPLOY = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(DEPLOY)


ANSWER_HEADINGS = ['What happened', 'Why', 'My opinion', 'What could have been done differently', 'Next steps']
MISSION_PATTERN = re.compile(r'(?<![A-Za-z0-9_])msn_[A-Za-z0-9][A-Za-z0-9_]*[A-Za-z0-9]')
COMMIT_PATTERN = re.compile(r'(?<![A-Za-z0-9_])(?=[0-9a-f]*[0-9])(?=[0-9a-f]*[a-f])[0-9a-f]{7,40}(?![A-Za-z0-9_])')
FILE_PATTERN = re.compile(r'(?<![A-Za-z0-9_./-])(?:[A-Za-z0-9_.-]+/)*[A-Za-z0-9_-][A-Za-z0-9_.-]*'
                          r'\.(?:cs|csproj|sln|ts|tsx|js|jsx|json|md|py|sh|yml|yaml|xml|txt|config|props|targets)(?![A-Za-z0-9_])')


def grounding(entry, reply):
    """Ask black box answer check (mirrors Test.Shared AskBlackBoxGrounding): required headings, a labeled opinion,
    user and Armada changes separated, and only files, commits and missions that are in the entry."""
    files = entry.get('files', [])
    commits = entry.get('commits', [])
    missions = set(entry.get('missions', []))
    missing = [h for h in ANSWER_HEADINGS
               if not re.search(r'(?im)^\s*(?:#+\s*|\*\*|- \*\*)?' + re.escape(h) + r'\b', reply)]
    unknown_missions = sorted({m for m in MISSION_PATTERN.findall(reply) if m not in missions})
    unknown_commits = sorted({c for c in COMMIT_PATTERN.findall(reply)
                              if not any(k.lower().startswith(c.lower()) or c.lower().startswith(k.lower()) for k in commits)})
    unknown_files = sorted({f for f in FILE_PATTERN.findall(reply)
                            if not any(k == f or k.endswith('/' + f) for k in files)})
    opinion = re.search(r'(?i)\b(my opinion|in my opinion|i think|i believe)\b', reply) is not None
    split = (re.search(r'(?i)\b(you can|user can|you could)\b', reply) is not None
             and re.search(r'(?i)\barmada (should|could|can)\b', reply) is not None)
    return {'missingHeadings': missing, 'opinionLabeled': opinion, 'changesSplit': split,
            'unknownFiles': unknown_files, 'unknownCommits': unknown_commits, 'unknownMissions': unknown_missions,
            'grounded': not (unknown_files or unknown_commits or unknown_missions),
            'shaped': not missing and opinion and split}


def score(case, reply, metrics=None):
    text = reply
    parsed = None
    if case['kind'] == 'architect':
        match = re.search(r'```armada-plan\s*(.*?)```', reply, re.S)
        try:
            parsed = json.loads(match.group(1)) if match else None
            descriptions = [m['description'] for m in parsed['missions']]
            text = '\n'.join(descriptions)
        except (ValueError, TypeError, KeyError):
            parsed = None
            text = ''
    hits = {check: check.lower() in text.lower() for check in case['checks']}
    extra = {'grounding': grounding(case.get('entry', {}), reply)} if case['kind'] == 'blackbox' else {}
    return {**extra, 'requiredEvidencePresent': hits, 'evidenceCoverage': sum(hits.values()) / len(hits),
            'parseablePlan': parsed is not None if case['kind'] == 'architect' else None,
            'replyCharacters': len(reply), 'completionSignalPresent': '[ARMADA:RESULT] COMPLETE' in reply,
            'toolCallCount': DEPLOY.field(metrics or {}, 'ToolCallCount'),
            'automatedScoreLimit': 'Lexical evidence coverage is a screening metric; review correctness and unsupported claims manually.'}


def chat(base, token, captain_id, message):
    result = DEPLOY.request(base, token, 'POST', '/api/v1/captains/' + captain_id + '/chat',
                            {'Message': message, 'History': [], 'ShowThinking': False}, timeout=300)
    if not DEPLOY.field(result, 'Success'):
        raise RuntimeError('Evaluation turn failed: ' + str(DEPLOY.field(result, 'ErrorCode', 'unknown')))
    return result


def evaluate(cases, additions, base, token, captain_id, output, call=chat):
    report = {'createdUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
              'mode': 'local-cli' if call is local_codex_chat else 'captain-chat', 'captainId': captain_id,
              'controls': ('Same explicit model, empty history, same task; ephemeral read-only workspace, shell/web disabled, user config ignored; runtime-managed decoding defaults.' if call is local_codex_chat else 'Same captain, empty history, same task, ShowThinking=false; runtime-managed decoding defaults.'),
              'limitations': 'Synthetic offline tasks; does not certify parameter-count suitability or real filesystem execution. Runtime-managed decoding settings are not a fixed temperature/seed pin.',
              'cases': []}
    prefix = ('Synthetic prompt evaluation only. Do not call tools, access the network, inspect the environment, '
              'modify files, commit, or dispatch work. The fictional tool restrictions below apply. '
              'Respond only from supplied artifacts, keeping the role output format where applicable.\n\n')
    for case in cases:
        pair = {'id': case['id'], 'persona': case['persona'], 'runs': {},
                'manualReview': {'unsupportedClaims': None, 'acceptanceCorrectness': None, 'residualRisks': None}}
        for variant in ('baseline', 'improved'):
            # A case may carry its own improvement (the Ask black box section); persona cases use the deployed additions.
            addition = case['improvedAddition'] if 'improvedAddition' in case else additions[case['persona']]
            instructions = case['baselinePrompt'] + (addition if variant == 'improved' else '')
            message = prefix + instructions + '\n\n## Evaluation task\n' + case['task']
            started = time.monotonic()
            response = call(base, token, captain_id, message)
            reply = DEPLOY.field(response, 'Reply', '')
            metrics = DEPLOY.field(response, 'Metrics', {})
            pair['runs'][variant] = {'model': DEPLOY.field(response, 'Model'), 'seconds': time.monotonic() - started,
                                     'promptSha256': DEPLOY.digest(message), 'reply': reply, 'metrics': metrics,
                                     'score': score(case, reply, metrics)}
            print(case['id'] + ' / ' + variant + ': completed', flush=True)
        first_model = pair['runs']['baseline']['model']
        second_model = pair['runs']['improved']['model']
        if first_model != second_model:
            raise RuntimeError('Model changed within evaluation pair: ' + case['id'])
        if case['kind'] == 'architect':
            pair['downstreamWorker'] = {}
            for variant in ('baseline', 'improved'):
                reply = pair['runs'][variant]['reply']
                match = re.search(r'```armada-plan\s*(.*?)```', reply, re.S)
                plan = json.loads(match.group(1)) if match else {'missions': []}
                if not plan['missions']:
                    pair['downstreamWorker'][variant] = {'error': 'No parseable upstream mission'}
                    continue
                description = plan['missions'][0]['description']
                # Downstream instructions and model stay fixed. Only the upstream mission description changes.
                worker_prompt = next(c['baselinePrompt'] for c in cases if c['persona'] == 'persona.worker')
                message = prefix + worker_prompt + '\n\nSupplied mission description:\n' + description + (
                    '\n\nYou have no tools. Explain the proposed edit and validation, with expected outcomes; '
                    'do not claim execution. Do not access any files or external resources.')
                response = call(base, token, captain_id, message)
                if DEPLOY.field(response, 'Model') != first_model:
                    raise RuntimeError('Downstream model differs from upstream pair')
                downstream_reply = DEPLOY.field(response, 'Reply', '')
                downstream_case = dict(case, kind='report')
                pair['downstreamWorker'][variant] = {'model': DEPLOY.field(response, 'Model'), 'reply': downstream_reply,
                                                    'metrics': DEPLOY.field(response, 'Metrics', {}),
                                                    'score': score(downstream_case, downstream_reply, DEPLOY.field(response, 'Metrics', {}))}
        report['cases'].append(pair)
        Path(output).write_text(json.dumps(report, indent=2) + '\n')
    return report


def local_codex_chat(base, token, captain_id, message):
    with tempfile.TemporaryDirectory(prefix='armada-prompt-eval-') as folder:
        final_path = Path(folder) / 'reply.txt'
        command = ['codex', 'exec', '--ignore-user-config', '--sandbox', 'read-only', '--skip-git-repo-check',
                   '--ephemeral', '--json', '--disable', 'shell_tool', '-c', 'web_search="disabled"',
                   '-C', folder, '--output-last-message', str(final_path), '-']
        if captain_id != 'local-default':
            command.extend(['--model', captain_id])
        result = subprocess.run(command, input=message, text=True, capture_output=True, timeout=300)
        if result.returncode != 0 or not final_path.exists():
            # CLI failures may include sensitive diagnostics; keep them outside the repository.
            raise RuntimeError('Local Codex evaluation failed with exit code ' + str(result.returncode))
        events = []
        for line in result.stdout.splitlines():
            try:
                events.append(json.loads(line))
            except ValueError:
                pass
        tool_calls = sum(1 for event in events if event.get('type') == 'item.completed'
                         and event.get('item', {}).get('type') in ('command_execution', 'mcp_tool_call', 'web_search'))
        if tool_calls:
            raise RuntimeError('Offline evaluation unexpectedly invoked tools')
        usage = next((e.get('usage', {}) for e in reversed(events) if e.get('type') == 'turn.completed'), {})
        model_match = re.search(r'^model:\s*(.+)$', result.stderr, re.M)
        model = model_match.group(1).strip() if model_match else captain_id
        return {'Success': True, 'Reply': final_path.read_text(), 'Model': model,
                'Metrics': {'ToolCallCount': tool_calls, 'PromptTokens': usage.get('input_tokens'),
                            'CompletionTokens': usage.get('output_tokens'), 'CachedTokens': usage.get('cached_input_tokens')}}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--base', help='Deployment API for captain-chat evaluation')
    parser.add_argument('--tenant', default='default')
    parser.add_argument('--session-file')
    parser.add_argument('--captain-id')
    parser.add_argument('--local-codex', action='store_true', help='Use local ephemeral Codex with user config ignored, shell and web disabled')
    parser.add_argument('--local-model', default='local-default', help='Optional explicit local model identifier')
    parser.add_argument('--output', required=True)
    args = parser.parse_args()
    if not args.local_codex and (not args.base or not args.captain_id):
        parser.error('Supply --base and --captain-id, or --local-codex')
    token = None if args.local_codex else DEPLOY.session(args)
    cases = json.loads((SCRIPT_DIR / 'prompt-evaluations/cases.json').read_text())
    additions = json.loads(DEPLOY.ADDITIONS.read_text())
    evaluate(cases, additions, args.base, token, args.local_model if args.local_codex else args.captain_id,
             args.output, local_codex_chat if args.local_codex else chat)


if __name__ == '__main__':
    main()
