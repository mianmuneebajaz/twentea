# Contributing

Twentea ships the same features on macOS and Windows. A pull request that changes behaviour changes
both apps, or records the gap in [docs/PARITY.md](docs/PARITY.md).

1. Branch from `main`.
2. Change the Mac core (`mac/Sources/Twentea/Model`, `Core/Scheduler.swift`) and the Windows core
   (`windows/src/Twentea.Core`) the same way, and add the same test to both suites with the same name.
3. Run `mac/scripts/test.sh` and `scripts/check-parity.sh`. CI builds and tests Windows.
4. Open a pull request. If the change has no counterpart on the other platform, add the
   `platform-specific` label.

The full rule list is in [CLAUDE.md](CLAUDE.md).
