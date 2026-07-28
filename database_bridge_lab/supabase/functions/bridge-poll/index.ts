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

Deno.serve(async (req) => {
  if (req.method === "OPTIONS") return new Response("ok", { headers: cors });
  if (req.method !== "POST") {
    return new Response(JSON.stringify({ error: "method_not_allowed" }), {
      status: 405,
      headers: { ...cors, "Content-Type": "application/json" },
    });
  }

  try {
    const secret = req.headers.get("x-bridge-secret") ?? "";
    if (!secret) {
      return new Response(JSON.stringify({ error: "missing_bridge_secret" }), {
        status: 401,
        headers: { ...cors, "Content-Type": "application/json" },
      });
    }

    const body = await req.json().catch(() => ({}));
    const tunnelId = String(body.tunnelId ?? "").trim();
    const limit = Math.min(Math.max(Number(body.limit ?? 1) || 1, 1), 5);
    const waitMs = Math.min(Math.max(Number(body.waitMs ?? 20000) || 20000, 0), 25000);

    if (!tunnelId) {
      return new Response(JSON.stringify({ error: "tunnel_id_required" }), {
        status: 400,
        headers: { ...cors, "Content-Type": "application/json" },
      });
    }

    const admin = createClient(
      Deno.env.get("SUPABASE_URL")!,
      Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!,
    );

    const secretHash = await sha256Hex(secret);
    const { data: device } = await admin
      .from("bridge_devices")
      .select("id, tunnel_id, device_secret_hash, status")
      .eq("tunnel_id", tunnelId)
      .maybeSingle();

    if (!device || device.device_secret_hash !== secretHash || device.status === "revoked") {
      return new Response(JSON.stringify({ error: "unauthorized" }), {
        status: 401,
        headers: { ...cors, "Content-Type": "application/json" },
      });
    }

    await admin.from("bridge_devices").update({
      status: "online",
      last_seen_at: new Date().toISOString(),
      updated_at: new Date().toISOString(),
      pairing_expires_at: new Date(Date.now() + 24 * 60 * 60 * 1000).toISOString(),
    }).eq("id", device.id);

    const started = Date.now();
    let commands: unknown[] = [];
    while (Date.now() - started <= waitMs) {
      const { data, error } = await admin.rpc("bridge_claim_pending_commands", {
        p_device_id: device.id,
        p_limit: limit,
      });
      if (error) {
        return new Response(JSON.stringify({ error: error.message }), {
          status: 500,
          headers: { ...cors, "Content-Type": "application/json" },
        });
      }
      commands = data ?? [];
      if (commands.length > 0) break;
      await new Promise((r) => setTimeout(r, 1000));
    }

    const mapped = (commands as Array<Record<string, unknown>>).map((c) => ({
      protocolVersion: c.protocol_version ?? "1.0",
      messageType: c.message_type,
      requestId: c.request_id,
      tunnelId: c.tunnel_id,
      sentAtUtc: c.created_at,
      payload: c.payload ?? {},
    }));

    return new Response(JSON.stringify({ success: true, commands: mapped }), {
      headers: { ...cors, "Content-Type": "application/json" },
    });
  } catch (e) {
    return new Response(JSON.stringify({ error: String(e) }), {
      status: 500,
      headers: { ...cors, "Content-Type": "application/json" },
    });
  }
});
