using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.Core;

/// <summary>
/// Phase 0/1 change-stream foundation: the small, fixed set of domains the
/// bridge currently watches. Shared between the changes.probe/changes.pull
/// handlers (Core) and <c>DomainWatchService</c> (Infrastructure) so both
/// agree on which (system, domain) pairs exist and which AppSettings flag
/// gates each one, without a circular project reference.
/// </summary>
public sealed record ChangeDomainDescriptor(
    string System,
    string Domain,
    string DisplayName,
    Func<AppSettings, bool> IsEnabled);

public static class ChangeDomainCatalog
{
    public static readonly IReadOnlyList<ChangeDomainDescriptor> Domains =
    [
        new(
            "marketing",
            "debt_invoice_events",
            "أبوغريس · أحداث الديون (دلتا)",
            settings => settings.EnableDebtInvoiceChangeStream),
        new(
            "marketing",
            "shift_close_events",
            "أبوغريس · إغلاق الورديات (دلتا)",
            settings => settings.EnableShiftCloseChangeStream),
        new(
            "infinity",
            "purchase_invoice_events",
            "إنفينيتي · فواتير الشراء (دلتا)",
            settings => settings.EnableInfinityPurchaseInvoiceChangeStream),
        new(
            "infinity",
            "sales_invoice_events",
            "إنفينيتي · فواتير المبيعات (دلتا)",
            settings => settings.EnableInfinitySalesInvoiceChangeStream),
        new(
            "infinity",
            "expiry",
            "إنفينيتي · الصلاحية (دلتا)",
            settings => settings.EnableInfinityExpiryChangeStream),
    ];

    public static ChangeDomainDescriptor? Find(string system, string domain) =>
        Domains.FirstOrDefault(descriptor =>
            string.Equals(descriptor.System, system, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(descriptor.Domain, domain, StringComparison.OrdinalIgnoreCase));
}
