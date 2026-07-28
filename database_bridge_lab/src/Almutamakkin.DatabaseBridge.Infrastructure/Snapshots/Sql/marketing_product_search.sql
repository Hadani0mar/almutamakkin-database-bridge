-- Marketing product_search snapshot — fields shown on أبوغريس HomeScreen search.
-- One row per barcode line (matches suggestion + barcode lookup).
-- SQL Server 2005 compatible (no DATE type).

WITH CurrentStock AS
(
    SELECT
        ITEM_ID,
        CAST(SUM(ISNULL(QTY, 0)) AS decimal(18, 4)) AS CURRENT_QTY
    FROM dbo.ITEMS_SUB
    WHERE ITEM_ID IS NOT NULL
    GROUP BY ITEM_ID
),
BaseUnitCandidates AS
(
    SELECT
        b.ITEM_ID,
        CASE
            WHEN LTRIM(RTRIM(ISNULL(u.UNIT_DISC, ''))) = ''
              OR UPPER(LTRIM(RTRIM(ISNULL(u.UNIT_DISC, '')))) IN ('N/A', 'NA')
            THEN N'وحدة أساسية'
            ELSE LTRIM(RTRIM(u.UNIT_DISC))
        END AS BASE_UNIT_LABEL
    FROM dbo.BARCODE b
    LEFT JOIN dbo.UNITS u
        ON u.UNIT_ID = b.UNIT_ID
    WHERE b.ITEM_ID IS NOT NULL
      AND COALESCE(NULLIF(b.UNIT_QTY, 0), NULLIF(u.UNIT_QTY, 0), 1) = 1
),
BaseUnits AS
(
    SELECT
        ITEM_ID,
        CASE
            WHEN COUNT(DISTINCT BASE_UNIT_LABEL) = 1
            THEN MAX(BASE_UNIT_LABEL)
            ELSE N'وحدة أساسية'
        END AS BASE_UNIT_LABEL
    FROM BaseUnitCandidates
    GROUP BY ITEM_ID
),
NearestExpiryCandidates AS
(
    SELECT
        stock.ITEM_ID,
        CONVERT(varchar(10), stock.CATEOGRY3, 120) AS EXPIRY_DATE,
        CAST(SUM(ISNULL(stock.QTY, 0)) AS decimal(18, 2)) AS EXPIRY_QTY,
        ROW_NUMBER() OVER
        (
            PARTITION BY stock.ITEM_ID
            ORDER BY stock.CATEOGRY3 ASC
        ) AS EXPIRY_RANK
    FROM dbo.ITEMS_SUB stock
    WHERE stock.ITEM_ID IS NOT NULL
      AND ISNULL(stock.QTY, 0) > 0
      AND stock.CATEOGRY3 IS NOT NULL
    GROUP BY stock.ITEM_ID, stock.CATEOGRY3
    HAVING SUM(ISNULL(stock.QTY, 0)) > 0
),
NearestExpiry AS
(
    SELECT ITEM_ID, EXPIRY_DATE, EXPIRY_QTY
    FROM NearestExpiryCandidates
    WHERE EXPIRY_RANK = 1
),
LastBuyCandidates AS
(
    SELECT
        bi.ITEM_ID,
        CAST(bi.PRICE AS decimal(18, 4)) AS LAST_BUY_PRICE,
        LTRIM(RTRIM(ISNULL(supplier.CUST_NAME, ''))) AS LAST_SUPPLIER_NAME,
        CONVERT(varchar(10), invoice.B_DATE, 120) AS LAST_BUY_DATE,
        ROW_NUMBER() OVER
        (
            PARTITION BY bi.ITEM_ID
            ORDER BY invoice.B_DATE DESC, bi.B_ID DESC, bi.B_ITEM_ID DESC
        ) AS BUY_RANK
    FROM dbo.BUY_ITEMS bi
    INNER JOIN dbo.BUY_INVOICE invoice
        ON invoice.B_ID = bi.B_ID
    LEFT JOIN dbo.CUSTOMERS supplier
        ON supplier.CUST_ID = invoice.CUST_ID
    WHERE bi.ITEM_ID IS NOT NULL
      AND ISNULL(invoice.B_STATUES, 1) NOT IN (0, 2)
),
LastBuy AS
(
    SELECT ITEM_ID, LAST_BUY_PRICE, LAST_SUPPLIER_NAME, LAST_BUY_DATE
    FROM LastBuyCandidates
    WHERE BUY_RANK = 1
)
SELECT
    i.ITEM_ID AS item_id,
    LTRIM(RTRIM(i.ITEM_NAME)) AS item_name,
    bc.BAR_ID AS bar_id,
    LTRIM(RTRIM(ISNULL(bc.BARCODE, ''))) AS barcode,
    CAST(bc.PRICE1 AS decimal(18, 4)) AS sale_price,
    CAST(
        CASE
            WHEN bc.UNIT_QTY IS NOT NULL AND bc.UNIT_QTY > 0 THEN bc.UNIT_QTY
            WHEN u.UNIT_QTY IS NOT NULL AND u.UNIT_QTY > 0 THEN u.UNIT_QTY
            ELSE 1
        END
        AS decimal(18, 4)
    ) AS unit_factor,
    CASE
        WHEN UPPER(LTRIM(RTRIM(ISNULL(u.UNIT_DISC, '')))) NOT IN ('', 'N/A', 'NA')
        THEN LTRIM(RTRIM(u.UNIT_DISC))
        WHEN
            CASE
                WHEN bc.UNIT_QTY IS NOT NULL AND bc.UNIT_QTY > 0 THEN bc.UNIT_QTY
                WHEN u.UNIT_QTY IS NOT NULL AND u.UNIT_QTY > 0 THEN u.UNIT_QTY
                ELSE 1
            END > 1
        THEN N'وحدة بيع'
        ELSE N'وحدة أساسية'
    END AS unit_label,
    CAST(ISNULL(st.CURRENT_QTY, 0) AS decimal(18, 4)) AS stock_qty,
    ISNULL(bu.BASE_UNIT_LABEL, N'وحدة أساسية') AS base_unit_label,
    ne.EXPIRY_DATE AS nearest_expiry,
    ne.EXPIRY_QTY AS nearest_expiry_qty,
    lb.LAST_BUY_PRICE AS last_buy_price,
    CASE
        WHEN NULLIF(lb.LAST_SUPPLIER_NAME, '') IS NULL THEN N'لا يوجد شراء سابق'
        ELSE lb.LAST_SUPPLIER_NAME
    END AS last_supplier_name,
    lb.LAST_BUY_DATE AS last_buy_date
FROM dbo.ITEMS i
INNER JOIN dbo.BARCODE bc
    ON bc.ITEM_ID = i.ITEM_ID
LEFT JOIN dbo.UNITS u
    ON u.UNIT_ID = bc.UNIT_ID
LEFT JOIN CurrentStock st
    ON st.ITEM_ID = i.ITEM_ID
LEFT JOIN BaseUnits bu
    ON bu.ITEM_ID = i.ITEM_ID
LEFT JOIN NearestExpiry ne
    ON ne.ITEM_ID = i.ITEM_ID
LEFT JOIN LastBuy lb
    ON lb.ITEM_ID = i.ITEM_ID
WHERE ISNULL(i.ITEM_INVISIBLE, 0) = 0
  AND i.ITEM_ID IS NOT NULL
ORDER BY
    i.ITEM_NAME ASC,
    bc.BAR_ID ASC
;
