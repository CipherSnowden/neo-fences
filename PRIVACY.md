# Privacy

NeoFences has **no telemetry, no accounts and no ads**. It does not collect, send or sell anything about you.

## What goes over the network

- **Updates** — NeoFences asks GitHub whether a newer version exists and, if so, downloads it from this project's
  release page (`github.com/CipherSnowden/neo-fences/releases`). GitHub sees the request like any download. You can
  turn the update check off in Settings → Updates.
- **Covers and website icons — only if you turn them on.** The switch **Find covers and website icons online** (asked
  once; also in Settings → Game Library) is off by default. When it is on:
  - the names of your games are sent to Steam's store search, and their cover pictures are downloaded from Steam;
  - for a website item, its home page is read, and its icon is downloaded from where that page says it is (usually the
    website itself, sometimes a service it uses for pictures).

Nothing else. Opening an item you put in a fence (a website, a program) is you opening it, as from the desktop.

## What stays on your PC

Your fences, items, settings, snapshots, backups, the icon cache and the logs live in `%LOCALAPPDATA%\NeoFences` and
never leave your PC. NeoFences never moves, renames, changes or deletes your files: fences hold links to them.

## Logs and bug reports

The logs help find bugs. They never contain your Windows user name (your profile folder is written as `%USERPROFILE%`),
but they do contain the names of folders and files your fences point at. If you attach logs to a bug report, they
become public on GitHub — look through them first and leave out what you do not want to share.
