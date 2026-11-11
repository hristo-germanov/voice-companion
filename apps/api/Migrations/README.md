# Database schema

The API currently creates a fresh SQLite schema with `EnsureCreated()` at startup. This directory contains no generated migrations; existing databases are not automatically upgraded after model changes.

Read [Database lifecycle and migrations](../../../docs/migrations.md) before changing the schema or introducing migration commands.
