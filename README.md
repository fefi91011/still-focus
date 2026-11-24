# Still

**A quiet, local-first focus app for Windows.**

Still blocks distracting apps and websites, ties schedules to daily limits, turns finished tasks into earned time, and can be guarded by a trusted person instead of a cloud account.

![Still dashboard](docs/screenshots/01-heute.png)

> **Status: 0.2.0.** Windows-first. The interface and core also run on Linux and macOS, where blocking is intentionally simulated. Website blocking now runs through the bundled browser extension; the hosts file is only cleaned up on upgrades from older versions. The Windows installer, full-screen app overlay, contact action, grace action and extension bridge have been exercised locally. Scheduled tasks still need a broader administrator-level test matrix. The desktop UI currently ships in German; the documentation is in English.

[Deutsche README](README.de.md)

## Why Still?

- **Local by design.** No account, no sync, no telemetry. Your rules and usage data stay on your machine.
- **Limits with a human escape hatch.** The familiar **+5 minutes** grace button is configurable per rule and limited per day.
- **More than a timer.** Schedules, allowances, tasks, Pomodoro focus, Cold Turkey sessions, and app/website rules share one simple model.
- **A real full-screen block.** Blocked apps stay open but are covered by an overlay with **Close app**, **Contact approver**, and **+N minutes**.
- **The same card in the browser.** The extension covers websites and subdomains in Chrome, Edge, Firefox and other Chromium/Firefox-based browsers.

## Features

| Area | What it does |
| --- | --- |
| **Rules** | Block apps and websites permanently or during selected weekly time windows. Apps can be picked directly from the installed-programs list, or entered by process name. A daily limit is optional. |
| **Limits & grace** | Foreground usage is counted per rule. When a limit is reached, each rule can offer a configurable grace period (5 minutes by default), capped per day. |
| **Usage** | Shows used and remaining time per rule, plus total screen time and per-app foreground time for today and the last seven days. Browsers are grouped by process, not by tab. |
| **Allowances** | Global allowlists with their own time window. They override rules and do not consume the rule budget. |
| **Focus timer** | A Pomodoro-style timer. Work phases block the targets of your rules; breaks only release the timer lock. Schedules remain active during breaks. |
| **Tasks** | Finished tasks earn bonus time for one rule or every rule with a daily limit. Optional reflection note and a daily completion cap. |
| **Contacts** | Trusted approvers with a locally stored PBKDF2-hashed code. The code can authorize edits or an early exit from Cold Turkey. No server involved. |
| **Cold Turkey** | A hard lock without pauses or grace. Optionally releasable only with an approver's code. Browser processes can be included. A watchdog restarts the app if its heartbeat disappears. |
| **Statistics** | Focus minutes, screen time, app time, blocked attempts, completed tasks, rule budgets, and a seven-day history. |
| **Autostart** | Installs a scheduled task for the app and watchdog from Settings. |
| **Browser extension** | Chromium and Firefox extensions show the same block card for websites: rule, grace, **Contact approvers**, and **Close tab**. They cover subdomains and reconnect automatically to the running app. |

## Screenshots

The screenshots come from the deterministic demo dataset. The UI is currently in German.

| Today | Focus |
| --- | --- |
| ![Today](docs/screenshots/01-heute.png) | ![Focus](docs/screenshots/02-fokus.png) |

| Rules | Cold Turkey |
| --- | --- |
| ![Rules](docs/screenshots/05-regeln.png) | ![Cold Turkey](docs/screenshots/15-cold-turkey.png) |

| Tasks | Statistics |
| --- | --- |
| ![Tasks](docs/screenshots/03-aufgaben.png) | ![Statistics](docs/screenshots/07-statistik.png) |

| Full-screen block | Settings |
| --- | --- |
| ![Full-screen block](docs/screenshots/18-blockiert.png) | ![Settings](docs/screenshots/08-einstellungen.png) |

<details>
<summary>More screens</summary>

| Contacts | Schedule |
| --- | --- |
| ![Contacts](docs/screenshots/04-kontakte.png) | ![Schedule](docs/screenshots/06-zeitplan.png) |

