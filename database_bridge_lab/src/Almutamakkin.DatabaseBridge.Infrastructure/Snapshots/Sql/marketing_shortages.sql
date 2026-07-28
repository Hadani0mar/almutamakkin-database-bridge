WITH QueryClock AS
(
    SELECT GETDATE() AS AS_OF_DATE
),
DateBounds AS
(
    SELECT
        AS_OF_DATE,
        DATEADD(day, -30, AS_OF_DATE) AS START_ANALYSIS,
        DATEADD(day, -7, AS_OF_DATE) AS START_ACCELERATION,
        DATEADD(day, -365, AS_OF_DATE) AS START_PURCHASE_UNITS
    FROM QueryClock
),
ItemsBase AS
(
    SELECT
        ITEM_ID,
        MAX(ITEM_NAME) AS ITEM_NAME,
        MAX(CAT1_NAME) AS CAT1_NAME,
        CAST(MAX(ISNULL(MIN_LEVEL, 0)) AS decimal(18, 4)) AS MIN_LEVEL,
        CAST(MAX(ISNULL(MAX_LEVEL, 0)) AS decimal(18, 4)) AS MAX_LEVEL
    FROM dbo.ITEMS_VIEW
    WHERE ISNULL(ITEM_INVISIBLE, 0) = 0
      AND ITEM_ID IS NOT NULL
    GROUP BY ITEM_ID
),
BaseUnitRows AS
(
    SELECT
        b.ITEM_ID,
        CASE
            WHEN LTRIM(RTRIM(ISNULL(u.UNIT_DISC, ''))) = ''
              OR UPPER(LTRIM(RTRIM(ISNULL(u.UNIT_DISC, '')))) = 'N/A'
            THEN N'وحدة أساسية'
            ELSE LTRIM(RTRIM(u.UNIT_DISC))
        END AS BASE_UNIT_LABEL
    FROM dbo.BARCODE b
    LEFT JOIN dbo.UNITS u
        ON u.UNIT_ID = b.UNIT_ID
    WHERE b.ITEM_ID IS NOT NULL
      AND COALESCE(NULLIF(b.UNIT_QTY, 0), NULLIF(u.UNIT_QTY, 0), 1) = 1
),
ItemBaseUnits AS
(
    SELECT
        ITEM_ID,
        CASE
            WHEN COUNT(DISTINCT BASE_UNIT_LABEL) = 1
            THEN MAX(BASE_UNIT_LABEL)
            ELSE N'وحدة أساسية'
        END AS BASE_UNIT_LABEL
    FROM BaseUnitRows
    GROUP BY ITEM_ID
),
PurchaseUnitCandidates AS
(
    SELECT
        p.ITEM_ID,
        LTRIM(RTRIM(p.UNIT_DISC)) AS PURCHASE_UNIT_LABEL,
        CAST(p.UNIT_QTY AS decimal(18, 4)) AS PURCHASE_UNIT_FACTOR,
        ROW_NUMBER() OVER
        (
            PARTITION BY p.ITEM_ID
            ORDER BY p.B_DATE DESC, p.B_ITEM_ID DESC
        ) AS UNIT_RANK
    FROM dbo.BUY_ITEMS_INVOICE_VIEW p
    CROSS JOIN DateBounds d
    WHERE p.B_STATUES = 1
      AND p.B_DATE >= d.START_PURCHASE_UNITS
      AND p.B_DATE <= d.AS_OF_DATE
      AND ISNULL(p.QTY, 0) > 0
      AND ISNULL(p.UNIT_QTY, 0) > 1
      AND LTRIM(RTRIM(ISNULL(p.UNIT_DISC, ''))) <> ''
      AND UPPER(LTRIM(RTRIM(ISNULL(p.UNIT_DISC, '')))) <> 'N/A'
),
PurchaseUnits AS
(
    SELECT
        ITEM_ID,
        PURCHASE_UNIT_LABEL,
        PURCHASE_UNIT_FACTOR
    FROM PurchaseUnitCandidates
    WHERE UNIT_RANK = 1
),
LastPurchaseCandidates AS
(
    SELECT
        p.ITEM_ID,
        LTRIM(RTRIM(p.CUST_NAME)) AS SUPPLIER_NAME,
        CAST(p.PRICE AS decimal(18, 4)) AS LAST_PURCHASE_PRICE,
        ROW_NUMBER() OVER
        (
            PARTITION BY p.ITEM_ID
            ORDER BY p.B_DATE DESC, p.B_ITEM_ID DESC
        ) AS PURCHASE_RANK
    FROM dbo.BUY_ITEMS_INVOICE_VIEW p
    CROSS JOIN DateBounds d
    WHERE p.B_STATUES = 1
      AND p.B_DATE >= d.START_PURCHASE_UNITS
      AND p.B_DATE <= d.AS_OF_DATE
      AND ISNULL(p.QTY, 0) > 0
),
LastPurchases AS
(
    SELECT
        ITEM_ID,
        SUPPLIER_NAME,
        LAST_PURCHASE_PRICE
    FROM LastPurchaseCandidates
    WHERE PURCHASE_RANK = 1
),
Sales30 AS
(
    SELECT
        si.ITEM_ID,
        CAST(
            SUM(CAST(ISNULL(si.QTY, 0) AS decimal(18, 4)))
            AS decimal(18, 4)
        ) AS SOLD_30,
        CAST(
            SUM(
                CASE
                    WHEN si.S_TIME >= d.START_ACCELERATION
                    THEN CAST(ISNULL(si.QTY, 0) AS decimal(18, 4))
                    ELSE CAST(0 AS decimal(18, 4))
                END
            )
            AS decimal(18, 4)
        ) AS SOLD_7,
        COUNT(DISTINCT si.S_ID) AS INVOICE_COUNT_30,
        MAX(si.S_TIME) AS LAST_SALE_DATE
    FROM dbo.SALE_ITEMS_VIEW si
    INNER JOIN dbo.SALE_INVOICE sh
        ON sh.S_ID = si.S_ID
    CROSS JOIN DateBounds d
    WHERE si.S_TIME >= d.START_ANALYSIS
      AND si.S_TIME <= d.AS_OF_DATE
      AND sh.S_STATUES = 1
      AND si.S_ITEM_INVISIBLE = 1
      AND ISNULL(si.QTY, 0) > 0
      AND si.ITEM_ID IS NOT NULL
    GROUP BY si.ITEM_ID
),
Returns30 AS
(
    SELECT
        ri.ITEM_ID,
        CAST(
            SUM(CAST(ISNULL(ri.QTY, 0) AS decimal(18, 4)))
            AS decimal(18, 4)
        ) AS RETURNED_30,
        CAST(
            SUM(
                CASE
                    WHEN rh.S_R_DATE >= d.START_ACCELERATION
                    THEN CAST(ISNULL(ri.QTY, 0) AS decimal(18, 4))
                    ELSE CAST(0 AS decimal(18, 4))
                END
            )
            AS decimal(18, 4)
        ) AS RETURNED_7
    FROM dbo.S_R_ITEMS_VIEW ri
    INNER JOIN dbo.S_R_INVOICE_VIEW rh
        ON rh.S_R_ID = ri.S_R_ID
    CROSS JOIN DateBounds d
    WHERE rh.S_R_DATE >= d.START_ANALYSIS
      AND rh.S_R_DATE <= d.AS_OF_DATE
      AND rh.S_R_STATUES = 1
      AND ISNULL(ri.QTY, 0) > 0
      AND ri.ITEM_ID IS NOT NULL
    GROUP BY ri.ITEM_ID
),
CurrentStock AS
(
    SELECT
        ITEM_ID,
        CAST(
            SUM(CAST(ISNULL(QTY, 0) AS decimal(18, 4)))
            AS decimal(18, 4)
        ) AS STOCK_QTY
    FROM dbo.ITEM_SUB_VIEW
    WHERE ITEM_ID IS NOT NULL
    GROUP BY ITEM_ID
),
BaseData AS
(
    SELECT
        i.ITEM_ID,
        i.ITEM_NAME,
        i.CAT1_NAME,
        i.MIN_LEVEL,
        i.MAX_LEVEL,
        ISNULL(bu.BASE_UNIT_LABEL, N'وحدة أساسية') AS BASE_UNIT_LABEL,
        pu.PURCHASE_UNIT_LABEL,
        pu.PURCHASE_UNIT_FACTOR,
        ISNULL(NULLIF(lp.SUPPLIER_NAME, ''), N'غير محدد') AS SUPPLIER_NAME,
        ISNULL(lp.LAST_PURCHASE_PRICE, 0) AS LAST_PURCHASE_PRICE,
        ISNULL(s.SOLD_30, 0) AS SOLD_30,
        ISNULL(r.RETURNED_30, 0) AS RETURNED_30,
        CASE
            WHEN ISNULL(s.SOLD_30, 0) - ISNULL(r.RETURNED_30, 0) < 0
            THEN 0
            ELSE ISNULL(s.SOLD_30, 0) - ISNULL(r.RETURNED_30, 0)
        END AS NET_SOLD_30,
        ISNULL(s.SOLD_7, 0) AS SOLD_7,
        ISNULL(r.RETURNED_7, 0) AS RETURNED_7,
        CASE
            WHEN ISNULL(s.SOLD_7, 0) - ISNULL(r.RETURNED_7, 0) < 0
            THEN 0
            ELSE ISNULL(s.SOLD_7, 0) - ISNULL(r.RETURNED_7, 0)
        END AS NET_SOLD_7,
        ISNULL(st.STOCK_QTY, 0) AS STOCK_RAW,
        CASE
            WHEN ISNULL(st.STOCK_QTY, 0) < 0 THEN 0
            ELSE ISNULL(st.STOCK_QTY, 0)
        END AS CURRENT_STOCK,
        ISNULL(s.INVOICE_COUNT_30, 0) AS INVOICE_COUNT_30,
        s.LAST_SALE_DATE
    FROM ItemsBase i
    LEFT JOIN Sales30 s
        ON s.ITEM_ID = i.ITEM_ID
    LEFT JOIN Returns30 r
        ON r.ITEM_ID = i.ITEM_ID
    LEFT JOIN CurrentStock st
        ON st.ITEM_ID = i.ITEM_ID
    LEFT JOIN ItemBaseUnits bu
        ON bu.ITEM_ID = i.ITEM_ID
    LEFT JOIN PurchaseUnits pu
        ON pu.ITEM_ID = i.ITEM_ID
    LEFT JOIN LastPurchases lp
        ON lp.ITEM_ID = i.ITEM_ID
    WHERE ISNULL(s.SOLD_30, 0) > 0
       OR ISNULL(st.STOCK_QTY, 0) < 0
),
DemandRate AS
(
    SELECT
        ITEM_ID,
        ITEM_NAME,
        CAT1_NAME,
        MIN_LEVEL,
        MAX_LEVEL,
        BASE_UNIT_LABEL,
        PURCHASE_UNIT_LABEL,
        PURCHASE_UNIT_FACTOR,
        SUPPLIER_NAME,
        LAST_PURCHASE_PRICE,
        SOLD_30,
        RETURNED_30,
        NET_SOLD_30,
        SOLD_7,
        RETURNED_7,
        NET_SOLD_7,
        STOCK_RAW,
        CURRENT_STOCK,
        INVOICE_COUNT_30,
        LAST_SALE_DATE,
        CAST(
            NET_SOLD_30 / 30.0
            AS decimal(18, 4)
        ) AS AVG_DAILY_30,
        CAST(
            NET_SOLD_7 / 7.0
            AS decimal(18, 4)
        ) AS AVG_DAILY_7,
        CAST(
            CASE
                WHEN NET_SOLD_7 >= 3
                 AND (NET_SOLD_7 / 7.0)
                     > (NET_SOLD_30 / 30.0) * 1.25
                THEN
                    ((NET_SOLD_7 / 7.0) * 0.70)
                    +
                    ((NET_SOLD_30 / 30.0) * 0.30)
                ELSE NET_SOLD_30 / 30.0
            END
            AS decimal(18, 4)
        ) AS FORECAST_DAILY
    FROM BaseData
),
CoverageData AS
(
    SELECT
        ITEM_ID,
        ITEM_NAME,
        CAT1_NAME,
        MIN_LEVEL,
        MAX_LEVEL,
        BASE_UNIT_LABEL,
        PURCHASE_UNIT_LABEL,
        PURCHASE_UNIT_FACTOR,
        SUPPLIER_NAME,
        LAST_PURCHASE_PRICE,
        SOLD_30,
        RETURNED_30,
        NET_SOLD_30,
        SOLD_7,
        RETURNED_7,
        NET_SOLD_7,
        STOCK_RAW,
        CURRENT_STOCK,
        INVOICE_COUNT_30,
        LAST_SALE_DATE,
        AVG_DAILY_30,
        AVG_DAILY_7,
        FORECAST_DAILY,
        CAST(
            CASE
                WHEN FORECAST_DAILY > 0
                THEN CURRENT_STOCK / FORECAST_DAILY
                ELSE NULL
            END
            AS decimal(18, 1)
        ) AS DAYS_COVER,
        CAST(
            CASE
                WHEN MIN_LEVEL > FORECAST_DAILY * 35.0
                THEN MIN_LEVEL
                ELSE FORECAST_DAILY * 35.0
            END
            AS decimal(18, 2)
        ) AS TARGET_STOCK
    FROM DemandRate
),
FinalData AS
(
    SELECT
        ITEM_ID,
        ITEM_NAME,
        CAT1_NAME,
        MIN_LEVEL,
        MAX_LEVEL,
        BASE_UNIT_LABEL,
        PURCHASE_UNIT_LABEL,
        PURCHASE_UNIT_FACTOR,
        SUPPLIER_NAME,
        LAST_PURCHASE_PRICE,
        SOLD_30,
        RETURNED_30,
        NET_SOLD_30,
        SOLD_7,
        RETURNED_7,
        NET_SOLD_7,
        STOCK_RAW,
        CURRENT_STOCK,
        INVOICE_COUNT_30,
        LAST_SALE_DATE,
        AVG_DAILY_30,
        AVG_DAILY_7,
        FORECAST_DAILY,
        DAYS_COVER,
        TARGET_STOCK,
        CAST(
            CASE
                WHEN TARGET_STOCK - CURRENT_STOCK > 0
                THEN CEILING(TARGET_STOCK - CURRENT_STOCK)
                ELSE 0
            END
            AS decimal(18, 2)
        ) AS REQUIRED_QTY
    FROM CoverageData
),
QualifiedData AS
(
    SELECT
        ITEM_ID,
        ITEM_NAME,
        CAT1_NAME,
        MIN_LEVEL,
        MAX_LEVEL,
        BASE_UNIT_LABEL,
        PURCHASE_UNIT_LABEL,
        PURCHASE_UNIT_FACTOR,
        SUPPLIER_NAME,
        LAST_PURCHASE_PRICE,
        SOLD_30,
        RETURNED_30,
        NET_SOLD_30,
        SOLD_7,
        RETURNED_7,
        NET_SOLD_7,
        STOCK_RAW,
        CURRENT_STOCK,
        INVOICE_COUNT_30,
        LAST_SALE_DATE,
        AVG_DAILY_30,
        AVG_DAILY_7,
        FORECAST_DAILY,
        DAYS_COVER,
        TARGET_STOCK,
        REQUIRED_QTY
    FROM FinalData
    WHERE STOCK_RAW < 0
       OR
       (
           FORECAST_DAILY > 0
           AND DAYS_COVER <= 35
           AND REQUIRED_QTY > 0
           AND
           (
               INVOICE_COUNT_30 >= 2
               OR NET_SOLD_7 > 0
           )
       )
),
StatusData AS
(
    SELECT
        ITEM_ID,
        ITEM_NAME,
        CAT1_NAME,
        MIN_LEVEL,
        MAX_LEVEL,
        BASE_UNIT_LABEL,
        PURCHASE_UNIT_LABEL,
        PURCHASE_UNIT_FACTOR,
        SUPPLIER_NAME,
        LAST_PURCHASE_PRICE,
        SOLD_30,
        RETURNED_30,
        NET_SOLD_30,
        SOLD_7,
        RETURNED_7,
        NET_SOLD_7,
        STOCK_RAW,
        CURRENT_STOCK,
        INVOICE_COUNT_30,
        LAST_SALE_DATE,
        AVG_DAILY_30,
        AVG_DAILY_7,
        FORECAST_DAILY,
        DAYS_COVER,
        TARGET_STOCK,
        REQUIRED_QTY,
        CASE
            WHEN STOCK_RAW < 0 THEN 0
            WHEN CURRENT_STOCK <= 0 THEN 1
            WHEN DAYS_COVER <= 7 THEN 2
            WHEN DAYS_COVER <= 15 THEN 3
            WHEN DAYS_COVER <= 35 THEN 4
            ELSE 5
        END AS SHORTAGE_STATUS_CODE
    FROM QualifiedData
)
SELECT
    ITEM_ID AS itemId,
    ITEM_NAME AS itemName,
    CAT1_NAME AS categoryName,
    STOCK_RAW AS stockRaw,
    CURRENT_STOCK AS currentStock,
    MIN_LEVEL AS minLevel,
    MAX_LEVEL AS maxLevel,
    BASE_UNIT_LABEL AS baseUnitLabel,
    PURCHASE_UNIT_LABEL AS purchaseUnitLabel,
    PURCHASE_UNIT_FACTOR AS purchaseUnitFactor,
    SUPPLIER_NAME AS supplierName,
    LAST_PURCHASE_PRICE AS lastPurchasePrice,
    SOLD_30 AS grossSales30Days,
    RETURNED_30 AS returns30Days,
    NET_SOLD_30 AS netSales30Days,
    SOLD_7 AS grossSales7Days,
    RETURNED_7 AS returns7Days,
    NET_SOLD_7 AS netSales7Days,
    AVG_DAILY_30 AS averageDailySales30,
    AVG_DAILY_7 AS averageDailySales7,
    FORECAST_DAILY AS forecastDailySales,
    DAYS_COVER AS daysOfStockCover,
    TARGET_STOCK AS targetStock35Days,
    REQUIRED_QTY AS suggestedOrderQty,
    INVOICE_COUNT_30 AS invoiceCount,
    LAST_SALE_DATE AS lastSaleDate,
    SHORTAGE_STATUS_CODE AS shortageStatusCode
FROM StatusData
ORDER BY
    SHORTAGE_STATUS_CODE ASC,
    DAYS_COVER ASC,
    NET_SOLD_30 DESC,
    ITEM_ID ASC
