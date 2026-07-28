WITH BaseUnitCandidates AS
(
    SELECT
        b.ITEM_ID,
        COUNT(DISTINCT CASE
            WHEN
                (CASE
                    WHEN b.UNIT_QTY IS NOT NULL AND b.UNIT_QTY > 0 THEN b.UNIT_QTY
                    WHEN u.UNIT_QTY IS NOT NULL AND u.UNIT_QTY > 0 THEN u.UNIT_QTY
                    ELSE 1
                END) = 1
                AND UPPER(LTRIM(RTRIM(ISNULL(u.UNIT_DISC, '')))) NOT IN ('', 'N/A', 'NA')
            THEN LTRIM(RTRIM(u.UNIT_DISC))
            ELSE NULL
        END) AS BASE_NAME_COUNT,
        MIN(CASE
            WHEN
                (CASE
                    WHEN b.UNIT_QTY IS NOT NULL AND b.UNIT_QTY > 0 THEN b.UNIT_QTY
                    WHEN u.UNIT_QTY IS NOT NULL AND u.UNIT_QTY > 0 THEN u.UNIT_QTY
                    ELSE 1
                END) = 1
                AND UPPER(LTRIM(RTRIM(ISNULL(u.UNIT_DISC, '')))) NOT IN ('', 'N/A', 'NA')
            THEN b.UNIT_ID
            ELSE NULL
        END) AS BASE_UNIT_ID,
        MIN(CASE
            WHEN
                (CASE
                    WHEN b.UNIT_QTY IS NOT NULL AND b.UNIT_QTY > 0 THEN b.UNIT_QTY
                    WHEN u.UNIT_QTY IS NOT NULL AND u.UNIT_QTY > 0 THEN u.UNIT_QTY
                    ELSE 1
                END) = 1
                AND UPPER(LTRIM(RTRIM(ISNULL(u.UNIT_DISC, '')))) NOT IN ('', 'N/A', 'NA')
            THEN LTRIM(RTRIM(u.UNIT_DISC))
            ELSE NULL
        END) AS BASE_UNIT_NAME
    FROM BARCODE b
    LEFT JOIN UNITS u ON u.UNIT_ID = b.UNIT_ID
    GROUP BY b.ITEM_ID
),
BaseUnits AS
(
    SELECT
        ITEM_ID,
        CASE WHEN BASE_NAME_COUNT = 1 THEN BASE_UNIT_ID ELSE NULL END AS BASE_UNIT_ID,
        CASE
            WHEN BASE_NAME_COUNT = 1 THEN BASE_UNIT_NAME
            ELSE N'وحدة أساسية'
        END AS BASE_UNIT_NAME
    FROM BaseUnitCandidates
)
SELECT
    sub.ITEM_ID AS item_id,
    LTRIM(RTRIM(i.ITEM_NAME)) AS item_name,
    CONVERT(VARCHAR(10), sub.CATEOGRY3, 120) AS expiry_date,
    CAST(SUM(ISNULL(sub.QTY, 0)) AS DECIMAL(18, 2)) AS quantity,
    baseUnit.BASE_UNIT_ID AS unit_id,
    ISNULL(baseUnit.BASE_UNIT_NAME, N'وحدة أساسية') AS unit_name,
    CAST(1 AS DECIMAL(18, 4)) AS unit_factor,
    DATEDIFF(
        day,
        DATEADD(day, DATEDIFF(day, 0, GETDATE()), 0),
        DATEADD(day, DATEDIFF(day, 0, sub.CATEOGRY3), 0)
    ) AS days_remaining
FROM ITEMS_SUB sub
INNER JOIN ITEMS i ON i.ITEM_ID = sub.ITEM_ID
LEFT JOIN BaseUnits baseUnit ON baseUnit.ITEM_ID = sub.ITEM_ID
WHERE
    sub.CATEOGRY3 IS NOT NULL
    AND sub.CATEOGRY3 < DATEADD(month, 1, DATEADD(month, DATEDIFF(month, 0, GETDATE()), 0))
    AND ISNULL(i.ITEM_INVISIBLE, 0) = 0
GROUP BY
    sub.ITEM_ID,
    i.ITEM_NAME,
    sub.CATEOGRY3,
    baseUnit.BASE_UNIT_ID,
    baseUnit.BASE_UNIT_NAME
HAVING SUM(ISNULL(sub.QTY, 0)) > 0
ORDER BY
    sub.CATEOGRY3 ASC,
    i.ITEM_NAME ASC
