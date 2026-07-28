using System.Text.Json;
using Almutamakkin.BarcodeAgent.Database;
using Almutamakkin.BarcodeAgent.Models;

namespace Almutamakkin.BarcodeAgent.Tests;

public sealed class SqlProductRepositoryContractTests
{
    [Fact]
    public void Projection_PreservesBarIdAndResolvesBarcodeUnitBeforeUnitFallback()
    {
        var sql = SqlProductRepository.Projection;
        var barcodeFactor = sql.IndexOf("NULLIF(b.UNIT_QTY, 0)", StringComparison.Ordinal);
        var unitFallback = sql.IndexOf("NULLIF(u.UNIT_QTY, 0)", StringComparison.Ordinal);

        Assert.Contains("b.BAR_ID", sql, StringComparison.Ordinal);
        Assert.Contains("b.UNIT_ID AS UNIT_ID", sql, StringComparison.Ordinal);
        Assert.Contains("LEFT JOIN UNITS u ON u.UNIT_ID = b.UNIT_ID", sql, StringComparison.Ordinal);
        Assert.Contains("'N/A'", sql, StringComparison.Ordinal);
        Assert.True(barcodeFactor >= 0, "The BARCODE unit factor must be part of the projection.");
        Assert.True(unitFallback > barcodeFactor, "UNITS.UNIT_QTY must only be used after BARCODE.UNIT_QTY.");
    }

    [Fact]
    public void ProductDto_SerializesSelectedUnitMetadataWithoutChangingBarId()
    {
        var product = new ProductDto(
            BarId: 41,
            ItemId: 7,
            Name: "Product",
            Barcode: "12345",
            Quantity: 12,
            SalePrice: 8.5m,
            UnitId: 3,
            UnitName: "Box",
            UnitQty: 10,
            LastPurchasePrice: 6,
            LastSupplier: null,
            Printable: true,
            PrintabilityReason: null);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(
            product,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var root = json.RootElement;

        Assert.Equal(41, root.GetProperty("barId").GetInt64());
        Assert.Equal(3, root.GetProperty("unitId").GetInt32());
        Assert.Equal("Box", root.GetProperty("unitName").GetString());
        Assert.Equal(10, root.GetProperty("unitQty").GetDecimal());
    }
}
