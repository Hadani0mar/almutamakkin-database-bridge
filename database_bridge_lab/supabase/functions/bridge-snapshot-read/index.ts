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

Deno.serve(async (req) => {
  if (req.method === "OPTIONS") return new Response("ok", { headers: cors });
  if (req.method !== "POST") return json(405, { error: "method_not_allowed" });

  try {
    const sessionToken = req.headers.get("x-bridge-session") ?? "";
    if (!sessionToken) {
      return json(401, { error: "missing_session" });
    }

    const body = await req.json();
    const system = String(body.system ?? "marketing").trim().toLowerCase();
    if (!SYSTEMS.has(system)) {
      return json(400, { error: "invalid_system" });
    }

    const typesRaw = Array.isArray(body.types)
      ? body.types
      : body.snapshotType
      ? [body.snapshotType]
      : [];
    const types = [...new Set(
      typesRaw.map((t: unknown) => String(t ?? "").trim()).filter(Boolean),
    )];

    if (types.length === 0) {
      return json(400, { error: "missing_snapshot_type" });
    }
    for (const t of types) {
      if (!SNAPSHOT_TYPES.has(t)) {
        return json(400, { error: "unsupported_snapshot_type", snapshotType: t });
      }
    }

    const admin = createClient(
      Deno.env.get("SUPABASE_URL")!,
      Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!,
    );

    const tokenHash = await sha256Hex(sessionToken);
    const { data: session } = await admin
      .from("bridge_mobile_sessions")
      .select("id, device_id, expires_at")
      .eq("session_token_hash", tokenHash)
      .maybeSingle();

    if (!session || new Date(session.expires_at).getTime() < Date.now()) {
      return json(401, { error: "invalid_or_expired_session" });
    }

    const { data: device } = await admin
      .from("bridge_devices")
      .select("id, tunnel_id, status")
      .eq("id", session.device_id)
      .maybeSingle();

    if (!device || device.status === "revoked") {
      return json(403, { error: "device_unavailable" });
    }

    await admin.from("bridge_mobile_sessions").update({
      last_used_at: new Date().toISOString(),
    }).eq("id", session.id);

    const tunnelId = device.tunnel_id as string;
    const snapshots: Record<string, unknown> = {};

    for (const snapshotType of types) {
      const { data: meta } = await admin
        .from("bridge_activity_snapshots")
        .select(
          "id, snapshot_type, calculation_version, generated_at, row_count, status, error_message",
        )
        .eq("tunnel_id", tunnelId)
        .eq("system", system)
        .eq("snapshot_type", snapshotType)
        .eq("status", "ready")
        .order("generated_at", { ascending: false })
        .limit(1)
        .maybeSingle();

      if (!meta?.id) {
        snapshots[snapshotType] = {
          ready: false,
          message: "لا توجد لقطة جاهزة. شغّل مزامنة اللقطات من الجسر.",
          rows: [],
          headers: [],
        };
        continue;
      }

      if (snapshotType === "purchase_orders") {
        const { data: headers } = await admin
          .from("bridge_snapshot_purchase_order_headers")
          .select("*")
          .eq("snapshot_id", meta.id)
          .order("sort_order", { ascending: true });

        const { data: items } = await admin
          .from("bridge_snapshot_purchase_order_items")
          .select("*")
          .eq("snapshot_id", meta.id)
          .order("sort_order", { ascending: true });

        const itemsByHeader = new Map<string, unknown[]>();
        for (const item of items ?? []) {
          const headerId = String(item.header_id ?? "");
          const list = itemsByHeader.get(headerId) ?? [];
          list.push(item);
          itemsByHeader.set(headerId, list);
        }

        const headersWithItems = (headers ?? []).map((header) => ({
          ...header,
          items: itemsByHeader.get(String(header.id)) ?? [],
        }));

        snapshots[snapshotType] = {
          ready: true,
          snapshotId: meta.id,
          calculationVersion: meta.calculation_version,
          generatedAt: meta.generated_at,
          rowCount: meta.row_count,
          headers: headersWithItems,
          rows: [],
        };
        continue;
      }

      const table = ITEM_TABLE[snapshotType];
      const { data: rows, error: rowsErr } = await admin
        .from(table)
        .select("*")
        .eq("snapshot_id", meta.id)
        .order("sort_order", { ascending: true });

      if (rowsErr) {
        snapshots[snapshotType] = {
          ready: false,
          message: rowsErr.message,
          rows: [],
          headers: [],
        };
        continue;
      }

      snapshots[snapshotType] = {
        ready: true,
        snapshotId: meta.id,
        calculationVersion: meta.calculation_version,
        generatedAt: meta.generated_at,
        rowCount: meta.row_count,
        rows: rows ?? [],
        headers: [],
      };
    }

    return json(200, {
      success: true,
      tunnelId,
      system,
      snapshots,
    });
  } catch (error) {
    return json(500, {
      error: "internal_error",
      message: error instanceof Error ? error.message : String(error),
    });
  }
});
