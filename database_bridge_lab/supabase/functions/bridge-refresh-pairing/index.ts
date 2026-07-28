import "jsr:@supabase/functions-js/edge-runtime.d.ts";
import { createClient } from "jsr:@supabase/supabase-js@2";

const cors = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Headers":
    "authorization, x-client-info, apikey, content-type, x-bridge-secret, x-bridge-session",
};

const PAIRING_TTL_MS = 24 * 60 * 60 * 1000;

async function sha256Hex(value: string): Promise<string> {
  const data = new TextEncoder().encode(value);
  const digest = await crypto.subtle.digest("SHA-256", data);
  return Array.from(new Uint8Array(digest))
    .map((b) => b.toString(16).padStart(2, "0"))
    .join("");
}

function pairingCode(): string {
  const alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
  const arr = new Uint8Array(8);
  crypto.getRandomValues(arr);
  return Array.from(arr, (b) => alphabet[b % alphabet.length]).join("");
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
    const tunnelId = String(body.tunnelId ?? "").trim().toUpperCase();
    const rotateCode = body.rotateCode === true;

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
    const { data: device, error } = await admin
      .from("bridge_devices")
      .select("id, tunnel_id, pairing_code, device_secret_hash, status")
      .eq("tunnel_id", tunnelId)
      .maybeSingle();

    if (
      error || !device || device.device_secret_hash !== secretHash ||
      device.status === "revoked"
    ) {
      return new Response(JSON.stringify({ error: "unauthorized" }), {
        status: 401,
        headers: { ...cors, "Content-Type": "application/json" },
      });
    }

    const pairingExpiresAt = new Date(Date.now() + PAIRING_TTL_MS).toISOString();
    const nextPairingCode = rotateCode ? pairingCode() : device.pairing_code;

    const { error: updateError } = await admin.from("bridge_devices").update({
      pairing_code: nextPairingCode,
      pairing_expires_at: pairingExpiresAt,
      updated_at: new Date().toISOString(),
    }).eq("id", device.id);

    if (updateError) {
      return new Response(JSON.stringify({ error: updateError.message }), {
        status: 500,
        headers: { ...cors, "Content-Type": "application/json" },
      });
    }

    return new Response(
      JSON.stringify({
        success: true,
        tunnelId: device.tunnel_id,
        pairingCode: nextPairingCode,
        pairingExpiresAt,
        rotated: rotateCode,
      }),
      { headers: { ...cors, "Content-Type": "application/json" } },
    );
  } catch (e) {
    return new Response(JSON.stringify({ error: String(e) }), {
      status: 500,
      headers: { ...cors, "Content-Type": "application/json" },
    });
  }
});
