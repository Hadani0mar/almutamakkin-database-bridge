SELECT
    si.S_ID AS invoice_id,
    si.ITEM_ID AS item_id,
    LTRIM(RTRIM(i.ITEM_NAME)) AS item_name,
    si.QTY AS qty,
    si.PRICE AS price,
    (((((si.QTY * si.PRICE) + (ISNULL(si.COLOR_PRICE, 0) * si.QTY)) - (((si.QTY * si.PRICE) + (ISNULL(si.COLOR_PRICE, 0) * si.QTY)) * ISNULL(s.S_DISCOUNT, 0) / 100.0)) + ((((si.QTY * si.PRICE) + (ISNULL(si.COLOR_PRICE, 0) * si.QTY)) - (((si.QTY * si.PRICE) + (ISNULL(si.COLOR_PRICE, 0) * si.QTY)) * ISNULL(s.S_DISCOUNT, 0) / 100.0)) * ISNULL(s.S_TAX1, 0) / 100.0)) + (((((si.QTY * si.PRICE) + (ISNULL(si.COLOR_PRICE, 0) * si.QTY)) - (((si.QTY * si.PRICE) + (ISNULL(si.COLOR_PRICE, 0) * si.QTY)) * ISNULL(s.S_DISCOUNT, 0) / 100.0)) + ((((si.QTY * si.PRICE) + (ISNULL(si.COLOR_PRICE, 0) * si.QTY)) - (((si.QTY * si.PRICE) + (ISNULL(si.COLOR_PRICE, 0) * si.QTY)) * ISNULL(s.S_DISCOUNT, 0) / 100.0)) * ISNULL(s.S_TAX1, 0) / 100.0)) * ISNULL(s.S_TAX2, 0) / 100.0)) AS line_total,
    COALESCE(NULLIF(si.UNIT_QTY, 0), 1) AS unit_factor,
    u.UNIT_DISC AS unit_label,
    si.S_ITEM_ID AS sort_key
FROM SALE_ITEMS si
INNER JOIN SALE_INVOICE s ON s.S_ID = si.S_ID
INNER JOIN ITEMS i ON i.ITEM_ID = si.ITEM_ID
LEFT JOIN UNITS u ON u.UNIT_ID = si.UNIT_ID
WHERE si.S_ID IN ({{INVOICE_IDS}})
  AND si.QTY > 0
ORDER BY si.S_ID, si.S_ITEM_ID DESC
;
