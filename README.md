# OdinVault

OdinVault is an open-source, self-hosted database backup, verification, replication, and mobile-management platform.

## Goals

- Run close to the primary database for fast, on-demand backups.
- Support multiple database servers and multiple databases per agent.
- Work without a secondary server; replication targets are optional.
- Keep configuration and backup history in an embedded local database.
- Protect credentials and OAuth refresh tokens at rest instead of storing plaintext secrets.
- Expose an API that a mobile app can use directly.
- Support optional storage providers such as Google Drive, S3/MinIO, SFTP, and secondary OdinVault agents.

## Architecture

```text
SQL Server
    |
    v
OdinVault Agent
    |-- REST API
    |-- Backup engine
    |-- RESTORE VERIFYONLY verification
    |-- Catch-up capable cron scheduler
    |-- SQLite metadata/history
    |-- Local retention
    |-- Storage replication
    |      |-- Google Drive
    |      `-- future S3 / SFTP / OdinVault replica
    |
    +---- Mobile App
```

A second backup server is optional. The primary Agent can run beside SQL Server and be managed directly by the mobile client.

## Repository layout

```text
src/
  OdinVault.Core/
  OdinVault.Persistence/
  OdinVault.Database.SqlServer/
  OdinVault.Storage.Local/
  OdinVault.Storage.GoogleDrive/
  OdinVault.Agent/
tests/
docs/
mobile/               # Flutter client will be moved/added here
```

## Current implementation

- Multiple SQL Server database definitions.
- SQLite-backed configuration, backup history, storage targets, and replica history.
- Protected database passwords using ASP.NET Core Data Protection.
- Connection testing.
- Immediate full `BACKUP DATABASE` execution.
- `RESTORE VERIFYONLY ... WITH CHECKSUM` verification.
- Per-database execution locks so manual and scheduled backups cannot overlap for the same database.
- Cron scheduling with missed-slot catch-up after Agent restarts.
- Local retention that deletes old local files without deleting backup history or cloud-replica history.
- Range-enabled local backup download for large `.bak` files.
- Agent API authentication using `X-OdinVault-Key`.
- Database and backup-policy CRUD APIs.
- Google Drive OAuth pairing with short-lived anti-forgery state.
- Encrypted Google Drive refresh-token storage.
- Database-to-storage-target linking.
- Automatic Google Drive replication after a successful backup.
- Replica success/failure history and manual retry.
- Resumable Google Drive upload for large backup files.
- GitHub Actions CI for .NET 10 restore/build.

## First run

Requirements: .NET 10 SDK/runtime and access to a SQL Server instance.

```bash
dotnet restore OdinVault.slnx
dotnet run --project src/OdinVault.Agent/OdinVault.Agent.csproj
```

On first startup OdinVault creates:

```text
data/odinvault.db
data/keys/
data/agent-api-key.txt
data/storage/
```

`agent-api-key.txt` is generated locally and ignored by Git. Send its value on management requests:

```text
X-OdinVault-Key: <agent key>
```

`GET /api/health` is intentionally available without the API key.

## Database API

```text
GET    /api/health
GET    /api/databases
GET    /api/databases/{id}
POST   /api/databases
PUT    /api/databases/{id}
PUT    /api/databases/{id}/policy
DELETE /api/databases/{id}?deleteHistory=false&deleteFiles=false
POST   /api/databases/{id}/test
POST   /api/databases/{id}/backups
GET    /api/databases/{id}/backups
GET    /api/backups/{id}/download
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
  "scheduleCron": "0 2 * * *",
  "isEnabled": true
}
```

Cron expressions are evaluated in UTC. `0 2 * * *` means every day at 02:00 UTC.

The backup directory is currently interpreted from SQL Server's point of view. Running the Agent beside SQL Server is the recommended v1 configuration.

## Google Drive

Create a Google OAuth client for OdinVault and configure the Agent using environment variables (recommended):

```text
ODINVAULT_GOOGLE_CLIENT_ID=<client id>
ODINVAULT_GOOGLE_CLIENT_SECRET=<client secret>
```

The same values can be placed under `OdinVault:GoogleDrive` in local configuration, but secrets must never be committed.

Pairing flow:

```text
POST /api/storage-targets/google-drive/pair/start
POST /api/storage-targets/google-drive/pair/complete
```

Start body:

```json
{
  "targetName": "My Google Drive",
  "redirectUri": "https://your-mobile-callback.example/oauth/google",
  "folderId": null
}
```

The start endpoint returns an authorization URL and short-lived `state`. The mobile app opens the URL. After Google returns an authorization code, the app sends the code and state to `pair/complete`. The Agent exchanges the code, encrypts the refresh token, and stores only the protected value in SQLite.

Storage management API:

```text
GET    /api/storage-targets
PUT    /api/storage-targets/{id}
DELETE /api/storage-targets/{id}

GET    /api/databases/{databaseId}/storage-targets
PUT    /api/databases/{databaseId}/storage-targets/{targetId}
DELETE /api/databases/{databaseId}/storage-targets/{targetId}

GET    /api/backups/{backupId}/replicas
POST   /api/backups/{backupId}/replicate
```

Once a Google Drive target is linked and enabled for a database, every successful backup is automatically uploaded to that target. A failed cloud upload does not turn a valid local database backup into a failed backup; the replica receives its own failed status and can be retried.

## Security

Do not commit connection strings, passwords, API keys, OAuth client secrets, refresh tokens, or backup credentials.

For internet-facing deployments, put the Agent behind HTTPS/reverse proxy. The generated API key is the initial Agent authorization layer. Device pairing and stronger client identity are planned before the stable release.

## Roadmap

- Flutter mobile client integration.
- OdinVault-to-OdinVault replication.
- S3/MinIO and SFTP providers.
- Differential and transaction-log backups.
- Restore workflows and automated restore tests.
- Optional central Hub for managing many Agents.

## Status

Early development. APIs and schemas will change before the first stable release.
