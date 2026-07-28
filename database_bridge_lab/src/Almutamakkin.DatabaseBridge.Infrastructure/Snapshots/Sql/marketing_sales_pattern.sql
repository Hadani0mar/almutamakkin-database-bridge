-- Automatic sales-pattern metrics for Marketing (invoice workflow + activity units).
-- One result row; mobile classifies locally from these numbers.

WITH RecentLineItems AS (
    SELECT
        s.S_ID,
        ISNULL(s.S_STATUES, 1) AS invoice_status,
        ISNULL(s.CUST_ID, 0) AS customer_id,
        COALESCE(si.S_TIME, s.S_DATE) AS line_time,
        CAST(ISNULL(si.QTY, 0) AS decimal(18, 4)) AS BASE_QUANTITY,
        COALESCE(NULLIF(si.UNIT_QTY, 0), 1) AS UNIT_FACTOR
    FROM dbo.SALE_INVOICE s
    INNER JOIN dbo.SALE_ITEMS si ON si.S_ID = s.S_ID
    WHERE ISNULL(s.S_STATUES, 1) <> 2
      AND ISNULL(si.QTY, 0) > 0
      AND COALESCE(si.S_TIME, s.S_DATE) >= DATEADD(day, -60, GETDATE())
),
InvoiceFacts AS (
    SELECT
        S_ID,
        invoice_status,
        customer_id,
        MIN(line_time) AS first_line_at,
        MAX(line_time) AS last_line_at,
        COUNT(*) AS line_count,
        DATEDIFF(minute, MIN(line_time), MAX(line_time)) AS duration_minutes,
        CASE WHEN customer_id IN (0, 1) THEN 1 ELSE 0 END AS is_cash_invoice,
        SUM(BASE_QUANTITY) AS invoice_base_quantity
    FROM RecentLineItems
    GROUP BY S_ID, invoice_status, customer_id
),
CashMetrics AS (
    SELECT
        COUNT(*) AS cash_invoice_count,
        ISNULL(SUM(CASE WHEN duration_minutes >= 120 THEN 1 ELSE 0 END), 0)
            AS long_cash_invoice_count,
        ISNULL(SUM(CASE WHEN line_count >= 15 THEN 1 ELSE 0 END), 0)
            AS large_cash_invoice_count,
        ISNULL(SUM(CASE
            WHEN duration_minutes <= 30 AND line_count <= 8 THEN 1 ELSE 0 END), 0)
            AS short_cash_invoice_count,
        ISNULL(AVG(CAST(duration_minutes AS DECIMAL(18, 2))), 0)
            AS average_cash_duration_minutes,
        ISNULL(AVG(CAST(line_count AS DECIMAL(18, 2))), 0)
            AS average_cash_line_count
    FROM InvoiceFacts
    WHERE is_cash_invoice = 1
),
SalesLines365 AS (
    SELECT
        CAST(ISNULL(s.QTY, 0) AS decimal(18, 4)) AS BASE_QUANTITY,
        COALESCE(NULLIF(s.UNIT_QTY, 0), 1) AS UNIT_FACTOR,
        s.S_ID
    FROM dbo.SALE_ITEMS_INVOICE_VIEW s
    WHERE s.S_STATUES = 1
      AND s.S_ITEM_INVISIBLE = 1
      AND ISNULL(s.QTY, 0) > 0
      AND s.S_DATE >= DATEADD(day, -365, GETDATE())
),
SalesMetrics AS (
    SELECT
        COUNT(*) AS sale_line_count,
        ISNULL(SUM(CASE WHEN BASE_QUANTITY <= 3 THEN 1 ELSE 0 END), 0) AS small_sale_lines,
        ISNULL(SUM(CASE WHEN BASE_QUANTITY >= 12 THEN 1 ELSE 0 END), 0) AS bulk_sale_lines,
        ISNULL(SUM(CASE WHEN UNIT_FACTOR > 1 THEN 1 ELSE 0 END), 0) AS packaged_sale_lines,
        ISNULL(AVG(BASE_QUANTITY), 0) AS average_base_quantity
    FROM SalesLines365
)
SELECT
    ISNULL(c.cash_invoice_count, 0) AS cash_invoice_count,
    CAST(100.0 * ISNULL(c.long_cash_invoice_count, 0) /
        NULLIF(c.cash_invoice_count, 0) AS DECIMAL(8, 2)) AS long_cash_invoice_percent,
    CAST(100.0 * ISNULL(c.large_cash_invoice_count, 0) /
        NULLIF(c.cash_invoice_count, 0) AS DECIMAL(8, 2)) AS large_cash_invoice_percent,
    CAST(100.0 * ISNULL(c.short_cash_invoice_count, 0) /
        NULLIF(c.cash_invoice_count, 0) AS DECIMAL(8, 2)) AS short_cash_invoice_percent,
    CAST(ISNULL(c.average_cash_duration_minutes, 0) AS DECIMAL(18, 2))
        AS average_cash_duration_minutes,
    CAST(ISNULL(c.average_cash_line_count, 0) AS DECIMAL(18, 2))
        AS average_cash_line_count,
    ISNULL(m.sale_line_count, 0) AS sale_line_count,
    ISNULL(m.small_sale_lines, 0) AS small_sale_lines,
    ISNULL(m.bulk_sale_lines, 0) AS bulk_sale_lines,
    ISNULL(m.packaged_sale_lines, 0) AS packaged_sale_lines,
    CAST(ISNULL(m.average_base_quantity, 0) AS DECIMAL(18, 4)) AS average_base_quantity
FROM CashMetrics c
CROSS JOIN SalesMetrics m;
