-- Read-only fingerprint for closed shifts (7-day window).
SELECT
  COUNT(*) AS closed_7d,
  MAX(CHECK_OUT) AS max_check_out,
  CHECKSUM_AGG(CHECKSUM(ID, CHECK_OUT, IS_OPEN)) AS shift_ck
FROM dbo.MUTAMAKKIN_HIK_SHIFTS
WHERE IS_OPEN = 0
  AND CHECK_OUT >= DATEADD(day, -7, CONVERT(datetime, CONVERT(varchar(8), GETDATE(), 112), 112));
