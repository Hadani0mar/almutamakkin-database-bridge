-- Read-only fingerprint for recent debt invoices (3-day window).
SELECT
  COUNT(*) AS debt_inv_3d,
  MAX(S_DATE) AS max_sale_date,
  CHECKSUM_AGG(CHECKSUM(S_ID, S_DATE, CUST_ID)) AS sale_ck
FROM dbo.SALE_INVOICE
WHERE CUST_ID > 1
  AND ISNULL(S_STATUES, 1) = 1
  AND S_DATE >= DATEADD(day, -3, CONVERT(datetime, CONVERT(varchar(8), GETDATE(), 112), 112));
