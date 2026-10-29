# Twentea

Free, MIT, eye-break app with two native apps that behave the same:

| Folder | App | Stack |
|---|---|---|
| `mac/` | macOS menu bar app | Swift 6 (Swift 5 mode), SwiftUI, AppKit, SPM. No Xcode project |
| `windows/` | Windows tray app | C# on .NET 10, WPF windows, WinForms tray icon |
| `shared/` | Contract both apps follow | `presets.json`, `parity-exceptions.txt` |
| `brand/` | Icons | SVG sources, `generated/` holds the committed `.icns` and `.ico` |

Read `docs/TECHNICAL.md` (Mac design), `docs/WINDOWS.md` (Windows plan) and `docs/PARITY.md`
(feature matrix) before changing behaviour.

## Rules

Each rule has a stable id. Cite it when you apply or flag one, add new rules at the end of a
section, and never renumber.

### 1. Parity

- **1.1** Every user-facing feature ships on both apps in the same pull request. If one side cannot
  land yet, add or update its row in `docs/PARITY.md` with status `pending` in that same pull request.
- **1.2** Behaviour lives in the core: `mac/Sources/Twentea/Model` and `Core/Scheduler.swift` on the
  Mac, `windows/src/Twentea.Core` on Windows. The two cores are line-for-line ports. Change one, change
  the other the same way.
- **1.3** Every core test exists in both suites with the same name (Swift `camelCase`, C# `PascalCase`).
  `scripts/check-parity.sh` fails CI otherwise. A test that only applies to one platform goes in
  `shared/parity-exceptions.txt` with a comment.
- **1.4** A change with no counterpart (an AppKit-only fix, a Windows registry quirk) gets the
  `platform-specific` label on its pull request. That label is the only way past the CI parity check.
- **1.5** Settings JSON is one format. Both apps read and write the same keys and enum strings, so a
  `.twentea` preset file exported on one platform imports on the other. Add new keys as optional with
  a default (`decodeIfPresent` in Swift, a property initializer in C#) so old files keep loading.

### 2. Shared contract

- **2.1** `shared/presets.json` is the source of truth for built-in presets. Windows embeds it. The
  Mac keeps `Preset.builtIns` in Swift and `SharedPresetsTests` checks they match. After changing a
  preset in Swift, regenerate with `TWENTEA_WRITE_PRESETS=1 mac/scripts/test.sh`.
- **2.2** The `twentea://` URL table in `docs/TECHNICAL.md` applies to both apps.
- **2.3** Icons come from `brand/twentea-icon.svg`. Regenerate both outputs with `brand/make-icons.sh`
  on a Mac and commit `brand/generated/`. CI never renders SVG.

### 3. Platform code

- **3.1** Platform code stays out of the core: no AppKit in `Model` or `Scheduler.swift`, no
  `System.Windows.Forms` or Win32 in `Twentea.Core`. Core types reach the platform through
  `ActivitySource` and `BreakPresenting` (Mac) and `IActivitySource` and `IBreakPresenter` (Windows).
- **3.2** Activity signals must work without a permission prompt. If a signal needs one, it is opt-in
  and off by default.
- **3.3** Cards and soft dims never take keyboard focus. Full overlay and hard lock do, and focus
  returns to the previous app afterwards. Long-running terminal and agent sessions must keep running
  untouched during a break, so both apps keep the system awake while a break shows.
- **3.4** A level that a platform cannot show must degrade through `Features.resolve`, never silently
  do nothing.

### 4. Build, CI and releases

- **4.1** CI (`.github/workflows/ci.yml`) must pass on both platforms before merge: Mac build, Swift
  tests and app bundle; Windows build, C# tests and publish; parity check.
- **4.2** Releases come only from a `v*` tag (`.github/workflows/release.yml`). The tag is the version.
  Never upload release files by hand.
- **4.3** Signing secrets live in repository secrets only. Unsigned builds stay allowed so forks can
  build, and the release notes say when a build is unsigned.
- **4.4** Verify locally before pushing: `mac/scripts/test.sh` and `scripts/check-parity.sh`. There is
  no local Windows toolchain, so Windows changes are verified by CI.

### 5. Writing and git

- **5.1** Commits and tags use name `Muneeb` and email `muneeb@reporteq.com`.
- **5.2** No AI attribution anywhere: commits, pull requests, docs, code comments.
- **5.3** No em dashes or en dashes in code, comments, docs, commits or pull requests.
- **5.4** Docs are short and visual: diagrams and tables, proposal only, no research recap.
- **5.5** Branch from `main`, open a pull request, merge when CI is green. Do not push to `main`.
