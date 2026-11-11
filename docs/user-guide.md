# User guide

This guide describes the current source as reviewed on 2026-11-11. Start both services using [Run locally](local-run.md), then open http://localhost:4200/.

## Listening workspace

Use **Paste text** for manual reading and its history, or **ChatGPT** for captured messages and automatic-reading toggles. **Voice settings** opens a separate dialog for the engine, voice, and speed; close it with **Done** or Escape. The shared **Now listening** player stays available across tabs.

Each active history row has **Pause / Resume** and **Stop**. Inactive rows have **Play / Continue**: selecting one replaces current playback and clears queued items. Use **Queue** instead to preserve current playback. Each recording is read once, then the next queued recording starts; playback stops when the queue is empty. Connection and error details are under **Connection & diagnostics** at the bottom.

## Listen to pasted text

While a recording plays, the main action becomes **Add to queue**. Paste the next text and submit it without interrupting playback. History rows also have **Queue**. **Next** lists waiting recordings and their audio-buffer status; remove one entry or clear the waiting list independently. Kokoro buffers the next recording while the current one plays and starts it automatically afterward. **Stop** ends playback and clears the waiting list; **Next recording** skips only the current one.

The current text highlights words using Kokoro timing metadata, with optional automatic scrolling. The small player uses only elapsed/total time and one audio slider; percentages remain beside the text on the left. During generation, it shows the amount of audio ready in minutes/seconds and an unknown total (`--:--`). The slider and **−15 / +15 seconds** activate once the entire recording is buffered, keeping a fixed full-recording scale instead of resizing as parts arrive. Playback may begin before the full timeline is ready.

Every history entry shows its percentage, including 0% for new text and 100% after completion. Interrupted entries offer **Continue** from the saved audio position and **From beginning** to restart. Positions are saved in SQLite periodically and on explicit controls, so refresh/restart preserves progress. Generated audio is rebuilt when an interrupted recording is resumed after stopping.

Paste or type text in the **Paste text** tab, then select **Read aloud**. Each submission is saved in **Your listening history** and added after any current speech. The limit is 50,000 characters; blank text cannot be submitted. No browser extension is needed, and capture reading toggles do not affect manual submissions.

Choose the voice and speed under **Voice settings**. Use **Pause**, **Resume**, and **Stop** on the active history entry or in **Now listening**. If the queue is paused, select **Resume**. **Clear text** clears only the draft, and history entries can be replayed with **Play**. Drafts remain after submission but are not retained across page reloads. Edge and Cloud modes send the text to their online speech providers, just as they do for captured messages.

## Connect browser capture

1. In Chrome, open `chrome://extensions`; in Edge, open `edge://extensions`.
2. Enable **Developer mode**, choose **Load unpacked**, and select `C:\Work\Codex\Text to speach\apps\extension`.
3. Open the extension's **Options** page from its extension details.
4. Set **Backend port** to `17832`. Set **API key** to the value of `Api:ApiKey` in `apps/api/appsettings.json`. The extension's initial `change-me` value may differ from the configured backend key.
5. Enable capture, save, and refresh the ChatGPT tab. Capture supports `https://chatgpt.com/` and `https://chat.openai.com/`.
6. Generate a new assistant reply. It should appear under **Captured messages** in the **ChatGPT** tab; enabled assistant replies are queued after their text stops changing for the stability window (normally 1.2 seconds).

The extension also scans messages already visible on the page when it attaches. Loading a conversation can therefore capture and queue existing replies. Disable extension capture when you want to stop forwarding text; the dashboard's reading toggles control automatic speech eligibility, not storage.

## Choose a speech engine

Kokoro with Heart US is the default. Open **Voice settings** and choose **Bella US** to change the voice. Your selection is saved and used for the next reading; switching voices does not reload the model.

| Mode | Setup | Tracker speed control |
| --- | --- | --- |
| Local (Windows voices) | Select an installed voice or **System default** | Rate from -10 to 10 |
| Kokoro (local) | Run `scripts/setup-kokoro.ps1` once; select Heart US or Bella US | Speed from 0.5× to 2× |
| Cloud (OpenAI TTS) | Enter the API key, model, and voice | Speed from 0.25 to 4 |

Kokoro generates speech on your computer with no online synthesis request. Its model loads once at API startup. Cloud mode still sends text to OpenAI; Windows Local uses installed system voices.

Settings save as controls change; there is no separate dashboard Save button. Check the status message for success. Speech settings are read when each queued message starts, so rate/voice changes generally apply to subsequent playback rather than the current message.

Short messages are generated as one audio file. Long Kokoro messages use bounded parts and prepare the next part while the current part plays. The player shows preparation status and part number. Formatting-only lines are skipped. Stop cancels playback and pending API requests while keeping the local Kokoro model loaded.

## Playback controls

| Control | Behavior |
| --- | --- |
| History **Play** | Replaces active playback with that specific message |
| **Pause** / **Resume** | Pauses/resumes local or Edge playback; Cloud pauses at segment boundaries |
| **Stop** | Stops current playback, cancels Edge audio generation, and drains the speech queue |
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
