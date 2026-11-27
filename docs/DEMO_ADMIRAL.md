# Demo Admiral

A demo Admiral holds representative, fictional content so app store reviewers and store screenshots see a working
fleet on first launch. `src/Armada.DemoSeed` writes it into an empty data directory through the database driver, so the
schema matches a real install. Nothing in it is real: repositories point at `https://github.com/example/...` (they do
not exist), the open model endpoint is `https://models.example.test/v1`, and the users are `admin@armada` and
`reviewer@example.com`.

## What it seeds

- Fleets **Platform** and **Apps**, five vessels with repository health.
- Captains on open and frontier models: two Qwen3.8 Workers on an OpenAI-compatible endpoint (Economy tier), GPT-6.1
  and Claude Sonnet Workers, and Claude Opus Architect and Judge (Premium).
- A model profile per model with a tier, speed figures, and a completed evaluation, so each profile has a scorecard.
- The pipeline **Mixed open and frontier** (Architect, Worker, Judge).
- Five voyages: one running the mixed pipeline, one in flight, one complete, one failed, one cancelled. Missions are
  Pending, Testing, WorkProduced, PullRequestOpen, Review (two approvals waiting), Complete, Failed, LandingFailed,
  Cancelled and Blocked.
- One mission with three attempts: Qwen3.8 needed an output repair, then failed its tests and was escalated to GPT-6.1,
  which finished it. Its id is `MISSION_ID` for the Attempts screenshot.
- Two weeks of stage outcomes per captain, so the Reliability summary has numbers per model and persona.
- The admin password, and a non-admin reviewer user that owns the seeded rows (so it sees them all).

The seeded rows stay as they are while the Admiral runs. InProgress and Assigned missions are not seeded: without a
live agent process the Admiral treats them as interrupted and re-dispatches them. Pending missions wait on in-flight
ones, autonomous rescue is off (`maxMissionRecoveryAttempts` 0), and no vessel can be cloned, so no agent ever starts.

## Run it locally

```bash
export ARMADA_DEMO_ADMIN_PASSWORD='<8+ characters>'
export ARMADA_DEMO_REVIEWER_PASSWORD='<8+ characters>'
dotnet run --project src/Armada.DemoSeed -f net10.0 -- --data-dir /tmp/armada-demo --admiral-port 17890 --mcp-port 17891
ARMADA_DATA_DIR=/tmp/armada-demo dotnet run --project src/Armada.Server -f net10.0
```

The seeder refuses a directory that already has a database. It prints `MISSION_ID`, `VOYAGE_ID` and `PROFILE_ID` and
writes them to `demo-ids.json` in the data directory. Sign in with `reviewer@example.com` (tenant `default`) or
`admin@armada`. The iOS simulator reaches the Mac's `127.0.0.1`; the Android emulator uses `10.0.2.2`.

Options: `--hostname` (default `127.0.0.1`; `0.0.0.0` in a container), `--settings-data-dir` (the data path the Admiral
sees, for example `/app/data`), `--reviewer-email`, `--tunnel-url` and `--instance-id` for remote control, with the
tunnel password in `ARMADA_DEMO_TUNNEL_PASSWORD`. Passwords come only from the arguments or the environment.

## Store screenshots

With the demo Admiral running and the app signed in as the reviewer:

```bash
eval "$(python3 -c 'import json;d=json.load(open("/tmp/armada-demo/demo-ids.json"));print(" ".join("export %s=%s" % kv for kv in d.items()))')"
scripts/mobile/capture-store-screenshots.sh ios-6.9
```

See `src/Armada.Mobile/store/README.md` for the targets and devices.

## Hosted demo for reviewers

`docker/demo/compose.yaml` runs `armada-server` on loopback port 7890 with the seeded `./data`, capped at half a CPU and
512 MB so it cannot starve the host it shares (the remote-access proxy host). `docker/demo/reset.sh` stops it, reseeds
`./data` with the seeder from this checkout (in the .NET SDK image), and starts it again; run it once to create the
demo and nightly from cron to undo reviewer changes. Put the passwords, and `ARMADA_DEMO_TUNNEL_URL` with
`ARMADA_DEMO_TUNNEL_PASSWORD` to reach it through the proxy, in `docker/demo/.env` (git-ignored). Reviewer
credentials go only in the store consoles and the private store notes. `../update.sh demo/compose.yaml` pulls a newer
server image without reseeding.
