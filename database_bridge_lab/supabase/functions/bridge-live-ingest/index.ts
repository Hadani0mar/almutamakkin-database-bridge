import "jsr:@supabase/functions-js/edge-runtime.d.ts";
import { createClient } from "jsr:@supabase/supabase-js@2";

const cors = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Headers":
    "authorization, x-client-info, apikey, content-type, x-bridge-secret, x-bridge-session",
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

function asText(value: unknown): string | null {
  if (value == null) return null;
  const text = String(value).trim();
  return text.length === 0 ? null : text;
}

function asNumber(value: unknown): number | null {
  if (value == null || value === "") return null;
  const n = typeof value === "number" ? value : Number(value);
  return Number.isFinite(n) ? n : null;
}

Deno.serve(async (req) => {
  if (req.method === "OPTIONS") return new Response("ok", { headers: cors });
  if (req.method !== "POST") return json(405, { error: "method_not_allowed" });

  const admin = createClient(
    Deno.env.get("SUPABASE_URL")!,
    Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!,
  );

  try {
    const secret = req.headers.get("x-bridge-secret") ?? "";
    const body = await req.json();
    const tunnelId = String(body.tunnelId ?? "").trim();
    const system = String(body.system ?? "marketing").trim().toLowerCase() || "marketing";
    const channel = String(body.channel ?? "").trim();
    const invoices = Array.isArray(body.invoices) ? body.invoices : [];

    if (!secret || !tunnelId || channel !== "active_invoices") {
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

    // Replace the live set for this tunnel/system.
    const { error: deleteErr } = await admin
      .from("bridge_live_active_invoices")
      .delete()
      .eq("tunnel_id", tunnelId)
      .eq("system", system);
    if (deleteErr) throw new Error(deleteErr.message);

    if (invoices.length > 0) {
      const rows = invoices.map((invoice: Record<string, unknown>) => ({
        tunnel_id: tunnelId,
        system,
        invoice_id: asText(invoice.invoiceId) ?? crypto.randomUUID(),
        employee_id: asText(invoice.employeeId),
        employee_name: asText(invoice.employeeName) ?? "غير محدد",
        customer_id: asText(invoice.customerId),
        invoice_kind: asText(invoice.invoiceKind),
        invoice_lifecycle: asText(invoice.invoiceLifecycle) ?? "live",
        total_amount: asNumber(invoice.totalAmount) ?? 0,
        line_count: asNumber(invoice.lineCount) ?? 0,
        started_at: asText(invoice.startedAt),
        last_item_at: asText(invoice.lastItemAt),
        items: Array.isArray(invoice.items) ? invoice.items : [],
        updated_at: new Date().toISOString(),
      }));

      const { error: insertErr } = await admin
        .from("bridge_live_active_invoices")
        .insert(rows);
      if (insertErr) throw new Error(insertErr.message);
    }

    await admin.from("bridge_devices").update({
      last_seen_at: new Date().toISOString(),
      status: "online",
    }).eq("id", device.id);

    return json(200, {
      success: true,
      rowCount: invoices.length,
      channel: "active_invoices",
    });
  } catch (e) {
    return json(500, { error: String(e) });
  }
});
