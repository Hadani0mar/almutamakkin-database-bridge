import "jsr:@supabase/functions-js/edge-runtime.d.ts";
import { createClient } from "jsr:@supabase/supabase-js@2";

const cors = { "Access-Control-Allow-Origin": "*", "Access-Control-Allow-Headers": "authorization, x-client-info, apikey, content-type" };

function bytesToBase64(bytes: Uint8Array): string {
  let binary = "";
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary);
}

async function encryptSql(sql: string, keyBase64: string): Promise<{ ciphertext: string; iv: string }> {
  const raw = Uint8Array.from(atob(keyBase64), (c) => c.charCodeAt(0));
  const key = await crypto.subtle.importKey("raw", raw, { name: "AES-GCM" }, false, ["encrypt"]);
  const iv = crypto.getRandomValues(new Uint8Array(12));
  const cipher = await crypto.subtle.encrypt({ name: "AES-GCM", iv }, key, new TextEncoder().encode(sql));
  return { ciphertext: bytesToBase64(new Uint8Array(cipher)), iv: bytesToBase64(iv) };
}

function isReadOnly(sql: string): boolean {
  const normalized = sql.replace(/\/\*[\s\S]*?\*\//g, " ").replace(/--.*$/gm, " ").trim().toUpperCase();
  if (!(normalized.startsWith("SELECT") || normalized.startsWith("WITH"))) return false;
  return !/\b(INSERT|UPDATE|DELETE|MERGE|ALTER|DROP|CREATE|EXEC(?:UTE)?|TRUNCATE|GRANT|REVOKE|SELECT\s+[^;]+\s+INTO)\b/.test(normalized);
}

Deno.serve(async (req) => {
  if (req.method === "OPTIONS") return new Response("ok", { headers: cors });
  if (req.method !== "POST") return new Response(JSON.stringify({ error: "method_not_allowed" }), { status: 405, headers: { ...cors, "Content-Type": "application/json" } });
  const authorization = req.headers.get("authorization") ?? "";
  if (authorization !== `Bearer ${Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")}`) {
    return new Response(JSON.stringify({ error: "publisher_unauthorized" }), { status: 401, headers: { ...cors, "Content-Type": "application/json" } });
  }
  try {
    const body = await req.json();
    const packageData = body?.definition;
    const queryId = String(packageData?.queryId ?? "");
    const system = String(packageData?.system ?? "");
    const databaseProfile = String(packageData?.databaseProfile ?? "");
    const sql = String(packageData?.sql ?? "");
    const version = Number(packageData?.version ?? 0);
    const timeoutSeconds = Number(packageData?.timeoutSeconds ?? 30);
    const maxRows = Number(packageData?.maxRows ?? 1000);
    const parameters = Array.isArray(packageData?.parameters) ? packageData.parameters : [];
    const keyId = String(body?.keyId ?? "");
    const signatureBase64 = String(body?.signatureBase64 ?? "");
    const systemMatches = (system === "marketing" && databaseProfile === "Marketing") || (system === "infinity" && databaseProfile === "InfinityRetailDB");
    if (!/^[a-z][a-z0-9_.-]{2,119}$/.test(queryId) || !systemMatches || !isReadOnly(sql) || !Number.isInteger(version) || version < 1 || !Number.isInteger(timeoutSeconds) || timeoutSeconds < 1 || timeoutSeconds > 600 || !Number.isInteger(maxRows) || maxRows < 1 || maxRows > 30000 || !keyId || !signatureBase64) {
      return new Response(JSON.stringify({ error: "invalid_signed_package" }), { status: 400, headers: { ...cors, "Content-Type": "application/json" } });
    }
    const admin = createClient(Deno.env.get("SUPABASE_URL")!, Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!);
    const { data: config, error: configError } = await admin.rpc("get_bridge_query_catalog_crypto_config");
    if (configError || !config?.bridge_query_catalog_aes_key) throw new Error("query_catalog_crypto_unavailable");
    const encrypted = await encryptSql(sql, config.bridge_query_catalog_aes_key);
    const { error } = await admin.from("bridge_query_packages").upsert({ query_id: queryId, version, system, database_profile: databaseProfile, encrypted_sql: encrypted.ciphertext, encryption_iv: encrypted.iv, parameter_schema: parameters, timeout_seconds: timeoutSeconds, max_rows: maxRows, key_id: keyId, signature_base64: signatureBase64, is_enabled: true, updated_at: new Date().toISOString() }, { onConflict: "query_id" });
    if (error) throw error;
    return new Response(JSON.stringify({ success: true, queryId, version }), { headers: { ...cors, "Content-Type": "application/json" } });
  } catch (_) {
    return new Response(JSON.stringify({ error: "query_package_publish_failed" }), { status: 500, headers: { ...cors, "Content-Type": "application/json" } });
  }
});
