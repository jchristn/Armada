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

## Windows

Produces the Harbor `.exe` (Inno), Server `.msi` (WiX), Chocolatey, winget, and the CLI NuGet tool.

```powershell
choco install innosetup -y
dotnet tool install --global wix
.\build-installers.bat 0.9.0
```

## macOS

Produces the Harbor `.dmg`, Server `.pkg`, and the Homebrew cask.

```bash
brew install create-dmg
chmod +x build-installers.sh
./build-installers.sh 0.9.0
```

## Linux

Produces `.deb` / `.rpm` (all three components) and the CLI AppImage.

```bash
sudo gem install --no-document fpm
# AppImage:
curl -L -o /usr/local/bin/appimagetool \
  https://github.com/AppImage/AppImageKit/releases/download/continuous/appimagetool-x86_64.AppImage
chmod +x /usr/local/bin/appimagetool
chmod +x build-installers.sh
./build-installers.sh 0.9.0
```

## Where the installers land

The version is the single argument you pass; it is never stored in the manifest. Finished installers are
flattened into a version-specific directory:

```
installers/
  0.9.0/
    armada-harbor-0.9.0-win-x64.exe
    armada-server-0.9.0-win-x64.msi
    armada-harbor_0.9.0_amd64.deb
    armada-0.9.0-x86_64.AppImage
    Armada.Helm.0.9.0.nupkg
    ...
  _work/            <- intermediate publish + per-channel output (safe to delete)
```

`installers/` is git-ignored. Each script builds every channel its OS owns, records a Built/Failed
summary, and continues past any channel whose tool is missing or whose recipe is unimplemented.

## Source assets (where inputs live)

- **`publisher.json`** (repo root) - artifacts, channels, RID matrix, and signing secret names.
- **App icon:** `src/Armada.Harbor/Assets/logo.ico` (referenced by `artifacts[].icon`).
- **Packaging recipes:** `src/Armada.Publisher/Channels/` (one class per channel).

## All three OSes at once (CI)

Pushing a tag runs the GitHub Actions matrix (`windows`/`macos`/`ubuntu`), which builds, signs, and uploads
every installer to a GitHub Release. This is the normal release path.

```bash
git tag v0.9.0 && git push origin v0.9.0
```

## Signing

Signing runs only when the corresponding secrets are set (`WINDOWS_CERT_*`, `APPLE_*`); otherwise artifacts
are produced unsigned. Windows unsigned installers trip SmartScreen; unsigned macOS apps are blocked by
Gatekeeper until the user clears quarantine. Linux repo metadata is GPG-signed (free) and is unaffected.
