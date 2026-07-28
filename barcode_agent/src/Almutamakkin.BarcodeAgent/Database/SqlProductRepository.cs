using System.Data;
using Almutamakkin.BarcodeAgent.Configuration;
using Almutamakkin.BarcodeAgent.Models;
using System.Data.SqlClient;
using Almutamakkin.BarcodeAgent.Printing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Almutamakkin.BarcodeAgent.Database;

public sealed class SqlProductRepository(
    IOptions<DatabaseOptions> options,
    IMemoryCache cache,
    IBarcodePrintability printability) : IProductRepository
{
    private readonly DatabaseOptions _options = options.Value;
    internal const string Projection = """
SELECT TOP (@limit)
    b.BAR_ID,
    i.ITEM_ID,
    LTRIM(RTRIM(i.ITEM_NAME)) AS ITEM_NAME,
    LTRIM(RTRIM(b.BARCODE)) AS BARCODE,
    ISNULL((SELECT SUM(ISNULL(s.QTY, 0)) FROM ITEMS_SUB s WHERE s.ITEM_ID = i.ITEM_ID), 0) AS QTY,
    ISNULL(b.PRICE1, 0) AS SALE_PRICE,
    b.UNIT_ID AS UNIT_ID,
    ISNULL(NULLIF(LTRIM(RTRIM(u.UNIT_DISC)), ''), 'N/A') AS UNIT_NAME,
    COALESCE(NULLIF(b.UNIT_QTY, 0), NULLIF(u.UNIT_QTY, 0), 1) AS UNIT_QTY,
    ISNULL(lastbuy.PRICE, 0) AS LAST_PURCHASE_PRICE,
    lastbuy.CUST_NAME AS LAST_SUPPLIER
FROM BARCODE b
INNER JOIN ITEMS i ON i.ITEM_ID = b.ITEM_ID
LEFT JOIN UNITS u ON u.UNIT_ID = b.UNIT_ID
OUTER APPLY (
    SELECT TOP 1 bi.PRICE, c.CUST_NAME
    FROM BUY_ITEMS bi
    INNER JOIN BUY_INVOICE inv ON inv.B_ID = bi.B_ID
    LEFT JOIN CUSTOMERS c ON c.CUST_ID = inv.CUST_ID
    WHERE bi.ITEM_ID = i.ITEM_ID AND bi.PRICE > 0 AND ISNULL(inv.B_STATUES, 1) NOT IN (0, 2)
    ORDER BY inv.B_DATE DESC, inv.B_ID DESC
) lastbuy
""";

    public async Task<IReadOnlyList<ProductDto>> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        const string sql = Projection + "\n" + """
WHERE ISNULL(i.ITEM_INVISIBLE, 0) = 0
  AND LEN(LTRIM(RTRIM(ISNULL(b.BARCODE, '')))) > 0
  AND (i.ITEM_NAME LIKE @pattern OR b.BARCODE LIKE @pattern)
ORDER BY CASE
    WHEN b.BARCODE = @query THEN 0
    WHEN i.ITEM_NAME = @query THEN 1
    WHEN i.ITEM_NAME LIKE @prefix THEN 2
    ELSE 3 END,
    i.ITEM_NAME ASC, b.BAR_ID ASC;
""";
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.Add("@limit", SqlDbType.Int).Value = Math.Clamp(limit, 1, 20);
        command.Parameters.Add("@query", SqlDbType.NVarChar, 200).Value = query;
        command.Parameters.Add("@prefix", SqlDbType.NVarChar, 201).Value = query + "%";
        command.Parameters.Add("@pattern", SqlDbType.NVarChar, 202).Value = "%" + query + "%";
        return await ReadProductsAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<ProductDto>> GetByBarcodeAsync(string barcode, CancellationToken cancellationToken)
    {
        const string sql = Projection + "\n" + """
WHERE ISNULL(i.ITEM_INVISIBLE, 0) = 0
  AND LTRIM(RTRIM(b.BARCODE)) = @barcode
ORDER BY i.ITEM_NAME ASC, b.BAR_ID ASC;
""";
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.Add("@limit", SqlDbType.Int).Value = 20;
        command.Parameters.Add("@barcode", SqlDbType.VarChar, 128).Value = barcode;
        return await ReadProductsAsync(command, cancellationToken);
    }

    public async Task<ProductDto?> GetByBarIdAsync(long barId, CancellationToken cancellationToken)
    {
        const string sql = Projection + "\n" + """
WHERE ISNULL(i.ITEM_INVISIBLE, 0) = 0
  AND b.BAR_ID = @barId
  AND LEN(LTRIM(RTRIM(ISNULL(b.BARCODE, '')))) > 0;
""";
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.Add("@limit", SqlDbType.Int).Value = 1;
        command.Parameters.Add("@barId", SqlDbType.Int).Value = checked((int)barId);
        return (await ReadProductsAsync(command, cancellationToken)).SingleOrDefault();
    }

    public Task<string?> GetBusinessNameAsync(CancellationToken cancellationToken) =>
        cache.GetOrCreateAsync("business-name", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = CreateCommand(connection, """
SELECT TOP 1 LTRIM(RTRIM(A_NAME))
FROM SITTEINGS
WHERE A_NAME IS NOT NULL AND LEN(LTRIM(RTRIM(A_NAME))) > 0;
""");
            return (await command.ExecuteScalarAsync(cancellationToken))?.ToString();
        });

    public async Task<bool> CanConnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = CreateCommand(connection, "SELECT 1");
            return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private SqlCommand CreateCommand(SqlConnection connection, string sql) =>
        new(sql, connection) { CommandTimeout = _options.CommandTimeoutSeconds };

    private async Task<IReadOnlyList<ProductDto>> ReadProductsAsync(
        SqlCommand command,
        CancellationToken cancellationToken)
    {
        var products = new List<ProductDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var barcode = reader["BARCODE"]?.ToString() ?? string.Empty;
            var printable = printability.Analyze(barcode);
            products.Add(new ProductDto(
                Convert.ToInt64(reader["BAR_ID"]),
                Convert.ToInt64(reader["ITEM_ID"]),
                reader["ITEM_NAME"]?.ToString() ?? string.Empty,
                barcode,
                Convert.ToDecimal(reader["QTY"]),
                Convert.ToDecimal(reader["SALE_PRICE"]),
                reader["UNIT_ID"] is DBNull ? null : Convert.ToInt32(reader["UNIT_ID"]),
                reader["UNIT_NAME"]?.ToString() ?? "N/A",
                Convert.ToDecimal(reader["UNIT_QTY"]),
                Convert.ToDecimal(reader["LAST_PURCHASE_PRICE"]),
                reader["LAST_SUPPLIER"] is DBNull ? null : reader["LAST_SUPPLIER"]?.ToString(),
                printable.Printable,
                printable.Reason));
        }
        return products;
    }
}
