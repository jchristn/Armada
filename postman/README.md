# Model provider smoke simulation

The fixture models the native xAI Responses and embeddings contracts used by
PolyPrompt 3.5.0, following the upstream PolyPrompt `postman/xai.openapi.json` and
its xAI fixture. The owner's published mock search was unavailable because the
Postman CLI was signed out. This fixture runs locally without login or real keys.

```sh
postman simulation run postman/simulations/model-providers.sim.yaml --port 4918 --no-history --output ndjson
```

In another terminal:

```sh
ARMADA_MODEL_PROVIDER_SIMULATION=http://127.0.0.1:4918/xai \
  dotnet run --project src/Test.Automated -f net10.0 -- \
  --suites Services.ModelEndpoint,Runtimes.ApiAgent
```

The optional HTTP cases exercise real PolyPrompt clients, endpoint validation,
and an API captain's local file tool execution with encrypted reasoning replay.
Other paths return 501. This proves local contract behavior, not live provider
availability. Ollama Cloud's fixed HTTPS destination is not redirected; its
factory, storage, authentication, and form behavior have offline coverage.
Use an accessible hosted model for Cloud, and an explicit catalog model for xAI
embeddings. Model access and Cloud embedding support depend on the account.

The fixture also serves Ollama `/api/chat` reasoning responses. The Harbor HTTP case reproduces a
4,096-token reasoning-only `length` response, checks typed failure transport and remediation, and verifies
that an explicit 32,768-token budget can finish in one request without continuation. The synthetic
response follows Ollama's chat `message.thinking`, `done_reason`, and token count fields; it does not
claim to reproduce the quality or timing of a live DeepSeek answer.

The continuation-state regression uses the real Harbor job runner, API runtime, local file tools, and installed
PolyPrompt SDK over HTTP. The Ollama fixture rejects any loss or whitespace change in both prior assistant
thinking turns, missing native tool-result names, or a file tool that did not execute. A three-request run
includes sequential and parallel tools and completes without a retry. This is the regression for the upstream
3.4.0 reasoning-loss issue; it must be run with the simulation environment variable above.

# Black Box folder

The `Black Box` folder of `Armada.postman_collection.json` has one request per route in
`src/Armada.Server/Routes/BlackBoxRoutes.cs`, plus the mission read that carries `LastBlackBoxEntryId`.
Every request has `pm.test` assertions and saved examples captured from a real Admiral. The folder runs top to
bottom. The first request stores `blackboxEntryId`, `missionId`, `voyageId` and `blackboxFingerprint`.
`Read the Analysis section` stores `blackboxPathId`, and `List entry objects` stores `blackboxObjectName`.
The recovery requests run in a fixed order: the refusal without a token (409 `BlackBoxConfirmationRequired`),
then the dry run (stores the `bbc_` token in `blackboxConfirmToken`), then the confirmed run. The confirmed run
really restarts the mission, so use a throwaway Admiral. The delete requests run last and target
`blackboxDeleteId`. Its default, `bbx_doesnotexist_0000000000`, names no entry, so a run deletes nothing.

## Running it against a local Admiral

Start an Admiral on unused ports with a throwaway data directory. Ports come from `settings.json` in the
data directory; the Admiral fills in the other settings on first start:

```sh
export ARMADA_DATA_DIR=$(mktemp -d)
printf '{"admiralPort":17890,"mcpPort":17891}' > "$ARMADA_DATA_DIR/settings.json"
dotnet run --project src/Armada.Server -f net10.0
```

Seed one failed mission so an entry with a recovery path exists. Register a vessel first (`POST /api/v1/vessels`)
if the Admiral has none. With no captain registered, the mission stays Pending, so mark it failed in the
database and let the backfill capture it:

```sh
B=http://localhost:17890; A='Authorization: Bearer default'
M=$(curl -s -X POST $B/api/v1/missions -H "$A" -H 'Content-Type: application/json' \
  -d '{"Title":"Black box seed","Description":"Seed for the Postman run.","VesselId":"<vsl_ id>"}' | jq -r .Id)
sqlite3 "$ARMADA_DATA_DIR/armada.db" "update missions set status='Failed', failure_kind='Crash', \
  failure_reason='Captain process exited with code 134 while running dotnet test (System.AccessViolationException).', \
  last_update_utc=strftime('%Y-%m-%dT%H:%M:%fZ','now') where id='$M'"
curl -s -X POST $B/api/v1/blackbox/backfill -H "$A" -H 'Content-Type: application/json' -d '{"MaxSubjects":100}'
```

The new entry is a current `ProcessCrash` entry with the `bbx-exec-006.restart` recovery path. Run the folder:

```sh
postman collection run Armada.postman_collection.json -i "Black Box" \
  --env-var baseUrl=http://localhost:17890 --env-var bearerToken=default
```

`Find a current black box entry` takes the newest current entry whose step is `blackboxStep` (default
`ProcessCrash`). Pass `--env-var blackboxSeedMissionId=$M` to pin the run to the seeded mission, or
`--env-var blackboxStep=` to take the newest entry of any step. Do not pass `missionId` or `blackboxEntryId`
as `--env-var`: environment values shadow the collection variables that the scripts set. A second run against
the same entry passes too, but seed a fresh failure when you want the recovery path to restart a failed mission.

The voyage requests accept 404 when the Admiral has no voyage entry. `Apply black box recommendation (dry run)`
accepts 404 when the entry's analysis has no config recommendation. `Start black box storage migration`
copies objects to Disk without deleting the source, and it accepts 409 while a migration is running.

## Black box mock

`postman/mocks/blackbox` replays the folder's saved examples (`responses.json`) without an Admiral. Use it to
develop the dashboard and the VS Code extension against the black box routes:

```sh
postman mock run postman/mocks/blackbox --port 4931 --no-history
curl -s http://localhost:4931/api/v1/blackbox?pageSize=1 -H 'Authorization: Bearer default'
postman collection run Armada.postman_collection.json -i "Black Box" --env-var baseUrl=http://localhost:4931
```

The mock routes on method and path. Every id returns the same sample entry, and ids starting
`bbx_doesnotexist` return 404 `BlackBoxNotFound`. `GET /api/v1/blackbox` filters the sample page by `step`,
`subjectType`, `missionId`, `isCurrent` and the other list filters, and it pages the result. The recovery route
keeps the confirm-token contract: a request without a token gets 409, a dry run issues a fresh `bbc_` token,
and a token works once. Any request needs an `Authorization: Bearer` header. Paths outside the black box
routes return 501. The mock replays example data only and holds no state, apart from the confirm tokens.
