# Browser extension

Manifest V3 extension for capturing messages on `chatgpt.com` and `chat.openai.com` and forwarding them to the local API. It is plain JavaScript and requires no build step.

## Setup

1. Start the API and dashboard using [Run locally](../../docs/local-run.md).
2. Open `chrome://extensions` or `edge://extensions`, enable Developer mode, and load this `apps/extension` directory unpacked.
3. Open extension Options, set port `17832`, and copy the local `Api:ApiKey` value from `apps/api/appsettings.json`. The initial extension key `change-me` may not match it.
4. Enable capture, save, and refresh the ChatGPT tab.

Existing visible messages are scanned on attachment. Disable forwarding in Options when you want capture stopped. Settings use `chrome.storage.sync`.

## Files and maintenance

- `manifest.json`: page matches, permissions, content scripts, and background worker.
- `dom-adapter.js`: selector fallbacks and text extraction.
- `content-script.js`: mutation observer, source IDs, unchanged-text suppression, and runtime messages.
- `background.js`: script attachment and token-authenticated API forwarding.
- `options.html` / `options.js`: port, token, and enabled settings.

After edits, reload the unpacked extension and refresh the ChatGPT tab. Inspect page-console messages prefixed `[VoiceCompanion]` and the extension's service-worker console when debugging. Failed forwarding is not queued for retry.

See the [User guide](../../docs/user-guide.md), [Architecture](../../docs/architecture.md), and [API reference](../../docs/api.md).
