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
    |-- RESTORE VERIFYONLY verification
    |-- SQLite metadata store
    |-- Local storage abstraction
    |-- Optional replication/storage providers
    |
    +---- Mobile App
```

A second backup server is optional. The primary OdinVault Agent can run on the same machine as SQL Server and can be managed directly by the mobile client.

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

## Current implementation

The initial Agent supports:

- Multiple SQL Server database definitions.
- SQLite-backed metadata and backup history.
- Protected database passwords using ASP.NET Core Data Protection.
- Connection testing.
- Immediate full `BACKUP DATABASE` execution.
- `RESTORE VERIFYONLY ... WITH CHECKSUM` verification.
- Local retention by backup count.
- Range-enabled backup download for large `.bak` files.
- Agent API authentication using `X-OdinVault-Key`.

## First run

Requirements: .NET 10 SDK/runtime and access to a SQL Server instance.

```bash
dotnet restore
dotnet run --project src/OdinVault.Agent/OdinVault.Agent.csproj
```

On first startup OdinVault creates:

```text
data/odinvault.db
data/keys/
data/agent-api-key.txt
```

`agent-api-key.txt` is generated locally and ignored by Git. Send its value as:

```text
X-OdinVault-Key: <agent key>
```

`GET /api/health` is intentionally available without the API key.

## Initial API

```text
GET  /api/health
GET  /api/databases
POST /api/databases
POST /api/databases/{id}/test
POST /api/databases/{id}/backups
GET  /api/databases/{id}/backups
GET  /api/backups/{id}/download
```

Example database registration body:

```json
{
  "name": "Production",
  "host": "localhost",
  "port": 1433,
  "databaseName": "MyDatabase",
  "username": "sa",
  "password": "do-not-commit-this",
  "trustServerCertificate": true,
  "backupDirectory": "D:\\Backups\\OdinVault",
  "maxLocalBackups": 7,
  "verifyAfterBackup": true,
  "scheduleCron": null
}
```

The backup directory is currently interpreted from SQL Server's point of view. Running the Agent beside SQL Server is the recommended v1 configuration.

## Security

Do not commit real connection strings, passwords, API keys, OAuth tokens, or backup credentials. OdinVault stores secret values encrypted at rest and only keeps non-secret connection metadata in SQLite.

For internet-facing deployments, put the Agent behind HTTPS/reverse proxy. The generated API key is the first security layer; pairing and stronger device identity will be added as the mobile workflow matures.

## Roadmap

- Scheduling/background jobs.
- Google Drive resumable storage provider.
- Optional OdinVault-to-OdinVault replication.
- Flutter mobile client integration.
- Differential and transaction-log backups.
- Restore workflows and automated restore tests.
- Optional central Hub for managing many Agents.

## Status

Early development. APIs and schemas will change before the first stable release.
