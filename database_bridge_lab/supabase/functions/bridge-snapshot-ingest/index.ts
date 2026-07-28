import "jsr:@supabase/functions-js/edge-runtime.d.ts";
import { createClient } from "jsr:@supabase/supabase-js@2";

const cors = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Headers":
    "authorization, x-client-info, apikey, content-type, x-bridge-secret, x-bridge-session",
};

const SNAPSHOT_TYPES = new Set([
  "business_profile",
  "shortages",
  "debts_customers",
  "debts_suppliers",
  "expiry",
  "required_items",
  "purchase_orders",
  "product_search",
  "daily_statistics",
  "debt_invoice_events",
  "shift_close_events",
  "sales_pattern",
]);

const SYSTEMS = new Set(["marketing", "infinity"]);

const ITEM_TABLE: Record<string, string> = {
  business_profile: "bridge_snapshot_business_profile",
  shortages: "bridge_snapshot_shortages",
  debts_customers: "bridge_snapshot_debts_customers",
  debts_suppliers: "bridge_snapshot_debts_suppliers",
  expiry: "bridge_snapshot_expiry",
  required_items: "bridge_snapshot_required_items",
  product_search: "bridge_snapshot_product_search",
  daily_statistics: "bridge_snapshot_daily_statistics",
  debt_invoice_events: "bridge_snapshot_debt_invoice_events",
  shift_close_events: "bridge_snapshot_shift_close_events",
  sales_pattern: "bridge_snapshot_sales_pattern",
};

async function sha256Hex(value: string): Promise<string> {
  const data = new TextEncoder().encode(value);
  const digest = await crypto.subtle.digest("SHA-256", data);
  return Array.from(new Uint8Array(digest))
    .map((b) => b.toString(16).padStart(2, "0"))
    .join("");
}

function json(status: number, body: unknown) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { ...cors, "Content-Type": "application/json" },
  });
}

function asNumber(value: unknown): number | null {
  if (value == null || value === "") return null;
  const n = typeof value === "number" ? value : Number(value);
  return Number.isFinite(n) ? n : null;
}

function asText(value: unknown): string | null {
  if (value == null) return null;
  const text = String(value).trim();
  return text.length === 0 ? null : text;
}

