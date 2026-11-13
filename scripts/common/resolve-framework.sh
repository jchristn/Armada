#!/usr/bin/env bash
# Resolves the target .NET framework for Armada scripts. Source this file, then call
# armada_resolve_framework "$@" -- it sets ARMADA_TARGET_FRAMEWORK and reports how many leading
# arguments it consumed in ARMADA_FRAMEWORK_ARGS_CONSUMED so the caller can `shift` them off before
# forwarding the remaining arguments.
#
# Resolution priority (matching scripts/windows/resolve-framework.bat):
#   1. -f <fw> or --framework <fw>
#   2. a bare leading framework moniker (net8.0, net10.0, ...)
#   3. the ARMADA_TARGET_FRAMEWORK environment variable
#   4. net10.0
#
# Only a leading "net*" token is treated as a bare framework, so scripts that forward other
# arguments (e.g. update.sh passing a Helm command) are not misinterpreted.

armada_resolve_framework() {
    ARMADA_TARGET_FRAMEWORK="${ARMADA_TARGET_FRAMEWORK:-net10.0}"
    ARMADA_FRAMEWORK_ARGS_CONSUMED=0

    case "${1:-}" in
        -f|--framework)
            if [ -z "${2:-}" ]; then
                echo "ERROR: Missing framework value after $1." >&2
                exit 1
            fi
            ARMADA_TARGET_FRAMEWORK="$2"
            ARMADA_FRAMEWORK_ARGS_CONSUMED=2
            ;;
        net*)
            ARMADA_TARGET_FRAMEWORK="$1"
            ARMADA_FRAMEWORK_ARGS_CONSUMED=1
            ;;
    esac

    export ARMADA_TARGET_FRAMEWORK
    export ARMADA_FORWARD_FRAMEWORK_ARGS="--framework ${ARMADA_TARGET_FRAMEWORK}"
    # Pass these to every dotnet build, publish, pack, test, or (building) run: overriding TargetFrameworks keeps restore
    # from evaluating the other framework, which an SDK without it (for example .NET 8 only) rejects with NETSDK1045.
    export ARMADA_DOTNET_MSBUILD_FRAMEWORK_ARGS="-p:TargetFramework=${ARMADA_TARGET_FRAMEWORK} -p:TargetFrameworks=${ARMADA_TARGET_FRAMEWORK}"
    export ARMADA_DOTNET_FRAMEWORK_ARGS="--framework ${ARMADA_TARGET_FRAMEWORK} ${ARMADA_DOTNET_MSBUILD_FRAMEWORK_ARGS}"

    # Also honor an "ignore TLS certificate" flag (-k / --insecure) from the same command line, so every
    # script that resolves a framework transparently supports it too.
    # shellcheck source=scripts/common/resolve-insecure.sh
    . "$(dirname "${BASH_SOURCE[0]}")/resolve-insecure.sh"
    armada_resolve_insecure "$@"
}

# Call after armada_resolve_framework with the same arguments, in scripts that take nothing else: exits 2 on any
# argument that is neither the leading framework selection nor an "ignore TLS certificate" flag.
armada_reject_unexpected_args() {
    local arg
    shift "${ARMADA_FRAMEWORK_ARGS_CONSUMED:-0}"
    for arg in "$@"; do
        case "$arg" in
            -k|--insecure|--no-strict-ssl|--ignore-cert-errors) ;;
            *)
                echo "ERROR: Unexpected argument: $arg (run with -h for usage)" >&2
                exit 2
                ;;
        esac
    done
}
