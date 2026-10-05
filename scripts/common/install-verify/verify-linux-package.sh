#!/usr/bin/env bash
# =====================================================================
# verify-linux-package.sh -- install verification for the Linux server
# packages (linux-server channel: .deb and .rpm) (V1 readiness W5.5).
#
# 1. Builds the packages with Armada.Publisher, unless --package is given:
#    natively on Linux when fpm is installed, otherwise inside a throwaway
#    .NET SDK container with fpm (so it also works from macOS).
# 2. Starts a clean distro container (ubuntu:24.04 for .deb, fedora:42 for
#    .rpm), installs the package with apt-get / dnf, and runs
#    in-container-package-test.sh there: package contents, the
#    /usr/bin/armada-server symlink, "--install-service --dry-run", then
#    the installed server plus the REST smoke test (login, dashboard,
#    fleet, vessel, one mission on a stub-inference captain).
# Containers run with --rm under unique names and are removed on exit.
#
# Usage: verify-linux-package.sh [--format deb|rpm] [--package <file>]
#                                [--packages-dir <dir>] [--version 0.9.0] [--keep]
#   --packages-dir: reuse a package already in <dir>, or copy the freshly
#   built .deb/.rpm files there (build once, test both formats).
# =====================================================================
set -uo pipefail
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/lib.sh"

FORMAT="deb"
PACKAGE=""
PACKAGES_DIR=""
VERSION=""
KEEP=0
while [ $# -gt 0 ]; do
  case "$1" in
    --format) FORMAT="$2"; shift 2 ;;
    --package) PACKAGE="$2"; shift 2 ;;
    --packages-dir) PACKAGES_DIR="$2"; shift 2 ;;
    --version) VERSION="$2"; shift 2 ;;
    --keep) KEEP=1; shift ;;
    -h|--help) grep '^#' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
done
case "$FORMAT" in deb|rpm) ;; *) iv_die "--format must be deb or rpm" ;; esac
command -v docker >/dev/null 2>&1 || iv_die "docker is required"
if [ -z "$VERSION" ]; then
  VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$IV_REPO_ROOT/src/Directory.Build.props" | head -1)"
fi

# The distro container runs the host's native architecture.
case "$(uname -m)" in
  arm64|aarch64) DEB_ARCH="arm64"; RPM_ARCH="aarch64" ;;
  *) DEB_ARCH="amd64"; RPM_ARCH="x86_64" ;;
esac

find_package() {
  if [ "$FORMAT" = "deb" ]; then find "$1" -name "armada-server_*_${DEB_ARCH}.deb" 2>/dev/null | head -1
  else find "$1" -name "armada-server-*.${RPM_ARCH}.rpm" 2>/dev/null | head -1; fi
}
if [ -z "$PACKAGE" ] && [ -n "$PACKAGES_DIR" ] && [ -d "$PACKAGES_DIR" ]; then
  PACKAGE="$(find_package "$PACKAGES_DIR")"
fi

IV_WORK="$(mktemp -d "${TMPDIR:-/tmp}/armada-iv-linux.XXXXXX")"
BUILDER="armada-iv-pkgbuild-$$"
TESTER="armada-iv-pkgtest-$$"
cleanup() {
  docker rm -f "$BUILDER" "$TESTER" >/dev/null 2>&1 || true
  if [ "$KEEP" -eq 0 ]; then
    rm -rf "$IV_WORK" 2>/dev/null || docker run --rm -v "$IV_WORK:/w" busybox:1.37 rm -rf /w/out /w/src >/dev/null 2>&1 || true
    rm -rf "$IV_WORK" 2>/dev/null || true
  else
    iv_log "work directory kept: $IV_WORK"
  fi
}
trap cleanup EXIT

if [ -z "$PACKAGE" ]; then
  if [ "$(uname -s)" = "Linux" ] && command -v fpm >/dev/null 2>&1; then
    iv_log "building linux-server $VERSION natively with Armada.Publisher"
    ( cd "$IV_REPO_ROOT" && dotnet run --project src/Armada.Publisher -c Release -- \
        --channel linux-server --version "$VERSION" --output "$IV_WORK/out" ) > "$IV_WORK/publish.log" 2>&1 \
      || { tail -40 "$IV_WORK/publish.log"; iv_die "package build failed"; }
  else
    # Copy the sources (no bin/obj/node_modules) so the container never writes into the checkout.
    iv_log "building linux-server $VERSION in a .NET SDK container with fpm"
    mkdir -p "$IV_WORK/src"
    ( cd "$IV_REPO_ROOT" && tar -cf - --exclude='bin' --exclude='obj' --exclude='node_modules' \
        src publisher.json LICENSE.md ) | ( cd "$IV_WORK/src" && tar -xf - )
    docker run --rm --name "$BUILDER" -v "$IV_WORK:/work" -w /work/src \
      mcr.microsoft.com/dotnet/sdk:10.0.401-noble bash -c "
        set -e
        apt-get update -qq >/dev/null
        DEBIAN_FRONTEND=noninteractive apt-get install -y -qq ruby ruby-dev build-essential rpm >/dev/null
        gem install --no-document fpm >/dev/null
        dotnet run --project src/Armada.Publisher -c Release -- --channel linux-server --version '$VERSION' --output /work/out
      " > "$IV_WORK/publish.log" 2>&1 || { tail -40 "$IV_WORK/publish.log"; iv_die "package build failed"; }
  fi
  grep -E "^\[(publish|deb|rpm|fpm)\]|Created package" "$IV_WORK/publish.log" | cut -c1-200 || true
  PACKAGE="$(find_package "$IV_WORK/out")"
  if [ -n "$PACKAGES_DIR" ]; then
    mkdir -p "$PACKAGES_DIR"
    find "$IV_WORK/out" \( -name '*.deb' -o -name '*.rpm' \) -exec cp {} "$PACKAGES_DIR"/ \;
    iv_log "packages copied to $PACKAGES_DIR"
  fi
fi
[ -f "$PACKAGE" ] || iv_die "no ${FORMAT} package for this architecture found"
iv_log "package: $PACKAGE ($(du -h "$PACKAGE" | cut -f1))"

mkdir -p "$IV_WORK/pkg"
cp "$PACKAGE" "$IV_WORK/pkg/"
PKG_NAME="$(basename "$PACKAGE")"
if [ "$FORMAT" = "deb" ]; then IMAGE="ubuntu:24.04"; else IMAGE="fedora:42"; fi

iv_log "installing in a clean $IMAGE container"
docker run --rm --name "$TESTER" \
  -v "$IV_WORK/pkg:/pkg:ro" -v "$IV_LIB_DIR:/iv/scripts/common/install-verify:ro" \
  "$IMAGE" bash /iv/scripts/common/install-verify/in-container-package-test.sh "/pkg/$PKG_NAME"
RESULT=$?

if [ "$RESULT" -eq 0 ]; then iv_log "Linux .${FORMAT} install verification: PASS"; else iv_log "Linux .${FORMAT} install verification: FAIL"; fi
exit "$RESULT"
