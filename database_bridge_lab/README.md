# Almutamakkin Database Bridge Lab

مختبر مستقل لتجربة **Database Bridge** — تطبيق WinForms يربط SQL Server محليًا بطلبات JSON (Local Test أو WebSocket أو نفق Supabase).

The lab lives in the bridge worktree at:

```text
C:\AlmutamakkinLabs\pharmacy_app_bridge_wt\database_bridge_lab
```

## Run — التشغيل

Requirements: .NET 8 SDK, Windows, SQL Server (optional for DB tests).

```powershell
$env:Path = "C:\Program Files\dotnet;" + $env:Path
cd C:\AlmutamakkinLabs\pharmacy_app_bridge_wt\database_bridge_lab
dotnet build Almutamakkin.DatabaseBridgeLab.sln -c Release
dotnet run --project src/Almutamakkin.DatabaseBridge.App -c Release
```

Or run the executable:

```text
src/Almutamakkin.DatabaseBridge.App/bin/Release/net8.0-windows/Almutamakkin.DatabaseBridgeLab.exe
```

## Isolation — العزل

- Does **not** modify `pharmacy_app/barcode_agent` or the production bridge.
- Uses its own data folder: `%LocalAppData%\Almutamakkin\DatabaseBridgeLab`
- Default tunnel ID: `LAB-TNL-001` (not production; Supabase registration assigns a live ID)
- Create the lab database manually with `scripts/create_lab_database.sql` — the app does **not** run it automatically.

## Live connection routes — مسارات الاتصال الحي

- **اتصال محلي تلقائي** يكتشف نسخ SQL Server المحلية ويصادقها بحساب ويندوز الحالي، ثم يحفظ ملفاً محلياً.
- **اتصال شبكي** يطلب عنوان الخادم وبيانات مصادقة SQL، ثم يحفظ ملفاً شبكياً مستقلاً.
- ملف واحد فقط يكون نشطاً لطلبات الهاتف الحية. طلب (Marketing) لا ينفذ إلا على ملف (Marketing) النشط، وطلب (InfinityRetailDB) لا ينفذ إلا على ملف إنفينيتي النشط.
- لا يوجد سقوط تلقائي من المسار المحلي إلى المسار الشبكي، ولا إلى ملف لقطات أو ملف نظام آخر.
- تعرض الواجهة النظام النشط ونوع اتصاله فقط، وتسجل ملخص الاستعلامات الحية دون إظهار نص SQL أو الأسرار.

## Supabase tunnel — نفق Supabase

Use when the mobile app sends commands through Supabase Edge Functions (no inbound port on Windows):

1. Open the lab → click **تسجيل جهاز الجسر** → copy the **pairing code** into the Flutter app.
2. Select transport **Supabase Tunnel** → **Start Bridge**.
3. Status shows **Tunnel ID** and **Last Poll** activity.

Edge API base (default): `https://mapfattjpsuizvlklddl.supabase.co/functions/v1`

Functions: `bridge-register`, `bridge-poll`, `bridge-respond`. Device secret is stored encrypted (DPAPI) in local settings.

## Updates — التحديثات

عند فتح الجسر يفحص أحدث إصدار منشور في (`GitHub Releases`) لمستودع المتمكن. إذا كان رقم الإصدار المنشور أحدث من النسخة المثبتة، يظهر إشعار ويندوز ويمكن الضغط عليه لفتح صفحة التنزيل. الفحص لا ينزّل ولا يثبت شيئاً تلقائياً، وأي عطل إنترنت لا يؤثر في تشغيل الجسر.

## Operational Defaults — الإعدادات الافتراضية

مراقبة الدلتا تستخرج النظام من ملف الاتصال النشط فقط. عند اختيار (`Marketing`) لا ينفذ الجسر أي فحص أو اتصال لـ (`InfinityRetailDB`)، وعند اختيار إنفينيتي لا يفحص (`Marketing`). هذا يمنع مهلات الاتصال للنظام غير المستخدم.

الإصدار الحالي يفعّل تسجيل الاستعلامات، مراقبة التغييرات الخاصة بالنظامين، وكتابة صور المنتجات افتراضياً. تبقى الكتابة محصورة في مسار الصور الذي يتحقق من النظام وملف قاعدة البيانات؛ لا توسع هذه الإعدادات صلاحيات أوامر (`SQL`) العامة.

تقبل دالة (`bridge-relay`) أوامر حركة المنتج المسماة (`marketing.product_movement`) و(`infinity.product_movement`). ينفذ كل أمر على ملف النظام المطابق فقط، ولا يُسمح بالسقوط إلى ملف قاعدة بيانات نشط مختلف.

## Projects

| Project | Purpose |
| --- | --- |
| `Almutamakkin.DatabaseBridge.App` | WinForms UI |
| `Almutamakkin.DatabaseBridge.Core` | Validation, permissions, dispatch |
| `Almutamakkin.DatabaseBridge.Infrastructure` | SQL, DPAPI, transports, file stores |
| `Almutamakkin.DatabaseBridge.Protocol` | JSON message models |
| `Almutamakkin.DatabaseBridge.Tests` | Unit tests |

See `docs/` for architecture, protocol, and testing notes.
