# Releasing Armada

Cutting an Armada release means working through the checklist below, from a release candidate through the final tag. Work it top to
bottom and do not skip a gate because the previous release passed it. Most of what goes wrong in a release is
something that was green last time.

One rule sits above everything else: **version numbers change only on the maintainer's explicit instruction.** An
agent preparing a release does not bump `Version` in `src/Directory.Build.props`, `src/Armada.Helm/Armada.Helm.csproj`,
`ProductVersion` in `src/Armada.Core/Constants.cs`, the dashboard's `package.json` and `package-lock.json`, the build
scripts, the Postman collection, or the CHANGELOG heading on its own. It may propose a bump and explain why, then wait
for approval. The rules are in `VERSIONING.md` in the shared requirements; while Armada is `0.x`, releases carry the
`alpha` label unless the maintainer says otherwise.

## 1. Decide what is in the release

Pick the commit you intend to tag and make sure `main` (or the release branch) is at it. Read the `## Unreleased`
section of [CHANGELOG.md](../CHANGELOG.md) against the commits since the last tag. Every user-visible change, every
new or changed setting, every migration, and every breaking change belongs there. A release with an incomplete
CHANGELOG is not ready, however green the tests are.

If the release changes the database schema, confirm that the migration is registered for all four providers and that
[UPGRADING.md](UPGRADING.md) describes anything an operator must do by hand.

## 2. Continuous integration is green

`.github/workflows/ci.yml` runs on every push and pull request. For the release commit, every job must be green:

- `dotnet build src/Armada.sln -warnaserror` on Windows, macOS, and Linux. A new compiler warning fails the build.
- `Test.Automated` for both `net8.0` and `net10.0` on all three operating systems, with results uploaded as
  artifacts.
- The dashboard: `npm ci`, `npm run build`, and `npm run test:run` in `src/Armada.Dashboard`.
- The dist drift check, which fails when `src/Armada.Dashboard/dist` does not match a fresh build. The `dist/`
  folder is committed on purpose, so a forgotten rebuild ships a stale dashboard to anyone installing without Node.

A rerun that turns a red job green is not a pass until you know why it was red. Intermittent end-to-end failures are
tracked under W4.2 in [V1_READINESS.md](../V1_READINESS.md); note any you saw in the release notes.

## 3. Provider parity is green

`.github/workflows/nightly-parity.yml` runs `scripts/common/run-db-parity-tests.sh` every night against SQLite,
PostgreSQL, MySQL, and SQL Server on an Ubuntu runner. The most recent nightly run against the release commit (or a
manual dispatch of the workflow on it) must report `RESULT: PARITY OK`. To run it locally:

```bash
scripts/common/run-db-parity-tests.sh
```

Docker must be running. On Apple Silicon, SQL Server runs under emulation and can time out; trust the CI run on
native amd64 over a local one.

## 4. The upgrade test passes

Install the previous release, seed it with representative data (fleets, vessels, missions in every status, voyages,
merge queue entries, edited personas, pipelines, and prompt templates, Ask threads, fleet actions, health findings,
import batches), then upgrade to the candidate and confirm the data and the template edits survive. Do this on every
provider. The procedure and the automated job are described in [UPGRADING.md](UPGRADING.md) (W3.1).

## 5. Simulated user testing

Run a full session per `SIMULATED_USER_TESTING.md` from the shared requirements against the candidate. Use an
isolated `armada-usertest` stack with its own data directory and ports so it cannot touch a real install or a
developer's `~/.armada`. Triage every finding: S1 and S2 findings block the release; lower severities are either fixed
or recorded in [BACKLOG.md](BACKLOG.md) with a reason. Release candidates (`-rc.N`) also get at least a week of real
use on the maintainer's own repositories before promotion.

## 6. Build the packages

Packaging is driven by `publisher.json` and `src/Armada.Publisher`. The 1.0 install paths (decision D1) are:

| Platform | Channel | Artifact |
|----------|---------|----------|
| Any | Docker images (`build-all.sh` / `build-all.bat`) | Admiral, dashboard, proxy |
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
pushed with `build-all.sh <tag>` (or `build-admiral.sh` and `build-proxy.sh` separately) to `jchristn77/armada-server`
and `jchristn77/armada-proxy`.

Smoke-test at least one artifact per platform before publishing: install it on a clean machine or VM, start it, reach
a logged-in dashboard, and dispatch one mission.

## 7. Signing

Signing runs only when the credentials are present, and every channel logs a clear line when it falls back to an
unsigned build.

On Windows, the Inno and WiX channels Authenticode-sign their installers with `signtool` when `WINDOWS_CERT_BASE64`
(a base64-encoded PFX) and `WINDOWS_CERT_PASSWORD` are set. On macOS, the `.app`, `.dmg`, and `.pkg` are signed with
the Developer ID identity from the imported certificate (or `APPLE_SIGNING_IDENTITY` and `APPLE_INSTALLER_IDENTITY`)
and submitted for notarization and stapling when the `APPLE_CERT_*`
and `APPLE_NOTARY_*` secrets are set. Without them, the macOS app is ad-hoc signed (Apple Silicon will not run a
completely unsigned binary) and is not notarized. Linux packages are not signed individually; the apt and yum
repository metadata is signed with the GPG key in `GPG_PRIVATE_KEY`.

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
(`dotnet tool install -g Armada.Helm --version <version>`), and that `docker/update.sh` against the published images
brings up a healthy stack. Then add a dated row for the release to the Progress Log in
[V1_READINESS.md](../V1_READINESS.md) while that plan is active, and tell users about any manual upgrade steps.