Deno.serve(async (req) => {
  if (req.method === "OPTIONS") return new Response("ok", { headers: cors });
  if (req.method !== "POST") return json(405, { error: "method_not_allowed" });

  let snapshotId: string | null = null;
  const admin = createClient(
    Deno.env.get("SUPABASE_URL")!,
    Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!,
  );

  try {
    const secret = req.headers.get("x-bridge-secret") ?? "";
    const body = await req.json();
    const tunnelId = String(body.tunnelId ?? "").trim();
    const system = String(body.system ?? "").trim().toLowerCase();
    const snapshotType = String(body.snapshotType ?? "").trim();
    const calculationVersion = String(body.calculationVersion ?? "1").trim() || "1";
    const params = body.params && typeof body.params === "object" ? body.params : {};
    const generatedAt = asText(body.generatedAt) ?? new Date().toISOString();
    const rows = Array.isArray(body.rows) ? body.rows : [];
    const headers = Array.isArray(body.headers) ? body.headers : [];

    if (!secret || !tunnelId || !SYSTEMS.has(system) || !SNAPSHOT_TYPES.has(snapshotType)) {
      return json(400, { error: "invalid_payload" });
    }

    const secretHash = await sha256Hex(secret);
    const { data: device } = await admin
      .from("bridge_devices")
      .select("id, device_secret_hash, status")
      .eq("tunnel_id", tunnelId)
      .maybeSingle();

    if (!device || device.device_secret_hash !== secretHash || device.status === "revoked") {
      return json(401, { error: "unauthorized" });
    }

    const { data: snapshot, error: snapErr } = await admin
      .from("bridge_activity_snapshots")
      .insert({
        tunnel_id: tunnelId,
        system,
        snapshot_type: snapshotType,
        calculation_version: calculationVersion,
        params,
        generated_at: generatedAt,
        row_count: 0,
        status: "building",
      })
      .select("id")
      .single();

    if (snapErr || !snapshot?.id) {
      return json(500, { error: snapErr?.message ?? "snapshot_insert_failed" });
    }
    snapshotId = snapshot.id as string;

    let rowCount = 0;

    if (snapshotType === "purchase_orders") {
      for (let h = 0; h < headers.length; h++) {
        const header = headers[h] ?? {};
        const { data: headerRow, error: headerErr } = await admin
          .from("bridge_snapshot_purchase_order_headers")
          .insert({
            snapshot_id: snapshotId,
            tunnel_id: tunnelId,
            system,
            sort_order: h,
            supplier_id: asText(header.supplierId),
            supplier_name: asText(header.supplierName) ?? "غير محدد",
            supplier_phone: asText(header.supplierPhone),
            item_count: asNumber(header.itemCount) ?? 0,
            total_estimated_cost: asNumber(header.totalEstimatedCost) ?? 0,
            extras: header.extras && typeof header.extras === "object" ? header.extras : {},
          })
          .select("id")
          .single();

        if (headerErr || !headerRow?.id) {
          throw new Error(headerErr?.message ?? "header_insert_failed");
        }

        const items = Array.isArray(header.items) ? header.items : [];
        if (items.length > 0) {
          const itemPayload = items.map((item: Record<string, unknown>, index: number) => ({
            snapshot_id: snapshotId,
            header_id: headerRow.id,
            tunnel_id: tunnelId,
            system,
            sort_order: index,
            item_id: asText(item.itemId),
            item_name: asText(item.itemName) ?? "صنف",
            purchase_unit_label: asText(item.purchaseUnitLabel),
            suggested_qty: asNumber(item.suggestedQty),
            suggested_packs: asNumber(item.suggestedPacks),
            unit_cost: asNumber(item.unitCost),
            estimated_cost: asNumber(item.estimatedCost),
            current_stock: asNumber(item.currentStock),
            coverage_days: asNumber(item.coverageDays),
            decision_code: asText(item.decisionCode),
            extras: item.extras && typeof item.extras === "object" ? item.extras : {},
          }));
          const { error: itemsErr } = await admin
            .from("bridge_snapshot_purchase_order_items")
            .insert(itemPayload);
          if (itemsErr) throw new Error(itemsErr.message);
          rowCount += items.length;
        }
      }
    } else {
      const table = ITEM_TABLE[snapshotType];
      if (!table) throw new Error("unsupported_snapshot_type");

      if (rows.length > 0) {
        const mapped = rows.map((row: Record<string, unknown>, index: number) => {
          const base = {
            snapshot_id: snapshotId,
            tunnel_id: tunnelId,
            system,
            sort_order: asNumber(row.sortOrder) ?? index,
            extras: row.extras && typeof row.extras === "object" ? row.extras : {},
          };

          switch (snapshotType) {
            case "business_profile":
              return {
                ...base,
                business_name: asText(row.businessName),
                activity_name: asText(row.activityName),
                address: asText(row.address),
                city: asText(row.city),
                phone: asText(row.phone),
              };
            case "product_search":
              return {
                ...base,
                item_id: asText(row.itemId),
                item_name: asText(row.itemName),
                barcode: asText(row.barcode),
                bar_id: asText(row.barId),
                sale_price: asNumber(row.salePrice),
                unit_label: asText(row.unitLabel),
                unit_factor: asNumber(row.unitFactor),
                stock_qty: asNumber(row.stockQty),
                base_unit_label: asText(row.baseUnitLabel),
                nearest_expiry: asText(row.nearestExpiry),
                nearest_expiry_qty: asNumber(row.nearestExpiryQty),
                last_buy_price: asNumber(row.lastBuyPrice),
                last_supplier_name: asText(row.lastSupplierName),
                last_buy_date: asText(row.lastBuyDate),
              };
            case "daily_statistics":
              return {
                ...base,
                employee_id: asText(row.employeeId),
                employee_name: asText(row.employeeName),
                total_sales: asNumber(row.totalSales),
                cash_sales: asNumber(row.cashSales),
                debt_sales: asNumber(row.debtSales),
                total_purchases: asNumber(row.totalPurchases),
                cash_document_count: asNumber(row.cashDocumentCount),
                debt_invoice_count: asNumber(row.debtInvoiceCount),
                cash_sale_line_count: asNumber(row.cashSaleLineCount),
                open_cash_invoice_count: asNumber(row.openCashInvoiceCount),
                open_debt_invoice_count: asNumber(row.openDebtInvoiceCount),
                open_invoice_sales: asNumber(row.openInvoiceSales),
                purchase_invoices: asNumber(row.purchaseInvoices),
                sold_qty: asNumber(row.soldQty),
                purchased_qty: asNumber(row.purchasedQty),
                closed_shift_count: asNumber(row.closedShiftCount),
              };
            case "debt_invoice_events":
              return {
                ...base,
                sale_id: asText(row.saleId),
                sale_date: asText(row.saleDate),
                customer_name: asText(row.customerName),
                employee_name: asText(row.employeeName),
                total_amount: asNumber(row.totalAmount),
              };
            case "shift_close_events":
              return {
                ...base,
                shift_id: asText(row.shiftId),
                employee_no: asText(row.employeeNo),
                employee_name: asText(row.employeeName),
                check_in: asText(row.checkIn),
                check_out: asText(row.checkOut),
                hours: asNumber(row.hours),
                session_key: asText(row.sessionKey),
                shift_minutes: asNumber(row.shiftMinutes),
                cash_revenue: asNumber(row.cashRevenue),
                debt_revenue: asNumber(row.debtRevenue),
                total_revenue: asNumber(row.totalRevenue),
              };
            case "sales_pattern":
              return {
                ...base,
                cash_invoice_count: asNumber(row.cashInvoiceCount),
                long_cash_invoice_percent: asNumber(row.longCashInvoicePercent),
                large_cash_invoice_percent: asNumber(row.largeCashInvoicePercent),
                short_cash_invoice_percent: asNumber(row.shortCashInvoicePercent),
                average_cash_duration_minutes: asNumber(row.averageCashDurationMinutes),
                average_cash_line_count: asNumber(row.averageCashLineCount),
                sale_line_count: asNumber(row.saleLineCount),
                small_sale_lines: asNumber(row.smallSaleLines),
                bulk_sale_lines: asNumber(row.bulkSaleLines),
                packaged_sale_lines: asNumber(row.packagedSaleLines),
                average_base_quantity: asNumber(row.averageBaseQuantity),
              };
            case "shortages":
              return {
                ...base,
                item_id: asText(row.itemId),
                item_name: asText(row.itemName),
                category_name: asText(row.categoryName),
                supplier_name: asText(row.supplierName),
                status_code: asText(row.statusCode),
                status_label: asText(row.statusLabel),
                current_stock: asNumber(row.currentStock),
                days_of_cover: asNumber(row.daysOfCover),
                suggested_order_qty: asNumber(row.suggestedOrderQty),
                net_sales_30: asNumber(row.netSales30),
                base_unit_label: asText(row.baseUnitLabel),
                purchase_unit_label: asText(row.purchaseUnitLabel),
                last_purchase_price: asNumber(row.lastPurchasePrice),
              };
            case "debts_customers":
              return {
                ...base,
                customer_id: asText(row.customerId),
                customer_name: asText(row.customerName),
                phone: asText(row.phone),
                total_debt: asNumber(row.totalDebt),
                invoice_count: asNumber(row.invoiceCount),
                last_invoice_at: asText(row.lastInvoiceAt),
                overdue_amount: asNumber(row.overdueAmount),
              };
            case "debts_suppliers":
              return {
                ...base,
                supplier_id: asText(row.supplierId),
                supplier_name: asText(row.supplierName),
                debt_amount: asNumber(row.debtAmount),
              };
            case "expiry":
              return {
                ...base,
                item_id: asText(row.itemId),
                item_name: asText(row.itemName),
                batch_code: asText(row.batchCode),
                expire_date: asText(row.expireDate),
                qty: asNumber(row.qty),
                unit_label: asText(row.unitLabel),
                days_remaining: asNumber(row.daysRemaining),
                status_code: asText(row.statusCode),
              };
            case "required_items":
              return {
                ...base,
                item_id: asText(row.itemId),
                item_code: asText(row.itemCode),
                item_name: asText(row.itemName),
                net_required: asNumber(row.netRequired),
                stock_qty: asNumber(row.stockQty),
                avg_daily: asNumber(row.avgDaily),
                days_of_cover: asNumber(row.daysOfCover),
                main_supplier_name: asText(row.mainSupplierName),
                flexible_supplier_name: asText(row.flexibleSupplierName),
                estimated_value: asNumber(row.estimatedValue),
              };
            default:
              return base;
          }
        });

        const chunkSize = 500;
        for (let i = 0; i < mapped.length; i += chunkSize) {
          const chunk = mapped.slice(i, i + chunkSize);
          const { error: rowsErr } = await admin.from(table).insert(chunk);
          if (rowsErr) throw new Error(rowsErr.message);
        }
        rowCount = mapped.length;
      }
    }

    const { error: finalizeErr } = await admin.rpc("bridge_finalize_activity_snapshot", {
      p_snapshot_id: snapshotId,
      p_status: "ready",
      p_row_count: rowCount,
      p_error_message: null,
    });
    if (finalizeErr) throw new Error(finalizeErr.message);

    await admin.from("bridge_devices").update({
      last_seen_at: new Date().toISOString(),
      status: "online",
    }).eq("id", device.id);

    return json(200, {
      success: true,
      snapshotId,
      rowCount,
      status: "ready",
    });
  } catch (e) {
    if (snapshotId) {
      try {
        await admin.rpc("bridge_finalize_activity_snapshot", {
          p_snapshot_id: snapshotId,
          p_status: "failed",
          p_row_count: null,
          p_error_message: String(e).slice(0, 500),
        });
      } catch (_) {
        // best-effort
      }
    }
    return json(500, { error: String(e) });
  }
});
