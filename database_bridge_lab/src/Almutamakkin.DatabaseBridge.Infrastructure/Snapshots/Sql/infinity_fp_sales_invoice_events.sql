-- Read-only fingerprint for recent Infinity POS sales invoices (3-day window).
SELECT
  COUNT(*) AS sales_inv_3d,
  MAX(i.SalesInvoiceDate) AS max_sales_date,
  CHECKSUM_AGG(CHECKSUM(i.SalesInvoiceID_PK, CAST(i.SalesInvoiceDate AS date), i.POSTerminalShiftID_FK)) AS sales_ck
FROM SALES.Data_SalesInvoices AS i
WHERE i.DocumentTypeID_FK IN (15, 16)
  AND i.SalesInvoiceStateID_FK IN (200, 300)
  AND CAST(i.SalesInvoiceDate AS date) >= DATEADD(day, -3, CONVERT(date, GETDATE()));
