using Microsoft.Win32;

namespace Almutamakkin.BarcodeBridge.Windows;

public static class StartupManager
{
    private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Almutamakkin Barcode Bridge";

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegistryPath, writable: true)
            ?? throw new InvalidOperationException("تعذر فتح إعدادات بدء التشغيل في ويندوز.");
        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        var executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("تعذر تحديد مسار البرنامج.");
        key.SetValue(ValueName, $"\"{executable}\" --startup", RegistryValueKind.String);
    }

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: false);
        return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
    }
}
