-- Infinity expiry batches for current branch (read-only).
-- Aliases match MapExpiryRow / marketing contract where possible.
SELECT
    CAST(pi.ProductID_FK AS nvarchar(50)) AS item_id,
    p.ProductName AS item_name,
    CONVERT(varchar(10), pi.ExpiryDate, 23) AS expiry_date,
    SUM(pi.StockOnHand) AS quantity,
    MAX(ISNULL(r.UOMName, N'وحدة أساسية')) AS unit_name,
    DATEDIFF(day, CAST(GETDATE() AS date), CAST(pi.ExpiryDate AS date)) AS days_remaining,
    p.ProductCode AS product_code,
    pi.BranchID_FK AS branch_id,
    MAX(br.BranchName) AS branch_name,
    MAX(bar.ProductBarcode) AS barcode,
    p.MainSupplierID_FK AS supplier_id,
    MAX(s.SupplierName) AS supplier_name
FROM Inventory.Data_ProductInventories AS pi
INNER JOIN Inventory.Data_Products AS p ON p.ProductID_PK = pi.ProductID_FK
INNER JOIN MyCompany.Config_Branchs AS br ON br.BranchID_PK = pi.BranchID_FK
INNER JOIN Inventory.Data_ProductUOMs AS u
    ON u.ProductID_FK = p.ProductID_PK AND u.UomID_FK = p.DefaultSellUomID_FK
LEFT JOIN Inventory.RefUOMs AS r ON r.UOMID_PK = u.UomID_FK
LEFT JOIN Purchase.Data_Suppliers AS s ON s.SupplierID_PK = p.MainSupplierID_FK
OUTER APPLY (
    SELECT TOP (1) pb.ProductBarcode
    FROM Inventory.Data_ProductBarcodes AS pb
    WHERE pb.ProductUOMID_FK = u.ProductUomID_PK
    ORDER BY pb.ProductBarcode
) AS bar
WHERE pi.StockOnHand > 0
  AND pi.ExpiryDate IS NOT NULL
  AND p.IsInActive = 0
  AND br.IsCurrentBranch = 1
  AND pi.ExpiryDate < DATEADD(month, 1, DATEADD(month, DATEDIFF(month, 0, GETDATE()), 0))
GROUP BY
    pi.ProductID_FK,
    p.ProductName,
    pi.ExpiryDate,
    p.ProductCode,
    pi.BranchID_FK,
    p.MainSupplierID_FK
ORDER BY
    pi.ExpiryDate ASC,
    p.ProductName ASC;
