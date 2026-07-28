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
    const body = await req.json();
    const tunnelId = String(body.tunnelId ?? "").trim();
    const requestId = String(body.requestId ?? "").trim();
    const response = body.response;

    if (!secret || !tunnelId || !requestId || response == null) {
      return new Response(JSON.stringify({ error: "invalid_payload" }), {
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
      .select("id, device_secret_hash, status")
      .eq("tunnel_id", tunnelId)
      .maybeSingle();

    if (!device || device.device_secret_hash !== secretHash || device.status === "revoked") {
      return new Response(JSON.stringify({ error: "unauthorized" }), {
        status: 401,
        headers: { ...cors, "Content-Type": "application/json" },
      });
    }

    const success = Boolean(response.success);
    const { data: updated, error } = await admin
      .from("bridge_commands")
      .update({
        status: success ? "completed" : "failed",
        response,
        error_code: response?.error?.code ?? null,
        error_message: response?.error?.message ?? null,
        completed_at: new Date().toISOString(),
      })
      .eq("request_id", requestId)
      .eq("device_id", device.id)
      .select("request_id, status")
      .maybeSingle();

    if (error || !updated) {
      return new Response(JSON.stringify({ error: error?.message ?? "command_not_found" }), {
        status: 404,
        headers: { ...cors, "Content-Type": "application/json" },
      });
    }

    await admin.from("bridge_devices").update({
      last_seen_at: new Date().toISOString(),
      status: "online",
    }).eq("id", device.id);

    return new Response(JSON.stringify({ success: true, requestId: updated.request_id }), {
      headers: { ...cors, "Content-Type": "application/json" },
    });
  } catch (e) {
    return new Response(JSON.stringify({ error: String(e) }), {
      status: 500,
      headers: { ...cors, "Content-Type": "application/json" },
    });
  }
});
