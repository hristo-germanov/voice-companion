# Web dashboard

Angular 21 control panel for ChatGPT Voice Companion. Source lives in `src/app/app.ts`, `app.html`, and `app.css`. The HTTP API base is currently `http://127.0.0.1:17832` in `app.ts`.

## Develop

From this directory:

```powershell
# First setup, or after a lockfile change:
npm.cmd ci
# Normal development:
npm.cmd start -- --host localhost --port 4200
```

Open http://localhost:4200/. Start the API separately; see [Run locally](../../docs/local-run.md). Source edits reload automatically.

## Checks

```powershell
npm.cmd test -- --watch=false
npm.cmd run build
```

The current tests cover component creation and the dashboard title. They do not test capture transport or audible playback. Use the development server for routine local runs; production builds are for build/release checks.

The UI polls tracker/diagnostics every two seconds and history about every six seconds while connected. Settings save on change. Mode display has known default/loading caveats described in [Configuration](../../docs/configuration.md#mode-default-caveats).

See [User guide](../../docs/user-guide.md), [API reference](../../docs/api.md), and [Development](../../docs/development.md).
