# Architecture — البنية

## Overview

The Database Bridge Lab is a standalone Windows agent that:

1. Maintains an **outbound** transport connection (Local Test, WebSocket, or Supabase long-poll tunnel).
2. Receives JSON **commands** (`bridge.health`, `database.test`, `sql.execute`).
3. Validates requests, enforces **permission policy**, executes SQL locally.
4. Returns JSON **responses** and writes file logs.

```text
Transport (LocalTest / WebSocket / SupabaseTunnel)
        │
        ▼
CommandDispatcher
        │
   RequestValidator
        │
   Command Handler
        │
   PermissionPolicy + SqlCommandExecutor
        │
   SQL Server (local profile)
```

## Layers

### Protocol

DTOs only: `BridgeCommand`, `BridgeResponse`, payloads, error codes. No business logic.

### Core

- `RequestValidator` — protocol, tunnel, age, SQL limits, parameter types.
- `QueryClassifier` — Read / Write / Schema / Administrative.
- `PermissionPolicy` — profile permission levels.
- `CommandDispatcher` — routing, concurrency, duplicate `requestId` cache.
- Handlers — `BridgeHealthHandler`, `DatabaseTestHandler`, `SqlExecuteHandler`.
- `ActiveRequestTracker` — cancel in-flight SQL by `requestId`.

### Infrastructure

- `JsonDatabaseProfileStore` — profiles under `%LocalAppData%\Almutamakkin\DatabaseBridgeLab\settings`.
- `DpapiSecretProtector` — encrypt SQL passwords (CurrentUser DPAPI).
- `SqlCommandExecutor` — parameterized execution via `Microsoft.Data.SqlClient`.
- `LocalTestCommandTransport` / `WebSocketCommandTransport` / `SupabaseBridgeTransport`.
- `FileBridgeLogger` — daily log files.

#### Supabase tunnel — نفق Supabase

Outbound-only HTTP long-poll to Supabase Edge Functions (no inbound Tailscale port):

1. **Register** (once, from UI): `POST …/bridge-register` → `tunnelId`, `pairingCode`, `deviceSecret`.
2. **Poll loop** (while bridge running): `POST …/bridge-poll` with `x-bridge-secret` → `commands[]`.
3. **Respond**: `POST …/bridge-respond` with command `requestId` and `BridgeResponse`.

Credentials persist in `%LocalAppData%\Almutamakkin\DatabaseBridgeLab\appsettings.json`:

- `tunnelId` — assigned at registration.
- `encryptedDeviceSecret` — DPAPI-protected `deviceSecret`.
- `supabaseUrl` / `anonKey` — optional overrides (defaults point to project Edge Functions base URL).

`BridgeHostService` wires `CommandReceived` → dispatcher → `SendResponseAsync` for all transports.

### App (WinForms)

- **MainForm** — start/stop bridge, status, open logs, test DB, profiles, test console.
- **DatabaseProfilesForm** — CRUD connection profiles; blank password on edit keeps existing encrypted secret.
- **TestConsoleForm** — paste/run JSON through `ICommandDispatcher.DispatchAsync`.
- **BridgeHostService** — wires `CommandReceived` → dispatcher → `SendResponseAsync`.

## Data locations

```text
%LocalAppData%\Almutamakkin\DatabaseBridgeLab\
  settings\appsettings.json
  settings\database-profiles.json
  logs\bridge-YYYYMMDD.log
```

## Security notes

- Passwords are never stored in plain text in JSON files.
- Requests must not contain connection strings or passwords in payload.
- `ReadOnly` is the default profile permission; `FullAccess` requires explicit confirmation in the UI.
- `ReadOnly` allows session-local analysis batches (`CREATE`/`INSERT`/`DROP`/`SELECT INTO` on `#temp` / `##temp` / `@table` only) and still blocks permanent data or schema changes.
- Prefer a dedicated SQL login with least privilege for lab testing (`AlmutamakkinBridgeLab` database).

## Fresh session behavior

The desktop bridge is intentionally stateless between launches. Before the
dependency container loads settings or profiles, it deletes the persisted
pairing, database-profile, snapshot-fingerprint, and change-cursor files under
`%LocalAppData%\Almutamakkin\DatabaseBridgeLab`. Each start therefore requires
a new pairing and a new local or network database selection. Diagnostic logs
remain available separately for troubleshooting.
