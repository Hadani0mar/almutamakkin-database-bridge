# Message Protocol — بروتوكول الرسائل

Protocol version: **1.0**

## Command envelope

All commands share this shape:

```json
{
  "protocolVersion": "1.0",
  "messageType": "<command>",
  "requestId": "REQ-unique-id",
  "tunnelId": "LAB-TNL-001",
  "sentAtUtc": "2026-07-20T18:00:00Z",
  "payload": { }
}
```

| Field | Rules |
| --- | --- |
| `protocolVersion` | Must be `1.0` |
| `messageType` | See Commands below (`sql.execute`, `product.photo`, printer.*, …) |
| `requestId` | Non-empty; duplicates return cached response |
| `tunnelId` | Must match local bridge settings |
| `sentAtUtc` | UTC; max age 5 minutes (configurable) |
| `payload` | Command-specific object |

## Commands

### bridge.health

Empty payload `{}`. Returns bridge status without SQL.

Response `messageType`: `bridge.health.response`

### database.test

```json
{
  "databaseProfile": "BridgeLab"
}
```

Tests connectivity for a named profile. Response: `database.test.response`

### database.list

```json
{
  "databaseProfile": "Marketing"
}
```

Lists every `ONLINE` database on the SQL Server behind the active Marketing (or Infinity) live profile credentials. Used by the phone “جرد قديم” picker. Response: `database.list.response` with `databases: [{ name, compatibilityHint }]`.

### sql.execute

```json
{
  "databaseProfile": "Marketing",
  "catalog": "Marketing2024",
  "sql": "SELECT TOP (@limit) Id FROM dbo.BridgeTestItems",
  "parameters": {
    "limit": { "type": "int", "value": 10 }
  },
  "timeoutSeconds": 30,
  "maxRows": 1000
}
```

`databaseProfile` stays the canonical system route (`Marketing` / `InfinityRetailDB`). Optional `catalog` overrides `Initial Catalog` for same-schema Marketing backups selected on the phone.

Supported parameter types: `string`, `int`, `long`, `decimal`, `double`, `bool`, `datetime`, `guid`, `null`.

Response `messageType`: `sql.execute.response`

### query.execute

المسار المفضل لأي استعلام جديد محمي. الهاتف لا يرسل نص (SQL) ولا اسم قاعدة ولا حدود تنفيذ.

```json
{
  "queryId": "marketing.inventory.shortages",
  "parameters": {
    "storeId": { "type": "int", "value": 3 }
  }
}
```

الجسر يجلب الحزمة المشفرة من الكتالوج بعد مصادقة جهازه، ويتحقق من توقيعها، ثم يطابق `system` و`databaseProfile` مع الملف الحي قبل التنفيذ. الاستعلامات الموقعة فقط والتي تصنف قراءة صرفة مسموح بها.

Response `messageType`: `query.execute.response`

### product.photo

Loads one product image from `Inventory.Data_ProductPhotos`, resizes it on the
bridge host, and returns a JPEG as Base64.

```json
{
  "databaseProfile": "InfinityRetailDB",
  "productId": 866786,
  "maxEdgePx": 512,
  "jpegQuality": 75
}
```

Success payload fields: `productId`, `mimeType`, `encoding`, `width`, `height`,
`bytes`, `sourceBytes`, `data` (Base64 JPEG).

Response `messageType`: `product.photo.response`

### product.photo.upsert

Converts an uploaded image to GIF89a and MERGEs it into
`Inventory.Data_ProductPhotos` for Infinity only. Requires
`enableInfinityProductPhotoWrite: true` in bridge appsettings.

```json
{
  "system": "infinity",
  "databaseProfile": "InfinityRetailDB",
  "productId": 866786,
  "imageBase64": "<base64>",
  "maxEdgePx": 640
}
```

Success payload fields: `productId`, `mimeType`, `width`, `height`, `bytes`,
`sourceBytes`, `isGif89a`.

Response `messageType`: `product.photo.upsert.response`

## Response envelope

```json
{
  "protocolVersion": "1.0",
  "messageType": "sql.execute.response",
  "requestId": "REQ-...",
  "tunnelId": "LAB-TNL-001",
  "respondedAtUtc": "2026-07-20T18:00:01Z",
  "success": true,
  "payload": { },
  "error": null
}
```

On failure, `success` is `false` and `error` contains:

```json
{
  "code": "SQL_PERMISSION_DENIED",
  "message": "Human-readable message",
  "details": "Optional details",
  "retryable": false
}
```

## Limits (defaults)

| Limit | Value |
| --- | --- |
| Max SQL length | 100,000 chars |
| Default timeout | 30 s |
| Max timeout | 120 s |
| Default max rows | 1,000 |
| Max max rows | 5,000 |
| Max request age | 5 minutes |

## Examples

See `docs/examples/` for sample request/response JSON files.
