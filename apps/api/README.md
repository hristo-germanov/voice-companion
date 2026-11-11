# Local API and speech service

ASP.NET Core application targeting .NET 8. It binds to `http://127.0.0.1:17832`, stores state in SQLite, and runs capture stability and speech workers. Windows is the intended playback platform.

From this directory, with existing build output:

```powershell
& '.\bin\Debug\net8.0\ChatGPTVoiceCompanion.Api.exe'
```

Keep this working directory so configuration and `app.db` resolve correctly. Health: `GET /api/health`. Stop with `Ctrl+C`. Backend changes require rebuilding; the fast launch script skips builds.

See [Run locally](../../docs/local-run.md) for first setup and rebuild commands, [API reference](../../docs/api.md) for routes, [Configuration](../../docs/configuration.md) for settings, and [Architecture](../../docs/architecture.md) for worker behavior.

Startup uses `EnsureCreated()`, not automatic migrations. Read [Database lifecycle](../../docs/migrations.md) before changing models. Only capture currently validates the API token; other local endpoints are unauthenticated.
