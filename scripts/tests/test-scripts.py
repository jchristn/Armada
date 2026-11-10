"""Exercise the per-OS scripts in scripts/{macos,linux,wsl,wsl2} with fake platform and build tools.

Run with: python3 scripts/tests/test-scripts.py

Nothing real is built, installed, registered or started: dotnet, uname, ps, curl, docker, systemctl, launchctl,
plutil, ip, cmd.exe, wslpath and wscript.exe are fakes on PATH that log their arguments, and the scripts run against
a temporary copy of the repository with HOME pointing into the temp directory. The WSL scripts run with
WSL_SKIP_CHECK=1 and WSL_DISTRO_NAME=Test. A real `sleep` process stands in for a running Armada.Server where a
script has to stop one. The Windows .bat/.ps1 files cannot run here; they are checked for the same script set,
line endings and the -h handling, and read by hand.

Checks: the same script set in all five directories (Windows included), the OS guards, -h/--help and unexpected
arguments, the key commands each script runs, install/reinstall/remove semantics, the start-at-login trio on every
OS (files written: systemd unit, XDG autostart entry, launchd plist, Windows Startup launcher), stop/start helpers,
the Docker and display requirements under WSL 1 and WSL 2, WSL 2 localhost rewriting for MCP registration, line
endings, executable bits and shell syntax.
"""
import json
import os
import shutil
import subprocess
import tempfile
import threading
from pathlib import Path

root = Path(__file__).resolve().parents[2]
UNIX = ['macos', 'linux', 'wsl', 'wsl2']
ALL = UNIX + ['windows']
# Deprecated names kept on their own OS only; each forwards to the *-startup script of the same directory.
ALIASES = {
    'linux': {'install-systemd-user': 'install-startup', 'update-systemd-user': 'update-startup', 'remove-systemd-user': 'remove-startup'},
    'macos': {'install-launchd-agent': 'install-startup', 'update-launchd-agent': 'update-startup', 'remove-launchd-agent': 'remove-startup'},
    'windows': {'install-windows-task': 'install-startup', 'update-windows-task': 'update-startup', 'remove-windows-task': 'remove-startup'},
}
# Platform-native helpers that exist on one OS only (rule: only these may).
NATIVE_ONLY = {'macos': {'build-harbor-app'}, 'windows': {'resolve-framework', 'resolve-insecure'}, 'wsl': {'_wsl-common'}, 'wsl2': {'_wsl-common'}}

FAKE = r'''#!/usr/bin/env python3
import json, os, pathlib, sys
name = pathlib.Path(sys.argv[0]).name
args = sys.argv[1:]
env = os.environ
def log():
    with open(env['CALL_LOG'], 'a') as f:
        f.write(json.dumps([name, *args, {'ARMADA_SERVER_URL': env.get('ARMADA_SERVER_URL')}]) + '\n')
if name == 'uname':
    if args == ['-s']: print(env['MOCK_UNAME'])
    elif args == ['-r']: print(env.get('MOCK_UNAME_R', '6.6.0-generic'))
    else: print('x86_64')
    sys.exit(0)
if name == 'ps':
    pids = [p for p in env.get('MOCK_SERVER_PIDS', '').split(',') if p]
    if args == ['-eo', 'pid=,comm=']:
        for p in pids: print(f'{p} /home/test/.armada/bin/Armada.Server')
        print('1 init')
    elif args[:2] == ['-o', 'args=']:
        print('/home/test/.armada/bin/Armada.Server' if args[-1] in pids else 'other')
    elif args == ['-eo', 'pid=,args=']:
        print(env.get('MOCK_PS_ARGS', '1 /sbin/init'))
    sys.exit(0)
if name == 'cmd.exe':
    print('C:\\Users\\test\\AppData\\Roaming\r'); sys.exit(0)
if name == 'wslpath':
    print(env['MOCK_APPDATA'] if args[0] == '-u' else 'C:\\fake\\' + pathlib.Path(args[1]).name); sys.exit(0)
if name == 'ip':
    print('default via 172.20.0.1 dev eth0'); sys.exit(0)
log()
if name == 'curl':
    url = args[-1]
    local = '://localhost' in url or '://127.0.0.1' in url
    sys.exit(1 if (env.get('MOCK_CURL_FAIL') or (local and env.get('MOCK_LOCAL_DOWN'))) else 0)
if name == 'docker':
    if args[:1] == ['ps']: print('armada-e2e-split')
    sys.exit(1 if args[:1] == ['info'] and env.get('MOCK_DOCKER_DOWN') else 0)
if name == 'systemctl':
    if args[:2] == ['--user', 'show-environment']: sys.exit(0 if env.get('MOCK_SYSTEMD') else 1)
    if args[:2] == ['--user', 'is-active']: sys.exit(1)
    sys.exit(0)
if name == 'launchctl':
    sys.exit(1 if args[:1] == ['print'] else 0)
if name == 'dotnet':
    if args[:3] == ['tool', 'list', '--global']:
        print('Package Id      Version      Commands')
        print('-------------------------------------')
        # The install state starts from MOCK_INSTALLED and follows tool install/uninstall within one run.
        state = pathlib.Path(env['CALL_LOG'] + '.installed')
        installed = state.read_text() == '1' if state.exists() else bool(env.get('MOCK_INSTALLED'))
        if installed: print('armada.helm     0.9.0        armada')
        sys.exit(0)
    if args[:2] in (['tool', 'install'], ['tool', 'uninstall']):
        pathlib.Path(env['CALL_LOG'] + '.installed').write_text('1' if args[1] == 'install' else '0')
    if args[:1] == ['publish'] and '-o' in args:
        out = pathlib.Path(args[args.index('-o') + 1]); out.mkdir(parents=True, exist_ok=True)
        exe = out / 'Armada.Server'; exe.write_text('#!/bin/sh\nexit 0\n'); exe.chmod(0o755)
    sys.exit(0)
sys.exit(0)
'''
TOOLS = ['dotnet', 'uname', 'ps', 'curl', 'docker', 'systemctl', 'launchctl', 'plutil', 'ip', 'cmd.exe', 'wslpath', 'wscript.exe']


