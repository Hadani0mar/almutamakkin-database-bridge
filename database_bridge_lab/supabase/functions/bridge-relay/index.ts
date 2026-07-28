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

function requestId(): string {
  const d = new Date();
  const stamp = d.toISOString().replace(/[-:TZ.]/g, "").slice(0, 14);
  const arr = new Uint8Array(3);
  crypto.getRandomValues(arr);
  const suffix = Array.from(arr, (b) => b.toString(16).padStart(2, "0")).join("");
  return `REQ-${stamp}-${suffix}`;
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
    const sessionToken = req.headers.get("x-bridge-session") ?? "";
    if (!sessionToken) {
      return new Response(JSON.stringify({ error: "missing_session" }), {
        status: 401,
        headers: { ...cors, "Content-Type": "application/json" },
      });
    }

    const body = await req.json();
    const messageType = String(body.messageType ?? "sql.execute").trim();
    const payload = body.payload ?? {};
    const waitMs = Math.min(Math.max(Number(body.waitMs ?? 45000) || 45000, 1000), 180000);
    const allowed = new Set([
      "bridge.health",
      "database.test",
      "database.list",
      "sql.execute",
      "query.execute",
      "marketing.product_movement",
      "infinity.product_movement",
      "product.photo",
      "product.photo.upsert",
      "printer.health",
      "printer.products.search",
      "printer.products.byBarcode",
      "printer.products.byBarId",
      "printer.print.submit",
      "printer.test.submit",
    ]);
    if (!allowed.has(messageType)) {
      return new Response(JSON.stringify({ error: "unsupported_command" }), {
        status: 400,
        headers: { ...cors, "Content-Type": "application/json" },
      });
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
      return new Response(JSON.stringify({ error: "invalid_or_expired_session" }), {
        status: 401,
        headers: { ...cors, "Content-Type": "application/json" },
      });
    }

    const { data: device } = await admin
      .from("bridge_devices")
      .select("id, tunnel_id, status")
      .eq("id", session.device_id)
      .maybeSingle();

    if (!device || device.status === "revoked") {
      return new Response(JSON.stringify({ error: "device_unavailable" }), {
        status: 403,
        headers: { ...cors, "Content-Type": "application/json" },
      });
    }

    const reqId = String(body.requestId ?? "").trim() || requestId();
    const expiresAt = new Date(Date.now() + 10 * 60 * 1000).toISOString();

    const { error: insertError } = await admin.from("bridge_commands").insert({
      request_id: reqId,
      device_id: device.id,
      tunnel_id: device.tunnel_id,
      message_type: messageType,
      protocol_version: "1.0",
      payload,
      status: "pending",
      expires_at: expiresAt,
    });

    if (insertError) {
      return new Response(JSON.stringify({ error: insertError.message }), {
        status: 500,
        headers: { ...cors, "Content-Type": "application/json" },
      });
    }

    await admin.from("bridge_mobile_sessions").update({
      last_used_at: new Date().toISOString(),
    }).eq("id", session.id);

    const started = Date.now();
    let lastStatus = "pending";
    while (Date.now() - started < waitMs) {
      const { data: cmd } = await admin
        .from("bridge_commands")
        .select("status, response, error_code, error_message")
        .eq("request_id", reqId)
        .maybeSingle();

      if (cmd) {
        lastStatus = String(cmd.status ?? lastStatus);
      }

      if (cmd && (cmd.status === "completed" || cmd.status === "failed")) {
        return new Response(
          JSON.stringify({
            success: cmd.status === "completed",
            requestId: reqId,
            tunnelId: device.tunnel_id,
            response: cmd.response,
            errorCode: cmd.error_code,
            errorMessage: cmd.error_message,
          }),
          { headers: { ...cors, "Content-Type": "application/json" } },
        );
      }
      await new Promise((r) => setTimeout(r, 700));
    }

    const busy = lastStatus === "claimed" || lastStatus === "processing";
    return new Response(
      JSON.stringify({
        success: false,
        requestId: reqId,
        tunnelId: device.tunnel_id,
        errorCode: busy ? "BRIDGE_BUSY" : "BRIDGE_OFFLINE",
        errorMessage: busy
          ? "الجسر مشغول بتنفيذ طلب آخر. أعد المحاولة بعد لحظات."
          : "الجسر لم يرد خلال المهلة. تأكد أن برنامج الجسر يعمل ومتصل.",
      }),
      { status: 504, headers: { ...cors, "Content-Type": "application/json" } },
    );
  } catch (e) {
    return new Response(JSON.stringify({ error: String(e) }), {
      status: 500,
      headers: { ...cors, "Content-Type": "application/json" },
    });
  }
});
