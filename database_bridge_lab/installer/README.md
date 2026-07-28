# Database Bridge Lab — Installer

## ملف النشر (Setup)

```text
C:\AlmutamakkinLabs\pharmacy_app_bridge_wt\database_bridge_lab\dist\Almutamakkin-DatabaseBridgeLab-Setup-1.0.12.exe
```

نسخة على سطح المكتب / مجلد الجسر:

```text
C:\Users\DELL\Desktop\الجسر الرقمي\Almutamakkin-DatabaseBridgeLab-Setup-1.0.12.exe
```

نسخة داخل مستودع المتمكن (للمراجعة المحلية فقط):

```text
C:\Users\DELL\Desktop\al-mutamakkin\pharmacy_app\dist\Almutamakkin-DatabaseBridgeLab-Setup-1.0.12.exe
```

الحجم التقريبي: ~49 MB — self-contained (لا يحتاج تثبيت .NET مسبقاً).

## إعادة البناء

```powershell
$env:Path = "C:\Program Files\dotnet;" + $env:Path
cd C:\AlmutamakkinLabs\pharmacy_app_bridge_wt\database_bridge_lab

dotnet publish src\Almutamakkin.DatabaseBridge.App\Almutamakkin.DatabaseBridge.App.csproj `
  -c Release -r win-x64 --self-contained true `
  -o .\publish\win-x64 --nologo

& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" .\installer\DatabaseBridgeLab.iss
```

## ملاحظات

- سكربت Inno: `installer\DatabaseBridgeLab.iss`
- مجلد الإعدادات بعد التثبيت: `%LocalAppData%\Almutamakkin\DatabaseBridgeLab`
- المسار الافتراضي للتثبيت: `C:\Program Files\Almutamakkin\DatabaseBridgeLab`
