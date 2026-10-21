-- Read-only pre-deployment report. Does not change catalog or warehouse quantities.
-- New orders reserve ProductVariant.StockQuantity (available-to-sell stock).
SELECT v."Id" AS variant_id, v."Sku", v."StockQuantity" AS catalog_available,
       COALESCE(SUM(i."AvailableQuantity" - i."ReservedQuantity"), 0) AS warehouse_available,
       COUNT(i."Id") AS warehouse_rows
FROM product_variants v
LEFT JOIN inventory i ON i."VariantId" = v."Id"
GROUP BY v."Id", v."Sku", v."StockQuantity"
HAVING COUNT(i."Id") > 0
ORDER BY v."Sku";

-- These legacy orders require a deliberate stock reconciliation before processing.
SELECT r."OrderId", o."OrderNumber", o."Status" AS order_status, r."VariantId",
       r."WarehouseId", r."Quantity", r."Status" AS reservation_status
FROM inventory_reservations r
LEFT JOIN orders o ON o."Id" = r."OrderId"
WHERE r."WarehouseId" <> '00000000-0000-0000-0000-000000000000'::uuid
  AND r."Status" IN ('ACTIVE', 'CONFIRMED')
ORDER BY r."OrderId", r."VariantId";
