-- Read-only fingerprint for recent Infinity purchase invoices (3-day window).
SELECT
  COUNT(*) AS purch_inv_3d,
  MAX(i.InvoiceDate) AS max_invoice_date,
  CHECKSUM_AGG(CHECKSUM(i.InvoiceID_PK, CAST(i.InvoiceDate AS date), i.SupplierID_FK)) AS purch_ck
FROM Purchase.Data_PurchaseInvoices AS i
WHERE i.DocumentTypeID_FK IN (3, 6)
  AND i.PurchaseInvoiceStateID_PK IN (200, 300)
  AND CAST(i.InvoiceDate AS date) >= DATEADD(day, -3, CONVERT(date, GETDATE()));
