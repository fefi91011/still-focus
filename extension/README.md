# Still Website Blocker

The browser extension shows the same card for a blocked website as Still's full-screen app overlay:

- **Contact approvers**
- **Close tab**
- **+N minutes** grace, when the matching rule allows it
- **×** to close the tab

It asks the running Still app at `http://127.0.0.1:47821` for the current rules and grace state. There is no cloud service and no account. The connection is reachable only from the local machine. The interface is not started in demo mode; the extension requires the real Still app.

## Build

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-extension.ps1
```

Output:

```text
dist/extension/chrome/
dist/extension/firefox/
dist/extension/still-website-blocker-chrome.zip
dist/extension/still-website-blocker-firefox.zip
```

## Chrome / Edge

1. Open `chrome://extensions` (or `edge://extensions`).
2. Enable **Developer mode**.
3. Choose **Load unpacked** and select `dist/extension/chrome`.
4. Start Still. The extension connects automatically.

## Firefox

For local testing:

1. Open `about:debugging#/runtime/this-firefox`.
2. Choose **Load Temporary Add-on**.
3. Select `dist/extension/firefox/manifest.json`.
4. Start Still.

For a permanent installation, Mozilla must sign the extension. Release Firefox does not keep an unsigned local XPI permanently installed.

## Behavior and limitations

- The extension also blocks **subdomains**, for example `m.youtube.com` for the rule `youtube.com`.
- It only affects the browser. Still's hosts-file block remains the system-wide fallback.
- If Still is not reachable, the block page cannot grant grace or open the approver view.
- The blocked browser app is not terminated automatically. **Close tab** or **×** closes only the tab.
- The extension is not DRM: it can be disabled in the browser.
- After a grace period, the block list is refreshed in the background before the original page is loaded again.
- The extension block card currently shares the German labels used by the desktop app. The repository documentation is in English.