def stems(platform):
    d = root / 'scripts' / platform
    exts = ('.bat', '.ps1') if platform == 'windows' else ('.sh',)
    return {f.stem for f in d.iterdir() if f.suffix in exts}


def make_repo(temp):
    repo = temp / 'repo'
    shutil.copytree(root / 'scripts', repo / 'scripts', ignore=shutil.ignore_patterns('__pycache__', 'node_modules'))
    dash = repo / 'src/Armada.Dashboard'
    (dash / 'dist').mkdir(parents=True)
    (dash / 'package.json').write_text('{}')
    (dash / 'dist/index.html').write_text('<html></html>')
    return repo


count = 0
with tempfile.TemporaryDirectory(prefix='armada scripts ') as temp:
    temp = Path(temp)
    repo = make_repo(temp)
    bin_dir = temp / 'bin'
    bin_dir.mkdir()
    for name in TOOLS:
        (bin_dir / name).write_text(FAKE)
        (bin_dir / name).chmod(0o755)
    log = temp / 'calls.jsonl'
    appdata = temp / 'appdata'
    startup_dir = appdata / 'Microsoft/Windows/Start Menu/Programs/Startup'
    launcher = startup_dir / 'armada-wsl-Test.vbs'   # wsl_launcher_file armada, with WSL_DISTRO_NAME=Test
    base_env = {k: v for k, v in os.environ.items() if not k.startswith(('ARMADA_', 'WSL_', 'DOTNET_', 'XDG_'))}
    base_env.update(PATH=f'{bin_dir}:/usr/bin:/bin', CALL_LOG=str(log), WSL_SKIP_CHECK='1', WSL_DISTRO_NAME='Test',
                    DISPLAY=':0', MOCK_APPDATA=str(appdata))
    for key in ['WAYLAND_DISPLAY', 'DOCKER_HOST', 'NODE_TLS_REJECT_UNAUTHORIZED', 'npm_config_strict_ssl']:
        base_env.pop(key, None)
    home = temp / 'home'

    def fresh_home():
        shutil.rmtree(home, ignore_errors=True)
        shutil.rmtree(appdata, ignore_errors=True)
        home.mkdir()

    def run(platform, script, *args, stdin=None, drop=(), **env):
        log.unlink(missing_ok=True)
        Path(str(log) + '.installed').unlink(missing_ok=True)
        e = dict(base_env, HOME=str(home), MOCK_UNAME='Darwin' if platform == 'macos' else 'Linux',
                 MOCK_UNAME_R={'wsl': '4.4.0-19041-Microsoft', 'wsl2': '5.15.0-microsoft-standard-WSL2'}.get(platform, '6.6.0-generic'))
        e.update(env)
        for k in drop:
            e.pop(k, None)
        p = subprocess.run(['bash', str(repo / f'scripts/{platform}/{script}.sh'), *args], env=e, input=stdin,
                           stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, timeout=120)
        calls = [json.loads(x) for x in log.read_text().splitlines()] if log.exists() else []
        return p, calls

    def dn(calls):
        return [c[1:-1] for c in calls if c[0] == 'dotnet']

    def spawn_server():
        proc = subprocess.Popen(['sleep', '60'])
        threading.Thread(target=proc.wait, daemon=True).start()   # reap it, so kill -0 fails once it is gone
        return proc

    # ---- Same script set in all five directories ----
    sets = {p: stems(p) - set(ALIASES.get(p, {})) - NATIVE_ONLY.get(p, set()) for p in ALL}
    for p in ALL:
        assert sets[p] == sets['linux'], (p, sorted(sets[p] ^ sets['linux']))
    for p, aliases in ALIASES.items():
        for old, new in aliases.items():
            ext = 'bat' if p == 'windows' else 'sh'
            text = (root / f'scripts/{p}/{old}.{ext}').read_text()
            assert f'{new}.{ext}' in text, (p, old)
    for p in ['wsl', 'wsl2']:
        assert not {a for al in ALIASES.values() for a in al} & stems(p), p
    assert (root / 'scripts/wsl/_wsl-common.sh').read_bytes() == (root / 'scripts/wsl2/_wsl-common.sh').read_bytes()
    SCRIPTS = sorted(sets['linux'])
    assert {'install', 'reinstall', 'remove', 'install-mcp', 'remove-mcp', 'install-startup', 'update-startup',
            'remove-startup', 'stop-armada-server', 'start-armada-server'} <= set(SCRIPTS)

    # ---- Self-contained WSL scripts: they never run another script ----
    for p in ['wsl', 'wsl2']:
        for s in SCRIPTS:
            text = (root / f'scripts/{p}/{s}.sh').read_text()
            for bad in ['../linux/', 'scripts/linux/', 'wsl_run ', 'common/install', 'common/publish', 'common/stop-',
                        'common/deploy', 'common/healthcheck', 'common/run-', 'common/e2e', 'common/build']:
                assert bad not in text.replace('scripts/linux version', ''), (p, s, bad)
            assert 'WSL_FLAVOR=' + p in text and 'wsl_require\n' in text and 'wsl_sanitize_path' in text and 'wsl_check_repo' in text, (p, s)
        # WSL 1 copies carry no WSL 2-only text.
        for s in SCRIPTS:
            text = (root / f'scripts/wsl/{s}.sh').read_text()
            for bad in ['NAT', 'mirrored', 'wsl_resolve_local_url', 'WSL 2 version']:
                assert bad not in text, (s, bad)

    # ---- OS guards ----
    fresh_home()
    for platform, wrong, msg in [('linux', 'Darwin', 'This script is for Linux'), ('macos', 'Linux', 'This script is for macOS')]:
        for s in SCRIPTS:
            p, calls = run(platform, s, MOCK_UNAME=wrong)
            assert p.returncode != 0 and msg in p.stderr and not dn(calls), (platform, s, p.stderr)
            count += 1

    # ---- -h / --help: usage, exit 0, nothing run ----
    for platform in UNIX:
        for s in SCRIPTS:
            for flag in ['-h', '--help']:
                p, calls = run(platform, s, flag)
                assert p.returncode == 0 and 'Usage' in p.stdout and not dn(calls), (platform, s, flag, p.stdout, p.stderr)
                count += 1

    # ---- Unexpected arguments are rejected before anything runs ----
    REJECT = {s: ['--bogus'] for s in ['install', 'reinstall', 'remove', 'update', 'install-mcp', 'remove-mcp',
                                        'publish-server', 'deploy-dashboard', 'run-local', 'install-startup',
                                        'update-startup', 'remove-startup', 'factory-reset', 'stop-armada-server',
                                        'start-armada-server', 'e2e-split-mode', 'generate-api-surface',
                                        'generate-blackbox-schema', 'run-db-parity-tests', 'run-upgrade-test',
                                        'build-server', 'build-dashboard', 'build-proxy', 'build-all']}
    REJECT['healthcheck-server'] = ['http://a', 'extra']
    REJECT['stop-repo-mcp-hosts'] = [str(repo), 'extra']
    assert set(REJECT) | {'run-tui', 'run-harbor'} == set(SCRIPTS), set(SCRIPTS) - set(REJECT)
    for platform in UNIX:
        for s, bad in REJECT.items():
            p, calls = run(platform, s, *bad)
            assert p.returncode == 2 and not dn(calls) and not [c for c in calls if c[0] == 'docker'], (platform, s, p.returncode, p.stderr)
            count += 1
        p, calls = run(platform, 'install', '--framework')
        assert p.returncode != 0 and not dn(calls), p.stderr

    # ---- install / reinstall / remove ----
    for platform in UNIX:
        fresh_home()
        p, calls = run(platform, 'install', 'net8.0', '--insecure')
        assert p.returncode == 0, (platform, p.stderr)
        d = dn(calls)
        build = next(c for c in d if c[0] == 'build')
        pack = next(c for c in d if c[0] == 'pack')
        assert build[1].endswith('src/Armada.sln') and '-p:TargetFrameworks=net8.0' in build, build
        assert '-p:TargetFrameworks=net8.0' in pack and pack[pack.index('-o') + 1].endswith('src/nupkg'), pack
        assert ['tool', 'install', '--global', '--add-source', str(repo / 'src/nupkg'), 'Armada.Helm'] in d, d
        assert (home / '.armada/dashboard/index.html').exists()
        assert 'TLS certificate validation disabled' in p.stderr
        p, calls = run(platform, 'install', MOCK_INSTALLED='1')
        assert p.returncode != 0 and f'scripts/{platform}/reinstall.sh' in p.stderr and not [c for c in dn(calls) if c[0] == 'pack'], p.stderr
        srv = spawn_server()
        p, calls = run(platform, 'install', MOCK_SERVER_PIDS=str(srv.pid))
        assert p.returncode != 0 and f'scripts/{platform}/stop-armada-server.sh' in p.stderr and not [c for c in dn(calls) if c[0] == 'pack'], p.stderr
        p, calls = run(platform, 'reinstall', MOCK_SERVER_PIDS=str(srv.pid))
        assert p.returncode != 0 and 'stop-armada-server' in p.stderr and not [c for c in dn(calls) if c[0] in ('pack', 'tool') and c[1] != 'list'], p.stderr
        p, calls = run(platform, 'remove', MOCK_SERVER_PIDS=str(srv.pid), MOCK_INSTALLED='1')
        assert p.returncode != 0 and 'stop-armada-server' in p.stderr and ['tool', 'uninstall', '--global', 'Armada.Helm'] not in dn(calls)
        srv.kill()
        p, calls = run(platform, 'reinstall', MOCK_INSTALLED='1')
        assert p.returncode == 0, p.stderr
        order = [c[:2] for c in dn(calls) if c[0] in ('tool', 'pack') and c[1] != 'list']
        assert order.index(['tool', 'uninstall']) < order.index(['pack', str(repo / 'src/Armada.Helm')]) < order.index(['tool', 'install']), order
        p, calls = run(platform, 'reinstall')
        assert p.returncode == 0 and 'not installed; continuing' in p.stdout and ['tool', 'uninstall', '--global', 'Armada.Helm'] not in dn(calls)
        p, calls = run(platform, 'remove', MOCK_INSTALLED='1')
        assert p.returncode == 0 and ['tool', 'uninstall', '--global', 'Armada.Helm'] in dn(calls), p.stderr
        assert not (home / '.armada/dashboard').exists()
        p, calls = run(platform, 'remove')
        assert p.returncode == 0 and 'nothing to remove' in p.stdout and ['tool', 'uninstall', '--global', 'Armada.Helm'] not in dn(calls), p.stdout
        count += 9

    # ---- update: stop, reinstall, start, in that order ----
    for platform in UNIX:
        fresh_home()
        p, calls = run(platform, 'update', 'net8.0', MOCK_INSTALLED='1')
        assert p.returncode == 0, (platform, p.stderr)
        seq = [('stop' if c[-2:] == ['server', 'stop'] else 'start' if c[-2:] == ['server', 'start'] else c[0] + ' ' + c[1])
               for c in dn(calls) if c[-2:] in (['server', 'stop'], ['server', 'start']) or c[:2] in (['tool', 'uninstall'], ['tool', 'install']) or c[0] == 'pack']
        assert seq == ['stop', 'tool uninstall', 'pack ' + str(repo / 'src/Armada.Helm'), 'tool install', 'start'], (platform, seq)
        count += 1

    # ---- publish-server ----
    for platform in UNIX:
        fresh_home()
        p, calls = run(platform, 'publish-server', '-f', 'net8.0')
        pub = next(c for c in dn(calls) if c[0] == 'publish')
        assert p.returncode == 0 and pub[pub.index('-o') + 1] == str(home / '.armada/bin') and '--framework' in pub and 'net8.0' in pub, p.stderr
        (home / '.armada/bin/current').write_text('2026-01-01_000000_abc')
        p, calls = run(platform, 'publish-server')
        assert p.returncode == 0 and not (home / '.armada/bin/current').exists() and 'Cleared' in p.stdout, (platform, p.stdout)
        srv = spawn_server()
        p, calls = run(platform, 'publish-server', MOCK_SERVER_PIDS=str(srv.pid))
        srv.kill()
        assert p.returncode != 0 and 'stop-armada-server' in p.stderr and not [c for c in dn(calls) if c[0] == 'publish']
        count += 2

    # ---- MCP registration ----
    for platform in UNIX:
        for script, verb in [('install-mcp', 'install'), ('remove-mcp', 'remove')]:
            p, calls = run(platform, script)
            assert p.returncode == 0 and any(c[0] == 'run' and c[-3:] == ['mcp', verb, '--yes'] for c in dn(calls)), (platform, script, p.stderr)
            count += 1
    # WSL 2 NAT: no local Admiral but the Windows host answers -> point the clients at the host, unless the user chose a URL.
    p, calls = run('wsl2', 'install-mcp', MOCK_LOCAL_DOWN='1')
    mcp = next(c for c in calls if c[0] == 'dotnet' and c[-4:-1] == ['mcp', 'install', '--yes'])
    assert p.returncode == 0 and mcp[-1]['ARMADA_SERVER_URL'] == 'http://172.20.0.1:7890', (mcp, p.stderr)
    p, calls = run('wsl2', 'install-mcp', MOCK_LOCAL_DOWN='1', ARMADA_SERVER_URL='http://mine:1')
    mcp = next(c for c in calls if c[0] == 'dotnet' and c[-4:-1] == ['mcp', 'install', '--yes'])
    assert mcp[-1]['ARMADA_SERVER_URL'] == 'http://mine:1'
    p, calls = run('wsl', 'install-mcp', MOCK_LOCAL_DOWN='1')
    mcp = next(c for c in calls if c[0] == 'dotnet' and c[-4:-1] == ['mcp', 'install', '--yes'])
    assert mcp[-1]['ARMADA_SERVER_URL'] is None
    # The real guard (no WSL_SKIP_CHECK): not WSL at all, then the wrong WSL version for the directory.
    p, calls = run('wsl', 'install-mcp', drop=['WSL_SKIP_CHECK', 'WSL_DISTRO_NAME'], MOCK_UNAME_R='6.6.0-generic')
    assert p.returncode != 0 and 'This script is for WSL' in p.stderr and not dn(calls), p.stderr
    p, calls = run('wsl2', 'install-mcp', drop=['WSL_SKIP_CHECK'], MOCK_UNAME_R='4.4.0-19041-Microsoft')
    assert p.returncode != 0 and 'scripts/wsl instead' in p.stderr and p.stderr.startswith('ERROR:') and not dn(calls), p.stderr
    p, calls = run('wsl', 'install-mcp', drop=['WSL_SKIP_CHECK'], MOCK_UNAME_R='5.15.0-microsoft-standard-WSL2')
    assert p.returncode != 0 and 'scripts/wsl2 instead' in p.stderr and not dn(calls), p.stderr
    count += 4

    # ---- healthcheck-server ----
    for platform in UNIX:
        p, calls = run(platform, 'healthcheck-server', 'net10.0', 'http://h:1/')
        assert p.returncode == 0 and any(c[0] == 'curl' and c[-2] == 'http://h:1/api/v1/status/health' for c in calls), (platform, calls)
        count += 1
    p, calls = run('wsl2', 'healthcheck-server', MOCK_LOCAL_DOWN='1')
    assert p.returncode == 0 and calls[-1][-2] == 'http://172.20.0.1:7890/api/v1/status/health', calls
    # A URL chosen through any of the variables is never rewritten.
    for var in ['ARMADA_BASE_URL', 'ARMADA_SERVER_URL', 'ARMADA_URL']:
        p, calls = run('wsl2', 'healthcheck-server', MOCK_LOCAL_DOWN='1', MOCK_CURL_FAIL='1', **{var: 'http://localhost:7890'})
        assert p.returncode != 0 and all('172.20.0.1' not in c[-2] for c in calls if c[0] == 'curl'), (var, calls)
        p, calls = run('wsl2', 'install-mcp', MOCK_LOCAL_DOWN='1', **{var: 'http://localhost:7890'})
        mcp = next(c for c in calls if c[0] == 'dotnet' and c[-4:-1] == ['mcp', 'install', '--yes'])
        assert '172.20.0.1' not in str(mcp[-1]['ARMADA_SERVER_URL']), (var, mcp)
        count += 2

    # ---- start at login ----
    def startup_cycle(platform, artifact, check, **env):
        fresh_home()
        p, calls = run(platform, 'install-startup', 'net8.0', **env)
        assert p.returncode == 0, (platform, p.stderr)
        assert any(c[0] == 'publish' for c in dn(calls)), calls
        assert any(c[0] == 'curl' and c[-2].endswith('/api/v1/status/health') for c in calls), calls
        assert artifact().exists(), (platform, artifact())
        check(artifact().read_bytes().decode(), calls)
        p, calls = run(platform, 'install-startup', **env)
        assert p.returncode != 0 and f'scripts/{platform}/update-startup.sh' in p.stderr and not dn(calls), p.stderr
        p, calls = run(platform, 'update-startup', **env)
        assert p.returncode == 0 and any(c[0] == 'publish' for c in dn(calls)), p.stderr
        p, calls = run(platform, 'remove-startup', **env)
        assert p.returncode == 0 and not artifact().exists(), p.stderr
        p, calls = run(platform, 'remove-startup', **env)
        assert p.returncode == 0 and 'nothing to remove' in p.stdout
        p, calls = run(platform, 'update-startup', **env)
        assert p.returncode != 0 and f'scripts/{platform}/install-startup.sh' in p.stderr and not dn(calls)

    PCT = '/opt/50%"tools'   # a PATH entry with characters every format must escape
    def linux_unit(text, calls):
        assert f'ExecStart={home}/.armada/bin/Armada.Server' in text and 'Restart=on-failure' in text, text
        assert 'Environment="PATH=' + str(bin_dir) + ':/usr/bin:/bin:/opt/50%%\\"tools"' in text, text
        assert ['systemctl', '--user', 'enable', '--now', 'armada.service', {'ARMADA_SERVER_URL': None}] in calls, calls
    PATH_PCT = f'{bin_dir}:/usr/bin:/bin:{PCT}'
    startup_cycle('linux', lambda: home / '.config/systemd/user/armada.service', linux_unit, MOCK_SYSTEMD='1', PATH=PATH_PCT)

    def linux_desktop(text, calls):
        assert f'Exec=env "PATH={bin_dir}:/usr/bin:/bin:/opt/50%%\\"tools" "{home}/.armada/bin/Armada.Server"' in text and f'Path={home}/.armada' in text, text
    startup_cycle('linux', lambda: home / '.config/autostart/armada.desktop', linux_desktop, PATH=PATH_PCT)

    def mac_plist(text, calls):
        assert '<string>com.armada.admiral</string>' in text and f'<string>{home}/.armada/bin/Armada.Server</string>' in text, text
        assert f'<key>PATH</key>\n        <string>{bin_dir}:/usr/bin:/bin:/opt/50%&quot;tools</string>' in text, text
        assert any(c[0] == 'launchctl' and c[1] == 'bootstrap' for c in calls) and any(c[0] == 'plutil' for c in calls)
    startup_cycle('macos', lambda: home / 'Library/LaunchAgents/com.armada.admiral.plist', mac_plist, PATH=PATH_PCT)

    # update-startup rewrites the registration, so a changed PATH reaches the server; the old install names stay
    # idempotent (they update an existing registration); the server's own --install-service registration blocks all.
    for platform, art, env in [('linux', lambda: home / '.config/systemd/user/armada.service', {'MOCK_SYSTEMD': '1'}),
                               ('linux', lambda: home / '.config/autostart/armada.desktop', {}),
                               ('macos', lambda: home / 'Library/LaunchAgents/com.armada.admiral.plist', {}),
                               ('wsl', lambda: launcher, {}), ('wsl2', lambda: launcher, {})]:
        fresh_home()
        p, _ = run(platform, 'install-startup', **env)
        assert p.returncode == 0, (platform, p.stderr)
        p, _ = run(platform, 'update-startup', PATH=f'{bin_dir}:/usr/bin:/bin:/opt/newtools', **env)
        assert p.returncode == 0 and '/opt/newtools' in art().read_bytes().decode(), (platform, p.stderr)
        old = {'linux': 'install-systemd-user', 'macos': 'install-launchd-agent'}.get(platform)
        if old:
            p, calls = run(platform, old, **env)
            assert p.returncode == 0 and 'running update-startup.sh' in p.stderr and any(c[0] == 'publish' for c in dn(calls)), (platform, p.stderr)
            fresh_home()
            p, _ = run(platform, old, **env)
            assert p.returncode == 0 and art().exists() and 'running that' in p.stderr, (platform, p.stderr)
        count += 2
    product = {'linux': (home / '.config/systemd/user/armada.service', '[Service]\nExecStart=/x/Armada.Server --run-service\n'),
               'wsl': (home / '.config/systemd/user/armada.service', '[Service]\nExecStart=/x/Armada.Server --run-service\n'),
               'wsl2': (home / '.config/systemd/user/armada.service', '[Service]\nExecStart=/x/Armada.Server --run-service\n'),
               'macos': (home / 'Library/LaunchAgents/com.joelchristner.armada.server.plist', '<plist/>')}
    for platform, (path, body) in product.items():
        for s_ in ['install-startup', 'update-startup', 'remove-startup']:
            fresh_home()
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(body)
            p, calls = run(platform, s_, MOCK_SYSTEMD='1')
            assert p.returncode != 0 and '--install-service' in p.stderr and '--uninstall-service' in p.stderr and not dn(calls), (platform, s_, p.stderr)
            assert path.read_text() == body, 'the product registration must not be touched'
            count += 1

    for platform in ['wsl', 'wsl2']:
        def wsl_vbs(text, calls):
            assert text.startswith('CreateObject("WScript.Shell").Run "wsl.exe -d ""Test"" --cd ~ --exec /bin/sh -lc ""exec'), text
            assert "'env' 'PATH=" in text and "env' '-C'" not in text and "'/bin/sh' '-c' 'set -f;" in text, text
            assert f"' 'armada-server' '{home}/.armada'" in text and 'bin/current' in text and 'logs/wsl-startup-stdout.log' in text, text
            launch_script = text.split("'/bin/sh' '-c' '")[1].split("' 'armada-server'")[0]
            assert "'" not in launch_script and '"' not in launch_script, launch_script
            # Run what the launcher runs: the classic publish, then the slot named by bin/current.
            armada = home / '.armada'
            for target in [armada / 'bin/Armada.Server', armada / 'bin/slots/S1/Armada.Server']:
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_text('#!/bin/sh\necho "$0" > "$HOME/ran"\npwd >> "$HOME/ran"\necho out\n')
                target.chmod(0o755)
            for current, expected in [(None, armada / 'bin/Armada.Server'), ('S1\n', armada / 'bin/slots/S1/Armada.Server'), ('Gone', armada / 'bin/Armada.Server')]:
                if current is None:
                    (armada / 'bin/current').unlink(missing_ok=True)
                else:
                    (armada / 'bin/current').write_text(current)
                subprocess.run(['/bin/sh', '-c', launch_script, 'armada-server', str(armada)], check=True, cwd='/', env=dict(os.environ, HOME=str(home)))
                ran = (home / 'ran').read_text().split('\n')
                assert ran[0] == str(expected) and ran[1] == str(armada), (current, ran)
            assert 'out' in (armada / 'logs/wsl-startup-stdout.log').read_text()
            (armada / 'bin/current').unlink()
            assert "'DOTNET_ROOT=/opt/dotnet'" in text and text.endswith(', 0, False\r\n'), text
            assert '/mnt/' not in text.split("'PATH=")[1].split("'")[0], 'the Windows PATH must not reach the server'
            assert ['wscript.exe', 'C:\\fake\\armada-wsl-Test.vbs', {'ARMADA_SERVER_URL': None}] in calls, calls
        startup_cycle(platform, lambda: launcher, wsl_vbs, DOTNET_ROOT='/opt/dotnet')
        # A systemd unit left by scripts/linux is removed, so the server does not start twice.
        fresh_home()
        unit = home / '.config/systemd/user/armada.service'
        unit.parent.mkdir(parents=True)
        unit.write_text('[Unit]\n')
        p, _ = run(platform, 'install-startup')
        assert p.returncode == 0 and not unit.exists() and launcher.exists(), p.stderr
        p, _ = run(platform, 'remove', MOCK_INSTALLED='1')
        assert p.returncode == 0 and 'still registered to start at Windows login' in p.stdout
        count += 1
    count += 5

    # ---- stop / start helpers ----
    for platform in UNIX:
        srv = spawn_server()
        p, _ = run(platform, 'stop-armada-server', '--list', MOCK_SERVER_PIDS=str(srv.pid))
        assert p.returncode == 0 and p.stdout.split() == [str(srv.pid)] and srv.poll() is None, (platform, p.stdout)
        p, _ = run(platform, 'stop-armada-server', MOCK_SERVER_PIDS=str(srv.pid))
        assert p.returncode == 0, (platform, p.stderr)
        srv.wait(timeout=5)
        p, _ = run(platform, 'stop-armada-server')
        assert p.returncode == 0 and 'No Armada.Server process is running' in p.stdout
        host = spawn_server()
        other = spawn_server()
        ps_args = f'{host.pid} dotnet {repo}/src/Armada.Helm/bin/Debug/net10.0/Armada.Helm.dll mcp stdio\n{other.pid} dotnet /elsewhere/Armada.Helm.dll mcp stdio'
        p, _ = run(platform, 'stop-repo-mcp-hosts', MOCK_PS_ARGS=ps_args)
        assert p.returncode == 0 and f'Stopping MCP stdio host PID {host.pid}' in p.stdout, (platform, p.stdout, p.stderr)
        host.wait(timeout=5)
        assert other.poll() is None, 'a host from another checkout must be left alone'
        other.kill()
        fresh_home()
        p, _ = run(platform, 'start-armada-server')
        assert p.returncode != 0 and 'publish-server' in p.stderr
        (home / '.armada/bin').mkdir(parents=True)
        exe = home / '.armada/bin/Armada.Server'
        exe.write_text('#!/bin/sh\nexit 0\n')
        exe.chmod(0o755)
        p, calls = run(platform, 'start-armada-server')
        assert p.returncode == 0 and 'Started' in p.stdout, (platform, p.stderr)
        count += 5
    startup_dir.mkdir(parents=True)
    launcher.write_text('x')
    p, calls = run('wsl2', 'start-armada-server')
    assert p.returncode == 0 and any(c[0] == 'wscript.exe' for c in calls), p.stdout

    # ---- factory-reset ----
    for platform in UNIX:
        fresh_home()
        (home / '.armada/bin').mkdir(parents=True)
        (home / '.armada/armada.db').write_text('db')
        (home / '.armada/docks').mkdir()
        p, _ = run(platform, 'factory-reset', stdin='no\n')
        assert p.returncode == 1 and (home / '.armada/armada.db').exists(), p.stdout
        for answer in ['', 'yes\n']:   # end of input, and a lower-case answer: both abort
            p, _ = run(platform, 'factory-reset', stdin=answer)
            assert p.returncode == 1 and 'Aborted' in p.stdout and (home / '.armada/armada.db').exists(), (platform, answer, p.stdout, p.stderr)
        p, _ = run(platform, 'factory-reset', '-y')
        assert p.returncode == 0 and not (home / '.armada/armada.db').exists() and not (home / '.armada/docks').exists()
        assert (home / '.armada/bin').exists() and 'start-armada-server.sh' in p.stdout, p.stdout
        p, _ = run(platform, 'factory-reset', '--yes', '--all')
        assert p.returncode == 0 and not (home / '.armada/bin').exists() and f'scripts/{platform}/install-startup.sh' in p.stdout
        count += 3

    # ---- Docker: WSL 1 has no engine; WSL 2 and Linux need a reachable one ----
    for s in ['build-server', 'build-dashboard', 'build-proxy', 'build-all', 'e2e-split-mode']:
        p, calls = run('wsl', s, MOCK_DOCKER_DOWN='1')
        assert p.returncode != 0 and 'WSL 1 cannot run a Docker engine' in p.stderr and not [c for c in calls if c[0] == 'docker' and c[1] != 'info'], (s, p.stderr)
        for platform in ['wsl2', 'linux', 'macos']:
            p, calls = run(platform, s, MOCK_DOCKER_DOWN='1')
            assert p.returncode != 0 and not [c for c in calls if c[0] == 'docker' and c[1] in ('build', 'buildx', 'run')], (platform, s)
        count += 4
    for platform in ['wsl2', 'linux', 'wsl']:
        env = {}   # WSL 1: any docker CLI that reaches an engine (DOCKER_HOST or a docker context) works
        p, calls = run(platform, 'build-server', 'v1', **env)
        b = next(c for c in calls if c[0] == 'docker' and c[1:3] == ['buildx', 'build'])
        assert p.returncode == 0 and 'jchristn77/armada-server:v1' in b and '--push' in b, (platform, p.stderr)
        assert ['docker', 'pull', 'jchristn77/armada-server:v1', {'ARMADA_SERVER_URL': None}] in calls
        p, calls = run(platform, 'build-all', **env)
        assert p.returncode == 0 and len([c for c in calls if c[0] == 'docker' and c[1:3] == ['buildx', 'build']]) == 3, p.stderr
        count += 2
    for platform in ['wsl', 'wsl2']:
        fresh_home()
        (home / '.docker').mkdir()
        (home / '.docker/config.json').write_text('{"credsStore": "desktop.exe"}')
        p, calls = run(platform, 'build-server')
        assert p.returncode != 0 and 'docker-credential-desktop.exe' in p.stderr and p.stderr.startswith('ERROR:'), p.stderr
        helper_dir = temp / 'Docker Desktop bin'
        helper_dir.mkdir(exist_ok=True)
        (helper_dir / 'docker-credential-desktop.exe').write_text('')
        p, calls = run(platform, 'build-server', PATH=f'{bin_dir}:/usr/bin:/bin:{helper_dir}')
        assert p.returncode == 0, p.stderr
        (home / '.docker/config.json').unlink()
        p, calls = run(platform, 'e2e-split-mode')
        docker = [c[1:-1] for c in calls if c[0] == 'docker']
        if platform == 'wsl':
            assert any(c[0] == 'create' for c in docker) and any(c[0] == 'cp' for c in docker) and not any('-v' in c for c in docker), docker
        else:
            assert any(c[0] == 'run' and '-v' in c for c in docker), docker
        assert p.returncode == 0, (platform, p.stdout, p.stderr)
        p, calls = run(platform, 'run-db-parity-tests', '--no-build', '-h')
        assert p.returncode == 0 and 'Usage' in p.stdout and not dn(calls)
        p, calls = run(platform, 'run-upgrade-test', '--keep', '--help')
        assert p.returncode == 0 and 'Usage' in p.stdout and not dn(calls)
        count += 5
    p, calls = run('wsl', 'run-db-parity-tests', '--providers', 'postgresql', MOCK_DOCKER_DOWN='1')
    assert p.returncode != 0 and 'WSL 1 cannot run a Docker engine' in p.stderr and not dn(calls)
    for platform in UNIX:
        p, calls = run(platform, 'run-db-parity-tests', '--providers', 'sqlite', '--framework', 'net8.0')
        assert any(c[0] == 'build' and 'net8.0' in c for c in dn(calls)) and any(c[0] == 'run' and 'Database' in c for c in dn(calls)), (platform, p.stdout, p.stderr)
        assert not [c for c in calls if c[0] == 'docker'], calls
        p, calls = run(platform, 'run-db-parity-tests', '--providers')
        assert p.returncode == 2 and not dn(calls)
        count += 2

    # ---- GUI: Harbor needs a display on Linux and WSL (WSLg on WSL 2, an X server on WSL 1) ----
    for platform in ['linux', 'wsl', 'wsl2']:
        for s in ['run-harbor', 'run-local']:
            p, calls = run(platform, s, drop=['DISPLAY'])
            assert p.returncode != 0 and not dn(calls), (platform, s, p.stderr)
            count += 1
    p, calls = run('wsl', 'run-harbor', drop=['DISPLAY'], WAYLAND_DISPLAY='wayland-0')
    assert p.returncode != 0 and 'X server' in p.stderr
    p, calls = run('wsl2', 'run-harbor', drop=['DISPLAY'], WAYLAND_DISPLAY='wayland-0')
    assert p.returncode == 0, p.stderr
    for platform in UNIX:
        for s, tail in [('run-harbor', ['--', '--minimized']), ('run-tui', ['--', 'tui', '--profile', 'work'])]:
            extra = tail[1:] if s == 'run-harbor' else tail[2:]
            p, calls = run(platform, s, 'net8.0', *extra)
            d = dn(calls)
            assert p.returncode == 0 and d[0][0] == 'build' and '-p:TargetFrameworks=net8.0' in d[0], (platform, s, d)
            assert d[1][0] == 'run' and d[1][-len(tail):] == tail and 'net8.0' in d[1], (platform, s, d)
            count += 1

    # ---- generate-* ----
    for platform in UNIX:
        for s, flag in [('generate-api-surface', '--generate-api-surface'), ('generate-blackbox-schema', '--generate-blackbox-schema')]:
            out = temp / 'gen-out'
            p, calls = run(platform, s, 'net8.0', '--out', str(out))
            run_call = next(c for c in dn(calls) if c[0] == 'run')
            assert p.returncode == 0 and run_call[-2:] == [flag, str(out)] and '-p:TargetFrameworks=net8.0' in run_call, (platform, s, run_call)
            p, calls = run(platform, s, '--out')
            assert p.returncode == 2 and not dn(calls)
            count += 2

