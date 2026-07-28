-- Read-only fingerprint for business profile.
SELECT TOP 1
  CHECKSUM(A_NAME, ACTIVITYName, A_ADDRESS, CITY, PHONE) AS profile_ck
FROM dbo.SITTEINGS;
