-- Read-only fingerprint for sales pattern activity (cheap counters).
SELECT
  (SELECT MAX(COALESCE(si.S_TIME, s.S_DATE))
   FROM dbo.SALE_INVOICE s
   INNER JOIN dbo.SALE_ITEMS si ON si.S_ID = s.S_ID
   WHERE ISNULL(s.S_STATUES, 1) <> 2
     AND ISNULL(si.QTY, 0) > 0
     AND COALESCE(si.S_TIME, s.S_DATE) >= DATEADD(day, -60, GETDATE())) AS max_line_60d,
  (SELECT COUNT(DISTINCT s.S_ID)
   FROM dbo.SALE_INVOICE s
   INNER JOIN dbo.SALE_ITEMS si ON si.S_ID = s.S_ID
   WHERE ISNULL(s.S_STATUES, 1) <> 2
     AND ISNULL(si.QTY, 0) > 0
     AND COALESCE(si.S_TIME, s.S_DATE) >= DATEADD(day, -60, GETDATE())) AS inv_count_60d,
  (SELECT COUNT(*)
   FROM dbo.SALE_ITEMS_INVOICE_VIEW
   WHERE ISNULL(S_STATUES, 1) = 1
     AND ISNULL(S_ITEM_INVISIBLE, 1) = 1
     AND ISNULL(QTY, 0) > 0
     AND S_DATE >= DATEADD(day, -365, GETDATE())) AS lines_365d;
