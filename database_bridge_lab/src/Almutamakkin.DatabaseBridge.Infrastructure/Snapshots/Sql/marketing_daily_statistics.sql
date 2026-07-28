-- Marketing daily_statistics snapshot — employee sales/purchases for local calendar day.
-- SQL Server 2005 compatible. Period = today (GETDATE calendar day).

WITH DayBounds AS
(
    SELECT
        CONVERT(DATETIME, CONVERT(varchar(8), GETDATE(), 112), 112) AS day_start,
        DATEADD(day, 1, CONVERT(DATETIME, CONVERT(varchar(8), GETDATE(), 112), 112)) AS day_end
),
ShiftWindowsRaw AS (
    SELECT
        u.USERS_ID AS employee_id,
        COALESCE(erp.ERP_CHECK_IN, h.CHECK_IN) AS shift_start,
        COALESCE(erp.ERP_CHECK_OUT, h.CHECK_OUT) AS shift_end
    FROM MUTAMAKKIN_HIK_SHIFTS h
    INNER JOIN USERS u
        ON ISNUMERIC(h.EMPLOYEE_NO) = 1
       AND u.USERS_ID = CAST(h.EMPLOYEE_NO AS INT)
    CROSS JOIN DayBounds d
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
        WHERE entry_row.USERS_ID = u.USERS_ID
          AND entry_row.TRANS_FLAG = N'دخول'
          AND entry_row.TRANS_DATE <= h.CHECK_OUT
        ORDER BY ABS(DATEDIFF(second, exit_row.TRANS_DATE, h.CHECK_OUT)) ASC
    ) erp
    WHERE h.IS_OPEN = 0
      AND h.CHECK_OUT >= d.day_start
      AND h.CHECK_OUT < d.day_end
),
ShiftWindows AS (
    SELECT DISTINCT employee_id, shift_start, shift_end
    FROM ShiftWindowsRaw
),
ClosedShiftCounts AS (
    SELECT employee_id, COUNT(*) AS closed_shift_count
    FROM ShiftWindows
    GROUP BY employee_id
),
ShiftSaleItems AS (
    SELECT DISTINCT
        sw.employee_id,
        si.S_ITEM_ID,
        s.S_ID,
        s.CUST_ID,
        ISNULL(s.S_STATUES, 1) AS invoice_status,
        si.QTY,
        si.PRICE,
        si.COLOR_PRICE,
        s.S_DISCOUNT,
        s.S_TAX1,
        s.S_TAX2
    FROM ShiftWindows sw
    INNER JOIN SALE_INVOICE s
        ON s.USERS_ID = sw.employee_id
       AND ISNULL(s.S_STATUES, 1) <> 2
    INNER JOIN SALE_ITEMS si
        ON si.S_ID = s.S_ID
       AND si.QTY > 0
       AND si.S_TIME >= sw.shift_start
       AND si.S_TIME <= sw.shift_end
),
OpenTodaySaleItems AS (
    SELECT DISTINCT
        ISNULL(s.USERS_ID, 0) AS employee_id,
        si.S_ITEM_ID,
        s.S_ID,
        s.CUST_ID,
        ISNULL(s.S_STATUES, 1) AS invoice_status,
        si.QTY,
        si.PRICE,
        si.COLOR_PRICE,
        s.S_DISCOUNT,
        s.S_TAX1,
        s.S_TAX2
    FROM SALE_INVOICE s
    INNER JOIN SALE_ITEMS si ON si.S_ID = s.S_ID
    CROSS JOIN DayBounds d
    WHERE s.S_STATUES = 0
      AND si.QTY > 0
      AND EXISTS (
          SELECT 1 FROM SALE_ITEMS si2
          WHERE si2.S_ID = s.S_ID
            AND si2.S_TIME >= d.day_start
            AND si2.S_TIME < d.day_end
      )
),
ScopedSaleItems AS (
    SELECT
        employee_id, S_ITEM_ID, S_ID, CUST_ID, invoice_status, QTY, PRICE, COLOR_PRICE, S_DISCOUNT, S_TAX1, S_TAX2
    FROM ShiftSaleItems
    UNION
    SELECT
        employee_id, S_ITEM_ID, S_ID, CUST_ID, invoice_status, QTY, PRICE, COLOR_PRICE, S_DISCOUNT, S_TAX1, S_TAX2
    FROM OpenTodaySaleItems
),
NetSaleItems AS (
    SELECT
        *,
        (
            (
                (
                    ((QTY * PRICE) + (ISNULL(COLOR_PRICE, 0) * QTY))
                    - (((QTY * PRICE) + (ISNULL(COLOR_PRICE, 0) * QTY)) * ISNULL(S_DISCOUNT, 0) / 100.0)
                )
                + (
                    (
                        ((QTY * PRICE) + (ISNULL(COLOR_PRICE, 0) * QTY))
                        - (((QTY * PRICE) + (ISNULL(COLOR_PRICE, 0) * QTY)) * ISNULL(S_DISCOUNT, 0) / 100.0)
                    ) * ISNULL(S_TAX1, 0) / 100.0
                )
            )
            + (
                (
                    (
                        ((QTY * PRICE) + (ISNULL(COLOR_PRICE, 0) * QTY))
                        - (((QTY * PRICE) + (ISNULL(COLOR_PRICE, 0) * QTY)) * ISNULL(S_DISCOUNT, 0) / 100.0)
                    )
                    + (
                        (
                            ((QTY * PRICE) + (ISNULL(COLOR_PRICE, 0) * QTY))
                            - (((QTY * PRICE) + (ISNULL(COLOR_PRICE, 0) * QTY)) * ISNULL(S_DISCOUNT, 0) / 100.0)
                        ) * ISNULL(S_TAX1, 0) / 100.0
                    )
                ) * ISNULL(S_TAX2, 0) / 100.0
            )
        ) AS net_amount
    FROM ScopedSaleItems
),
Sales AS (
    SELECT
        employee_id,
        CAST(SUM(net_amount) AS DECIMAL(18, 2)) AS total_sales,
        CAST(SUM(CASE
            WHEN CUST_ID IN (0, 1) OR CUST_ID IS NULL
            THEN net_amount ELSE 0 END) AS DECIMAL(18, 2)) AS cash_sales,
        CAST(SUM(CASE
            WHEN CUST_ID > 1 THEN net_amount ELSE 0 END)
            AS DECIMAL(18, 2)) AS debt_sales,
        COUNT(DISTINCT CASE
            WHEN CUST_ID IN (0, 1) OR CUST_ID IS NULL THEN S_ID END)
            AS cash_document_count,
        COUNT(DISTINCT CASE WHEN CUST_ID > 1 THEN S_ID END)
            AS debt_invoice_count,
        COUNT(CASE
            WHEN CUST_ID IN (0, 1) OR CUST_ID IS NULL THEN S_ITEM_ID END)
            AS cash_sale_line_count,
        COUNT(DISTINCT CASE
            WHEN invoice_status = 0
             AND (CUST_ID IN (0, 1) OR CUST_ID IS NULL)
            THEN S_ID END) AS open_cash_invoice_count,
        COUNT(DISTINCT CASE
            WHEN invoice_status = 0 AND CUST_ID > 1 THEN S_ID END)
            AS open_debt_invoice_count,
        CAST(SUM(CASE
            WHEN invoice_status = 0 THEN net_amount ELSE 0 END)
            AS DECIMAL(18, 2)) AS open_invoice_sales,
        CAST(SUM(QTY) AS DECIMAL(18, 2)) AS sold_qty
    FROM NetSaleItems
    GROUP BY employee_id
),
Purchases AS (
    SELECT
        ISNULL(b.USERS_ID, 0) AS employee_id,
        CAST(ISNULL(SUM(CASE
            WHEN bi.QTY > 0 THEN bi.QTY * bi.PRICE ELSE 0 END), 0)
            AS DECIMAL(18, 2)) AS total_purchases,
        COUNT(DISTINCT b.B_ID) AS purchase_invoices,
        CAST(ISNULL(SUM(CASE
            WHEN bi.QTY > 0 THEN bi.QTY ELSE 0 END), 0)
            AS DECIMAL(18, 2)) AS purchased_qty
    FROM BUY_INVOICE b
    INNER JOIN BUY_ITEMS bi ON bi.B_ID = b.B_ID
    CROSS JOIN DayBounds d
    WHERE ISNULL(b.B_STATUES, 1) <> 0
      AND ISNULL(b.B_STATUES, 1) <> 2
      AND b.B_DATE >= d.day_start
      AND b.B_DATE < d.day_end
    GROUP BY ISNULL(b.USERS_ID, 0)
),
EmployeeIds AS (
    SELECT employee_id FROM ClosedShiftCounts
    UNION
    SELECT employee_id FROM Sales
    UNION
    SELECT employee_id FROM Purchases
)
SELECT
    e.employee_id,
    ISNULL(NULLIF(LTRIM(RTRIM(u.FULL_NAME)), ''), N'غير محدد') AS employee_name,
    ISNULL(s.total_sales, 0) AS total_sales,
    ISNULL(s.cash_sales, 0) AS cash_sales,
    ISNULL(s.debt_sales, 0) AS debt_sales,
    ISNULL(p.total_purchases, 0) AS total_purchases,
    ISNULL(s.cash_document_count, 0) AS cash_document_count,
    ISNULL(s.debt_invoice_count, 0) AS debt_invoice_count,
    ISNULL(s.cash_sale_line_count, 0) AS cash_sale_line_count,
    ISNULL(s.open_cash_invoice_count, 0) AS open_cash_invoice_count,
    ISNULL(s.open_debt_invoice_count, 0) AS open_debt_invoice_count,
    ISNULL(s.open_invoice_sales, 0) AS open_invoice_sales,
    ISNULL(p.purchase_invoices, 0) AS purchase_invoices,
    ISNULL(s.sold_qty, 0) AS sold_qty,
    ISNULL(p.purchased_qty, 0) AS purchased_qty,
    ISNULL(sc.closed_shift_count, 0) AS closed_shift_count
FROM EmployeeIds e
LEFT JOIN USERS u ON u.USERS_ID = e.employee_id
LEFT JOIN Sales s ON s.employee_id = e.employee_id
LEFT JOIN Purchases p ON p.employee_id = e.employee_id
LEFT JOIN ClosedShiftCounts sc ON sc.employee_id = e.employee_id
ORDER BY ISNULL(s.total_sales, 0) DESC, employee_name;
