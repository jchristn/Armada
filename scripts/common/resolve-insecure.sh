#!/usr/bin/env bash
# Scans all arguments for an "ignore TLS certificate" flag. When present, disables strict TLS
# certificate validation for npm/Node for the rest of this run so package restore works behind an
# SSL-inspecting corporate proxy that injects a self-signed root certificate. The exported
# environment variables propagate to every child process and sub-script.
#
# Source this file, then call: armada_resolve_insecure "$@"
#
# Recognized flags (anywhere on the command line): -k, --insecure, --no-strict-ssl, --ignore-cert-errors
#
# NOTE: npm ships its own CA bundle (separate from the OS certificate store), which is why an
# SSL-inspecting proxy breaks npm even when dotnet/NuGet (which use the OS store) work fine.
armada_resolve_insecure() {
    local arg
    for arg in "$@"; do
        case "$arg" in
            -k|--insecure|--no-strict-ssl|--ignore-cert-errors)
                export ARMADA_INSECURE=1
                export NODE_TLS_REJECT_UNAUTHORIZED=0
                export npm_config_strict_ssl=false
                echo "[insecure] TLS certificate validation disabled for this run (npm/Node)." >&2
                return 0
                ;;
        esac
    done
}
