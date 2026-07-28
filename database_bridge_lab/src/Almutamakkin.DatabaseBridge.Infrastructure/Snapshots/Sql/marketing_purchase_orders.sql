WITH SalesDaily AS (
    SELECT
        si.ITEM_ID,
        DATEADD(day, DATEDIFF(day, 0, s.S_DATE), 0) AS sale_day,
        SUM(ISNULL(si.QTY, 0) * ISNULL(NULLIF(si.UNIT_QTY, 0), 1)) AS sold_qty,
        SUM(ISNULL(si.PRICE, 0) * ISNULL(si.QTY, 0)) AS sale_value,
        SUM(ISNULL(si.QTY, 0)) AS sale_units
    FROM dbo.SALE_INVOICE s
    INNER JOIN dbo.SALE_ITEMS si ON si.S_ID = s.S_ID
    WHERE s.S_STATUES IN (1, 10)
      AND s.S_DATE >= DATEADD(day, -180, DATEADD(day, DATEDIFF(day, 0, GETDATE()), 0))
      AND s.S_DATE < DATEADD(day, 1, DATEADD(day, DATEDIFF(day, 0, GETDATE()), 0))
    GROUP BY si.ITEM_ID, DATEADD(day, DATEDIFF(day, 0, s.S_DATE), 0)
),
SalesAgg AS (
    SELECT
        ITEM_ID,
        SUM(CASE WHEN sale_day >= DATEADD(day, -7, DATEADD(day, DATEDIFF(day, 0, GETDATE()), 0)) THEN sold_qty ELSE 0 END) AS sales_7,
        SUM(CASE WHEN sale_day >= DATEADD(day, -14, DATEADD(day, DATEDIFF(day, 0, GETDATE()), 0))
                  AND sale_day < DATEADD(day, -7, DATEADD(day, DATEDIFF(day, 0, GETDATE()), 0)) THEN sold_qty ELSE 0 END) AS sales_8_14,
        SUM(CASE WHEN sale_day >= DATEADD(day, -30, DATEADD(day, DATEDIFF(day, 0, GETDATE()), 0))
                  AND sale_day < DATEADD(day, -14, DATEADD(day, DATEDIFF(day, 0, GETDATE()), 0)) THEN sold_qty ELSE 0 END) AS sales_15_30,
        SUM(CASE WHEN sale_day >= DATEADD(day, -60, DATEADD(day, DATEDIFF(day, 0, GETDATE()), 0))
                  AND sale_day < DATEADD(day, -30, DATEADD(day, DATEDIFF(day, 0, GETDATE()), 0)) THEN sold_qty ELSE 0 END) AS sales_31_60,
        SUM(CASE WHEN sale_day >= DATEADD(day, -90, DATEADD(day, DATEDIFF(day, 0, GETDATE()), 0))
                  AND sale_day < DATEADD(day, -60, DATEADD(day, DATEDIFF(day, 0, GETDATE()), 0)) THEN sold_qty ELSE 0 END) AS sales_61_90,
        SUM(CASE WHEN sale_day >= DATEADD(day, -180, DATEADD(day, DATEDIFF(day, 0, GETDATE()), 0))
                  AND sale_day < DATEADD(day, -90, DATEADD(day, DATEDIFF(day, 0, GETDATE()), 0)) THEN sold_qty ELSE 0 END) AS sales_91_180,
        SUM(CASE WHEN sale_day >= DATEADD(day, -30, DATEADD(day, DATEDIFF(day, 0, GETDATE()), 0)) THEN sold_qty ELSE 0 END) AS sales_30,
        SUM(sold_qty) AS sales_180,
        SUM(CASE WHEN sale_day >= DATEADD(day, -30, DATEADD(day, DATEDIFF(day, 0, GETDATE()), 0)) THEN 1 ELSE 0 END) AS selling_days_30,
        COUNT(*) AS selling_days_180,
        AVG(CASE WHEN sale_units > 0 THEN sale_value / sale_units ELSE NULL END) AS avg_sale_price,
        STDEV(sold_qty) AS sold_qty_std,
        AVG(sold_qty) AS sold_qty_avg
    FROM SalesDaily
    GROUP BY ITEM_ID
),
Returns180 AS (
    SELECT
        rsi.ITEM_ID,
        SUM(ISNULL(rsi.QTY, 0) * ISNULL(NULLIF(rsi.UNIT_QTY, 0), 1)) AS return_qty_180
    FROM dbo.R_S_INVOICE r
    INNER JOIN dbo.R_S_ITEMS rsi ON rsi.S_R_ID = r.S_R_ID
    WHERE r.S_R_STATUES IN (1, 10)
      AND r.S_R_DATE >= DATEADD(day, -180, DATEADD(day, DATEDIFF(day, 0, GETDATE()), 0))
      AND r.S_R_DATE < DATEADD(day, 1, DATEADD(day, DATEDIFF(day, 0, GETDATE()), 0))
    GROUP BY rsi.ITEM_ID
),
Spoil180 AS (
    SELECT
        spi.ITEM_ID,
        SUM(ISNULL(spi.QTY, 0) * ISNULL(NULLIF(spi.UNIT_QTY, 0), 1)) AS spoil_qty_180
    FROM dbo.SPOIL_INVOICE sp
    INNER JOIN dbo.SPOIL_ITEMS spi ON spi.SP_ID = sp.SP_ID
    WHERE sp.SP_STATUES IN (1, 10)
      AND sp.SP_DATE >= DATEADD(day, -180, DATEADD(day, DATEDIFF(day, 0, GETDATE()), 0))
      AND sp.SP_DATE < DATEADD(day, 1, DATEADD(day, DATEDIFF(day, 0, GETDATE()), 0))
    GROUP BY spi.ITEM_ID
),
StockAgg AS (
    SELECT
        ITEM_ID,
        SUM(CASE WHEN QTY > 0 THEN QTY ELSE 0 END) AS total_stock,
        SUM(CASE WHEN QTY > 0
                  AND ISNULL(OFS, 0) = 0
                  AND (CATEOGRY3 IS NULL OR CATEOGRY3 >= DATEADD(day, DATEDIFF(day, 0, GETDATE()), 0))
                 THEN QTY ELSE 0 END) AS sellable_stock,
        SUM(CASE WHEN QTY > 0
                  AND CATEOGRY3 IS NOT NULL
                  AND CATEOGRY3 < DATEADD(day, DATEDIFF(day, 0, GETDATE()), 0)
                 THEN QTY ELSE 0 END) AS expired_stock,
        SUM(CASE WHEN QTY > 0
                  AND CATEOGRY3 IS NOT NULL
                  AND CATEOGRY3 >= DATEADD(day, DATEDIFF(day, 0, GETDATE()), 0)
                  AND CATEOGRY3 < DATEADD(day, 90, DATEADD(day, DATEDIFF(day, 0, GETDATE()), 0))
                 THEN QTY ELSE 0 END) AS expiring_90_stock
    FROM dbo.ITEMS_SUB
    GROUP BY ITEM_ID
),
LatestPurchaseRanked AS (
    SELECT
        b.ITEM_ID,
        b.CUST_ID AS supplier_id,
        ISNULL(NULLIF(LTRIM(RTRIM(c.CUST_NAME)), ''), b.CUST_NAME) AS supplier_name,
        c.CUST_NO AS supplier_code,
        c.CUST_PHONE AS supplier_phone,
        c.CUST_MOBILE AS supplier_mobile,
        c.CUST_ADRESS AS supplier_address,
        c.CUST_E_MAIL AS supplier_email,
        b.UNIT_DISC AS purchase_unit,
        ISNULL(NULLIF(b.UNIT_QTY, 0), 1) AS pack_size,
        CASE WHEN ISNULL(NULLIF(b.UNIT_QTY, 0), 1) > 0
             THEN (ISNULL(b.PRICE, 0) * ISNULL(NULLIF(b.RATE, 0), 1)) / ISNULL(NULLIF(b.UNIT_QTY, 0), 1)
             ELSE 0 END AS unit_cost,
        ROW_NUMBER() OVER (PARTITION BY b.ITEM_ID ORDER BY b.B_DATE DESC, b.B_ITEM_ID DESC) AS rn
    FROM dbo.BUY_ITEMS_INVOICE_VIEW b
    LEFT JOIN dbo.CUSTOMERS c ON c.CUST_ID = b.CUST_ID
    WHERE b.B_STATUES IN (1, 10)
),
LatestPurchase AS (
    SELECT
        ITEM_ID,
        supplier_id,
        supplier_name,
        supplier_code,
        supplier_phone,
        supplier_mobile,
        supplier_address,
        supplier_email,
        purchase_unit,
        pack_size,
        unit_cost
    FROM LatestPurchaseRanked
    WHERE rn = 1
),
Base AS (
    SELECT
        i.ITEM_ID,
        i.ITEM_NAME,
        lp.supplier_id,
        lp.supplier_name,
        lp.supplier_code,
        lp.supplier_phone,
        lp.supplier_mobile,
        lp.supplier_address,
        lp.supplier_email,
        lp.purchase_unit,
        lp.pack_size,
        lp.unit_cost,
        ISNULL(sa.avg_sale_price, 0) AS avg_sale_price,
        ISNULL(sa.sales_30, 0) AS sales_30,
        ISNULL(sa.sales_180, 0) AS sales_180,
        ISNULL(sa.selling_days_30, 0) AS selling_days_30,
        ISNULL(sa.selling_days_180, 0) AS selling_days_180,
        ISNULL(sa.sold_qty_std, 0) AS sold_qty_std,
        ISNULL(sa.sold_qty_avg, 0) AS sold_qty_avg,
        ISNULL(st.total_stock, 0) AS total_stock,
        ISNULL(st.sellable_stock, 0) AS sellable_stock,
        ISNULL(st.expired_stock, 0) AS expired_stock,
        ISNULL(st.expiring_90_stock, 0) AS expiring_90_stock,
        ISNULL(r.return_qty_180, 0) AS return_qty_180,
        ISNULL(sp.spoil_qty_180, 0) AS spoil_qty_180,
        (ISNULL(sa.sales_7, 0) / 7.0) * 0.30
        + (ISNULL(sa.sales_8_14, 0) / 7.0) * 0.20
        + (ISNULL(sa.sales_15_30, 0) / 16.0) * 0.20
        + (ISNULL(sa.sales_31_60, 0) / 30.0) * 0.15
        + (ISNULL(sa.sales_61_90, 0) / 30.0) * 0.10
        + (ISNULL(sa.sales_91_180, 0) / 90.0) * 0.05 AS forecast_daily,
        ISNULL(sa.sales_31_60, 0) AS sales_31_60
    FROM dbo.ITEMS i
    INNER JOIN SalesAgg sa ON sa.ITEM_ID = i.ITEM_ID
    LEFT JOIN StockAgg st ON st.ITEM_ID = i.ITEM_ID
    LEFT JOIN LatestPurchase lp ON lp.ITEM_ID = i.ITEM_ID
    LEFT JOIN Returns180 r ON r.ITEM_ID = i.ITEM_ID
    LEFT JOIN Spoil180 sp ON sp.ITEM_ID = i.ITEM_ID
    WHERE i.ITEM_INVISIBLE = 0
),
Metrics AS (
    SELECT
        *,
        CASE WHEN sales_31_60 > 0 THEN sales_30 / sales_31_60
             WHEN sales_30 > 0 THEN 2.0 ELSE 0 END AS trend_ratio,
        CASE WHEN selling_days_180 > 0 THEN 180.0 / selling_days_180 ELSE 999.0 END AS adi,
        CASE WHEN sold_qty_avg > 0 THEN sold_qty_std / sold_qty_avg ELSE 99.0 END AS cv,
        CASE WHEN unit_cost > 0 THEN ((avg_sale_price - unit_cost) / unit_cost) * 100.0 ELSE -999.0 END AS margin_pct,
        1.65 * sold_qty_std * SQRT(7.0) AS safety_stock
    FROM Base
),
OrderCalc AS (
    SELECT
        *,
        forecast_daily * 7.0 + safety_stock AS reorder_point,
        forecast_daily * 21.0 + safety_stock AS target_stock,
        CASE WHEN forecast_daily > 0 THEN sellable_stock / forecast_daily ELSE 999.0 END AS coverage_days
    FROM Metrics
),
Scored AS (
    SELECT
        *,
        CASE WHEN target_stock > sellable_stock THEN CEILING(target_stock - sellable_stock) ELSE 0 END AS suggested_qty,
        CAST(
            (CASE WHEN sales_30 >= 60 THEN 20 ELSE sales_30 / 3.0 END)
          + (CASE WHEN selling_days_30 >= 20 THEN 15 ELSE selling_days_30 * 0.75 END)
          + (CASE WHEN trend_ratio >= 1.25 THEN 10 WHEN trend_ratio >= 1.0 THEN 7 WHEN trend_ratio >= 0.75 THEN 4 ELSE 0 END)
          + (CASE WHEN cv <= 1.0 THEN 10 WHEN cv <= 1.5 THEN 8 WHEN cv <= 2.0 THEN 5 ELSE 2 END)
          + (CASE WHEN coverage_days <= 0 THEN 20 WHEN coverage_days <= 7 THEN 18 WHEN coverage_days <= 14 THEN 14 WHEN coverage_days <= 21 THEN 8 ELSE 0 END)
          + (CASE WHEN margin_pct >= 30 THEN 10 WHEN margin_pct >= 20 THEN 8 WHEN margin_pct >= 10 THEN 5 WHEN margin_pct >= 0 THEN 2 ELSE -20 END)
          + (CASE WHEN sales_180 >= 180 THEN 10 WHEN sales_180 >= 90 THEN 8 WHEN sales_180 >= 45 THEN 6 WHEN sales_180 >= 20 THEN 4 ELSE 2 END)
          + 3
          - (CASE WHEN sales_180 > 0 AND return_qty_180 / sales_180 >= 0.10 THEN 10 ELSE 0 END)
          - (CASE WHEN sales_180 > 0 AND spoil_qty_180 / sales_180 >= 0.10 THEN 15 ELSE 0 END)
          - (CASE WHEN expiring_90_stock > 0 THEN 10 ELSE 0 END)
        AS int) AS priority_score
    FROM OrderCalc
)
SELECT TOP 200
    ITEM_ID AS item_id,
    ITEM_NAME AS item_name,
    supplier_id,
    supplier_name,
    supplier_code,
    supplier_phone,
    supplier_mobile,
    supplier_address,
    supplier_email,
    purchase_unit,
    pack_size,
    unit_cost,
    avg_sale_price,
    margin_pct,
    sales_30,
    sales_180,
    selling_days_30,
    selling_days_180,
    forecast_daily,
    trend_ratio,
    adi,
    cv,
    total_stock,
    sellable_stock,
    expired_stock,
    expiring_90_stock,
    coverage_days,
    safety_stock,
    reorder_point,
    target_stock,
    suggested_qty,
    CEILING(suggested_qty / ISNULL(NULLIF(pack_size, 0), 1)) AS suggested_packs,
    CEILING(suggested_qty / ISNULL(NULLIF(pack_size, 0), 1))
        * ISNULL(NULLIF(pack_size, 0), 1) * unit_cost AS estimated_cost,
    priority_score,
    CASE WHEN adi <= 1.32 AND cv * cv <= 0.49 THEN 'Regular'
         WHEN adi > 1.32 THEN 'Intermittent'
         ELSE 'Erratic' END AS demand_pattern,
    CASE WHEN selling_days_180 >= 20 AND cv <= 2.5 THEN 'High'
         WHEN selling_days_180 >= 8 THEN 'Medium'
         ELSE 'Low' END AS confidence,
    CASE WHEN priority_score >= 80 THEN 'Confirmed'
         WHEN priority_score >= 65 THEN 'Recommended'
         WHEN priority_score >= 50 THEN 'Review'
         ELSE 'Excluded' END AS decision
FROM Scored
WHERE supplier_id IS NOT NULL
  AND unit_cost > 0
  AND pack_size > 0
  AND forecast_daily > 0
  AND sellable_stock < reorder_point
  AND suggested_qty > 0
  AND margin_pct >= 0
  AND priority_score >= 50
ORDER BY supplier_name, priority_score DESC, ITEM_NAME;
