-- Infinity current branch profile (read-only).
SELECT TOP (1)
    LTRIM(RTRIM(ISNULL(BranchName, N''))) AS business_name,
    LTRIM(RTRIM(ISNULL(BranchName, N''))) AS activity_name,
    LTRIM(RTRIM(ISNULL(
        NULLIF(BranchAddressLine1, N'') +
        CASE WHEN NULLIF(BranchAddressLine2, N'') IS NULL THEN N'' ELSE N' ' + BranchAddressLine2 END +
        CASE WHEN NULLIF(BranchAddressLine3, N'') IS NULL THEN N'' ELSE N' ' + BranchAddressLine3 END,
        N''))) AS address,
    CAST(NULL AS nvarchar(100)) AS city,
    LTRIM(RTRIM(ISNULL(BranchPhone, N''))) AS phone,
    LTRIM(RTRIM(ISNULL(BranchEmailAddress, N''))) AS email,
    BranchID_PK AS branch_id
FROM MyCompany.Config_Branchs
WHERE IsCurrentBranch = 1
ORDER BY BranchID_PK;
