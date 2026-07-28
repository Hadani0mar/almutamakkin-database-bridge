-- Read-only fingerprint for Infinity business profile.
SELECT TOP 1
  CHECKSUM(BranchName, BranchPhone, BranchEmailAddress, BranchAddressLine1) AS profile_ck
FROM MyCompany.Config_Branchs
WHERE IsCurrentBranch = 1;
