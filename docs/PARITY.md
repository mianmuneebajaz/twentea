# Feature parity

One row per user-facing feature. Status is `done`, `partial` or `pending`. Rule 1.1 in `CLAUDE.md`
requires a row change in any pull request that ships a feature on one app only.

| Feature | Mac | Windows | Notes |
|---|---|---|---|
| Power switch | done | done | Tray left click toggles on Windows |
| Off icon with a line through it | done | done | |
| Built-in presets | done | done | From `shared/presets.json` |
| Switch preset from the menu | done | done | |
| Save, rename, duplicate, delete, import, export presets | done | partial | Core done, no UI until W2 |
| Pause 30 min, 1 hour, until tomorrow, resume | done | done | |
| Break now | done | done | |
| Countdown next to the icon | done | partial | Tooltip only |
| Notification breaks | done | done | Balloon tip on Windows |
| Sounds | done | done | System sounds on Windows |
| Floating card | done | pending | W1 |
| Soft dim | done | pending | W1, degrades to notification until then |
| Full overlay | done | pending | W1 |
| Hard lock | done | pending | W1 |
| Input restarts a dimmed break | done | pending | Needs W1 dim |
| Breakpoint detection | done | done | |
| Idle credit | done | done | |
| Smart pause: camera or mic | done | done | |
| Smart pause: video | done | partial | Full screen only until W1 |
| Smart pause: excluded apps | done | partial | Process names, no UI until W2 |
| Working hours schedule | done | partial | Core done, no UI until W2 |
| Settings window | done | pending | W2 |
| Today's stats | done | pending | W2 |
| Launch at login | done | pending | W2 |
| `twentea://` URLs | done | pending | W2 |
| Keep system awake during a break | done | pending | W1 |
| Signed and notarized download | pending | pending | Secrets not set yet |
