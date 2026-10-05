# Building Installers

Armada ships three deliverables: the **CLI** (`armada`), **Harbor** (tray app), and the **Admiral Server**.
Installers are produced by the `Armada.Publisher` app, driven by `publisher.json`, via the `build-installers`
scripts. Each OS can only build its own native formats, so build on all three OSes (or use CI).

## Prerequisites

- **.NET 10 SDK** on every build machine.
- The packaging tool for each channel (installed per OS below). Run the preflight to see what is missing:
  ```
  dotnet run --project src/Armada.Publisher -- doctor
  ```

## Supported install paths

Armada 1.0 ships these install paths and no others (decision D1 in `V1_READINESS.md`):

| Platform | Deliverable | Channel in `publisher.json` |
|----------|-------------|-----------------------------|
| Any | CLI as a .NET global tool (`dotnet tool install -g Armada.Helm`) | `nuget-cli` |
| Windows | Harbor `.exe` (Inno Setup), Server `.msi` (WiX) | `inno-harbor`, `wix-server` |
| macOS | Harbor `.app` in a `.dmg`, Server `.pkg` | `dmg-harbor`, `pkg-server` |
| Linux | `.deb` and `.rpm` for the CLI, Harbor, and Server | `linux-cli`, `linux-harbor`, `linux-server` |
| Docker | Admiral, dashboard, and proxy images | `docker/` (see [docs/DOCKER.md](docs/DOCKER.md)) |

Homebrew, Scoop, Chocolatey, winget, and AppImage channels are still declared in `publisher.json` but are
disabled (`"enabled": false`) because their recipes are not implemented. `--all` skips them, and naming one with
`--channel` prints a skip line instead of failing.

## Windows

Produces the Harbor `.exe` (Inno), the Server `.msi` (WiX), and the CLI NuGet tool.

```powershell
choco install innosetup -y
dotnet tool install --global wix
.\build-installers.bat 1.0.0
```

The WiX recipe (`src/Armada.Publisher/Channels/WixChannel.cs`) generates a `.wxs` per Windows runtime next to the
`.msi`, uses a stable UpgradeCode derived from the product, artifact, and runtime so new versions upgrade in place,
and needs WiX v5 or later (it harvests the publish directory with the `Files` element).

## macOS

Produces the Harbor `.app` bundle wrapped in a `.dmg`, and the Server `.pkg`. Everything used ships with macOS and
the Xcode command-line tools (`hdiutil`, `sips`, `iconutil`, `codesign`, `pkgbuild`, `productbuild`, `xcrun`); there is
nothing to `brew install`.

```bash
xcode-select --install   # once, if the command-line tools are missing
chmod +x build-installers.sh
./build-installers.sh 1.0.0
```

- **`Armada Harbor.app`**: `Contents/Info.plist` (bundle id `com.joelchristner.armada.harbor`), the self-contained
  binary in `Contents/MacOS`, and `Contents/Resources/AppIcon.icns` generated from
  `src/Armada.Harbor/Assets/logo-macos.png`. The `.dmg` is a compressed read-only image with the app and an
  `Applications` shortcut.
- **Server `.pkg`**: installs the binary to `/usr/local/lib/armada-server/`, a symlink at
  `/usr/local/bin/armada-server`, and a LaunchAgent at `/Library/LaunchAgents/com.joelchristner.armada.server.plist`
  that runs the Admiral in each user's login session (captains need the user's home directory, git credentials,
  and agent CLI logins, so the server does not run as root). The postinstall script loads the agent for the
  logged-in user. `sudo /usr/local/lib/armada-server/uninstall.sh` removes the package and leaves `~/.armada` alone.

## Linux

Produces `.deb` / `.rpm` for all three components.

```bash
sudo gem install --no-document fpm
chmod +x build-installers.sh
./build-installers.sh 1.0.0
```

## Where the installers land