# ---- Line endings, executable bits, shell syntax, Windows help ----
for f in root.glob('scripts/**/*'):
    if f.suffix in ('.bat', '.cmd', '.ps1') and 'node_modules' not in f.parts:
        data = f.read_bytes()
        assert data.count(b'\n') == data.count(b'\r\n'), f'{f} must be CRLF on disk'
    if f.suffix == '.sh' and f.parent.name in UNIX + ['common']:
        assert b'\r' not in f.read_bytes(), f'{f} has CR characters'
        assert os.access(f, os.X_OK) or f.name in ('_wsl-common.sh', 'resolve-framework.sh', 'resolve-insecure.sh'), f'{f} is not executable'
        subprocess.run(['bash', '-n', str(f)], check=True)
for f in (root / 'scripts/windows').glob('*.bat'):
    text = f.read_text()
    if f.stem in ('resolve-framework', 'resolve-insecure') or f.stem in ALIASES['windows']:
        continue
    assert '"%~1"=="-h"' in text or '"%ARG%"=="-h"' in text, f'{f.name} has no -h handling'
    assert 'setlocal' in text.lower(), f.name
    assert 'set "ARGS=%ARGS% %1"' not in text, f.name
for f in (root / 'scripts/windows').glob('*-startup.bat'):
    text = f.read_text()
    assert 'resolve-framework.bat" %*' in text and 'CurrentVersion\\Run' in text and 'schtasks /create' not in text, f.name
    # Paths reach PowerShell through the environment only; reg.exe with \" escapes is not used.
    assert 'reg add' not in text and 'call reg' not in text and '$env:STARTUP_VALUE_NAME' in text and 'sc.exe query armada' in text, f.name
    for line in text.splitlines():
        if line.lower().startswith('call powershell'):
            assert '%' not in line and '^' not in line, (f.name, line)
win = {n: (root / f'scripts/windows/{n}.bat').read_text() for n in ['factory-reset', 'e2e-split-mode', 'install-windows-task']}
assert 'set "CONFIRM="' in win['factory-reset'] and 'if not "!CONFIRM!"=="YES"' in win['factory-reset']
assert 'enabledelayedexpansion' not in win['e2e-split-mode'].lower()
assert 'update-startup.bat' in win['install-windows-task'] and 'install-startup.bat' in win['install-windows-task']
for f in (root / 'scripts/windows').glob('*.bat'):
    for line in f.read_text().splitlines():
        if line.strip().lower().startswith('echo error:') or ' ERROR:' in line and line.strip().lower().startswith('echo ['):
            assert line.rstrip().endswith('1>&2'), (f.name, line)

print(f'{count} script checks passed: same script set on all five OSes, OS guards, -h and unexpected arguments, '
      'install/reinstall/remove, update order, publish, MCP registration (WSL 2 host rewrite), health check, '
      'start-at-login on Linux (systemd and XDG autostart), macOS (launchd) and WSL 1/2 (Windows Startup launcher), '
      'stop/start helpers, factory reset, Docker and display requirements, generators, line endings and shell syntax.')
