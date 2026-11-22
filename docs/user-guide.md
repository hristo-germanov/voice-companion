# User guide

Start both services using [Run locally](local-run.md), then open http://localhost:4200/.

## Listening workspace

Use **Paste text** for manual reading and its history, or **ChatGPT** for captured messages and automatic-reading toggles. Open **Voice & language** for quick controls or select **More voice settings** for engine, voice, and speed settings. The **Reading space** is shared across tabs.

The active history row has **Pause** or **Resume**, along with **Stop**. Selecting **Play now**, **Resume**, or **Replay** on another row replaces current playback and clears queued items. Use **Add to queue** to preserve current playback. Each recording plays once and the next queued recording starts automatically.

## Listen to pasted text

Select **New text** above the reading space to open and focus the editor. While a recording plays, **Add to queue** saves the next text without interrupting playback. History rows also have **Queue**. The queue shows waiting recordings and their audio-buffer status, with controls to remove an entry or clear the waiting list. Kokoro buffers the next recording while the current one plays and starts it automatically afterward. **Stop** ends playback and clears the waiting list. **Next recording** skips the current one.

The current text highlights words using Kokoro timing metadata, with optional automatic scrolling. The small player uses only elapsed/total time and one audio slider; percentages remain beside the text on the left. During generation, it shows the amount of audio ready in minutes/seconds and an unknown total (`--:--`). The slider and **−15 / +15 seconds** activate once the entire recording is buffered, keeping a fixed full-recording scale instead of resizing as parts arrive. Playback may begin before the full timeline is ready.

Every history entry shows its percentage, including 0% for new text and 100% after completion. Interrupted entries offer **Continue** from the saved audio position and **From beginning** to restart. Positions are saved in SQLite periodically and on explicit controls, so refresh/restart preserves progress. Generated audio is rebuilt when an interrupted recording is resumed after stopping.

Paste or type text in the **Paste text** tab, then select **Play now** to start it. If a recording is active, **Play now** saves its progress and replaces it, clearing any waiting recordings. Each submission is saved in your listening history. The limit is 50,000 characters and the editor requires nonblank text. Manual submissions work independently of browser capture.

Choose the voice and speed under **Voice settings**. Use **Pause**, **Resume**, and **Stop** on the active history entry or in **Now listening**. If the queue is paused, select **Resume**. **Clear text** clears the draft, and history entries can be replayed with **Play**. A successful submission clears and closes the editor. A failed submission keeps the draft available for retry.

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

Kokoro generates shorter messages as one audio file. For longer passages, including a roughly 650-word article, it generates a small first part so reading can start while the remaining parts are still being prepared. The player shows preparation status and part number. Formatting-only chunks are skipped. Stop cancels playback and pending API requests while keeping the local Kokoro model loaded.

## Playback controls

| Control | Behavior |
| --- | --- |
| History **Play now** | Replaces active playback with that specific message |
| **Pause** / **Resume** | Pauses or resumes Kokoro or Windows playback. Cloud pauses at segment boundaries. |
| **Stop** | Stops current playback, cancels pending generation requests, and clears the speech queue |
| **Next recording** | Skips the current item and advances to the next item. Cloud may finish the active segment first. |
| **Replay** | Starts a finished recording from the beginning |
| **Refresh** | Reloads connection status, settings, voices, history, and diagnostics |

Stop does not disable incoming capture or clear the stability buffer. New captures, or an already buffered update, can enqueue speech afterward. Disable extension capture when you need forwarding to stop.

The reading space shows active text, position, and buffering status. Kokoro tracks playback time from the audio device and uses its word timings for the reading highlight. Cloud position advances between segments.

## Capture and history

- **Read assistant replies** defaults to enabled. Turning it off prevents newly eligible replies from entering the stability buffer; it does not remove existing queue items.
- **Read user prompts** is saved, but the current stability worker only enqueues assistant messages. User prompts can still appear in history and be played manually.
- History is newest first. The dashboard limit ranges from 10 to 500, with a default of 50.
- Captured text and settings persist in SQLite. Pending speech, repeat state, and deduplication state do not survive an API restart.
- Use the delete button on a history row and confirm **Delete text** to remove the recording and its queued copies.

## Diagnose problems

| Symptom | Check or action |
| --- | --- |
| API Offline / unavailable | Check the [health URL](http://127.0.0.1:17832/api/health), start the API, then click Refresh. A failed auxiliary load can also produce the generic unavailable message. |
| No captured messages | Confirm the extension is enabled, refresh the ChatGPT tab, check its port/key, and inspect browser console messages prefixed `[VoiceCompanion]`. |
| Capture returns 401 | Match the extension API key to `Api:ApiKey` and save. |
| Captured replies are silent | Confirm assistant reading is enabled, explicitly persist the desired mode, check output volume/device, and inspect Speech Diagnostics. |
| Kokoro playback fails | Check that the model and Python dependencies are installed and review the playback error in the reading space. |
| Cloud playback fails | Check the saved key/model/voice and diagnostics; the service records provider errors. |
| UI does not recover after API restart | Click Refresh or reload the dashboard. Background polling does not re-establish a failed initial connection. |
| History or playback seems to mix tabs | The current API reuses the latest database session and does not provide isolated queues per conversation. |

**Dismiss** clears the stored detail from the latest playback error. Use the history row to retry playback after resolving the cause.

See [Run locally](local-run.md) for startup problems and [Configuration](configuration.md) for storage and privacy details.
