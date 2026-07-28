import "jsr:@supabase/functions-js/edge-runtime.d.ts";
import { createClient } from "jsr:@supabase/supabase-js@2";

const cors = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Headers":
    "authorization, x-client-info, apikey, content-type, x-bridge-secret, x-bridge-session",
};

const SYSTEMS = new Set(["marketing", "infinity"]);

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

    const body = await req.json().catch(() => ({}));
    const system = String(body.system ?? "marketing").trim().toLowerCase();
    if (!SYSTEMS.has(system)) {
      return json(400, { error: "invalid_system" });
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
    const { data: rows, error } = await admin
      .from("bridge_live_active_invoices")
      .select("*")
      .eq("tunnel_id", tunnelId)
      .eq("system", system)
      .order("last_item_at", { ascending: false });

    if (error) {
      return json(500, { error: error.message });
    }

    return json(200, {
      success: true,
      tunnelId,
      system,
      invoices: rows ?? [],
      rowCount: (rows ?? []).length,
    });
  } catch (error) {
    return json(500, {
      error: "internal_error",
      message: error instanceof Error ? error.message : String(error),
    });
  }
});
