# Session State

Last updated: 2026-02-22

## Current Goal
- Keep Edge TTS workflow stable and usable with EN-US voices.
- Keep speech tracker accurate and actionable.
- Preserve a reliable handoff for tomorrow.

## Implemented So Far
- Edge mode is default in frontend and backend defaults.
- Default Edge voice changed to `en-US-AndrewMultilingualNeural`.
- Edge voice dropdown now uses EN-US voices only.
- EN-US voice source endpoint:
  - `GET /api/voices/edge-en-us`
  - `GET /api/voices/edge-en-eu` currently aliases to same EN-US handler for compatibility.
- Legacy fallback option in UI is now `en-US-AndrewMultilingualNeural` (labeled as default).
- Speech Tracker now has controls:
  - `Play`, `Resume`, `Pause`, `Stop`
- Speed/rate control moved into Speech Tracker and is mode-aware.
- Per-message playback tracking in history is present (current item + progress bar).
- Word-level highlight added in tracker text (based on `currentCharIndex`).
- Edge pause/resume backend behavior improved:
  - `Pause()` pauses active `WaveOutEvent`.
  - `Resume()` resumes paused `WaveOutEvent`.
- Edge progress now updates continuously during playback (not only at sentence end), to support moving word highlight.
- Removed large voice list under Edge dropdown (dropdown only).
- UI polling optimized:
  - no overlapping refresh calls
  - history refresh throttled relative to tracker/diagnostics.
- Added fast run scripts:
  - `scripts/run-api-fast.ps1`
  - `scripts/run-api-bootstrap.ps1` (smart restore/build only when needed)

## Known Open Items
- User reported restore/start still feels slow at times.
- Need real-user validation tomorrow that word highlight now moves correctly in Edge mode after API restart.
- `dotnet` build sometimes slow/locked if another API process is already running.

## Files Most Recently Edited
- `apps/api/Controllers/VoicesController.cs`
- `apps/api/Services/SpeechQueue.cs`
- `apps/api/Options/SpeechOptions.cs`
- `apps/api/appsettings.json`
- `apps/web/src/app/app.ts`
- `apps/web/src/app/app.html`
- `apps/web/src/app/app.css`
- `apps/web/angular.json`
- `scripts/run-api-fast.ps1`
- `scripts/run-api-bootstrap.ps1`
- `README.md`

## Quick Run Commands
- API fast start:
  - `cd "C:\Work\Codex\Text to speach"`
  - `powershell -ExecutionPolicy Bypass -File ".\scripts\run-api-fast.ps1"`
- API smart bootstrap (only if needed):
  - `cd "C:\Work\Codex\Text to speach"`
  - `powershell -ExecutionPolicy Bypass -File ".\scripts\run-api-bootstrap.ps1"`
- Web dev server:
  - `cd apps/web`
  - `npm.cmd start`
