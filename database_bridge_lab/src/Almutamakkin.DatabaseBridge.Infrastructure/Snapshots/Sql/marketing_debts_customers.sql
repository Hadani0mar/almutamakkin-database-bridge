SELECT 
            c.CUST_ID AS CustomerId,
            LTRIM(RTRIM(c.CUST_NAME)) AS CustomerName,
            LTRIM(RTRIM(
              ISNULL(
                NULLIF(LTRIM(RTRIM(ISNULL(c.CUST_MOBILE, N''))), N''),
                ISNULL(c.CUST_PHONE, N'')
              )
            )) AS CustomerPhone,
            ROUND(
              ISNULL(sales.SALES, 0)
              - ISNULL(buys.BUYS, 0)
              - ISNULL(paid.SALES_PAIED, 0)
              + ISNULL(gv.R_S_PAIED, 0)
              - ISNULL(rs.R_S, 0)
              + ISNULL(be.DEBIT, 0)
              - ISNULL(be.CREDIT, 0),
              3
            ) AS TotalDebt,
            ISNULL(inv.INVOICE_COUNT, 0) AS InvoiceCount,
            CONVERT(VARCHAR(10), activity.LAST_ACTIVITY, 120) AS LastInvoiceDate
        FROM CUSTOMERS c
        OUTER APPLY (
          SELECT SUM(((PRICE * QTY) + (ISNULL(COLOR_PRICE, 0) * QTY))
            - ((((PRICE * QTY) + (ISNULL(COLOR_PRICE, 0) * QTY)) * ISNULL(S_DISCOUNT, 0) / 100))
            + ((((PRICE * QTY) + (ISNULL(COLOR_PRICE, 0) * QTY))
              - ((((PRICE * QTY) + (ISNULL(COLOR_PRICE, 0) * QTY)) * ISNULL(S_DISCOUNT, 0) / 100))
            ) * ISNULL(S_TAX1, 0) / 100)
            + (((((PRICE * QTY) + (ISNULL(COLOR_PRICE, 0) * QTY))
              - ((((PRICE * QTY) + (ISNULL(COLOR_PRICE, 0) * QTY)) * ISNULL(S_DISCOUNT, 0) / 100))
            ) + ((((PRICE * QTY) + (ISNULL(COLOR_PRICE, 0) * QTY))
              - ((((PRICE * QTY) + (ISNULL(COLOR_PRICE, 0) * QTY)) * ISNULL(S_DISCOUNT, 0) / 100))
            ) * ISNULL(S_TAX1, 0) / 100)) * ISNULL(S_TAX2, 0) / 100)) AS SALES
          FROM SALE_ITEMS_INVOICE_VIEW
          WHERE S_STATUES NOT IN (0, 2) AND CUST_ID = c.CUST_ID
        ) sales
        OUTER APPLY (
          SELECT SUM(PRICE * QTY - (PRICE * QTY * ISNULL(B_DISCOUNT, 0) / 100)) AS BUYS
          FROM BUY_ITEMS_INVOICE_VIEW
          WHERE B_STATUES NOT IN (0, 2) AND CUST_ID = c.CUST_ID
        ) buys
        OUTER APPLY (
          SELECT SUM(ISNULL(T_VALUE, 0)) + SUM(ISNULL(G_DISCOUNT, 0)) AS SALES_PAIED
          FROM TAKE
          WHERE T_STATUES <> 2 AND CUST_ID = c.CUST_ID
        ) paid
        OUTER APPLY (
          SELECT SUM(PRICE * QTY) AS R_S
          FROM R_S_ITEMS_INVOICE_VIEW
          WHERE S_R_STATUES NOT IN (0, 2) AND CUST_ID = c.CUST_ID
        ) rs
        OUTER APPLY (
          SELECT SUM(ISNULL(G_VALUE, 0)) + SUM(ISNULL(G_DISCOUNT, 0)) AS R_S_PAIED
          FROM GIVE
          WHERE G_STATUES <> 2 AND CUST_ID = c.CUST_ID
        ) gv
        OUTER APPLY (
          SELECT SUM(ISNULL(BL_DEBIT, 0)) AS DEBIT, SUM(ISNULL(BL_CREDIT, 0)) AS CREDIT
          FROM BALANCE_EDIT
          WHERE BL_STATUES <> 2 AND CUST_ID = c.CUST_ID
        ) be
        OUTER APPLY (
          SELECT COUNT(DISTINCT s.S_ID) AS INVOICE_COUNT
          FROM SALE_INVOICE s
          WHERE s.CUST_ID = c.CUST_ID AND s.S_STATUES = 1
        ) inv
        OUTER APPLY (
          SELECT MAX(dt) AS LAST_ACTIVITY
          FROM (
            SELECT MAX(S_DATE) AS dt FROM SALE_INVOICE WHERE CUST_ID = c.CUST_ID AND S_STATUES NOT IN (0, 2)
            UNION ALL SELECT MAX(T_DATE) FROM TAKE WHERE CUST_ID = c.CUST_ID AND T_STATUES <> 2
            UNION ALL SELECT MAX(G_DATE) FROM GIVE WHERE CUST_ID = c.CUST_ID AND G_STATUES <> 2
            UNION ALL SELECT MAX(BL_DATE) FROM BALANCE_EDIT WHERE CUST_ID = c.CUST_ID AND BL_STATUES <> 2
          ) x
        ) activity
        WHERE c.CUST_INVISIBLE = 0
          AND c.CUST_CUSTOM <> 0
          AND c.CUST_ID <> 0
          AND ISNULL(c.BALANCE_HIDE, 0) = 0
          AND c.CUST_NAME NOT LIKE N'%نقدي%'
          AND c.CUST_NAME NOT LIKE N'%كاش%'
          AND ROUND(
              ISNULL(sales.SALES, 0)
              - ISNULL(buys.BUYS, 0)
              - ISNULL(paid.SALES_PAIED, 0)
              + ISNULL(gv.R_S_PAIED, 0)
              - ISNULL(rs.R_S, 0)
              + ISNULL(be.DEBIT, 0)
              - ISNULL(be.CREDIT, 0),
              3
            ) > 0
        ORDER BY TotalDebt DESC;