| Rule editor | Weekly rule editor |
| --- | --- |
| ![Rule editor](docs/screenshots/09-regel-editor.png) | ![Weekly rule editor](docs/screenshots/10-regel-editor-woche.png) |

| Allowances | Allowance editor |
| --- | --- |
| ![Allowances](docs/screenshots/11-ausnahmen.png) | ![Allowance editor](docs/screenshots/12-ausnahme-editor.png) |

| Task editor | Contact editor |
| --- | --- |
| ![Task editor](docs/screenshots/13-aufgaben-editor.png) | ![Contact editor](docs/screenshots/14-kontakt-editor.png) |

| Approval dialog | Toast |
| --- | --- |
| ![Approval dialog](docs/screenshots/16-dialog.png) | ![Toast](docs/screenshots/17-toast.png) |

</details>

## Quick start (demo)

Requirements: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
dotnet run --project src/Still.App -- --demo
```

Demo mode uses realistic sample data and simulates all blocking. It does not modify the system. Demo data stays in memory and the reported data path is `%TEMP%\still-demo`.

To use an isolated real data folder instead:

```powershell
$env:STILL_DATA_DIR = "C:\Temp\still-data"
dotnet run --project src/Still.App
```

## Install on Windows

The ready-to-run installer is published in [GitHub Releases](https://github.com/fefi91011/still-focus/releases/latest). Download `StillSetup-0.2.0.exe` and run it. The release notes include the SHA-256 checksum.

To build the installer locally:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-installer.ps1
```

The result is `dist\installer\StillSetup-0.2.0.exe`. It is self-contained, so the .NET runtime does not need to be installed separately.

The installer still runs elevated so it can install into Program Files and remove hosts entries left by older Still versions. App blocking and website blocking at runtime do not need administrator rights; website blocking uses the browser extension. The installer creates Start menu and optional desktop shortcuts and starts Still after installation. It is currently unsigned, so Windows SmartScreen may warn. A code-signing certificate is required for frictionless public distribution.

To uninstall, quit Still from the tray first, then use **Settings → Apps → Installed apps → Still → Uninstall**. The uninstaller removes old hosts entries and the autostart tasks. Remove the browser extension separately in the browser's extension page.

## Browser extension

Website blocking is implemented by the extension. Still prepares the files and opens the right browser page:

1. Open **Einstellungen → Browser-Schutz**.
2. Choose **Einrichten** next to a detected browser.
3. Follow the one-time browser prompt.

The extension is included in the app and the installer. Supported browsers:

- **Chromium:** Chrome, Edge, Brave, Vivaldi, Opera, Opera GX, Chromium, Arc
- **Firefox:** Firefox, Zen Browser, LibreWolf, Waterfox, Floorp

To build the packages separately:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-extension.ps1
```

Output:

```text
dist/extension/chromium/
dist/extension/firefox/
dist/extension/still-website-blocker-chromium.zip
dist/extension/still-website-blocker-firefox.xpi
```

Manual install:

- **Chromium:** open `chrome://extensions` (or the equivalent page), enable Developer mode, choose **Load unpacked**, and select `dist/extension/chromium`.
- **Firefox:** open `about:debugging#/runtime/this-firefox`, choose **Load Temporary Add-on**, and select `dist/extension/firefox/manifest.json`. A permanent install requires Mozilla signing.

The extension talks only to the running app at `http://127.0.0.1:47821`. It has no cloud access and no separate rules; it reads the current website status and grace state from Still.

### Why there is no fully silent install

Stock browsers do not let a normal desktop app add an unpacked extension to an existing profile:

- Chromium browsers only force-install through their stores, or through enterprise policy on domain-joined/managed devices.
- Firefox requires a Mozilla-signed XPI; auto-install through policy needs that signed package.

The one-time setup is therefore deliberate. See [extension/README.md](extension/README.md) for details.

## Privacy and data

Everything stays local:

