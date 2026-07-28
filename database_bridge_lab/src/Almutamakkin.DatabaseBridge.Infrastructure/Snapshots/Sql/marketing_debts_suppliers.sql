SELECT 
          supplierId,
          supplierName,
          debtAmount
      FROM (
          SELECT 
              c.CUST_ID AS supplierId,
              c.CUST_NAME AS supplierName,
              CAST(
                  (
                      ISNULL(buys.BUYS, 0) + ISNULL(takes.TAKES, 0) + ISNULL(bal.BALANCE, 0) + ISNULL(spends.SPENDS, 0) + ISNULL(masm.MASM, 0) + ISNULL(credit_q.CREDIT_Q, 0)
                  )
                  -
                  (
                      ISNULL(sales.SALES, 0) + ISNULL(b_returns.B_RETURNS, 0) + ISNULL(gives.GIVES, 0) + ISNULL(discount.DISCOUNT, 0) + ISNULL(masm_b.MASM_B, 0) + ISNULL(debit_q.DEBIT_Q, 0)
                  )
              AS DECIMAL(10,2)) AS debtAmount
          FROM CUSTOMERS c
          LEFT JOIN (
              SELECT CUST_ID, SUM((QTY * PRICE) - ((QTY * PRICE) * B_DISCOUNT / 100)) AS BUYS
              FROM BUY_ITEMS_INVOICE_VIEW WHERE B_STATUES <> 0 AND B_STATUES <> 2 GROUP BY CUST_ID
          ) buys ON c.CUST_ID = buys.CUST_ID
          LEFT JOIN (
              SELECT CUST_ID, SUM(T_VALUE) + SUM(G_DISCOUNT) AS TAKES
              FROM TAKE_VIEW WHERE T_STATUES <> 0 AND T_STATUES <> 2 GROUP BY CUST_ID
          ) takes ON c.CUST_ID = takes.CUST_ID
          LEFT JOIN (
              SELECT CUST_ID, SUM(BL_CREDIT - BL_DEBIT) AS BALANCE
              FROM BALANCE_EDIT_VIEW WHERE BL_STATUES <> 0 AND BL_STATUES <> 2 GROUP BY CUST_ID
          ) bal ON c.CUST_ID = bal.CUST_ID
          LEFT JOIN (
              SELECT CUST_ID, SUM(EXPENCES_VALUE) AS SPENDS
              FROM BUY_INVOICE_EXPENCES_VIEW WHERE EXP__STATUES <> 0 AND EXP__STATUES <> 2 GROUP BY CUST_ID
          ) spends ON c.CUST_ID = spends.CUST_ID
          LEFT JOIN (
              SELECT CUST_ID, SUM(MASM_S_VLAUE) AS MASM
              FROM MASM_INVOICE_VIEW WHERE MASM_S_STATUES <> 0 AND MASM_S_STATUES <> 2 GROUP BY CUST_ID
          ) masm ON c.CUST_ID = masm.CUST_ID
          LEFT JOIN (
              SELECT c2.CUST_ID, SUM(ACC_CREDIT) AS CREDIT_Q
              FROM QYODAT_ITEMS_INVOICE_VIEW q
              JOIN CUSTOMERS c2 ON q.ACC_ID = c2.ACC_ID
              WHERE Q_STATUES <> 0 AND Q_STATUES <> 2 AND R1 = 0 
              GROUP BY c2.CUST_ID
          ) credit_q ON c.CUST_ID = credit_q.CUST_ID
          LEFT JOIN (
              SELECT CUST_ID, SUM(
                ((QTY * PRICE) + ISNULL(QTY * COLOR_PRICE,0)) 
                - ((((QTY * PRICE) + ISNULL(QTY * COLOR_PRICE,0))) * S_DISCOUNT / 100)
                + ((((QTY * PRICE) + ISNULL(QTY * COLOR_PRICE,0)) - (((QTY * PRICE) + ISNULL(QTY * COLOR_PRICE,0))) * S_DISCOUNT / 100) * S_TAX1 / 100)
                + ((((QTY * PRICE) + ISNULL(QTY * COLOR_PRICE,0)) - (((QTY * PRICE) + ISNULL(QTY * COLOR_PRICE,0)) * S_DISCOUNT / 100) + ((((QTY * PRICE) + ISNULL(QTY * COLOR_PRICE,0)) - ((QTY * PRICE) + ISNULL(QTY * COLOR_PRICE,0)) * S_DISCOUNT / 100) * S_TAX1 / 100)) * S_TAX2 / 100)
              ) AS SALES
              FROM SALE_ITEMS_INVOICE_VIEW WHERE S_STATUES <> 0 AND S_STATUES <> 2 GROUP BY CUST_ID
          ) sales ON c.CUST_ID = sales.CUST_ID
          LEFT JOIN (
              SELECT CUST_ID, SUM(QTY * PRICE) AS B_RETURNS
              FROM B_R_ITEMS_INVOICE_VIEW WHERE B_R_STATUES <> 0 AND B_R_STATUES <> 2 GROUP BY CUST_ID
          ) b_returns ON c.CUST_ID = b_returns.CUST_ID
          LEFT JOIN (
              SELECT CUST_ID, SUM(G_VALUE) + SUM(G_DISCOUNT) AS GIVES
              FROM GIVE_VIEW WHERE G_STATUES <> 0 AND G_STATUES <> 2 GROUP BY CUST_ID
          ) gives ON c.CUST_ID = gives.CUST_ID
          LEFT JOIN (
              SELECT CUST_ID, SUM(BORROW_DISCOUNT + PENALTY) AS DISCOUNT
              FROM SALARIES_VIEW WHERE S_STATUES <> 0 GROUP BY CUST_ID
          ) discount ON c.CUST_ID = discount.CUST_ID
          LEFT JOIN (
              SELECT CUST_ID, SUM(MASM_S_VLAUE) AS MASM_B
              FROM MASM_B_INVOICE_VIEW WHERE MASM_S_STATUES <> 0 AND MASM_S_STATUES <> 2 GROUP BY CUST_ID
          ) masm_b ON c.CUST_ID = masm_b.CUST_ID
          LEFT JOIN (
              SELECT c2.CUST_ID, SUM(ACC_DEBIT) AS DEBIT_Q
              FROM QYODAT_ITEMS_INVOICE_VIEW q
              JOIN CUSTOMERS c2 ON q.ACC_ID = c2.ACC_ID
              WHERE Q_STATUES <> 0 AND Q_STATUES <> 2 AND R1 = 0
              GROUP BY c2.CUST_ID
          ) debit_q ON c.CUST_ID = debit_q.CUST_ID
          WHERE c.CUST_VENDOR = 1
      ) AS results
      WHERE debtAmount > 0
      ORDER BY debtAmount DESC
