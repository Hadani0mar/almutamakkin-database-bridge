-- Recent debt invoices for mobile debt notifications and tests.
-- Last 3 calendar days, confirmed credit sales (CUST_ID > 1, S_STATUES = 1).

SELECT TOP 200
    s.S_ID AS sale_id,
    CONVERT(varchar(23), s.S_DATE, 126) AS sale_date,
    LTRIM(RTRIM(ISNULL(s.CUST_NAME, N''))) AS customer_name,
    ISNULL(NULLIF(LTRIM(RTRIM(u.FULL_NAME)), N''), N'مجهول') AS employee_name,
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
    ), 0) AS decimal(18, 2)) AS total_amount
FROM SALE_INVOICE s
LEFT JOIN SALE_ITEMS si
    ON si.S_ID = s.S_ID
   AND si.QTY > 0
LEFT JOIN USERS u
    ON u.USERS_ID = s.USERS_ID
WHERE s.CUST_ID > 1
  AND s.S_STATUES = 1
  AND s.S_DATE >= DATEADD(day, -3, CONVERT(DATETIME, CONVERT(varchar(8), GETDATE(), 112), 112))
GROUP BY s.S_ID, s.S_DATE, s.CUST_NAME, u.FULL_NAME
ORDER BY s.S_DATE DESC, s.S_ID DESC;
