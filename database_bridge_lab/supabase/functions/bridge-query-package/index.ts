import "jsr:@supabase/functions-js/edge-runtime.d.ts";
import { createClient } from "jsr:@supabase/supabase-js@2";

const cors = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Headers":
    "authorization, x-client-info, apikey, content-type, x-bridge-secret",
};

function base64ToBytes(value: string): Uint8Array {
  const binary = atob(value);
  return Uint8Array.from(binary, (c) => c.charCodeAt(0));
}

function bytesToText(value: ArrayBuffer): string {
  return new TextDecoder().decode(value);
}

async function sha256Hex(value: string): Promise<string> {
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(value));
  return Array.from(new Uint8Array(digest), (b) => b.toString(16).padStart(2, "0")).join("");
}

async function decryptSql(ciphertext: string, iv: string, keyBase64: string): Promise<string> {
  const key = await crypto.subtle.importKey(
    "raw",
    base64ToBytes(keyBase64),
    { name: "AES-GCM" },
    false,
    ["decrypt"],
  );
  const plaintext = await crypto.subtle.decrypt(
    { name: "AES-GCM", iv: base64ToBytes(iv) },
    key,
    base64ToBytes(ciphertext),
  );
  return bytesToText(plaintext);
}

Deno.serve(async (req) => {
  if (req.method === "OPTIONS") return new Response("ok", { headers: cors });
  if (req.method !== "POST") return new Response(JSON.stringify({ error: "method_not_allowed" }), { status: 405, headers: { ...cors, "Content-Type": "application/json" } });

  try {
    const secret = req.headers.get("x-bridge-secret") ?? "";
    const body = await req.json().catch(() => ({}));
    const tunnelId = String(body.tunnelId ?? "").trim();
    const queryId = String(body.queryId ?? "").trim();
    if (!secret || !tunnelId || !/^[a-z][a-z0-9_.-]{2,119}$/.test(queryId)) {
      return new Response(JSON.stringify({ error: "invalid_payload" }), { status: 400, headers: { ...cors, "Content-Type": "application/json" } });
    }

    const admin = createClient(Deno.env.get("SUPABASE_URL")!, Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!);
    const secretHash = await sha256Hex(secret);
    const { data: device } = await admin
      .from("bridge_devices")
      .select("id, device_secret_hash, status")
      .eq("tunnel_id", tunnelId)
      .maybeSingle();
    if (!device || device.device_secret_hash !== secretHash || device.status === "revoked") {
      return new Response(JSON.stringify({ error: "unauthorized" }), { status: 401, headers: { ...cors, "Content-Type": "application/json" } });
    }

    const { data: row, error } = await admin
      .from("bridge_query_packages")
      .select("query_id, version, system, database_profile, encrypted_sql, encryption_iv, parameter_schema, timeout_seconds, max_rows, key_id, signature_base64")
      .eq("query_id", queryId)
      .eq("is_enabled", true)
      .maybeSingle();
    if (error) throw error;
    if (!row) return new Response(JSON.stringify({ error: "query_package_not_found" }), { status: 404, headers: { ...cors, "Content-Type": "application/json" } });

    const { data: config, error: configError } = await admin.rpc("get_bridge_query_catalog_crypto_config");
    if (configError || !config?.bridge_query_catalog_aes_key) throw new Error("query_catalog_crypto_unavailable");
    const sql = await decryptSql(row.encrypted_sql, row.encryption_iv, config.bridge_query_catalog_aes_key);

    return new Response(JSON.stringify({
      package: {
        definition: {
          queryId: row.query_id,
          version: row.version,
          system: row.system,
          databaseProfile: row.database_profile,
          sql,
          parameters: row.parameter_schema,
          timeoutSeconds: row.timeout_seconds,
          maxRows: row.max_rows,
        },
        keyId: row.key_id,
        signatureBase64: row.signature_base64,
      },
      cacheSeconds: 300,
    }), { headers: { ...cors, "Content-Type": "application/json", "Cache-Control": "no-store" } });
  } catch (error) {
    return new Response(JSON.stringify({ error: "query_package_unavailable" }), { status: 500, headers: { ...cors, "Content-Type": "application/json" } });
  }
});
