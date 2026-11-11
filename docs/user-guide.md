# User guide

This guide describes the current source as reviewed on 2026-11-11. Start both services using [Run locally](local-run.md), then open http://localhost:4200/.

## Connect browser capture

1. In Chrome, open `chrome://extensions`; in Edge, open `edge://extensions`.
2. Enable **Developer mode**, choose **Load unpacked**, and select `C:\Work\Codex\Text to speach\apps\extension`.
3. Open the extension's **Options** page from its extension details.
4. Set **Backend port** to `17832`. Set **API key** to the value of `Api:ApiKey` in `apps/api/appsettings.json`. The extension's initial `change-me` value may differ from the configured backend key.
5. Enable capture, save, and refresh the ChatGPT tab. Capture supports `https://chatgpt.com/` and `https://chat.openai.com/`.
6. Generate a new assistant reply. It should appear under **Recent Captured Messages**; enabled assistant replies are queued after their text stops changing for the stability window (normally 1.2 seconds).

The extension also scans messages already visible on the page when it attaches. Loading a conversation can therefore capture and queue existing replies. Disable extension capture when you want to stop forwarding text; the dashboard's reading toggles control automatic speech eligibility, not storage.

## Choose a speech engine

Select a different mode and then your desired mode once on first setup so `speech.mode` is saved. The displayed initial mode is not a reliable indication of the worker's default; see [configuration caveats](configuration.md#mode-default-caveats).

| Mode | Setup | Tracker speed control |
| --- | --- | --- |
| Local (Windows voices) | Select an installed voice or **System default** | Rate from -10 to 10 |
| Edge TTS (online) | Install `edge-tts`, make it available on PATH, restart the API, select an EN-US voice | Rate from -50% to +100% |
| Cloud (OpenAI TTS) | Enter the API key, model, and voice | Speed from 0.25 to 4 |

The source ships UI defaults of `en-US-AndrewMultilingualNeural` for Edge, and `gpt-4o-mini-tts` / `alloy` for Cloud. These are application defaults; this guide does not verify current provider availability. Edge and Cloud send the text being spoken to an online service. Local mode uses the Windows speech synthesizer.

Settings save as controls change; there is no separate dashboard Save button. Check the status message for success. Speech settings are read when each queued message starts, so rate/voice changes generally apply to subsequent playback rather than the current message.

## Playback controls

| Control | Behavior |
| --- | --- |
| History **Play** | Queues that specific captured message; does not replace the active item |
| Tracker **Play** | Resumes when paused; otherwise queues the last attempted message again; does nothing before a message has been attempted |
| **Pause** / **Resume** | Pauses/resumes local or Edge playback; Cloud pauses at segment boundaries |
| **Stop** | Stops current playback and drains the speech queue |
| **Skip** | Requests skipping the current item; Cloud may finish the current audio segment first |
| **Repeat** | Queues the last attempted message again |
| **Refresh** | Reloads connection status, settings, voices, history, and diagnostics |

Stop does not disable incoming capture or clear the stability buffer. New captures, or an already buffered update, can enqueue speech afterward. Disable extension capture when you need forwarding to stop.

**Speech Tracker** shows active text, queue count, segment, and estimated position. Edge position is estimated from audio progress; Cloud position advances between segments. Highlighting is approximate and based on English-style word characters, rather than speech-provider word timings. The dashboard polls the tracker and diagnostics every two seconds, and history about every six seconds while connected.

## Capture and history

- **Read assistant replies** defaults to enabled. Turning it off prevents newly eligible replies from entering the stability buffer; it does not remove existing queue items.
- **Read user prompts** is saved, but the current stability worker only enqueues assistant messages. User prompts can still appear in history and be played manually.
- History is newest first. The dashboard limit ranges from 10 to 500, with a default of 50.
- Captured text and settings persist in SQLite. Pending speech, repeat state, and deduplication state do not survive an API restart.
- There is no history deletion or export control in the current dashboard.

## Diagnose problems

| Symptom | Check or action |
| --- | --- |
| API Offline / unavailable | Check the [health URL](http://127.0.0.1:17832/api/health), start the API, then click Refresh. A failed auxiliary load can also produce the generic unavailable message. |
| No captured messages | Confirm the extension is enabled, refresh the ChatGPT tab, check its port/key, and inspect browser console messages prefixed `[VoiceCompanion]`. |
| Capture returns 401 | Match the extension API key to `Api:ApiKey` and save. |
| Captured replies are silent | Confirm assistant reading is enabled, explicitly persist the desired mode, check output volume/device, and inspect Speech Diagnostics. |
| Edge voice list appears but playback fails | The API can return a built-in fallback list. Verify `Get-Command edge-tts` and `edge-tts --version` in the API terminal, network access, and the latest error. |
| Cloud playback fails | Check the saved key/model/voice and diagnostics; the service records provider errors. |
| UI does not recover after API restart | Click Refresh or reload the dashboard. Background polling does not re-establish a failed initial connection. |
| History or playback seems to mix tabs | The current API reuses the latest database session and does not provide isolated queues per conversation. |

**Clear latest error** removes the error text from the latest speech event. It does not retry playback or change that event's status. Use history Play to retry after resolving the cause.

See [Run locally](local-run.md) for startup problems and [Configuration](configuration.md) for storage and privacy details.
