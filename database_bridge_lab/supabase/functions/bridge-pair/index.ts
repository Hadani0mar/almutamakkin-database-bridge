import "jsr:@supabase/functions-js/edge-runtime.d.ts";
import { createClient } from "jsr:@supabase/supabase-js@2";

const cors = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Headers":
    "authorization, x-client-info, apikey, content-type, x-bridge-secret, x-bridge-session",
};

const PAIRING_TTL_MS = 24 * 60 * 60 * 1000;
const LIVE_BRIDGE_MS = 5 * 60 * 1000;

async function sha256Hex(value: string): Promise<string> {
  const data = new TextEncoder().encode(value);
  const digest = await crypto.subtle.digest("SHA-256", data);
  return Array.from(new Uint8Array(digest))
    .map((b) => b.toString(16).padStart(2, "0"))
    .join("");
}

function randomToken(bytes = 32): string {
  const arr = new Uint8Array(bytes);
  crypto.getRandomValues(arr);
  return Array.from(arr, (b) => b.toString(16).padStart(2, "0")).join("");
}

function isTunnelId(value: string): boolean {
  return /^TNL-[A-Z0-9]+$/i.test(value);
}

function isBridgeLive(device: {
  status?: string | null;
  last_seen_at?: string | null;
}): boolean {
  if (device.status !== "online" || !device.last_seen_at) {
    return false;
  }
  return Date.now() - new Date(device.last_seen_at).getTime() <= LIVE_BRIDGE_MS;
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
    const body = await req.json();
    const rawInput = String(
      body.pairingCode ?? body.tunnelId ?? "",
    ).trim().toUpperCase();
    const mobileDeviceId = String(body.mobileDeviceId ?? "").trim() || null;

    if (!rawInput) {
      return new Response(JSON.stringify({ error: "pairing_code_required" }), {
        status: 400,
        headers: { ...cors, "Content-Type": "application/json" },
      });
    }

    const admin = createClient(
      Deno.env.get("SUPABASE_URL")!,
      Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!,
    );

    const lookupField = isTunnelId(rawInput) ? "tunnel_id" : "pairing_code";
    const { data: device, error } = await admin
      .from("bridge_devices")
      .select(
        "id, tunnel_id, pairing_code, pairing_expires_at, status, last_seen_at",
      )
      .eq(lookupField, rawInput)
      .maybeSingle();

    if (error || !device) {
      return new Response(JSON.stringify({ error: "invalid_pairing_code" }), {
        status: 404,
        headers: { ...cors, "Content-Type": "application/json" },
      });
    }

    if (device.status === "revoked") {
      return new Response(JSON.stringify({ error: "device_revoked" }), {
        status: 403,
        headers: { ...cors, "Content-Type": "application/json" },
      });
    }

    const pairingExpired =
      new Date(device.pairing_expires_at).getTime() < Date.now();
    const bridgeLive = isBridgeLive(device);

    if (pairingExpired && !bridgeLive) {
      return new Response(
        JSON.stringify({
          error: "pairing_expired",
          message:
            "انتهت صلاحية الرمز. شغّل الجسر على الكمبيوتر ثم اضغط «تحديث رمز الاقتران».",
          tunnelId: device.tunnel_id,
        }),
        {
          status: 410,
          headers: { ...cors, "Content-Type": "application/json" },
        },
      );
    }

    const sessionToken = randomToken(32);
    const tokenHash = await sha256Hex(sessionToken);
    const expiresAt = new Date(Date.now() + 30 * 24 * 60 * 60 * 1000)
      .toISOString();
    const pairingExpiresAt = new Date(Date.now() + PAIRING_TTL_MS).toISOString();

    const { error: sessionError } = await admin
      .from("bridge_mobile_sessions")
      .insert({
        device_id: device.id,
        session_token_hash: tokenHash,
        mobile_device_id: mobileDeviceId,
        expires_at: expiresAt,
      });

    if (sessionError) {
      return new Response(JSON.stringify({ error: sessionError.message }), {
        status: 500,
        headers: { ...cors, "Content-Type": "application/json" },
      });
    }

    await admin.from("bridge_devices").update({
      status: "online",
      pairing_expires_at: pairingExpiresAt,
      updated_at: new Date().toISOString(),
    }).eq("id", device.id);

    return new Response(
      JSON.stringify({
        success: true,
        tunnelId: device.tunnel_id,
        sessionToken,
        expiresAt,
        pairingCode: device.pairing_code,
        pairingExpiresAt,
        pairedVia: lookupField === "tunnel_id" ? "tunnel_id" : "pairing_code",
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
