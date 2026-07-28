using Almutamakkin.DatabaseBridge.Infrastructure;

namespace Almutamakkin.DatabaseBridge.Tests;

public sealed class SqlValueConverterTests
{
    [Fact]
    public void ConvertValue_DateTime_ReturnsIso8601()
    {
        var value = new DateTime(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc);

        var converted = SqlValueConverter.ConvertValue(value);

        Assert.Equal("2026-07-20T12:00:00.0000000Z", converted);
    }

    [Fact]
    public void BuildUniqueColumnNames_Duplicates_AreSuffixed()
    {
        var names = SqlValueConverter.BuildUniqueColumnNames(["ITEM_ID", "ITEM_ID", "Name"]);

        Assert.Equal(["ITEM_ID", "ITEM_ID_2", "Name"], names);
    }
}
