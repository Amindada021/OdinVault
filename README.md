# OdinVault

OdinVault is an open-source, self-hosted database backup, verification, replication, and mobile-management platform.

## Goals

- Run close to the primary database for fast, on-demand backups.
- Support multiple database servers and multiple databases per agent.
- Work without a secondary server; replication targets are optional.
- Keep configuration and backup history in an embedded local database.
- Protect credentials at rest instead of storing plaintext secrets.
- Expose an API that a mobile app can use directly.
- Add optional storage providers such as Google Drive, S3/MinIO, SFTP, and secondary OdinVault agents.

## Initial architecture

```text
SQL Server
    |
    v
OdinVault Agent
    |-- REST API
    |-- Backup engine
    |-- Verification
    |-- SQLite metadata store
    |-- Local storage
    |-- Optional replication/storage providers
    |
    +---- Mobile App
```

The first implementation targets SQL Server, local storage, SQLite metadata, and an extensible provider model.

## Repository layout

```text
src/
  OdinVault.Core/
  OdinVault.Persistence/
  OdinVault.Database.SqlServer/
  OdinVault.Storage.Local/
  OdinVault.Agent/
tests/
docs/
mobile/               # Flutter client will be moved/added here
```

## Security

Do not commit real connection strings, passwords, API keys, OAuth tokens, or backup credentials. OdinVault stores secret values encrypted at rest and only keeps non-secret connection metadata in SQLite.

## Status

Early development. APIs and schemas will change before the first stable release.
