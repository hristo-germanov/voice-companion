# Development and maintenance

Reviewed against source on 2026-11-11. Read [AGENTS.md](../AGENTS.md) for role scopes and handoff rules before changing code. See [Run locally](local-run.md) for installation, startup, rebuilding, and stopping services.

## Source map

| Area | Files | Responsibility |
| --- | --- | --- |
| Frontend | `apps/web/src/app/app.ts` | HTTP calls, signals/state, polling, settings, tracker calculations |
| Dashboard view | `apps/web/src/app/app.html`, `app.css` | Controls, history, layout, and highlighting |
| Frontend setup | `apps/web/src/app/app.config.ts`, `apps/web/angular.json`, `package.json` | Providers, build/serve/test commands |
| API composition | `apps/api/Program.cs` | DI, CORS, SQLite startup, hosted workers, controllers |
| API contracts | `apps/api/Controllers/` | Capture, history, settings, speech, health, voice routes |
| Capture processing | `apps/api/Services/MessageDeduper.cs`, `StabilityBuffer.cs` | Hash suppression and quiet-window buffering |
| Playback | `apps/api/Services/SpeechQueue.cs`, `ISpeechQueue.cs` | Queue, speech engines, controls, tracker, event persistence |
| Persistence | `apps/api/Data/AppDbContext.cs`, `Models/` | EF Core entities and indexes |
| Options | `apps/api/Options/`, `appsettings.json` | Startup configuration and fallback values |
| Browser capture | `apps/extension/dom-adapter.js`, `content-script.js` | Selectors, extraction, source identity, mutation scanning |
| Extension transport | `apps/extension/background.js` | Script attachment and API forwarding |
| Extension settings | `apps/extension/options.html`, `options.js`, `manifest.json` | Options page, permissions, registration |
| Launch scripts | `scripts/run-api-fast.ps1`, `run-api-bootstrap.ps1` | API fast start and missing-output setup |

## Change workflow

1. Reproduce the behavior and identify the owning component. Prefer the smallest change and observe the role boundaries in AGENTS.md.
2. Frontend edits reload through `ng serve`. Backend edits require stopping the API, building, and restarting. The fast scripts do not rebuild changed source.
3. For extension edits, reload the unpacked extension on its extensions page, then refresh the ChatGPT tab so the content script is replaced.
4. If changing an API contract, update its controller, corresponding Angular/extension caller, and [API reference](api.md) together with the appropriate handoff.
5. If changing models, review [database lifecycle](migrations.md) first; startup does not migrate existing databases.
6. Update the relevant guide and verify affected behavior. Distinguish source review from executed runtime checks in handoff notes.

## Available automated checks

From `apps/web`:

```powershell
npm.cmd test -- --watch=false
npm.cmd run build
```

The frontend suite exercises dashboard interactions with mocked API responses. The production build checks compilation and configured bundle and style budgets. See the [feature regression checklist](feature-regressions.md) for test commands for the API, browser extension, and actual Windows playback.

From the repository root, with the API stopped:

```powershell
$env:DOTNET_CLI_HOME = Join-Path (Get-Location).Path '.dotnet'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
dotnet build .\apps\api\ChatGPTVoiceCompanion.Api.csproj --no-restore
```

Restore first if assets are missing. Add directly impacted tests when changing behavior.

## Manual verification checklist

- Confirm health returns `ok` and the dashboard loads. Click Refresh and verify API Online.
- Save the desired mode explicitly; account for the [mode loading caveats](configuration.md#mode-default-caveats).
- Enable and configure the extension, generate a new reply, and confirm history updates. Check unchanged DOM updates do not continually queue duplicates.
- Play a history item and confirm actual audio, tracker progress, and a completed speech event. Verify each changed engine independently.
- Exercise Pause, Resume, Skip, Repeat, and Stop. Expect Cloud pauses/skips at segment boundaries and possible new queue entries from capture after Stop.
- Restart the API and confirm settings/history survive while pending queue and tracker state reset.
- Check extension console output and the API terminal for errors. Avoid recording credentials or private message text in shared output.

## Documentation maintenance

Keep the README as the documentation entry point. Update the user guide for interaction changes, configuration for settings/default changes, API reference for route/payload changes, and architecture for flow/persistence changes. Preserve `local-run.md` as the practical startup runbook.

`docs/spec.txt` and `docs/session-state.md` are historical context, not authoritative current behavior. Generated `bin*`, `obj*`, `node_modules`, and Angular build/cache directories are not source; exclude them from repository searches. This workspace contains nested `bin_verify*` outputs, so use narrow source paths when inspecting it.
