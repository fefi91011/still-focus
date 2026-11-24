# Still Browser Extension

The extension is Still's website blocker. It runs the same block card as the desktop app:

- **Contact approvers**
- **Close tab**
- **+N minutes** grace, when the matching rule allows it
- **×** to close the tab

It asks the running Still app at `http://127.0.0.1:47821` for the current rules and grace state. There is no cloud service and no account. The connection is reachable only from the local machine. The interface is not started in demo mode; the extension requires the real Still app.

The toolbar popup shows whether Still is connected, how many website rules are active and when the list was last refreshed. A badge on the toolbar icon shows the current number of blocked domains.

## Website time

The extension also measures how long the active website is in use, whether or not the site has a rule. Every minute, when the active tab changes and when browser focus changes, it sends the domain, the local calendar date and the elapsed seconds to Still over the same loopback bridge. The app aggregates this locally by day and host and shows it as **Website-Zeit** on the Today and Statistics pages.

- Only `http://` and `https://` tabs in the focused, normal browser window count. Extension pages, the Still block page and internal browser pages are ignored.
- Only the hostname, the local date and the duration are sent, never the full URL, page title or content.
- Sessions that cross local midnight are split into one report per day, so offline reports keep the day on which the time was actually spent even when Still was closed for several days.
- Reports are queued in `storage.local` while Still is closed and sent after the app is reachable again. A report is only removed after the app acknowledges it.
- Focusing another application or minimizing the browser pauses the timer. Idle detection and the operating-system lock screen are not tracked.
- The counter starts with the loaded extension. It does not reconstruct time from before installation.

## Supported browsers

Both packages use the same WebExtension source with Manifest V3. Any current browser based on Chromium or Gecko should work.

- **Chromium:** Google Chrome, Microsoft Edge, Brave, Vivaldi, Opera, Opera GX, Chromium, Arc
- **Firefox:** Mozilla Firefox, Zen Browser, LibreWolf, Waterfox, Floorp

The app detects the common Windows installations and prepares the right package for the selected browser.

## Build

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

The XPI is a normal ZIP archive with `manifest.json` at its root.

## Install from Still

Open **Einstellungen → Browser-Schutz**. Still lists every supported browser it can find. **Einrichten** prepares the package in `%LOCALAPPDATA%\Still\browser-extension\`, opens the browser's extension page and opens the prepared folder.

- **Chromium browsers:** enable **Developer mode**, choose **Load unpacked** and select the opened `chromium` folder.
- **Firefox:** a signed XPI opens the normal installation dialog. An unsigned local XPI cannot be installed permanently in release Firefox; Still then opens `about:debugging#/runtime/this-firefox` so the package can be loaded as a temporary add-on. For a permanent install, the XPI must be signed through addons.mozilla.org.

## Manual Chromium install

1. Open `chrome://extensions` (or `edge://extensions`, `brave://extensions`, `vivaldi://extensions`, `opera://extensions`).
2. Enable **Developer mode**.
3. Choose **Load unpacked** and select `dist/extension/chromium`.
4. Start Still. The extension connects automatically.

## Manual Firefox install

For local testing:

1. Open `about:debugging#/runtime/this-firefox`.
2. Choose **Load Temporary Add-on**.
3. Select `dist/extension/firefox/manifest.json`.
4. Start Still.

For a permanent installation, Mozilla must sign the extension. Release Firefox does not keep an unsigned local XPI permanently installed.

## Why silent auto-install is limited

A desktop app cannot silently add an unpacked extension to a normal browser profile:

- Chrome, Edge and other Chromium browsers only auto-install through their stores. Force-installing a self-hosted CRX through policy works only on domain-joined, Azure AD-joined or managed devices.
- Firefox requires a Mozilla-signed XPI. The `ExtensionSettings` policy can auto-install it, but only with a signed package and a policy file in the Firefox installation.

That is why Still uses the safe one-time setup flow. After the one-time load, the browser reads the prepared files again on its next start and the extension reconnects automatically.

## Behavior and limitations

- Rules with **Include subdomains** block subdomains, for example `m.youtube.com` for `youtube.com`. The **exact domain** option blocks only the host itself; `www.` counts as the same host.
- It only affects the browser. Website blocking no longer uses the Windows hosts file.
- If Still is not reachable, the cached block list still blocks websites. The block page cannot grant grace or open the approver view until Still runs again.
- The blocked browser app is not terminated automatically. **Close tab** or **×** closes only the tab.
- The extension is not DRM: it can be disabled or removed in the browser.
- After a grace period, the block list is refreshed in the background before the original page is loaded again.
- Website time is measured in one-minute intervals and on tab/focus changes, so short visits can be rounded. Only the active tab in the focused window counts; split-screen or multiple visible windows are not summed.
- Pending website time keeps its original local date in extension storage until Still confirms receipt. It is not sent anywhere else.
- The extension UI currently shares the German labels used by the desktop app. The repository documentation is in English.
