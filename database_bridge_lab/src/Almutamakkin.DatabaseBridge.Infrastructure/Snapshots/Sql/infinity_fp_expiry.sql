-- Read-only fingerprint for Infinity expiry window.
SELECT
  COUNT(*) AS batch_rows,
  SUM(CAST(ISNULL(pi.StockOnHand, 0) AS BIGINT)) AS total_qty,
  CHECKSUM_AGG(CHECKSUM(pi.ProductID_FK, pi.ExpiryDate, CAST(ISNULL(pi.StockOnHand, 0) * 100 AS INT))) AS batch_ck
FROM Inventory.Data_ProductInventories AS pi
INNER JOIN Inventory.Data_Products AS p ON p.ProductID_PK = pi.ProductID_FK
INNER JOIN MyCompany.Config_Branchs AS br ON br.BranchID_PK = pi.BranchID_FK
WHERE pi.StockOnHand > 0
  AND pi.ExpiryDate IS NOT NULL
  AND p.IsInActive = 0
  AND br.IsCurrentBranch = 1
  AND pi.ExpiryDate < DATEADD(month, 1, DATEADD(month, DATEDIFF(month, 0, GETDATE()), 0));
