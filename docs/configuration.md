# Configuration and storage

Source reviewed on 2026-11-11. Defaults below describe repository code, not guaranteed provider capabilities.

## Configuration locations

The repository includes `apps/api/appsettings.example.json` with a placeholder capture token. Copy it to `apps/api/appsettings.json` on a fresh clone and configure a private token. The real `appsettings.json` is ignored by Git and remains local; never force-add it.

| Location | Purpose | When applied |
| --- | --- | --- |
| `apps/api/appsettings.json` | Capture key, Kestrel address, SQLite connection, speech and stability fallbacks | API startup |
| SQLite `AppSettings` | Dashboard settings and runtime speech overrides | When each message begins; role flags during capture |
| Extension Options / `chrome.storage.sync` | Backend port, capture token, forwarding enabled | Each forwarded capture event |
| `apps/web/src/app/app.ts` | Hard-coded API base URL | Frontend compilation |

ASP.NET Core also uses its standard environment-variable configuration, for example `Stability__WindowMs`. `SpeechOptions` does not expose a cloud API-key field: the worker reads that key from `speech.cloud.api_key` in SQLite.

## Backend settings

| Setting | Shipped value / behavior |
| --- | --- |
| `Kestrel:Endpoints:Http:Url` | `http://127.0.0.1:17832`; controls the actual listener |
| `Api:Port` | `17832`; exists as an option but is not used to bind the listener |
| `Api:ApiKey` | Capture token; copy its local value to extension Options without publishing it |
| `ConnectionStrings:AppDb` | `Data Source=app.db`; relative to the working directory |
| `Stability:WindowMs` | `1200`; worker minimum is 300 ms |
| `Stability:PollMs` | `250`; worker minimum is 100 ms |
| `Speech:Mode` | `edge` in configuration; see fallback caveat below |
| `Speech:Rate`, `Speech:Volume`, `Speech:Voice` | `0`, `100`, empty (Windows default voice) |
| `Speech:EdgeVoice`, `Speech:EdgeRatePercent` | `en-US-AndrewMultilingualNeural`, `0` |
| `Speech:CloudModel`, `Speech:CloudVoice`, `Speech:CloudSpeed` | `gpt-4o-mini-tts`, `alloy`, `1.0` |

Launch the API from `apps/api` to use the intended appsettings and database. Restart after editing startup settings.

## Persisted speech settings

All values are strings. `PUT /api/settings` upserts a key/value pair; it requires a nonblank key but does not validate individual setting names or values. The speech worker parses recognized values and clamps numerical ranges.

| Key | Default / accepted range | Use |
| --- | --- | --- |
| `speech.mode` | `local`, `edge`, `cloud`; see caveat | Worker selection |
| `speech.read_assistant` | `true` | Automatic assistant reading eligibility |
| `speech.read_user` | `false` | Capture eligibility flag; automatic user playback is currently blocked by the stability worker |
| `speech.voice` | Empty | Windows voice name |
| `speech.rate` | `0`; -10 through 10 | Local speech rate |
| `speech.volume` | `100`; 0 through 100 | Local synthesizer volume; no dashboard control |
| `speech.edge.voice` | `en-US-AndrewMultilingualNeural` | Edge voice short name |
| `speech.edge.rate_percent` | `0`; -50 through 100 | Edge rate percent |
| `speech.cloud.api_key` | Empty | Cloud speech credential |
| `speech.cloud.model` | `gpt-4o-mini-tts` | Model sent to cloud speech endpoint |
| `speech.cloud.voice` | `alloy` | Voice sent to cloud speech endpoint |
| `speech.cloud.speed` | `1.0`; 0.25 through 4.0 | Cloud speed |

## Mode default caveats

The UI initially displays Edge, and the options/configuration also name Edge. However, `SpeechQueue.LoadRuntimeSettings` maps every configured mode except Cloud to Local before applying a saved `speech.mode` override. With no saved override, the worker can use Local while the UI shows Edge. The diagnostics endpoint independently falls back to `local` when the setting is absent.

Explicitly select a different mode, then your desired mode in the UI on first setup. This writes `speech.mode`. Alternatively, use the settings API example in the [API reference](api.md).

There is also a UI loading issue: `parseSpeechMode` maps a saved `local` value back to Edge on reload. If Local is desired, select it again after reload; the stored backend setting can still be Local despite the displayed value. These behaviors are documented here without changing product code.

## Changing ports

Keep the default ports for normal use. To change the API port, update the Kestrel URL, the frontend `apiBase`, and the extension Options port. Changing `Api:Port` alone has no effect on the listener. To move the frontend off port 4200, also update the allowed CORS origins in `apps/api/Program.cs` and restart the API.

## Data, credentials, and backup

- `apps/api/app.db` contains captured text, settings, session records, and speech events when launched from the intended directory.
- The cloud API key is stored as a plain setting and returned by settings GET; a password input only masks its display. Do not share settings responses, database copies, or logs containing credentials or private text.
- The capture token and Cloud API key are separate credentials. The extension uses the capture token; Cloud synthesis uses the Cloud API key.
- Extension settings use browser sync storage, so connection settings may sync with the browser profile.
- Edge audio is written to a temporary `edge-tts-*` directory; the worker attempts to delete it after playback. Cloud audio is held in memory.
- For backup, stop the API first, then copy `app.db` to your chosen backup location. Restore while the API is stopped. Preserve any existing SQLite sidecar files as needed; do not overwrite an active database.
- The app has no retention cleanup, history deletion UI, or key-encryption layer. Treat database copies as sensitive application data.
