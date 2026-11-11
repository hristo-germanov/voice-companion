# Database lifecycle and migrations

Reviewed on 2026-11-11. The current application uses EF Core 8.0.2 with SQLite. `Program.cs` calls `db.Database.EnsureCreated()` during API startup. There are no generated migration classes in `apps/api/Migrations`.

## Current behavior

- On a fresh database, startup creates the schema from `AppDbContext` and the model classes.
- `EnsureCreated()` does not upgrade an existing schema after model changes and does not create a migration history.
- The playback-progress feature adds a narrow, idempotent startup upgrade in `PlaybackProgressSchema.Ensure`: four progress columns are added to `CapturedMessages` inside a transaction when missing. Existing text/settings are preserved, and older successfully spoken entries are initially marked 100%. Repeated startup preserves subsequently saved partial progress. Other schema changes still require an explicit migration plan.
- The default connection is `Data Source=app.db`; launch from `apps/api` to use `apps/api/app.db`.
- Settings, captured messages, sessions, site profiles, and speech events share this database.

Normal startup does not require running `dotnet ef database update`. Earlier instructions to create an initial migration were setup suggestions, not an implemented migration workflow.

## Before changing a schema

Stop the API and back up the database. Decide whether existing data must be preserved. Recreating a disposable development database and migrating a database containing real history/settings are different operations; do not delete or recreate data as part of a routine launch.

For a future migration workflow, the backend owner should:

1. Configure a standard local tool manifest and a compatible `dotnet-ef` tool (the project references EF Core 8.0.2).
2. Create and review an initial migration against a separate database.
3. Plan how existing `EnsureCreated` databases will be baselined or have data copied into the migration-managed schema. Applying an initial migration directly can conflict with existing tables.
4. Replace `EnsureCreated()` with the chosen migration/application strategy and verify both fresh setup and upgrades with backups.

The repository has files named `dotnet-tools.json` outside the usual `.config/dotnet-tools.json` location, including an empty API-level file. Do not assume a working `dotnet ef` command is configured solely because those files exist. Migration adoption requires implementation and verification beyond this documentation update.

See [Configuration and storage](configuration.md) for backup and credential considerations, and [Development](development.md) for change workflow.
