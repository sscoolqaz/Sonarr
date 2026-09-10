#!/usr/bin/env sh
# Publishes Sonarr.SpacetimeModule to the spacetimedb service brought up by this compose
# file. Run this after `podman-compose up -d` (and again after any module code change) -
# publishing is a deliberate, scripted step rather than something the container does on
# every boot, since the module is versioned application code, not static config.
#
# Requires the `spacetime` CLI on PATH and its `local` server pointed at 127.0.0.1:3000
# (the port this compose file publishes spacetimedb on).

set -eu

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
MODULE_DIR="$SCRIPT_DIR/../../Sonarr.SpacetimeModule"

spacetime publish --server local -y sonarr-spacetime-dev --module-path "$MODULE_DIR/spacetimedb"
