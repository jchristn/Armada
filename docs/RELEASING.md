# Releasing Armada

Cutting an Armada release means working through the checklist below, from a release candidate through the final tag. Work it top to
bottom and do not skip a gate because the previous release passed it. Most of what goes wrong in a release is
something that was green last time.

One rule sits above everything else: **version numbers change only on the maintainer's explicit instruction.** An
agent preparing a release does not bump `Version` in `src/Directory.Build.props`, `src/Armada.Helm/Armada.Helm.csproj`,
`ProductVersion` in `src/Armada.Core/Constants.cs`, the dashboard's `package.json` and `package-lock.json`, the build
scripts, the Postman collection, or the CHANGELOG heading on its own. It may propose a bump and explain why, then wait
for approval. The rules are in `VERSIONING.md` in the shared requirements. The tree is at `1.0.0`; release
candidates (`1.0.0-rc.N`) and any other pre-release label are applied only on the maintainer's instruction.

## 1. Decide what is in the release

Pick the commit you intend to tag and make sure `main` (or the release branch) is at it. Read the `## Unreleased`
section of [CHANGELOG.md](../CHANGELOG.md) against the commits since the last tag. Every user-visible change, every
new or changed setting, every migration, and every breaking change belongs there. A release with an incomplete
CHANGELOG is not ready, however green the tests are.

If the release changes the database schema, confirm that the migration is registered for all four providers and that
[UPGRADING.md](UPGRADING.md) describes anything an operator must do by hand.

## 2. Continuous integration is green

`.github/workflows/ci.yml` runs on every push and pull request. For the release commit, every job must be green:

- `dotnet build src/Armada.sln -c Release -warnaserror` on Windows, macOS, and Linux (NuGet vulnerability-audit
  warnings `NU1900`-`NU1904` are left as warnings). A new compiler warning fails the build.
- `Test.Automated` for both `net8.0` and `net10.0` on all three operating systems, with results uploaded as
  artifacts.
- The dashboard: `npm ci`, `npm run build`, and `npm run test:run` in `src/Armada.Dashboard`.
- The dist drift check, which fails when `src/Armada.Dashboard/dist` does not match a fresh build. The `dist/`
  folder is committed on purpose, so a forgotten rebuild ships a stale dashboard to anyone installing without Node.
- The TUI parity gate: `python3 scripts/tui/generate-parity-manifest.py --check` (the manifest matches the dashboard
  source and has no planned entries).
- The compose check: every file in `docker/` renders with `docker compose config`, and each still refuses to render
  without its required secret (`ARMADA_INITIAL_ADMIN_PASSWORD`, `ARMADA_PROXY_PASSWORD`).

A rerun that turns a red job green is not a pass until you know why it was red. Intermittent end-to-end failures are
tracked under W4.2 in [V1_READINESS.md](../V1_READINESS.md); note any you saw in the release notes.

## 3. Provider parity is green

`.github/workflows/nightly-parity.yml` runs `scripts/common/run-db-parity-tests.sh` every night against SQLite,
PostgreSQL, MySQL, and SQL Server on an Ubuntu runner. The most recent nightly run against the release commit (or a
manual dispatch of the workflow on it) must report `RESULT: PARITY OK`. To run it locally:

```bash
scripts/common/run-db-parity-tests.sh                                # all four providers
scripts/common/run-db-parity-tests.sh --providers sqlite,postgresql   # a subset
```

The script also takes `--framework` (default `net10.0`) and `--no-build`. Docker must be running for the server
providers. On Apple Silicon, SQL Server runs under emulation and can time out; trust the CI run on
native amd64 over a local one.

## 4. The upgrade test passes

