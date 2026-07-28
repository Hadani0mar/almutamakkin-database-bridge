-- Closed shifts with revenue for shift-close notifications and tests.
-- Last 7 calendar days from MUTAMAKKIN_HIK_SHIFTS (IS_OPEN = 0).

WITH ClosedShifts AS
(
    SELECT TOP 100
        h.ID AS shift_id,
        h.EMPLOYEE_NO,
        LTRIM(RTRIM(ISNULL(h.EMPLOYEE_NAME, N''))) AS employee_name,
        h.CHECK_IN,
        h.CHECK_OUT,
        CAST(ISNULL(h.HOURS, 0) AS decimal(18, 4)) AS hours_value,
        h.SESSION_KEY,
        COALESCE(erp.ERP_CHECK_IN, h.CHECK_IN) AS window_start,
        COALESCE(erp.ERP_CHECK_OUT, h.CHECK_OUT) AS window_end
    FROM MUTAMAKKIN_HIK_SHIFTS h
    OUTER APPLY (
        SELECT TOP 1
            entry_row.TRANS_DATE AS ERP_CHECK_IN,
            exit_row.TRANS_DATE AS ERP_CHECK_OUT
        FROM USER_TIME_SHEET entry_row
        CROSS APPLY (
            SELECT TOP 1 candidate_exit.TRANS_DATE
            FROM USER_TIME_SHEET candidate_exit
            WHERE candidate_exit.USERS_ID = entry_row.USERS_ID
              AND candidate_exit.TRANS_FLAG = N'خروج'
              AND candidate_exit.TRANS_DATE > entry_row.TRANS_DATE
            ORDER BY candidate_exit.TRANS_DATE ASC
        ) exit_row
        WHERE entry_row.USERS_ID = CAST(h.EMPLOYEE_NO AS INT)
          AND entry_row.TRANS_FLAG = N'دخول'
          AND entry_row.TRANS_DATE <= h.CHECK_OUT
        ORDER BY ABS(DATEDIFF(second, exit_row.TRANS_DATE, h.CHECK_OUT)) ASC
    ) erp
    WHERE h.IS_OPEN = 0
      AND ISNUMERIC(h.EMPLOYEE_NO) = 1
      AND h.CHECK_OUT >= DATEADD(day, -7, CONVERT(DATETIME, CONVERT(varchar(8), GETDATE(), 112), 112))
    ORDER BY h.CHECK_OUT DESC, h.SESSION_KEY DESC
)
SELECT
    cs.shift_id,
    cs.EMPLOYEE_NO AS employee_no,
    ISNULL(NULLIF(LTRIM(RTRIM(u.FULL_NAME)), N''), cs.employee_name) AS employee_name,
    CONVERT(varchar(23), cs.CHECK_IN, 126) AS check_in,
    CONVERT(varchar(23), cs.CHECK_OUT, 126) AS check_out,
    cs.hours_value AS hours,
    cs.SESSION_KEY AS session_key,
    CAST(DATEDIFF(minute, cs.window_start, cs.window_end) AS int) AS shift_minutes,
    CAST(ISNULL(SUM(CASE
        WHEN s.CUST_ID IN (0, 1) OR s.CUST_ID IS NULL THEN
        (
            (
                ((si.QTY * si.PRICE) + (ISNULL(si.COLOR_PRICE, 0) * si.QTY))
                - (((si.QTY * si.PRICE) + (ISNULL(si.COLOR_PRICE, 0) * si.QTY)) * ISNULL(s.S_DISCOUNT, 0) / 100.0)
            )
            + (
                (
                    ((si.QTY * si.PRICE) + (ISNULL(si.COLOR_PRICE, 0) * si.QTY))
                    - (((si.QTY * si.PRICE) + (ISNULL(si.COLOR_PRICE, 0) * si.QTY)) * ISNULL(s.S_DISCOUNT, 0) / 100.0)
                ) * ISNULL(s.S_TAX1, 0) / 100.0
            )
            + (
                (
                    (
                        ((si.QTY * si.PRICE) + (ISNULL(si.COLOR_PRICE, 0) * si.QTY))
                        - (((si.QTY * si.PRICE) + (ISNULL(si.COLOR_PRICE, 0) * si.QTY)) * ISNULL(s.S_DISCOUNT, 0) / 100.0)
                    )
                    + (
                        (
                            ((si.QTY * si.PRICE) + (ISNULL(si.COLOR_PRICE, 0) * si.QTY))
                            - (((si.QTY * si.PRICE) + (ISNULL(si.COLOR_PRICE, 0) * si.QTY)) * ISNULL(s.S_DISCOUNT, 0) / 100.0)
                        ) * ISNULL(s.S_TAX1, 0) / 100.0
                    )
                ) * ISNULL(s.S_TAX2, 0) / 100.0
            )
        )
        ELSE 0 END), 0) AS decimal(18, 2)) AS cash_revenue,
    CAST(ISNULL(SUM(CASE
        WHEN s.CUST_ID > 1 THEN
        (
            (
                ((si.QTY * si.PRICE) + (ISNULL(si.COLOR_PRICE, 0) * si.QTY))
                - (((si.QTY * si.PRICE) + (ISNULL(si.COLOR_PRICE, 0) * si.QTY)) * ISNULL(s.S_DISCOUNT, 0) / 100.0)
            )
            + (
                (
                    ((si.QTY * si.PRICE) + (ISNULL(si.COLOR_PRICE, 0) * si.QTY))
                    - (((si.QTY * si.PRICE) + (ISNULL(si.COLOR_PRICE, 0) * si.QTY)) * ISNULL(s.S_DISCOUNT, 0) / 100.0)
                ) * ISNULL(s.S_TAX1, 0) / 100.0
            )
            + (
                (
                    (
                        ((si.QTY * si.PRICE) + (ISNULL(si.COLOR_PRICE, 0) * si.QTY))
                        - (((si.QTY * si.PRICE) + (ISNULL(si.COLOR_PRICE, 0) * si.QTY)) * ISNULL(s.S_DISCOUNT, 0) / 100.0)
                    )
                    + (
                        (
                            ((si.QTY * si.PRICE) + (ISNULL(si.COLOR_PRICE, 0) * si.QTY))
                            - (((si.QTY * si.PRICE) + (ISNULL(si.COLOR_PRICE, 0) * si.QTY)) * ISNULL(s.S_DISCOUNT, 0) / 100.0)
                        ) * ISNULL(s.S_TAX1, 0) / 100.0
                    )
                ) * ISNULL(s.S_TAX2, 0) / 100.0
            )
        )
        ELSE 0 END), 0) AS decimal(18, 2)) AS debt_revenue,
    CAST(ISNULL(SUM(
        (
            (
                ((si.QTY * si.PRICE) + (ISNULL(si.COLOR_PRICE, 0) * si.QTY))
                - (((si.QTY * si.PRICE) + (ISNULL(si.COLOR_PRICE, 0) * si.QTY)) * ISNULL(s.S_DISCOUNT, 0) / 100.0)
            )
            + (
                (
                    ((si.QTY * si.PRICE) + (ISNULL(si.COLOR_PRICE, 0) * si.QTY))
                    - (((si.QTY * si.PRICE) + (ISNULL(si.COLOR_PRICE, 0) * si.QTY)) * ISNULL(s.S_DISCOUNT, 0) / 100.0)
                ) * ISNULL(s.S_TAX1, 0) / 100.0
            )
            + (
                (
                    (
                        ((si.QTY * si.PRICE) + (ISNULL(si.COLOR_PRICE, 0) * si.QTY))
                        - (((si.QTY * si.PRICE) + (ISNULL(si.COLOR_PRICE, 0) * si.QTY)) * ISNULL(s.S_DISCOUNT, 0) / 100.0)
                    )
                    + (
                        (
                            ((si.QTY * si.PRICE) + (ISNULL(si.COLOR_PRICE, 0) * si.QTY))
                            - (((si.QTY * si.PRICE) + (ISNULL(si.COLOR_PRICE, 0) * si.QTY)) * ISNULL(s.S_DISCOUNT, 0) / 100.0)
                        ) * ISNULL(s.S_TAX1, 0) / 100.0
                    )
                ) * ISNULL(s.S_TAX2, 0) / 100.0
            )
        )
    ), 0) AS decimal(18, 2)) AS total_revenue
FROM ClosedShifts cs
LEFT JOIN USERS u
    ON u.USERS_ID = CAST(cs.EMPLOYEE_NO AS INT)
LEFT JOIN SALE_INVOICE s
    ON s.USERS_ID = CAST(cs.EMPLOYEE_NO AS INT)
   AND ISNULL(s.S_STATUES, 1) <> 2
LEFT JOIN SALE_ITEMS si
    ON si.S_ID = s.S_ID
   AND si.QTY > 0
   AND si.S_TIME >= cs.window_start
   AND si.S_TIME <= cs.window_end
GROUP BY
    cs.shift_id,
    cs.EMPLOYEE_NO,
    u.FULL_NAME,
    cs.employee_name,
    cs.CHECK_IN,
    cs.CHECK_OUT,
    cs.hours_value,
    cs.SESSION_KEY,
    cs.window_start,
    cs.window_end
ORDER BY cs.CHECK_OUT DESC, cs.SESSION_KEY DESC;
