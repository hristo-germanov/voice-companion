# Feature regression checklist

Use this list when changing capture, speech, the reader, or history. Run the checks for the affected rows and preserve the documented behavior for existing users and saved data.

| Feature | Behavior to preserve | Automated checks |
| --- | --- | --- |
| Pasted text | Trim input and accept 1–50,000 characters. **Play now** replaces the active recording and saves progress. **Add to queue** preserves active playback. Success saves the text and clears the editor. Failure keeps the draft. **New text** is the primary action above the reader and opens and focuses the editor. | Angular, .NET |
| Browser capture | Capture visible prompts and replies, keep source IDs stable as replies stream, and suppress duplicate DOM updates. Preserve list and code text without duplicating nested elements. Forward only while capture is enabled, using the configured loopback port and token. | Extension, .NET |
| Automatic assistant reading | Store prompt and reply history independently of automatic reading. When enabled, wait for the quiet window and queue the latest changed assistant text once. | .NET |
| Kokoro startup and buffering | A roughly 650-word passage starts from a small first chunk while later audio is still being generated. With the model warmed, the performance check allows two seconds from submission to audio-device progress. | Windows playback |
| Waiting queue | Keep recordings in order and show whether waiting audio is preparing or ready. Remove one recording or clear waiting recordings without stopping the active one. Start the next recording automatically when the current one finishes or is skipped. | Angular, .NET for queue state. Verify automatic transitions with Windows audio. |
| Playback and reader | Pause, resume, stop, skip to the next recording, and replay finished text. Follow the current word using Kokoro timings. Keep the seek scale fixed once full audio duration is available and support relative seeks. | Angular, .NET, Python. Verify physical playback and controls with Windows audio. |
| History and saved progress | Keep pasted text and captures in separate views. Resume unfinished recordings from saved audio time. Restart or replay from zero. Keep existing text and progress through startup schema upgrades. Confirm deletion and remove associated queue entries and speech events. | Angular, .NET |
| Voices and settings | Default to Kokoro Heart US. Offer Bella US, speed controls, installed Windows voices, and configured Cloud speech. Persist changes and map legacy Edge mode to Kokoro. | Angular, .NET for defaults and settings. Verify actual Windows Local and Cloud synthesis with configured engines. |
| Health and diagnostics | Expose API health, show connection failures and playback errors, and clear error details without changing the stored playback outcome. | Angular, .NET, Windows playback |
| Windows launcher | Start or reuse the API and dashboard, and keep them available after the launcher closes. | Manual launcher check |

## Run the automated checks

From the repository root with frontend dependencies installed:

```powershell
npm.cmd --prefix apps/web test -- --watch=false
node --test tests/extension-capture.test.cjs
dotnet run --project tests/ManualSpeech.Tests
python -m unittest discover -s tests
```

The extension tests run the shipped scripts against synthetic ChatGPT markup. The .NET checks use in-memory SQLite. The Python unit tests cover word timing and original UTF-16 offsets.

On Windows with an active audio device, build the API and run the buffering test. It starts an isolated API with a temporary database, plays a silent WAV, and withholds the second audio chunk until the test observes playback of the first. The optional real-voice run requires the installed Kokoro model and plays a synthetic passage aloud.

```powershell
dotnet build apps/api/ChatGPTVoiceCompanion.Api.csproj
python tests/check_playback_startup.py
python tests/check_playback_startup.py --real-voice
```

Pass `--api-dll <path>` to use a different build. The performance budget covers an idle queue with a warmed Heart US voice at 1× speed. Model loading and capture's quiet window are outside the measured interval.

## Manual checks

- Run the launcher twice and verify that both calls leave the same healthy API and dashboard running. Use `-NoBrowser` for a terminal-only check. [Run locally](local-run.md)
- Load the extension in Chrome or Edge, capture a synthetic exchange on ChatGPT, and verify that the reply appears once and plays after the quiet window. Disable forwarding in extension Options and verify that a new exchange stays out of history. [Connect browser capture](user-guide.md#connect-browser-capture)
- With two recordings queued, verify audible pause and resume, then skip to the next recording. Once a longer recording is fully buffered, seek forward and back. Stop it, restart the API, and resume from the saved point. Verify Windows Local and Cloud speech separately when those modes are configured. [Playback controls](user-guide.md#playback-controls)

The **Read user prompts** preference is stored and prompts can be played from history. Automatic prompt playback remains a known limitation because the stability worker enqueues only assistant replies. [Capture path](architecture.md#capture-path)
