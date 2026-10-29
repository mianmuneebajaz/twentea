#!/bin/bash
# Runs unit tests. With only the command line tools installed, swift test sometimes fails to find
# the Swift Testing macro plugin, so pass its path explicitly when it exists.
set -euo pipefail
cd "$(dirname "$0")/.."
PLUGINS="$(xcode-select -p)/usr/lib/swift/host/plugins/testing"
if [ -d "$PLUGINS" ]; then
  swift test -Xswiftc -plugin-path -Xswiftc "$PLUGINS" "$@"
else
  swift test "$@"
fi
