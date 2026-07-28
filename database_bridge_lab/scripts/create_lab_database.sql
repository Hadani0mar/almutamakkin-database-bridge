-- Almutamakkin Database Bridge Lab — manual setup script
-- Do NOT run from the application automatically.

IF DB_ID(N'AlmutamakkinBridgeLab') IS NULL
BEGIN
    CREATE DATABASE AlmutamakkinBridgeLab;
END
GO

USE AlmutamakkinBridgeLab;
GO

IF OBJECT_ID(N'dbo.BridgeTestItems', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BridgeTestItems
    (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        ItemName NVARCHAR(200) NOT NULL,
        Quantity DECIMAL(18,2) NOT NULL DEFAULT 0,
        IsActive BIT NOT NULL DEFAULT 1,
        CreatedAt DATETIME NOT NULL DEFAULT GETDATE()
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.BridgeTestItems)
BEGIN
    INSERT INTO dbo.BridgeTestItems (ItemName, Quantity, IsActive)
    VALUES
        (N'منتج تجريبي 1', 10, 1),
        (N'منتج تجريبي 2', 25, 1),
        (N'منتج متوقف', 0, 0);
END
GO
