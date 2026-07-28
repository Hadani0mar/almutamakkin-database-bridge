WITH SystemOpenShifts AS (
    SELECT
        entry.USERS_ID,
        MAX(entry.TRANS_DATE) AS shift_start
    FROM USER_TIME_SHEET entry
    WHERE entry.TRANS_FLAG = N'دخول'
      AND entry.TRANS_DATE >= DATEADD(hour, -36, GETDATE())
      AND entry.TRANS_DATE <= GETDATE()
      AND NOT EXISTS (
          SELECT 1
          FROM USER_TIME_SHEET exit_row
          WHERE exit_row.USERS_ID = entry.USERS_ID
            AND exit_row.TRANS_FLAG = N'خروج'
            AND exit_row.TRANS_DATE > entry.TRANS_DATE
            AND exit_row.TRANS_DATE <= GETDATE()
      )
    GROUP BY entry.USERS_ID
),
HikOpenShifts AS (
    SELECT
        u.USERS_ID,
        MAX(h.CHECK_IN) AS shift_start
    FROM MUTAMAKKIN_HIK_SHIFTS h
    INNER JOIN USERS u
        ON LTRIM(RTRIM(CONVERT(NVARCHAR(50), h.EMPLOYEE_NO))) =
           CONVERT(NVARCHAR(50), u.USERS_ID)
    WHERE h.IS_OPEN = 1
      AND h.CHECK_IN >= DATEADD(hour, -36, GETDATE())
      AND h.CHECK_IN <= GETDATE()
    GROUP BY u.USERS_ID
),
OpenShiftCandidates AS (
    SELECT USERS_ID, shift_start, 1 AS source_priority
    FROM SystemOpenShifts
    UNION ALL
    SELECT USERS_ID, shift_start, 2 AS source_priority
    FROM HikOpenShifts
),
ActiveShifts AS (
    SELECT
        USERS_ID,
        COALESCE(
            MAX(CASE WHEN source_priority = 1 THEN shift_start END),
            MAX(CASE WHEN source_priority = 2 THEN shift_start END)
        ) AS shift_start
    FROM OpenShiftCandidates
    GROUP BY USERS_ID
),
LatestLiveInvoices AS (
    SELECT
        s.USERS_ID,
        MAX(s.S_ID) AS S_ID
    FROM SALE_INVOICE s
    INNER JOIN ActiveShifts active_shift
        ON active_shift.USERS_ID = s.USERS_ID
    WHERE s.CUST_ID = 0
      AND ISNULL(s.S_STATUES, 1) <> 2
      AND (
          (s.S_DATE >= active_shift.shift_start AND s.S_DATE <= GETDATE())
          OR EXISTS (
              SELECT 1
              FROM SALE_ITEMS live_item
              WHERE live_item.S_ID = s.S_ID
                AND live_item.QTY > 0
                AND live_item.S_TIME >= active_shift.shift_start
                AND live_item.S_TIME <= GETDATE()
          )
      )
    GROUP BY s.USERS_ID
)
SELECT
    s.S_ID AS invoice_id,
    ISNULL(s.USERS_ID, 0) AS employee_id,
    ISNULL(NULLIF(LTRIM(RTRIM(u.FULL_NAME)), ''), N'غير محدد') AS employee_name,
    s.CUST_ID AS customer_id,
    CASE
        WHEN s.CUST_ID > 1 THEN N'debt'
        ELSE N'cash'
    END AS invoice_kind,
    N'live' AS invoice_lifecycle,
    CAST(
        ISNULL(
            SUM(
                CASE
                    WHEN si.QTY > 0 THEN (((((si.QTY * si.PRICE) + (ISNULL(si.COLOR_PRICE, 0) * si.QTY)) - (((si.QTY * si.PRICE) + (ISNULL(si.COLOR_PRICE, 0) * si.QTY)) * ISNULL(s.S_DISCOUNT, 0) / 100.0)) + ((((si.QTY * si.PRICE) + (ISNULL(si.COLOR_PRICE, 0) * si.QTY)) - (((si.QTY * si.PRICE) + (ISNULL(si.COLOR_PRICE, 0) * si.QTY)) * ISNULL(s.S_DISCOUNT, 0) / 100.0)) * ISNULL(s.S_TAX1, 0) / 100.0)) + (((((si.QTY * si.PRICE) + (ISNULL(si.COLOR_PRICE, 0) * si.QTY)) - (((si.QTY * si.PRICE) + (ISNULL(si.COLOR_PRICE, 0) * si.QTY)) * ISNULL(s.S_DISCOUNT, 0) / 100.0)) + ((((si.QTY * si.PRICE) + (ISNULL(si.COLOR_PRICE, 0) * si.QTY)) - (((si.QTY * si.PRICE) + (ISNULL(si.COLOR_PRICE, 0) * si.QTY)) * ISNULL(s.S_DISCOUNT, 0) / 100.0)) * ISNULL(s.S_TAX1, 0) / 100.0)) * ISNULL(s.S_TAX2, 0) / 100.0))
                    ELSE 0
                END
            ),
            0
        ) AS DECIMAL(18, 2)
    ) AS total_amount,
    COALESCE(MIN(CASE WHEN si.QTY > 0 THEN si.S_TIME END), s.S_DATE) AS started_at,
    MAX(CASE WHEN si.QTY > 0 THEN si.S_TIME END) AS last_item_at,
    ISNULL(SUM(CASE WHEN si.QTY > 0 THEN 1 ELSE 0 END), 0) AS line_count
FROM LatestLiveInvoices live
INNER JOIN SALE_INVOICE s ON s.S_ID = live.S_ID
LEFT JOIN USERS u ON u.USERS_ID = s.USERS_ID
LEFT JOIN SALE_ITEMS si ON si.S_ID = s.S_ID
GROUP BY s.S_ID, s.USERS_ID, u.FULL_NAME, s.CUST_ID, s.S_DATE, s.S_DISCOUNT, s.S_TAX1, s.S_TAX2
ORDER BY
    MAX(CASE WHEN si.QTY > 0 THEN si.S_TIME END) DESC,
    s.S_ID DESC
;
