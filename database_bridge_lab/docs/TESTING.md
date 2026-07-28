# Testing — الاختبار

## Unit tests (no SQL Server required)

```powershell
cd C:\AlmutamakkinLabs\DatabaseBridgeLab
dotnet test -c Release
```

Coverage includes:

- `QueryClassifierTests` — SELECT, WITH, INSERT, CREATE, BACKUP, etc.
- `PermissionPolicyTests` — ReadOnly / ReadWrite / FullAccess (including #temp analysis batches)
- `RequestValidatorTests` — empty SQL, bad protocol, expired request, tunnel mismatch, bad param type
- `CommandDispatcherDuplicateTests` — duplicate `requestId` returns cached response
- `SqlValueConverterTests` — JSON value conversion helpers

## Manual UI testing

1. Run the WinForms app.
2. Open **Database Profiles** and create profile `BridgeLab` pointing to `AlmutamakkinBridgeLab`.
3. Run `scripts/create_lab_database.sql` on your SQL instance first.
4. **Test Connection** from profiles form.
5. **Start Bridge** (Local Test mode).
6. Open **Test Console** and run sample commands from the dropdown.

## Sample scenarios

| Sample | Expected |
| --- | --- |
| Health Check | `success: true`, status online |
| Database Test | connection metadata or connection error |
| SELECT TOP 10 | rows from `BridgeTestItems` |
| Parameterized SELECT | filtered rows |
| UPDATE Test | succeeds only if profile is ReadWrite+ |
| Invalid Permission | `SQL_PERMISSION_DENIED` on ReadOnly profile |
| Timeout Test | timeout within configured seconds |
| Large Result Test | `wasTruncated: true` when maxRows exceeded |

## Integration tests (future)

`Almutamakkin.DatabaseBridge.IntegrationTests` is reserved for live SQL Server scenarios. Not required for first lab milestone.

## Lab database setup

Execute manually:

```powershell
sqlcmd -S . -i scripts/create_lab_database.sql
```

The application never auto-creates production or customer databases.
