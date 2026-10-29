#!/bin/bash
# Keeps the Mac and Windows apps in step. Runs in CI on every push and pull request.
#
# 1. Every core test exists in both suites (Swift camelCase, C# PascalCase, compared without case).
#    A test that only makes sense on one platform goes in shared/parity-exceptions.txt.
# 2. With BASE_REF set (pull requests), a change to one app's source must also touch the other app's
#    source or docs/PARITY.md. Set PLATFORM_SPECIFIC=1 (the `platform-specific` label) to skip this.
set -euo pipefail
cd "$(dirname "$0")/.."

fail=0
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

grep -rhoE '@Test func [A-Za-z0-9_]+' mac/Tests | sed 's/@Test func //' | tr 'A-Z' 'a-z' | sort -u > "$tmp/mac"
grep -rhA1 '\[Fact\]' windows/tests --include='*.cs' | grep -oE 'public void [A-Za-z0-9_]+' | sed 's/public void //' \
  | tr 'A-Z' 'a-z' | sort -u > "$tmp/win"
grep -vE '^\s*(#|$)' shared/parity-exceptions.txt 2>/dev/null | tr 'A-Z' 'a-z' | sort -u > "$tmp/skip" || true

missing_win="$(comm -23 "$tmp/mac" "$tmp/win" | comm -23 - "$tmp/skip")"
missing_mac="$(comm -13 "$tmp/mac" "$tmp/win" | comm -23 - "$tmp/skip")"
if [ -n "$missing_win" ]; then echo "Tests in the Mac suite but not the Windows suite:"; echo "$missing_win" | sed 's/^/  /'; fail=1; fi
if [ -n "$missing_mac" ]; then echo "Tests in the Windows suite but not the Mac suite:"; echo "$missing_mac" | sed 's/^/  /'; fail=1; fi
echo "Test parity: $(wc -l < "$tmp/mac" | tr -d ' ') Mac tests, $(wc -l < "$tmp/win" | tr -d ' ') Windows tests"

if [ -n "${BASE_REF:-}" ] && [ "${PLATFORM_SPECIFIC:-0}" != "1" ]; then
  changed="$(git diff --name-only "origin/$BASE_REF"...HEAD)"
  mac=$(echo "$changed" | grep -c '^mac/Sources/' || true)
  win=$(echo "$changed" | grep -c '^windows/src/' || true)
  doc=$(echo "$changed" | grep -c '^docs/PARITY.md$' || true)
  if { [ "$mac" -gt 0 ] && [ "$win" -eq 0 ]; } || { [ "$win" -gt 0 ] && [ "$mac" -eq 0 ]; }; then
    if [ "$doc" -eq 0 ]; then
      echo "This change touches one app only. Port it to the other app, or record the gap in docs/PARITY.md,"
      echo "or add the platform-specific label if it has no counterpart."
      fail=1
    fi
  fi
fi

exit $fail
