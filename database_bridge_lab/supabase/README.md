# Supabase Bridge Edge Functions — دوال الجسر على Supabase

Project URL:

```text
https://mapfattjpsuizvlklddl.supabase.co
```

All five functions run with `verify_jwt = false` and rely on custom headers plus the mobile anon key already embedded in the Flutter app (`lib/main.dart`).

## Database — قاعدة البيانات

Bridge state lives in the **public** schema (not a separate `bridge` schema). Edge functions use the default Supabase client without `.schema(...)`:

| Table / RPC | Purpose |
| --- | --- |
| `public.bridge_devices` | Registered Windows bridge PCs, pairing codes, device secret hashes |
| `public.bridge_mobile_sessions` | Mobile session tokens after pairing |
| `public.bridge_commands` | Pending/completed relay commands |
| `public.bridge_claim_pending_commands(p_device_id, p_limit)` | Atomically claim pending commands for polling |

Mirror source: `supabase/functions/<name>/index.ts`.

## Flow — مسار العمل

```text
Mobile app                Supabase Edge               Windows Bridge
    |                          |                            |
    |-- POST bridge-pair ----->|                            |
    |   {pairingCode,          |-- register session ------->|
    |    mobileDeviceId}       |                            |
    |<-- {tunnelId,            |                            |
    |     sessionToken} -------|                            |
    |                          |                            |
    |-- POST bridge-relay ---->|-- forward command -------->|
    |   x-bridge-session       |<-- bridge response --------|
    |   {messageType,payload}  |                            |
    |<-- {success,response} ---|                            |
```

1. Run **Almutamakkin Database Bridge** on the pharmacy PC and copy the pairing code.
2. In the app login screen choose **نفق الجسر**, paste the code, tap **اقتران**.
3. The app stores `tunnelId` and `sessionToken` securely per POS system.
4. SQL reads go through `bridge-relay` with `messageType: sql.execute`.
5. Direct Tailscale/JTDS mode remains available as **مباشر (Tailscale)**.

## bridge-pair

```http
POST /functions/v1/bridge-pair
Content-Type: application/json
Authorization: Bearer <anon-key>
```

Request body:

```json
{
  "pairingCode": "ABC-123",
  "mobileDeviceId": "<device-id>"
}
```

Response body:

```json
{
  "tunnelId": "LAB-TNL-001",
  "sessionToken": "<opaque-session-token>",
  "expiresAt": "2026-07-21T12:00:00Z"
}
```

## bridge-relay

```http
POST /functions/v1/bridge-relay
Content-Type: application/json
Authorization: Bearer <anon-key>
x-bridge-session: <sessionToken>
```

Request body:

```json
{
  "messageType": "sql.execute",
  "payload": {
    "databaseProfile": "Marketing",
    "sql": "SELECT 1 AS test",
    "maxRows": 5000,
    "timeoutSeconds": 30
  },
  "waitMs": 30000
}
```

Response body:

```json
{
  "success": true,
  "requestId": "REQ-...",
  "response": {
    "success": true,
    "messageType": "sql.execute.response",
    "payload": {
      "resultSets": [
        {
          "index": 0,
          "rows": [{ "test": 1 }]
        }
      ]
    }
  },
  "errorCode": null,
  "errorMessage": null
}
```

Supported relay `messageType` values match the bridge protocol:

| messageType | Purpose |
| --- | --- |
| `bridge.health` | Bridge online check |
| `database.test` | Test a named database profile on the PC |
| `sql.execute` | Run a read-only SELECT |

See also `../docs/MESSAGE_PROTOCOL.md` for the full command envelope.

## Database profile names — أسماء ملفات القاعدة

The mobile app sends `databaseProfile` from `DatabaseConnectionProfile.effectiveBridgeDatabaseProfile`:

| POS system | Default profile name |
| --- | --- |
| Marketing (`أبوغريس`) | `Marketing` |
| Infinity Retail (`إنفينيتي`) | `InfinityRetailDB` |

Lab bridge installs may register `MarketingLab`, `InfinityLab`, or `BridgeLab` instead. Override per system in secure storage if needed.

## Security notes — ملاحظات أمنية

- Never ship the Supabase **service_role** key in the mobile app or bridge installer.
- Pairing codes must be short-lived and single-use on the server.
- `sessionToken` is stored like a password in `FlutterSecureStorage`.
- Production should use least-privilege SQL logins on the pharmacy PC; the phone must not hold database passwords in bridge mode.

## Flutter integration — ربط Flutter

| File | Role |
| --- | --- |
| `lib/data/services/bridge_tunnel_service.dart` | `pair`, `relay`, `healthCheck` |
| `lib/data/services/bridge_sql_connection_client.dart` | `SqlConnectionClient` over relay |
| `lib/data/services/routing_sql_connection_client.dart` | Direct vs bridge delegation |
| `lib/data/services/pos_sql_client_factory.dart` | Shared routing client factory |
| `lib/ui/screens/login_screen.dart` | Transport toggle and pairing UI |

Invoke functions with:

```dart
Supabase.instance.client.functions.invoke('bridge-pair', body: {...});
Supabase.instance.client.functions.invoke(
  'bridge-relay',
  headers: {'x-bridge-session': sessionToken},
  body: {...},
);
```
