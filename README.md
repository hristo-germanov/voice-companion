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

Double-click `start.cmd` in the repository root, or run:

```powershell
.\start.cmd
```

The launcher starts the API and dashboard in the background, opens http://localhost:4200/, and reuses services that are already running. It builds the API when needed and installs missing or updated frontend dependencies. Logs are saved under `artifacts/launcher/`. Closing the launcher window leaves the services running.

For a terminal-only launch, use `.\start.cmd -NoBrowser`. Install the prerequisites and Kokoro model once as described in the [local run guide](docs/local-run.md).

For browser capture, load `apps/extension` as an unpacked extension and set its API key to match `Api:ApiKey` in `apps/api/appsettings.json`. See the [user guide](docs/user-guide.md).

## Speech modes

| Mode | Requires | Text processing |
| --- | --- | --- |
| Local | Installed Windows speech voices | On this computer |
| Kokoro (default) | Local Python environment and Kokoro-82M; Heart US / Bella US | On this computer |
| Cloud | Configured API key, network, Windows audio | OpenAI speech endpoint |

Heart US is the default voice. Select Bella US in **Voice settings** to switch. Run `powershell -File scripts/setup-kokoro.ps1` once for the local model and Python dependencies (`-CpuOnly` for a machine without CUDA). The API keeps the model loaded on loopback port 17835; text stays on this computer. Old Edge settings now select Kokoro.

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