The version is the single argument you pass; it is never stored in the manifest. Finished installers are
flattened into a version-specific directory with a `SHA256SUMS` manifest:

```
installers/
  1.0.0/
    armada-harbor-1.0.0-win-x64.exe
    armada-server-1.0.0-win-x64.msi
    armada-harbor-1.0.0-osx-arm64.dmg
    armada-server-1.0.0-osx-arm64.pkg
    armada-harbor_1.0.0_amd64.deb
    Armada.Helm.1.0.0.nupkg
    SHA256SUMS
    ...
  _work/            <- intermediate publish + per-channel output (safe to delete)
```

`installers/` is git-ignored. Each script builds every channel its OS owns, records a Built/Failed
summary, and continues past any channel whose tool is missing.

## Checksums

Every channel writes a `<file>.sha256` sidecar next to each package and a `SHA256SUMS` file in its output
directory. The build scripts regenerate `installers/<version>/SHA256SUMS` over the collected files, and the release
workflow writes one combined `SHA256SUMS` covering every OS and attaches it to the GitHub Release. Verify a
download with `sha256sum -c SHA256SUMS --ignore-missing` (Linux) or `shasum -a 256 -c SHA256SUMS` (macOS). To
regenerate the manifest for any directory:

```
dotnet run --project src/Armada.Publisher -- checksums --dir installers/1.0.0
```

## Source assets (where inputs live)

- **`publisher.json`** (repo root) - artifacts, channels, RID matrix, and signing secret names.
- **App icon:** `src/Armada.Harbor/Assets/logo.ico` (referenced by `artifacts[].icon`).
- **Packaging recipes:** `src/Armada.Publisher/Channels/` (one class per channel).

## All three OSes at once (CI)

Pushing a tag runs the GitHub Actions matrix (`windows`/`macos`/`ubuntu`), which builds, signs, and uploads
every installer to a GitHub Release. This is the normal release path.

```bash
git tag v1.0.0 && git push origin v1.0.0
```

## Signing

Signing runs only when the corresponding secrets are present; otherwise artifacts are produced unsigned and the
Publisher prints a line saying so.

- **Windows (Authenticode):** `WINDOWS_CERT_BASE64` (base64 PFX) and `WINDOWS_CERT_PASSWORD`. Both the Inno `.exe`
  and the WiX `.msi` are signed with `signtool` and an RFC 3161 timestamp. Unsigned installers trip SmartScreen
  ("More info", then "Run anyway").
- **macOS (Developer ID):** `APPLE_CERT_BASE64` (base64 `.p12` holding the Developer ID Application and, for the
  `.pkg`, Developer ID Installer certificates) and `APPLE_CERT_PASSWORD`. The certificate is imported into a
  temporary keychain that is deleted afterward; identities are discovered from it unless
  `APPLE_SIGNING_IDENTITY` / `APPLE_INSTALLER_IDENTITY` name them. On a workstation whose login keychain already
  holds the identities, set only `APPLE_SIGNING_IDENTITY` (and `APPLE_INSTALLER_IDENTITY`). Signed binaries use
  the hardened runtime with the JIT and library-validation entitlements .NET needs.
- **macOS notarization:** `APPLE_NOTARY_KEY` (App Store Connect API key, `.p8` contents), `APPLE_NOTARY_KEY_ID`,
  and `APPLE_NOTARY_ISSUER`. With a signing identity and these set, each `.dmg` and signed `.pkg` is submitted with
  `xcrun notarytool submit --wait` and stapled.
- **Without Apple credentials** the `.app` and the server binary are ad-hoc signed (`codesign -s -`), which Apple
  Silicon requires before it will run anything, and the `.dmg` and `.pkg` are unsigned. Gatekeeper then blocks the
  first open: use **System Settings > Privacy & Security > Open Anyway**, or run
  `xattr -dr com.apple.quarantine "/Applications/Armada Harbor.app"`.
- **Linux:** repository metadata is GPG-signed (free) and is unaffected.
