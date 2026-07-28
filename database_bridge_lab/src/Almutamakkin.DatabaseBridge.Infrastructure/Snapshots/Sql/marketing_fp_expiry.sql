-- Read-only fingerprint for expiry batches (same window idea as marketing_expiry).
SELECT
  COUNT(*) AS batch_rows,
  SUM(CAST(ISNULL(sub.QTY, 0) AS BIGINT)) AS total_qty,
  CHECKSUM_AGG(CHECKSUM(sub.ITEM_ID, sub.CATEOGRY3, CAST(ISNULL(sub.QTY, 0) * 100 AS INT))) AS batch_ck
FROM dbo.ITEMS_SUB sub
INNER JOIN dbo.ITEMS i ON i.ITEM_ID = sub.ITEM_ID
WHERE sub.CATEOGRY3 IS NOT NULL
  AND sub.CATEOGRY3 < DATEADD(month, 1, DATEADD(month, DATEDIFF(month, 0, GETDATE()), 0))
  AND ISNULL(i.ITEM_INVISIBLE, 0) = 0
  AND ISNULL(sub.QTY, 0) > 0;
