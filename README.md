# ChatGPT Voice Companion

A Windows companion that captures messages from ChatGPT in Chrome or Edge, stores them locally, and reads replies aloud. An Angular dashboard controls playback, speech settings, history, and diagnostics.

## Documentation

- [Run locally](docs/local-run.md): prerequisites, startup commands, health checks, rebuilds, and shutdown.
- [User guide](docs/user-guide.md): extension setup, speech modes, playback controls, and troubleshooting.
- [Configuration](docs/configuration.md): ports, persisted settings, defaults, and data storage.
- [Architecture](docs/architecture.md): components, message flow, persistence, and implementation limits.
- [API reference](docs/api.md): endpoints, payloads, responses, and capture authentication.
- [Development guide](docs/development.md): source map, change workflow, checks, and maintenance.
- [Database lifecycle](docs/migrations.md): schema creation and migration constraints.
- [GitHub preparation](docs/github.md): excluded local files, fresh-clone setup, and publishing commands.

## Quick launch

Use two PowerShell terminals. These commands assume dependencies and the API build already exist; see the [local run guide](docs/local-run.md) for first setup.

On a fresh clone, copy `apps/api/appsettings.example.json` to `apps/api/appsettings.json` and set a private capture token before building. The real configuration and database are excluded from Git.

API:

```powershell
Set-Location 'C:\Work\Codex\Text to speach\apps\api'
& '.\bin\Debug\net8.0\ChatGPTVoiceCompanion.Api.exe'
```

Frontend:

```powershell
Set-Location 'C:\Work\Codex\Text to speach\apps\web'
npm.cmd start -- --host localhost --port 4200
```

Open [the dashboard](http://localhost:4200/). Check [API health](http://127.0.0.1:17832/api/health) for `{"status":"ok"}`. Press `Ctrl+C` in each terminal to stop.

For browser capture, load `apps/extension` as an unpacked extension and set its API key to match `Api:ApiKey` in `apps/api/appsettings.json`. See the [user guide](docs/user-guide.md).

## Speech modes

| Mode | Requires | Text processing |
| --- | --- | --- |
| Local | Installed Windows speech voices | On this computer |
| Edge | `edge-tts` on PATH, network, Windows audio | Online speech service |
| Cloud | Configured API key, network, Windows audio | OpenAI speech endpoint |

The UI initially displays Edge. Explicitly select a mode to persist it before playback; see [default behavior](docs/configuration.md#mode-default-caveats).

## Repository layout

| Path | Contents |
| --- | --- |
| `apps/web/` | Angular 21 dashboard |
| `apps/api/` | ASP.NET Core API targeting .NET 8; SQLite and speech workers |
| `apps/extension/` | Manifest V3 browser capture extension |
| `packages/shared/`, `packages/ui/` | Shared-package areas |
| `scripts/` | Fast API launch and bootstrap scripts |
| `tests/` | Reserved integration-test area; currently empty |
| `docs/` | Application and development documentation |
| `AGENTS.md` | Contributor roles and ownership rules |

Documentation describes source behavior reviewed on 2026-11-11. The earlier local launch verified frontend HTTP 200 and API health/settings/voices/diagnostics responses; audible playback was not verified. `docs/spec.txt` and `docs/session-state.md` contain historical planning/context and may differ from the implementation.
