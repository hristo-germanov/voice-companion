# Run the app locally on Windows

Verified on 2026-11-11. Run commands in PowerShell. The workspace is `C:\Work\Codex\Text to speach`.

For daily operation after startup, see the [User guide](user-guide.md). For settings and default-mode caveats, see [Configuration](configuration.md).

## What runs

| Component | Address | Purpose |
| --- | --- | --- |
| Angular frontend | http://localhost:4200/ | Settings, history, and speech controls |
| .NET API | http://127.0.0.1:17832 | Local database and speech service |
| Health check | http://127.0.0.1:17832/api/health | Returns `{"status":"ok"}` |

Keep two terminals open, one for each service. Before starting, check whether the services already respond using the verification commands below; reuse an existing working instance.

## Prerequisites

- Windows, PowerShell, Node.js/npm, and a .NET SDK capable of building `net8.0`, plus the .NET 8 ASP.NET Core runtime.
- The frontend was successfully started with Node.js `v22.14.0`.
- Edge speech mode requires the `edge-tts` executable on the API process's PATH and network access. The executable was available during the verified launch; audible speech playback was not tested. If needed, install it with `py -m pip install edge-tts`, ensure the Python Scripts directory is on PATH, and restart the API terminal.

## Normal startup

Terminal 1: run the existing API build directly. This avoids SDK startup overhead and was the successful API launch method in the verified session.

```powershell
Set-Location 'C:\Work\Codex\Text to speach\apps\api'
& '.\bin\Debug\net8.0\ChatGPTVoiceCompanion.Api.exe'
```

Wait for `Now listening on: http://127.0.0.1:17832`. Run from `apps\api` so the API loads the correct configuration and SQLite database (`apps\api\app.db`). If the executable is missing or backend code has changed, build it using the instructions below first.

Terminal 2: start the frontend development server.

```powershell
Set-Location 'C:\Work\Codex\Text to speach\apps\web'
npm.cmd start -- --host localhost --port 4200
```

Wait for the build to finish and for the local URL to appear. Open http://localhost:4200/ in your browser. Keep port 4200: the API allows this frontend origin, and the frontend uses the API at port 17832.

## First setup or backend changes

On a fresh clone, create the API configuration once. Existing local configuration should be preserved:

```powershell
Set-Location 'C:\Work\Codex\Text to speach'
if (-not (Test-Path '.\apps\api\appsettings.json')) {
  Copy-Item '.\apps\api\appsettings.example.json' '.\apps\api\appsettings.json'
}
```

Edit `Api:ApiKey` in the new `apps/api/appsettings.json` to a private token and use the same token in extension Options. The example uses `change-me` as a placeholder. `appsettings.json`, SQLite data, logs, dependencies, and build outputs are ignored by Git. See [GitHub preparation](github.md).

Install frontend dependencies only if they are missing or the lockfile has changed:

```powershell
Set-Location 'C:\Work\Codex\Text to speach\apps\web'
npm.cmd ci
```

Stop the API before rebuilding, then restore and build:

```powershell
Set-Location 'C:\Work\Codex\Text to speach'
$env:DOTNET_CLI_HOME = Join-Path (Get-Location).Path '.dotnet'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
dotnet restore .\apps\api\ChatGPTVoiceCompanion.Api.csproj
dotnet build .\apps\api\ChatGPTVoiceCompanion.Api.csproj --no-restore
```

Start both services using the normal startup commands after successful installation/build.

The repository also provides these API launch scripts:

```powershell
Set-Location 'C:\Work\Codex\Text to speach'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-api-fast.ps1
# Alternative for missing restore/build output:
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-api-bootstrap.ps1
```

Run only one API launch command. The fast script uses `dotnet run --no-restore --no-build`. Bootstrap restores/builds only when output is missing; it does not detect stale binaries after source changes. Use the explicit build above after backend edits. During the verified session, `dotnet run` remained busy without opening the API port; launching the existing executable directly succeeded.

## Verify startup

Run in another PowerShell terminal:

```powershell
Invoke-RestMethod 'http://127.0.0.1:17832/api/health' -TimeoutSec 10
(Invoke-WebRequest 'http://localhost:4200/' -UseBasicParsing -TimeoutSec 10).StatusCode
```

Expected: health status `ok` and frontend HTTP status `200`. The API prints request and startup logs in its terminal; Angular prints build errors in its terminal. For a speech check, open the frontend, select a configured speech mode/voice, and play an existing history item. Confirm audible playback and tracker updates.

## Stop and restart

Press `Ctrl+C` in each service terminal. Restart using the normal startup commands. Frontend edits reload automatically; backend edits require stopping, rebuilding, and restarting the API. Avoid rebuilding the frontend for ordinary development.

For an agent running services in tool sessions, retain the session IDs and stop only the processes it started. If a process must be stopped by PID, verify its identity and command line first; do not terminate every Node or .NET process on the machine.

## Troubleshooting

- **Frontend says the API is unavailable:** check the health URL and API terminal. Start the API from `apps\api`, then refresh the frontend.
- **API script stays busy without a listening message:** stop that attempt with `Ctrl+C`, then launch the built executable directly. Do not leave duplicate startup attempts running.
- **Port is occupied:** check the existing service before starting another. If Angular offers another port, stop and resolve port 4200 instead; another port would require configuration changes.
- **Build reports a locked executable:** stop the running API, build, and restart.
- **Missing executable/assets:** perform first setup/build above, or use the bootstrap script.
- **Edge playback fails:** run `Get-Command edge-tts` and `edge-tts --version` in the API terminal; check its logs and the frontend speech diagnostics. Restart the API after changing PATH.

## Browser capture (optional)

The web app and API can run independently of browser capture. To capture chat messages, load `apps\extension` as an unpacked extension in Chrome/Edge and configure its backend port and API key to match the local API configuration. See [extension setup](../apps/extension/README.md). Do not copy the API key into documentation or logs.