`scripts/common/run-upgrade-test.sh --providers all` (Windows: `scripts\windows\run-upgrade-test.bat`, through Git
Bash) builds the previous release (default the `v0.9.0` tag), seeds it with representative data (fleets, vessels, a
captain, missions in every status, voyages, merge queue entries, edited personas, pipelines, and prompt templates,
signals, a backlog objective, and a second tenant), upgrades the database with the candidate, and runs the
`Upgrade.FromBaseline` suite on SQLite, PostgreSQL, MySQL, and SQL Server. It must pass on every provider. Use
`--from-ref <ref>` for another baseline. Details are in [UPGRADING.md](UPGRADING.md#testing-an-upgrade) (W3.1).

## 5. Simulated user testing

Run a full session per [SIMULATED_USER_TESTING.md](../SIMULATED_USER_TESTING.md) against the candidate (the 1.0
results are in [SIMULATED_USER_TESTING_RESULTS_1.0.md](SIMULATED_USER_TESTING_RESULTS_1.0.md)). Use an
isolated `armada-usertest` stack with its own data directory and ports so it cannot touch a real install or a
developer's `~/.armada`. Triage every finding: S1 and S2 findings block the release; lower severities are either fixed
or recorded in [BACKLOG.md](BACKLOG.md) with a reason. Release candidates (`-rc.N`) also get at least a week of real
use on the maintainer's own repositories before promotion.

## 6. Build the packages

Packaging is driven by `publisher.json` and `src/Armada.Publisher`. The 1.0 install paths (decision D1) are:

| Platform | Channel | Artifact |
|----------|---------|----------|
| Any | Docker images (`scripts/<os>/build-all`, run by hand) | Admiral, dashboard, proxy |
| Any | NuGet global tool (`nuget-cli`) | CLI (`armada`) |
| Windows | Inno Setup (`inno-harbor`) | Harbor `.exe` installer |
| Windows | WiX (`wix-server`) | Admiral `.msi` |
| macOS | `.app` in a `.dmg` (`dmg-harbor`) | Harbor |
| macOS | `.pkg` (`pkg-server`) | Admiral |
| Linux | fpm (`linux-cli`, `linux-harbor`, `linux-server`) | `.deb` and `.rpm` for all three |

Homebrew, Scoop, Chocolatey, winget, and AppImage channels are disabled in `publisher.json` until they are
implemented. Do not re-enable one for a release without implementing and testing it.

The normal path is CI: pushing the tag (step 9) runs `.github/workflows/release.yml`, which fans out to Windows,
macOS, and Linux runners and packages each OS's channels natively. To build locally, run the preflight and then the
script for your OS, passing the version you were told to release:

```bash
dotnet run --project src/Armada.Publisher -- doctor
./build-installers.sh <version>          # macOS or Linux
build-installers.bat <version>           # Windows
```

Installers land in `installers/<version>/`. Each OS builds only its own formats; see
[BUILDING_INSTALLERS.md](../BUILDING_INSTALLERS.md) for the toolchain per platform. Docker images are built and
pushed with `scripts/macos/build-all.sh <tag>` (or the `linux` / `windows` equivalent) to `jchristn77/armada-server`,
`jchristn77/armada-dashboard`, and `jchristn77/armada-proxy`; the repository-root `build-all.sh <tag>` builds the
Admiral and proxy images only (see [DOCKER.md](DOCKER.md#building-images-from-source)). The release workflow does not
build or push Docker images, so run one of these for every release. The `scripts/` proxy build is `linux/amd64`
only; use the repository-root `build-proxy.sh` for a multi-architecture proxy image.

On macOS, `scripts/macos/build-harbor-app.sh` builds the Harbor `.app` and `.dmg` for both architectures and checks
the result: the image verifies, the drag-to-install layout, every required `Info.plist` key (bundle id, version,
`LSUIElement`), the `.icns`, the code signature, and `--install-startup --dry-run` from the bundled binary. It reports
whether the image is Developer ID signed, notarized, and stapled; pass `--require-notarized` for a release build so an
unsigned image fails.

Then run the install verification below on the release commit, and smoke-test at least one built artifact per
platform by hand before publishing: install it on a clean machine or VM, start it, reach a logged-in dashboard, and
dispatch one mission.

### Install verification

`.github/workflows/install-verify.yml` installs every supported path on a clean runner or container and drives it
end to end: the Admiral becomes healthy, `/dashboard` serves the React build and its script loads, `admin@armada`
logs in through `POST /api/v1/authenticate` and the session works on `GET /api/v1/whoami`, a fleet and a vessel are
created from a temporary bare git repository, and one voyage with one mission is dispatched. The mission runs on an
API-endpoint captain whose model endpoint is `scripts/common/install-verify/stub_inference.py`, a stub
OpenAI-compatible server that asks the captain to write `INSTALL_SMOKE.md` and commit it, so no model or API key is
involved. The mission must reach `WorkProduced` (or later) and its diff must contain the file. The workflow runs on
pushes to `main` and pull requests that touch packaging, weekly, and on demand; the most recent run against the release
commit must be green.

| Path | Script | What it installs | Where |
|------|--------|------------------|-------|
| Docker | `verify-docker.sh` | `docker/armada/compose.yaml` built from the checkout, under a throwaway project with its own ports and data | ubuntu runner; local with Docker |
| Linux packages | `verify-linux-package.sh --format deb` / `--format rpm` | `linux-server` package installed with `apt-get` in `ubuntu:24.04` or `dnf` in `fedora:42`, plus `--install-service --dry-run` | ubuntu runner; local with Docker (builds with fpm in a container when fpm is missing) |
| NuGet global tool | `verify-dotnet-tool.sh` | `Armada.Helm` packed locally and installed with `dotnet tool install --tool-path`, then `armada server start` / `stop` | ubuntu and macOS runners; local |
| macOS server `.pkg` | `verify-macos-pkg.sh` | `pkg-server` built, expanded with `pkgutil`, payload checked and installed into a temp root, plus `--install-service --dry-run` | macOS runner; local Mac |
| Windows | `verify-windows.ps1` | the NuGet global tool as above, plus `Armada.Server.exe --install-service --dry-run` | windows runner |
| First-run (onboarding) | `verify-onboarding.sh` | the NuGet tool into a fresh `HOME` and data directory, then the first-run path to a mission landed (LocalMerge) into a local checkout, timed against the ten-minute goal | ubuntu and macOS runners; local |

To run one locally (each needs the .NET SDK, git, and Python 3; Docker for the first two):

```bash
scripts/common/install-verify/verify-docker.sh
scripts/common/install-verify/verify-linux-package.sh --format deb
scripts/common/install-verify/verify-linux-package.sh --format rpm
scripts/common/install-verify/verify-dotnet-tool.sh
scripts/common/install-verify/verify-macos-pkg.sh            # macOS only
pwsh scripts/common/install-verify/verify-windows.ps1        # Windows only
scripts/common/install-verify/verify-onboarding.sh           # prints a stage timing table
```

The scripts never touch `~/.armada`, the global dotnet tool directory, `docker/armada/db`, or any system location:
each uses a temp directory for `ARMADA_DATA_DIR` and `HOME`, binds only ports in 34000-34100 on 127.0.0.1 (move the
range with `IV_PORT_BASE`), installs the macOS payload into a temp root instead of running `installer`, uses
`--dry-run` for every service flag, and stops what it started by PID or compose project name. Pass `--keep` to keep
the temp directory (and, for Docker, the stack) for inspection. A script prints `PASS` or `FAIL` per step and exits
non-zero on any failure.

`verify-onboarding.sh` fails when any step fails or when the total exceeds `ONBOARDING_BUDGET_SECONDS` (600 by
default). With the stub captain the whole path takes seconds, so a run that approaches the budget is a regression to
investigate even when it passes.

Not covered: the Windows `.msi` and Inno installers (a real install registers a service and a login item), installing
the Harbor `.dmg` and Harbor packages (Harbor needs a desktop session; `build-harbor-app.sh` checks the built image
only), real service registration, and CLI captains (Claude Code, Codex, and the others need their own logins).
Exercise those by hand on a clean machine as above.

### Onboarding by hand with a real captain

Once per release candidate, time the first-run path as a new user would, on a machine with no `~/.armada` (or with
`ARMADA_DATA_DIR` pointed at an empty directory) and a working Claude Code login:

1. Start a timer. Install: `dotnet tool install -g Armada.Helm` (or the platform package).
2. `armada server start`; open `http://localhost:7890/dashboard` and sign in as `admin@armada` / `password`; change the
   password when asked.
3. Create a fleet, then a vessel from a local checkout (repository URL and working directory both set to the
   checkout, landing mode LocalMerge).
4. Add a captain with the Claude Code runtime, and dispatch a voyage with one small mission, for example
   "Add a CONTRIBUTORS.md file listing the maintainer".
5. Stop the timer when the mission shows Complete and the file is committed in the checkout.

Record the time and anything that made you stop and think in the release notes. Over ten minutes, or any step that
needed documentation the dashboard did not point to, is an S2 onboarding finding.

## 7. Signing

Signing runs only when the credentials are present, and every channel logs a clear line when it falls back to an
unsigned build.

On Windows, the Inno and WiX channels Authenticode-sign their installers with `signtool` when `WINDOWS_CERT_BASE64`
(a base64-encoded PFX) and `WINDOWS_CERT_PASSWORD` are set. On macOS, the `.app`, `.dmg`, and `.pkg` are signed with
the Developer ID identity from the imported certificate (or `APPLE_SIGNING_IDENTITY` and `APPLE_INSTALLER_IDENTITY`)
and submitted for notarization and stapling when the `APPLE_CERT_*`
and `APPLE_NOTARY_*` secrets are set. The certificates, App Store Connect key, environment variables, and
`publisher.json` keys are listed in [BUILDING_INSTALLERS.md](../BUILDING_INSTALLERS.md#macos-signing-and-notarization). Without them, the macOS app is ad-hoc signed (Apple Silicon will not run a
completely unsigned binary) and is not notarized. Linux `.deb` and `.rpm` packages are not signed in 1.0, and there
is no apt or yum repository: `publisher.json` reserves `GPG_PRIVATE_KEY` and `REPO_SYNC_CREDENTIALS` (the release
workflow passes both to the Linux job) for repository signing that is not implemented yet.

When a release ships unsigned on any platform, the release notes must carry the unsigned-software notice: Windows
users click **More info**, then **Run anyway** on the SmartScreen dialog; macOS users open **System Settings**,
**Privacy & Security**, and click **Open Anyway**, or run `xattr -dr com.apple.quarantine "/Applications/Armada
Harbor.app"`.

## 8. Checksums

The publisher writes a `.sha256` sidecar next to each built package and a `SHA256SUMS` file per channel, and the
build scripts regenerate `installers/<version>/SHA256SUMS` over everything they collected. In CI, the final
`release` job of `.github/workflows/release.yml` gathers the installers from all three OS jobs into one directory,
writes a single `SHA256SUMS` covering every file, and attaches both to the GitHub Release. After the release is
published, download a few artifacts from the Release page and verify them:

```bash
shasum -a 256 -c SHA256SUMS --ignore-missing      # macOS
sha256sum -c SHA256SUMS --ignore-missing          # Linux
```

If you built any installer by hand outside CI, regenerate the manifest before attaching it
(`dotnet run --project src/Armada.Publisher -- checksums --dir <directory>`); a stale `SHA256SUMS` is worse than none.

## 9. CHANGELOG and tag

Once the maintainer has approved the version number:

1. Rename `## Unreleased` in CHANGELOG.md to the release heading and start a fresh `## Unreleased` above it.
2. Commit, then tag the exact commit that passed steps 2 through 5:

   ```bash
   git tag v<version>
   git push origin v<version>
   ```

The tag triggers `.github/workflows/release.yml`, which builds, signs (when secrets exist), packages, and attaches
everything to a GitHub Release. The workflow can also be dispatched by hand with a version input. Watch it to the
end; a release with one failed OS job is a partial release and should be fixed or pulled, not left up.

## 10. After the release

Confirm the GitHub Release lists every expected artifact and `SHA256SUMS`, that the NuGet package is live
(`dotnet tool install -g Armada.Helm --version <version>`), that the Docker Hub tags `v<version>` and `latest` exist
for `jchristn77/armada-server`, `jchristn77/armada-dashboard`, and `jchristn77/armada-proxy`, and that `docker/update.sh`
from the tagged checkout brings up a healthy stack (the compose files build from source, so this checks the tagged
code rather than the pushed images). Then add a dated row for the release to the Progress Log in
[V1_READINESS.md](../V1_READINESS.md) while that plan is active, and tell users about any manual upgrade steps.