- **Real use:** `%LOCALAPPDATA%\Still\`
  - `state.json` and `state.bak.json`
  - `heartbeat` for the watchdog
  - `still.log` on errors
  - prepared browser extension files under `browser-extension\`
- **Demo:** `%TEMP%\still-demo`
- **Override:** set `STILL_DATA_DIR` to use another folder.

There is no internet connection, telemetry, or sync. The optional extension communicates only over loopback. Approver codes are stored as PBKDF2-SHA256 hashes (120,000 iterations, random salt); the plaintext is never persisted.

## Architecture

```text
src/
  Still.Core/       Rules, limits, timers, tasks, approvals, persistence, engine
  Still.Platform/   Windows process/elevation/scheduled-task adapters; NullPlatform
  Still.App/        Avalonia 11 UI, tray, full-screen block window, extension setup
  Still.Watchdog/   Restarts the app when its heartbeat is missing
tests/
  Still.Core.Tests/ 48 tests for the engine, app-time tracking, schedules, limits,
                   approvals, persistence, and legacy hosts cleanup
extension/          Chromium/Firefox WebExtension with a local Still block page
tools/
  Still.Screenshots/ Headless renderer for the UI
```

`Still.Core` contains the platform-independent logic. `Still.Platform` implements two capabilities: `IProcessController` and `IStartupInstaller`. The app coordinates them through `StillRuntime`.

Each tick evaluates a rule against four inputs:

1. Is the schedule active?
2. Is the daily limit (including task and grace bonuses) used up?
3. Is a Cold Turkey or Pomodoro lock active?
4. Is an allowance active for the specific target?

The result is a set of processes and domains to block. The platform enforces processes; the extension reads the domain set through the loopback bridge. The UI only reads the resulting snapshot.

## Build, test, and regenerate screenshots

```bash
dotnet build Still.sln
dotnet test tests/Still.Core.Tests/Still.Core.Tests.csproj
```

The test suite currently contains **48 tests**.

Regenerate the demo screenshots:

```powershell
dotnet run --project tools/Still.Screenshots -- artifacts/screenshots
```

The renderer uses the in-memory demo dataset. On Windows it needs no extra Skia or font setup. On Linux, install the Skia native assets and a usable font configuration.

Publish the Windows app:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/publish-win.ps1
```

Use `-SelfContained` for a runtime-independent build. See `scripts/publish-win.ps1` and `scripts/build-installer.ps1` for the exact publish settings.

## Known limitations

- **The UI is German.** English localization is not implemented yet. The README and extension docs are English.
- **Website blocking lives in the browser.** The extension only affects browsers where it is installed and enabled. It is not a system-wide packet filter or firewall, and it can be disabled.
- **No hosts fallback anymore.** Still removes hosts entries from older versions on upgrade/uninstall. New website blocks always go through the extension.
- **Silent extension install is not available on stock browsers.** Chromium requires store/enterprise distribution, Firefox requires a Mozilla-signed XPI.
- **App blocking polls about once per second** and shows a full-screen overlay. Blocked apps are not terminated automatically; the overlay's **Close app** action closes them explicitly. Protected system processes are never touched.
- **Usage is measured from foreground processes.** Website usage is approximated by foreground browser time, not per tab. Closing Still stops tracking.
- **No kernel driver or DRM.** A local administrator can kill the app or the watchdog with Task Manager. The extension can be removed in the browser. The watchdog can restart the app, but this is not tamper-proof.
- **No Windows service.** The app is a tray application and the watchdog is a scheduled task, which keeps installation and debugging simple.
- **Other platforms:** `Still.Core` and the Avalonia UI are portable, but `NullPlatform` provides simulated blocking only on Linux/macOS. The extension itself is cross-platform, but the desktop-side setup is Windows-first.

## License

No license file has been added yet. Add a license before distributing modified versions or accepting outside contributions under a specific license.

## Acknowledgements

Built with [Avalonia](https://avaloniaui.net/) and [.NET](https://dotnet.microsoft.com/). Thanks to the open-source projects that make local-first desktop tooling possible.
